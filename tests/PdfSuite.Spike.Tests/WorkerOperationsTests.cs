using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Pdfium.Authoring;
using PdfSuite.Pdf.Qpdf;
using PdfSuite.Pdf.Redaction;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Redaction and qpdf operations requested from a real worker process, which is where the product
/// runs them (spike findings 28 and 37). Results are compared with the same operation run
/// in-process, or checked with PDFium as an independent reader.
/// </summary>
public sealed class WorkerOperationsTests : IDisposable
{
    private const string UserPassword = "user-Pässword";
    private const string OwnerPassword = "owner-secret";

    private readonly WorkerClient _client = WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()) { EnableTestCommands = true });
    private readonly PdfiumEngine _pdfium = new();

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Redact_GivesTheSameFileAsRedactingInProcess()
    {
        byte[] source = Fixtures.CanaryDocumentLevel();
        RedactionArea area = RedactionTests.TargetOf("canary-document-level.pdf", source);

        RedactionResult remote = await _client.RedactAsync(source, [area], ["CANARY-08"], Ct);

        Assert.True(remote.Report.Passed);
        Assert.Equal(Redactor.Redact(source, [area], ["CANARY-08"]).Pdf, remote.Pdf);
    }

    [Fact]
    public async Task Redact_ReturnsTheVerifiersWarnings()
    {
        byte[] source = Fixtures.CanarySharedXObject();

        RedactionResult result = await _client.RedactAsync(source, [RedactionTests.TargetOf("canary-shared-xobject.pdf", source)], cancellationToken: Ct);

        Assert.Contains(result.Report.Warnings, w => w.Contains("CANARY-03"));
    }

    // A secret that is on a kept page but is not page text, here a note's contents, cannot be told
    // apart from a leak, so verification fails and no file comes back (spike finding 35).
    [Fact]
    public async Task Redact_WhenVerificationFails_ReturnsTheFailuresAndNoFile()
    {
        byte[] source;
        using (var document = PdfiumDocumentWriter.CreateNew())
        {
            using (var first = document.AddPage(612, 792))
            {
                first.AddText("Redact this line", 72, 700, 12);
            }

            using (var second = document.AddPage(612, 792))
            {
                second.AddNoteAnnotation("NOTE-SECRET", 72, 600, 92, 620);
            }

            source = document.ToArray();
        }

        var area = new RedactionArea(0, new PageRect(60, 80, 300, 100));
        var error = await Assert.ThrowsAsync<RedactionFailedException>(() => _client.RedactAsync(source, [area], ["NOTE-SECRET"], Ct));

        Assert.True(error.Report.Failed(RedactionCheck.TextRemoved));
        Assert.Contains(error.Report.Failures, f => f.Message.Contains("NOTE-SECRET"));
        Assert.DoesNotContain("NOTE-SECRET", error.Message);
    }

    [Fact]
    public async Task Encrypt_NeedsThePasswordInAnIndependentReader_AndDecryptRemovesIt()
    {
        byte[] encrypted = await _client.EncryptAsync(Fixtures.SimpleText(), UserPassword, OwnerPassword, new PdfPermissions(), cancellationToken: Ct);

        Assert.Equal(PdfOpenError.Password, Assert.Throws<PdfOpenException>(() => _pdfium.Open(encrypted)).Error);
        using (IPdfDocument document = _pdfium.Open(encrypted, UserPassword))
        {
            Assert.Equal(3, document.PageCount);
        }

        byte[] decrypted = await _client.DecryptAsync(encrypted, UserPassword, Ct);
        Assert.False(QpdfStructure.IsEncrypted(decrypted));
    }

    [Fact]
    public async Task WrongPassword_IsAPasswordError()
    {
        byte[] encrypted = await _client.EncryptAsync(Fixtures.SimpleText(), UserPassword, OwnerPassword, new PdfPermissions(), cancellationToken: Ct);

        var error = await Assert.ThrowsAsync<PdfOpenException>(() => _client.DecryptAsync(encrypted, "wrong", Ct));

        Assert.Equal(PdfOpenError.Password, error.Error);
    }

    [Fact]
    public async Task Metadata_RoundTrips_AndMatchesInProcessOutput()
    {
        var values = new Dictionary<string, string?> { ["/Title"] = "Résumé – naïve test ✓", ["/Author"] = null };

        byte[] titled = await _client.SetMetadataAsync(Fixtures.SimpleText(), values, cancellationToken: Ct);
        IReadOnlyDictionary<string, string> read = await _client.GetMetadataAsync(titled, ["/Title", "/Author"], cancellationToken: Ct);

        Assert.Equal("Résumé – naïve test ✓", read["/Title"]);
        Assert.False(read.ContainsKey("/Author"));
        Assert.Equal(QpdfStructure.SetMetadata(Fixtures.SimpleText(), values), titled);
        using var document = (PdfiumDocument)_pdfium.Open(titled);
        Assert.Equal("Résumé – naïve test ✓", document.GetMetadata("Title"));
    }

    [Fact]
    public async Task Rewrite_MatchesInProcessOutput()
    {
        byte[] source = Fixtures.CanaryIncremental();

        Assert.Equal(QpdfStructure.Rewrite(source), await _client.RewriteAsync(source, cancellationToken: Ct));
    }

    [Fact]
    public async Task InvalidData_IsAStructureFailure_AndTheWorkerKeepsServing()
    {
        var error = await Assert.ThrowsAsync<WorkerException>(() => _client.RewriteAsync("not a pdf"u8.ToArray(), cancellationToken: Ct));

        Assert.Equal(WorkerErrorCode.StructureFailed, error.Code);
        Assert.StartsWith("12.4.", await _client.GetQpdfVersionAsync(Ct));
    }

    // Spike finding 13: an open cancelled after the worker had started it used to leave the
    // document open in the worker. When a cancel lands varies, so opens are cancelled at several
    // moments. The count is read after a round trip, by which time every late answer has arrived
    // and its document has been closed.
    [Fact]
    public async Task CancelledOpens_LeaveNoDocumentOpenInTheWorker()
    {
        byte[] data = Fixtures.LargeMixed(pageCount: 500);
        for (int i = 0; i < 30; i++)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            Task<RemoteDocument> open = _client.OpenAsync(data, password: null, cancel.Token);
            cancel.CancelAfter(TimeSpan.FromMilliseconds(i % 6));
            try
            {
                RemoteDocument opened = await open;
                await _client.CloseDocumentAsync(opened.Id);
            }
            catch (OperationCanceledException)
            {
                // Cancelled before or while the worker opened it.
            }
        }

        await _client.GetQpdfVersionAsync(Ct);

        Assert.Equal(0, await _client.CountDocumentsForTestAsync());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
