namespace PdfSuite.Pdf.Abstractions;

/// <summary>
/// Opens PDF documents. Each implementation wraps one engine, and engine-specific
/// types must not appear in this interface or in the application layers above it.
/// </summary>
public interface IPdfEngine
{
    /// <summary>Opens a document from its bytes. The engine keeps its own copy of the data.</summary>
    /// <exception cref="PdfOpenException">The document could not be opened.</exception>
    IPdfDocument Open(ReadOnlySpan<byte> data, string? password = null);
}

/// <summary>An open document. Dispose it to release the engine's resources.</summary>
public interface IPdfDocument : IDisposable
{
    int PageCount { get; }

    PageSize GetPageSize(int pageIndex);

    /// <summary>Returns the page's text as the engine extracts it, in content order.</summary>
    string GetPageText(int pageIndex);

    /// <summary>
    /// Returns the page's text as lines with each line's bounds, in the order the engine extracts
    /// them. For PDFium that is content-stream order, which is not always visual reading order.
    /// Empty for a page with no text, such as a scan without OCR.
    /// </summary>
    IReadOnlyList<TextLine> GetTextLines(int pageIndex);

    /// <summary>
    /// Renders one region of a page, with annotations, as opaque 32-bit BGRA pixels
    /// (stride = width * 4). The page is scaled to <see cref="PageSize.ToPixels"/> at
    /// <paramref name="pixelsPerPoint"/>; the region is measured in those pixels from
    /// the scaled page's top-left corner.
    /// </summary>
    void RenderRegion(int pageIndex, double pixelsPerPoint, PixelRegion region, Span<byte> bgra);
}

/// <summary>Page width and height in PDF points (1/72 inch), as reported by the engine.</summary>
public readonly record struct PageSize(double Width, double Height)
{
    /// <summary>
    /// The page's size in whole pixels at a scale. Renderers and viewers both use this, so
    /// a tile grid always matches what the engine draws.
    /// </summary>
    public (int Width, int Height) ToPixels(double pixelsPerPoint) =>
        ((int)Math.Ceiling(Width * pixelsPerPoint), (int)Math.Ceiling(Height * pixelsPerPoint));
}

/// <summary>A rectangle in pixels of a scaled page, origin at its top-left corner.</summary>
public readonly record struct PixelRegion(int Left, int Top, int Width, int Height);

/// <summary>One line of page text and its bounds in points, measured from the page's top-left corner.</summary>
public sealed record TextLine(string Text, double Left, double Top, double Right, double Bottom);
