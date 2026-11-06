using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>Text search on one page, in the engine and through the worker (roadmap Milestone 1).</summary>
public sealed class SearchTests : IDisposable
{
    private readonly PdfiumEngine _engine = new();
    private readonly WorkerClient _client = WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()));

    public void Dispose() => _client.Dispose();

    // "CANARY-01" is set at 12 pt from x = 180 with its baseline 292 pt from the top (see FixtureTests).
    [Fact]
    public void FindText_ReturnsTheMatchAndWhereItIs()
    {
        using IPdfDocument document = _engine.Open(Fixtures.CanaryText());

        TextMatch match = Assert.Single(document.FindText(0, "canary-01"));

        Assert.Equal("CANARY-01", match.Text);
        PageRect bounds = Assert.Single(match.Bounds);
        Assert.InRange(bounds.Left, 178, 184);
        Assert.InRange(bounds.Right, 240, 260);
        Assert.InRange(bounds.Top, 280, 292);
        Assert.InRange(bounds.Bottom, 290, 298);
    }

    [Fact]
    public void FindText_HonoursMatchCaseAndWholeWord()
    {
        using IPdfDocument document = _engine.Open(Fixtures.SimpleText());

        Assert.Single(document.FindText(0, "Simple text page 1"));
        Assert.Empty(document.FindText(0, "simple TEXT", matchCase: true));
        Assert.NotEmpty(document.FindText(0, "simple TEXT"));
        Assert.Empty(document.FindText(0, "tex", wholeWord: true));
        Assert.NotEmpty(document.FindText(0, "tex"));
    }

    [Fact]
    public async Task FindTextAsync_MatchesTheEngine()
    {
        byte[] data = Fixtures.SimpleText();
        using IPdfDocument local = _engine.Open(data);
        RemoteDocument remote = await _client.OpenAsync(data, password: null, Ct);

        IReadOnlyList<TextMatch> expected = local.FindText(1, "e");
        IReadOnlyList<TextMatch> actual = await _client.FindTextAsync(remote.Id, 1, "e", cancellationToken: Ct);

        Assert.True(expected.Count > 10, $"Expected many matches; found {expected.Count}.");
        Assert.Equal(expected.Select(m => m.Text), actual.Select(m => m.Text));
        Assert.Equal(expected.SelectMany(m => m.Bounds), actual.SelectMany(m => m.Bounds));
    }

    // Like the reading lines (finding 32): the worker does not find text a sighted reader cannot see.
    [Fact]
    public async Task FindTextAsync_DoesNotMatchTextInAHiddenLayer()
    {
        byte[] data = Fixtures.CanaryDocumentLevel();
        RemoteDocument remote = await _client.OpenAsync(data, password: null, Ct);

        Assert.Empty(await _client.FindTextAsync(remote.Id, 0, "CANARY-13", cancellationToken: Ct));

        // Negative control: without the worker's layer information the engine finds it.
        using IPdfDocument local = _engine.Open(data);
        Assert.Single(local.FindText(0, "CANARY-13"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
