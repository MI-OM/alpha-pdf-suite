using System.Text;

namespace PdfSuite.Pdf.Abstractions;

/// <summary>
/// A page's characters that a reader can see, in the engine's order, each with its box in points
/// from the page's top-left corner. Line breaks the engine detects are '\n' characters with an empty
/// box. Text in hidden layers is left out, as for search and the reading lines.
/// </summary>
public sealed record PageCharacters(string Text, IReadOnlyList<PageRect> Boxes)
{
    public static PageCharacters Empty { get; } = new(string.Empty, []);

    public int Count => Text.Length;
}

/// <summary>
/// Text selection over <see cref="PageCharacters"/>, independent of any UI. A caret is a position
/// between characters: 0 is before the first, <see cref="PageCharacters.Count"/> after the last.
/// A selection on a page is the characters from one caret up to another.
/// </summary>
public static class TextSelection
{
    /// <summary>
    /// The caret nearest a point on the page: before the nearest character if the point is on its
    /// left half, after it otherwise. The nearest line is chosen first, then the nearest character
    /// on it, so dragging past the end of a line selects to its end.
    /// </summary>
    public static int CaretAt(PageCharacters characters, double x, double y)
    {
        int best = -1;
        double bestScore = double.MaxValue;
        for (int i = 0; i < characters.Count; i++)
        {
            PageRect box = characters.Boxes[i];
            if (IsEmpty(box))
            {
                continue;
            }

            double dx = x < box.Left ? box.Left - x : x > box.Right ? x - box.Right : 0;
            double dy = y < box.Top ? box.Top - y : y > box.Bottom ? y - box.Bottom : 0;

            // Lines first: a character on the nearest line beats a closer one on another line.
            double score = (dy * 1000) + dx;
            if (score < bestScore)
            {
                best = i;
                bestScore = score;
            }
        }

        if (best < 0)
        {
            return 0;
        }

        PageRect nearest = characters.Boxes[best];
        return x < (nearest.Left + nearest.Right) / 2 ? best : best + 1;
    }

    /// <summary>The word around a character: letters, digits and joining punctuation, or the single character if it is not part of one.</summary>
    public static (int Start, int End) WordAt(PageCharacters characters, int index)
    {
        if (characters.Count == 0)
        {
            return (0, 0);
        }

        index = Math.Clamp(index, 0, characters.Count - 1);
        if (!IsWordCharacter(characters.Text[index]))
        {
            return (index, index + 1);
        }

        int start = index, end = index + 1;
        while (start > 0 && IsWordCharacter(characters.Text[start - 1]))
        {
            start--;
        }

        while (end < characters.Count && IsWordCharacter(characters.Text[end]))
        {
            end++;
        }

        return (start, end);
    }

    /// <summary>The selected text from caret <paramref name="start"/> to caret <paramref name="end"/>, with the engine's line breaks.</summary>
    public static string Extract(PageCharacters characters, int start, int end)
    {
        (start, end) = Ordered(characters, start, end);
        return characters.Text[start..end];
    }

    /// <summary>
    /// Rectangles covering the selected characters, one per run on a line, for drawing a highlight.
    /// Adjacent characters on the same line are joined, so the highlight has no gaps between them.
    /// </summary>
    public static IReadOnlyList<PageRect> Highlight(PageCharacters characters, int start, int end)
    {
        (start, end) = Ordered(characters, start, end);
        var rects = new List<PageRect>();
        PageRect? run = null;
        for (int i = start; i < end; i++)
        {
            PageRect box = characters.Boxes[i];
            if (characters.Text[i] == '\n' || IsEmpty(box))
            {
                if (characters.Text[i] == '\n' && run is PageRect ended)
                {
                    rects.Add(ended);
                    run = null;
                }

                continue;
            }

            if (run is PageRect current && SameLine(current, box))
            {
                run = new PageRect(Math.Min(current.Left, box.Left), Math.Min(current.Top, box.Top), Math.Max(current.Right, box.Right), Math.Max(current.Bottom, box.Bottom));
            }
            else
            {
                if (run is PageRect previous)
                {
                    rects.Add(previous);
                }

                run = box;
            }
        }

        if (run is PageRect last)
        {
            rects.Add(last);
        }

        return rects;
    }

    /// <summary>Joins the text of several pages' selections, one line break between pages.</summary>
    public static string JoinPages(IEnumerable<string> pages)
    {
        var builder = new StringBuilder();
        foreach (string page in pages)
        {
            if (builder.Length > 0 && builder[^1] != '\n')
            {
                builder.Append('\n');
            }

            builder.Append(page);
        }

        return builder.ToString();
    }

    private static (int Start, int End) Ordered(PageCharacters characters, int a, int b)
    {
        a = Math.Clamp(a, 0, characters.Count);
        b = Math.Clamp(b, 0, characters.Count);
        return a <= b ? (a, b) : (b, a);
    }

    private static bool IsEmpty(PageRect box) => box.Right <= box.Left && box.Bottom <= box.Top;

    // Boxes on one line overlap vertically by at least half the shorter one's height.
    private static bool SameLine(PageRect a, PageRect b)
    {
        double overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        double shorter = Math.Min(a.Bottom - a.Top, b.Bottom - b.Top);
        return overlap >= shorter / 2;
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '\'' or '’' or '-';
}
