namespace PdfSuite.Pdf.Abstractions;

/// <summary>
/// One entry of a document's outline (its bookmarks). <see cref="PageIndex"/> is where it goes in
/// this document, or null if it goes nowhere here: a link to a website or another file is listed,
/// never followed (UI copy rules §12). <see cref="Top"/> is the point on the page in points from
/// its top, if the document gives one.
/// </summary>
public sealed record OutlineItem(string Title, int? PageIndex, double? Top, IReadOnlyList<OutlineItem> Children)
{
    /// <summary>Entries read at most. A larger outline is cut short.</summary>
    public const int MaxItems = 10_000;

    /// <summary>Levels read at most. Deeper entries are left out.</summary>
    public const int MaxDepth = 32;
}
