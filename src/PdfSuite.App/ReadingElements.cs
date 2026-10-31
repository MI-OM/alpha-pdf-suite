using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.App;

/// <summary>
/// The accessible stand-in for one page: a group named "Page N of M" that holds one focusable
/// <see cref="ReadingLine"/> per text line. A page without text is itself focusable and says so,
/// so a screen-reader user learns that the page is, for example, an image-only scan.
/// Screen readers announce focus changes on every OS, so moving keyboard focus through these
/// elements reads the document without needing a text pattern (spike S2).
/// </summary>
internal sealed class PageElement : Control
{
    private readonly ReadingLine[] _lines;

    public PageElement(int pageIndex, int pageCount, IReadOnlyList<TextLine> lines, bool textUnreadable)
    {
        PageIndex = pageIndex;
        Label = (lines.Count, textUnreadable) switch
        {
            (_, true) => $"Page {pageIndex + 1} of {pageCount}, the text on this page could not be read",
            (0, _) => $"Page {pageIndex + 1} of {pageCount}, no text on this page",
            _ => $"Page {pageIndex + 1} of {pageCount}",
        };
        _lines = [.. lines.Select((line, index) => new ReadingLine(pageIndex, index, line))];
        foreach (ReadingLine line in _lines)
        {
            VisualChildren.Add(line);
            LogicalChildren.Add(line);
        }

        Focusable = lines.Count == 0;
    }

    public int PageIndex { get; }

    public string Label { get; }

    public IReadOnlyList<ReadingLine> Lines => _lines;

    /// <summary>DIPs per point at the current zoom; lines are laid out from their bounds in points.</summary>
    public double Scale { get; set; } = 1;

    public override void Render(DrawingContext context)
    {
        if (IsFocused)
        {
            ReadingLine.DrawFocus(context, new Rect(Bounds.Size).Deflate(4));
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (ReadingLine line in _lines)
        {
            line.Measure(Size.Infinity);
        }

        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (ReadingLine line in _lines)
        {
            TextLine bounds = line.Line;
            line.Arrange(new Rect(
                bounds.Left * Scale,
                bounds.Top * Scale,
                Math.Max(1, (bounds.Right - bounds.Left) * Scale),
                Math.Max(1, (bounds.Bottom - bounds.Top) * Scale)).Inflate(2));
        }

        return finalSize;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PageElementPeer(this);

    private sealed class PageElementPeer(PageElement owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetNameCore() => owner.Label;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}

/// <summary>One line of page text as a focusable, accessible element. Draws a highlight while focused.</summary>
internal sealed class ReadingLine : Control
{
    private static readonly IBrush FocusFill = new ImmutableSolidColorBrush(Color.FromArgb(0x30, 0x00, 0x67, 0xC0));
    private static readonly IPen FocusPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0)), 2);

    public ReadingLine(int pageIndex, int index, TextLine line)
    {
        PageIndex = pageIndex;
        Index = index;
        Line = line;
        Focusable = true;
    }

    public int PageIndex { get; }

    public int Index { get; }

    public TextLine Line { get; }

    public static void DrawFocus(DrawingContext context, Rect rect) =>
        context.DrawRectangle(FocusFill, FocusPen, rect, 3, 3);

    public override void Render(DrawingContext context)
    {
        if (IsFocused)
        {
            DrawFocus(context, new Rect(Bounds.Size));
        }
    }

    protected override void OnGotFocus(Avalonia.Input.FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(Avalonia.Input.FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ReadingLinePeer(this);

    private sealed class ReadingLinePeer(ReadingLine owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetNameCore() => owner.Line.Text;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
