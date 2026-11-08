using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Text selection (roadmap Milestone 1, "select text where supported"): the characters and boxes the
/// worker sends, and the selection logic the view uses on them.
/// </summary>
public sealed class TextSelectionTests : IDisposable
{
    private readonly PdfiumEngine _engine = new();
    private readonly WorkerClient _client = WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()));

    public void Dispose() => _client.Dispose();

    // "CANARY-01" is set at 12 pt from x = 180 with its baseline 292 pt from the top (see FixtureTests).
    [Fact]
    public void Characters_HaveTheirTextAndBoxes()
    {
        using IPdfDocument document = _engine.Open(Fixtures.CanaryText());
        PageCharacters characters = document.GetCharacters(0);

        Assert.Equal(characters.Text.Length, characters.Boxes.Count);
        Assert.Contains('\n', characters.Text);
        int c = characters.Text.IndexOf("CANARY-01", StringComparison.Ordinal);
        Assert.True(c >= 0, "The canary text is missing.");
        PageRect box = characters.Boxes[c];
        Assert.InRange(box.Left, 178, 182);
        Assert.InRange(box.Top, 276, 286);
        Assert.InRange(box.Bottom, 292, 298);

        // Loose boxes: every character on the line has the same height.
        PageRect last = characters.Boxes[c + 8];
        Assert.Equal(box.Top, last.Top, 0.01);
        Assert.Equal(box.Bottom, last.Bottom, 0.01);
    }

    [Fact]
    public async Task Characters_AreTheSameThroughTheWorker()
    {
        byte[] data = Fixtures.SimpleText();
        using IPdfDocument local = _engine.Open(data);
        RemoteDocument remote = await _client.OpenAsync(data, password: null, Ct);

        PageCharacters expected = local.GetCharacters(1);
        PageCharacters actual = await _client.GetCharactersAsync(remote.Id, 1, Ct);

        Assert.Equal(expected.Text, actual.Text);
        Assert.Equal(expected.Boxes, actual.Boxes);
    }

    // Like search and the reading lines (finding 32): text a sighted reader cannot see cannot be
    // selected or copied.
    [Fact]
    public async Task Characters_LeaveOutTextInAHiddenLayer()
    {
        byte[] data = Fixtures.CanaryDocumentLevel();
        RemoteDocument remote = await _client.OpenAsync(data, password: null, Ct);
        Assert.DoesNotContain("CANARY-13", (await _client.GetCharactersAsync(remote.Id, 0, Ct)).Text, StringComparison.Ordinal);

        // Negative control: without the worker's layer information the engine has it.
        using IPdfDocument local = _engine.Open(data);
        Assert.Contains("CANARY-13", local.GetCharacters(0).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void DraggingAcrossAWord_SelectsIt_AndPastTheLineEnd_SelectsToTheEnd()
    {
        using IPdfDocument document = _engine.Open(Fixtures.CanaryText());
        PageCharacters characters = document.GetCharacters(0);
        int c = characters.Text.IndexOf("CANARY-01", StringComparison.Ordinal);
        PageRect first = characters.Boxes[c], end = characters.Boxes[c + 8];
        double middle = (first.Top + first.Bottom) / 2;

        int start = TextSelection.CaretAt(characters, first.Left + 0.5, middle);
        int stop = TextSelection.CaretAt(characters, end.Right - 0.5, middle);
        Assert.Equal("CANARY-01", TextSelection.Extract(characters, start, stop));

        // Backwards drags select the same text.
        Assert.Equal("CANARY-01", TextSelection.Extract(characters, stop, start));

        // Far to the right of the line, on the same line: up to the line's end, not into the next line.
        int beyond = TextSelection.CaretAt(characters, end.Right + 200, middle);
        string toEnd = TextSelection.Extract(characters, start, beyond);
        Assert.StartsWith("CANARY-01", toEnd, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', toEnd);
    }

    [Fact]
    public void DoubleClick_SelectsAWholeWord_JoinedByHyphens()
    {
        using IPdfDocument document = _engine.Open(Fixtures.CanaryText());
        PageCharacters characters = document.GetCharacters(0);
        int c = characters.Text.IndexOf("CANARY-01", StringComparison.Ordinal);

        var (start, end) = TextSelection.WordAt(characters, c + 3);
        Assert.Equal("CANARY-01", TextSelection.Extract(characters, start, end));

        // Not part of a word: just that character.
        int space = characters.Text.IndexOf(' ');
        Assert.Equal((space, space + 1), TextSelection.WordAt(characters, space));
    }

    [Fact]
    public void ASelectionOverTwoLines_IsHighlightedLineByLine_AndCopiedWithALineBreak()
    {
        using IPdfDocument document = _engine.Open(Fixtures.SimpleText());
        PageCharacters characters = document.GetCharacters(0);
        int firstBreak = characters.Text.IndexOf('\n');
        int secondBreak = characters.Text.IndexOf('\n', firstBreak + 1);

        // From the middle of line 1 to the middle of line 2.
        int start = firstBreak / 2, end = firstBreak + 1 + ((secondBreak - firstBreak) / 2);
        IReadOnlyList<PageRect> rects = TextSelection.Highlight(characters, start, end);
        string text = TextSelection.Extract(characters, start, end);

        Assert.Equal(2, rects.Count);
        Assert.True(rects[0].Bottom <= rects[1].Top + 0.01, "The second line's highlight should be below the first's.");
        Assert.Equal(1, text.Count(ch => ch == '\n'));
    }

    [Fact]
    public void CaretAt_ChoosesTheNearestLine_ThenTheNearestCharacter()
    {
        // Two lines: "ab" at the top, "cd" below, each character 10 pt wide.
        var characters = new PageCharacters("ab\ncd",
        [
            new PageRect(0, 0, 10, 10), new PageRect(10, 0, 20, 10), default,
            new PageRect(0, 20, 10, 30), new PageRect(10, 20, 20, 30),
        ]);

        Assert.Equal(0, TextSelection.CaretAt(characters, 2, 5));     // left half of 'a'
        Assert.Equal(1, TextSelection.CaretAt(characters, 8, 5));     // right half of 'a'
        Assert.Equal(2, TextSelection.CaretAt(characters, 90, 5));    // far right of line 1: after 'b'
        Assert.Equal(5, TextSelection.CaretAt(characters, 90, 26));   // far right of line 2: the end
        Assert.Equal(3, TextSelection.CaretAt(characters, -50, 24));  // far left of line 2: before 'c'
        Assert.Equal(0, TextSelection.CaretAt(PageCharacters.Empty, 5, 5));
        Assert.Equal("a\nc", TextSelection.JoinPages(["a", "c"]));
        Assert.Equal("a\nc", TextSelection.JoinPages(["a\n", "c"]));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
