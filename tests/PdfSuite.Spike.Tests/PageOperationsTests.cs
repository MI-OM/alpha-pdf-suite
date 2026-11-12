using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Qpdf;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Page operations through qpdf (roadmap Milestone 3: "page order, dimensions, orientation, and
/// supported metadata are validated"). Every result is checked with PDFium, an independent reader.
/// </summary>
public sealed class PageOperationsTests
{
    private readonly PdfiumEngine _pdfium = new();

    [Fact]
    public void Rotating_TurnsThePage_AndAddsToAnExistingRotation()
    {
        byte[] once = QpdfPages.Rebuild([new(Fixtures.SimpleText())], [new(0, 0), new(0, 1, QuarterTurns: 1), new(0, 2, QuarterTurns: -1)]);
        using (IPdfDocument document = _pdfium.Open(once))
        {
            Assert.Equal(new PageSize(612, 792), document.GetPageSize(0));
            Assert.Equal(new PageSize(792, 612), document.GetPageSize(1));
            Assert.Equal(new PageSize(792, 612), document.GetPageSize(2));
        }

        // A second quarter turn on the turned page makes it upright again, upside down.
        byte[] twice = QpdfPages.Rebuild([new(once)], [new(0, 1, QuarterTurns: 1)]);
        using IPdfDocument turned = _pdfium.Open(twice);
        Assert.Equal(new PageSize(612, 792), turned.GetPageSize(0));
    }

    [Fact]
    public void Deleting_Reordering_AndExtracting_KeepThePagesAsked()
    {
        byte[] source = Fixtures.SimpleText();

        Assert.Equal(["Simple text page 3", "Simple text page 2", "Simple text page 1"], Headings(QpdfPages.Rebuild([new(source)], [new(0, 2), new(0, 1), new(0, 0)])));
        Assert.Equal(["Simple text page 1", "Simple text page 3"], Headings(QpdfPages.Rebuild([new(source)], [new(0, 0), new(0, 2)])));
        Assert.Equal(["Simple text page 2"], Headings(QpdfPages.Rebuild([new(source)], [new(0, 1)])));
    }

    [Fact]
    public void Duplicating_APage_CopiesItsAnnotationsToo()
    {
        byte[] result = QpdfPages.Rebuild([new(Fixtures.CanaryAnnotation())], [new(0, 0), new(0, 0)]);

        using var document = (PdfiumDocument)_pdfium.Open(result);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(document.GetPageText(0), document.GetPageText(1));
        Assert.Equal(1, document.CountAnnotations(0));
        Assert.Equal(1, document.CountAnnotations(1));
    }

    [Fact]
    public void Inserting_PagesFromAnotherDocument_AndABlankPage()
    {
        byte[] result = QpdfPages.Rebuild(
            [new(Fixtures.SimpleText()), new(Fixtures.CanaryText())],
            [new(0, 0), new(1, 0), PageSpec.Blank(new PageSize(595, 842)), new(0, 1)]);

        using IPdfDocument document = _pdfium.Open(result);
        Assert.Equal(4, document.PageCount);
        Assert.Contains("CANARY-01", document.GetPageText(1), StringComparison.Ordinal);
        Assert.Equal(new PageSize(595, 842), document.GetPageSize(2));
        Assert.Empty(document.GetTextLines(2));
        Assert.Contains("Simple text page 2", document.GetPageText(3), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstDocumentsInformation_IsKept()
    {
        byte[] titled = QpdfStructure.SetMetadata(Fixtures.SimpleText(), new Dictionary<string, string?> { ["/Title"] = "Kept title" });
        byte[] result = QpdfPages.Rebuild([new(titled)], [new(0, 1)]);
        Assert.Equal("Kept title", QpdfStructure.GetMetadata(result, ["/Title"])["/Title"]);
    }

    [Fact]
    public void AnEncryptedDocument_StaysEncrypted()
    {
        byte[] encrypted = QpdfStructure.Encrypt(Fixtures.SimpleText(), "user-pass", "owner-pass", new PdfPermissions(Assemble: true));
        byte[] result = QpdfPages.Rebuild([new(encrypted, "user-pass")], [new(0, 2), new(0, 0)]);

        Assert.True(QpdfStructure.IsEncrypted(result));
        Assert.Equal(PdfOpenError.Password, Assert.Throws<PdfOpenException>(() => _pdfium.Open(result)).Error);
        using IPdfDocument document = _pdfium.Open(result, "user-pass");
        Assert.Contains("Simple text page 3", document.GetPageText(0), StringComparison.Ordinal);
    }

    // Like copying (decisions for review, D1): a document that forbids assembly is not changed,
    // unless it was opened with its owner password.
    [Fact]
    public void ADocumentThatForbidsAssembly_IsRefused_UnlessOpenedWithItsOwnerPassword()
    {
        byte[] restricted = QpdfStructure.Encrypt(Fixtures.SimpleText(), string.Empty, "owner-pass", new PdfPermissions(Assemble: false));

        var refused = Assert.Throws<PageAssemblyNotAllowedException>(() => QpdfPages.Rebuild([new(restricted)], [new(0, 0)]));
        Assert.Equal(0, refused.SourceIndex);

        // The owner password alone is not enough: the caller must say it checked it (the worker does).
        Assert.Throws<PageAssemblyNotAllowedException>(() => QpdfPages.Rebuild([new(restricted, "owner-pass")], [new(0, 0)]));
        byte[] allowed = QpdfPages.Rebuild([new(restricted, "owner-pass", OwnerAccess: true)], [new(0, 0)]);
        using IPdfDocument document = _pdfium.Open(allowed, "owner-pass");
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void ABookmarkToADeletedPage_GoesNowhere_RatherThanBreakingTheOutline()
    {
        // The document-level canary fixture's bookmark points at page 1.
        byte[] result = QpdfPages.Rebuild([new(Fixtures.CanaryDocumentLevel())], [new(0, 1)]);

        using IPdfDocument document = _pdfium.Open(result);
        Assert.Equal(1, document.PageCount);
        OutlineItem bookmark = Assert.Single(document.GetOutline());
        Assert.Null(bookmark.PageIndex);
    }

    [Fact]
    public void ImpossibleRequests_AreRejected()
    {
        byte[] source = Fixtures.SimpleText();
        Assert.Throws<ArgumentException>(() => QpdfPages.Rebuild([new(source)], []));
        Assert.Throws<ArgumentException>(() => QpdfPages.Rebuild([new(source)], [new(0, 3)]));
        Assert.Throws<ArgumentException>(() => QpdfPages.Rebuild([new(source)], [new(1, 0)]));
        Assert.Throws<ArgumentException>(() => QpdfPages.Rebuild([], [new(0, 0)]));
    }

    [Fact]
    public async Task ThroughTheWorker_TheOwnerPasswordIsRecognised_AndTheUsersIsNot()
    {
        using var client = WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()));
        byte[] restricted = QpdfStructure.Encrypt(Fixtures.SimpleText(), string.Empty, "owner-pass", new PdfPermissions(Assemble: false));

        var refused = await Assert.ThrowsAsync<PageAssemblyNotAllowedException>(
            () => client.RebuildPagesAsync([new(restricted)], [new(0, 0)], Ct));
        Assert.Equal(0, refused.SourceIndex);

        byte[] result = await client.RebuildPagesAsync([new(restricted, "owner-pass")], [new(0, 2), new(0, 0, QuarterTurns: 1)], Ct);
        using IPdfDocument document = _pdfium.Open(result, "owner-pass");
        Assert.Equal(2, document.PageCount);
        Assert.Equal(new PageSize(792, 612), document.GetPageSize(1));
    }

    [Fact]
    public async Task ASession_RebuildsItsOwnDocument_WithItsPassword_AndOtherDocumentsPages()
    {
        byte[] encrypted = QpdfStructure.Encrypt(Fixtures.SimpleText(), "user-pass", "owner-pass", new PdfPermissions(Assemble: true));
        using DocumentSession session = await DocumentSession.OpenAsync(
            () => WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath())), encrypted, "user-pass", Ct);

        byte[] result = await session.RebuildPagesAsync([new(0, 1), new(1, 0)], [new(Fixtures.CanaryText())], Ct);

        Assert.True(QpdfStructure.IsEncrypted(result));
        using IPdfDocument document = _pdfium.Open(result, "user-pass");
        Assert.Contains("Simple text page 2", document.GetPageText(0), StringComparison.Ordinal);
        Assert.Contains("CANARY-01", document.GetPageText(1), StringComparison.Ordinal);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string[] Headings(byte[] pdf)
    {
        using IPdfDocument document = _pdfium.Open(pdf);
        return [.. Enumerable.Range(0, document.PageCount).Select(i => document.GetTextLines(i)[0].Text.Trim())];
    }
}
