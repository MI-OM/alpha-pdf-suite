using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Qpdf;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>Document properties (roadmap Milestone 1): what a document says about itself, through the engine and the worker.</summary>
public sealed class DocumentInfoTests : IDisposable
{
    private readonly PdfiumEngine _engine = new();
    private readonly WorkerClient _client = WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()));

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task ReadsMetadata_IncludingNonAsciiText_TheSameThroughTheWorker()
    {
        const string title = "Résumé – naïve test ✓";
        byte[] data = QpdfStructure.SetMetadata(Fixtures.SimpleText(), new Dictionary<string, string?>
        {
            ["/Title"] = title,
            ["/Author"] = "PDF Suite",
            ["/CreationDate"] = "D:20261107143000+01'00'",
        });

        using IPdfDocument local = _engine.Open(data);
        DocumentInfo info = local.GetDocumentInfo();
        Assert.Equal(title, info.Metadata["Title"]);
        Assert.Equal("PDF Suite", info.Metadata["Author"]);
        Assert.False(info.Metadata.ContainsKey("Subject"));
        Assert.Matches(@"^\d\.\d$", info.PdfVersion);
        Assert.False(info.Encrypted);
        Assert.Equal(DocumentInfo.AllPermitted, info.Permissions);
        Assert.False(info.Tagged);

        RemoteDocument remote = await _client.OpenAsync(data, password: null, Ct);
        DocumentInfo viaWorker = await _client.GetDocumentInfoAsync(remote.Id, Ct);
        Assert.Equal(info.Metadata, viaWorker.Metadata);
        Assert.Equal((info.PdfVersion, info.Encrypted, info.Permissions, info.Tagged),
            (viaWorker.PdfVersion, viaWorker.Encrypted, viaWorker.Permissions, viaWorker.Tagged));

        Assert.True(PdfDate.TryParse(viaWorker.Metadata["CreationDate"], out DateTimeOffset created));
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 14, 30, 0, TimeSpan.FromHours(1)), created);
    }

    // ISO 32000-1 §14.8.1: a tagged PDF says so with /MarkInfo << /Marked true >>. A structure tree
    // alone, as in the document-level canary fixture, does not make it one.
    [Fact]
    public void ADocumentIsTagged_OnlyIfItsCatalogSaysSo()
    {
        byte[] structureOnly = Fixtures.CanaryDocumentLevel();
        using (IPdfDocument document = _engine.Open(structureOnly))
        {
            Assert.False(document.GetDocumentInfo().Tagged);
        }

        byte[] marked;
        using (var edit = QpdfEdit.Open(structureOnly))
        {
            QpdfObject markInfo = edit.NewDictionary();
            edit.Set(markInfo, "/Marked", edit.NewBoolean(true));
            edit.Set(edit.Root, "/MarkInfo", markInfo);
            marked = edit.Save();
        }

        using IPdfDocument tagged = _engine.Open(marked);
        Assert.True(tagged.GetDocumentInfo().Tagged);
    }

    // Decisions for review, D1: the app honours "copy text". A user password of "" opens the
    // document without asking, with the restricted permissions; the owner password lifts them.
    [Fact]
    public async Task ReadsTheRestrictedPermissions_AndTheOwnerPasswordLiftsThem()
    {
        byte[] restricted = QpdfStructure.Encrypt(Fixtures.SimpleText(), string.Empty, "owner-pass", new PdfPermissions(CopyText: false));

        RemoteDocument asUser = await _client.OpenAsync(restricted, password: null, Ct);
        DocumentInfo user = await _client.GetDocumentInfoAsync(asUser.Id, Ct);
        Assert.True(user.Encrypted);
        Assert.False(user.Permissions.CopyText);
        Assert.True(user.Permissions.Print);
        Assert.False(user.Permissions.Modify);

        RemoteDocument asOwner = await _client.OpenAsync(restricted, "owner-pass", Ct);
        DocumentInfo owner = await _client.GetDocumentInfoAsync(asOwner.Id, Ct);
        Assert.True(owner.Encrypted);
        Assert.True(owner.Permissions.CopyText);
        Assert.True(owner.Permissions.Modify);
    }

    [Theory]
    [InlineData("D:20261107143000+01'00'", 2026, 11, 7, 14, 30, 0, 60)]
    [InlineData("D:20261107143000-05'30'", 2026, 11, 7, 14, 30, 0, -330)]
    [InlineData("D:20261107143000Z", 2026, 11, 7, 14, 30, 0, 0)]
    [InlineData("D:20261107143000", 2026, 11, 7, 14, 30, 0, 0)]
    [InlineData("D:2026", 2026, 1, 1, 0, 0, 0, 0)]
    [InlineData("20261107", 2026, 11, 7, 0, 0, 0, 0)]
    [InlineData("D:20261107143000+01", 2026, 11, 7, 14, 30, 0, 60)]
    public void PdfDate_ParsesTheStandardForms(string text, int year, int month, int day, int hour, int minute, int second, int offsetMinutes)
    {
        Assert.True(PdfDate.TryParse(text, out DateTimeOffset value));
        Assert.Equal(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromMinutes(offsetMinutes)), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("D:20261340")]
    [InlineData("D:20260230")]
    [InlineData("D:20261107250000")]
    [InlineData("D:20261107143000+15'00'")]
    public void PdfDate_RejectsAnythingElse(string? text)
    {
        Assert.False(PdfDate.TryParse(text, out _));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
