using System.Runtime.InteropServices;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium.Native;

namespace PdfSuite.Pdf.Pdfium;

/// <summary>Page object types, matching PDFium's FPDF_PAGEOBJ_* values.</summary>
public enum PdfiumObjectKind
{
    Text = 1,
    Path = 2,
    Image = 3,
    Shading = 4,
    Form = 5,
}

public sealed unsafe class PdfiumDocument : IPdfDocument
{
    // Loading a page parses its content, so recently used pages stay loaded. Tiles of the
    // same page arrive in bursts; a handful of pages covers a screenful at any zoom.
    private const int LoadedPageCapacity = 8;

    private readonly List<(int Index, nint Page)> _loadedPages = new(LoadedPageCapacity);
    private nint _document;
    private void* _buffer;

    internal PdfiumDocument(nint document, void* buffer)
    {
        _document = document;
        _buffer = buffer;
    }

    public int PageCount
    {
        get
        {
            lock (PdfiumLibrary.Sync)
            {
                ThrowIfDisposed();
                return PdfiumNative.FPDF_GetPageCount(_document);
            }
        }
    }

    public PageSize GetPageSize(int pageIndex)
    {
        lock (PdfiumLibrary.Sync)
        {
            ThrowIfInvalidPage(pageIndex);
            FS_SIZEF size;
            if (PdfiumNative.FPDF_GetPageSizeByIndexF(_document, pageIndex, &size) == 0)
            {
                throw new InvalidOperationException($"PDFium could not read the size of page {pageIndex}.");
            }

            return new PageSize(size.Width, size.Height);
        }
    }

    public string GetPageText(int pageIndex)
    {
        lock (PdfiumLibrary.Sync)
        {
            ThrowIfInvalidPage(pageIndex);
            nint textPage = PdfiumNative.FPDFText_LoadPage(GetLoadedPage(pageIndex));
            if (textPage == 0)
            {
                throw new InvalidOperationException($"PDFium could not extract text from page {pageIndex}.");
            }

            try
            {
                int count = PdfiumNative.FPDFText_CountChars(textPage);
                if (count <= 0)
                {
                    return string.Empty;
                }

                // FPDFText_GetText writes a terminating NUL and returns the count including it.
                var buffer = new char[count + 1];
                fixed (char* p = buffer)
                {
                    int written = PdfiumNative.FPDFText_GetText(textPage, 0, count, p);
                    return new string(buffer, 0, Math.Max(written - 1, 0));
                }
            }
            finally
            {
                PdfiumNative.FPDFText_ClosePage(textPage);
            }
        }
    }

    public IReadOnlyList<TextLine> GetTextLines(int pageIndex)
    {
        lock (PdfiumLibrary.Sync)
        {
            double pageHeight = GetPageSize(pageIndex).Height;
            nint textPage = PdfiumNative.FPDFText_LoadPage(GetLoadedPage(pageIndex));
            if (textPage == 0)
            {
                throw new InvalidOperationException($"PDFium could not extract text from page {pageIndex}.");
            }

            try
            {
                int count = PdfiumNative.FPDFText_CountChars(textPage);
                if (count <= 0)
                {
                    return [];
                }

                var text = new char[count + 1];
                fixed (char* p = text)
                {
                    PdfiumNative.FPDFText_GetText(textPage, 0, count, p);
                }

                // PDFium separates the lines it detects with generated "\r\n" characters. Indexes
                // into this text are character indexes in the text page, which the rectangle calls use.
                // (A character outside the Basic Multilingual Plane would shift them; acceptable for the spike.)
                var lines = new List<TextLine>();
                int start = 0;
                for (int i = 0; i <= count; i++)
                {
                    if (i < count && text[i] is not ('\r' or '\n'))
                    {
                        continue;
                    }

                    if (i > start)
                    {
                        string lineText = new string(text, start, i - start).Trim();
                        if (lineText.Length > 0 && TryGetBounds(textPage, start, i - start, pageHeight) is { } bounds)
                        {
                            lines.Add(new TextLine(lineText, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
                        }
                    }

                    start = i + 1;
                }

                return lines;
            }
            finally
            {
                PdfiumNative.FPDFText_ClosePage(textPage);
            }
        }
    }

    /// <summary>Union of the text rectangles of a character range, converted to top-left-origin points.</summary>
    private static (double Left, double Top, double Right, double Bottom)? TryGetBounds(nint textPage, int start, int count, double pageHeight)
    {
        int rects = PdfiumNative.FPDFText_CountRects(textPage, start, count);
        double left = double.MaxValue, top = double.MinValue, right = double.MinValue, bottom = double.MaxValue;
        for (int r = 0; r < rects; r++)
        {
            double rl, rt, rr, rb;
            if (PdfiumNative.FPDFText_GetRect(textPage, r, &rl, &rt, &rr, &rb) == 0)
            {
                continue;
            }

            left = Math.Min(left, rl);
            right = Math.Max(right, rr);
            top = Math.Max(top, rt);
            bottom = Math.Min(bottom, rb);
        }

        // PDF space has its origin at the bottom-left; flip so callers measure from the top.
        return left <= right ? (left, pageHeight - top, right, pageHeight - bottom) : null;
    }

    public void RenderRegion(int pageIndex, double pixelsPerPoint, PixelRegion region, Span<byte> bgra)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerPoint);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(region.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(region.Height);
        int stride = region.Width * 4;
        ArgumentOutOfRangeException.ThrowIfLessThan(bgra.Length, stride * region.Height, nameof(bgra));

        lock (PdfiumLibrary.Sync)
        {
            var (pageWidth, pageHeight) = GetPageSize(pageIndex).ToPixels(pixelsPerPoint);
            nint page = GetLoadedPage(pageIndex);
            fixed (byte* pixels = bgra)
            {
                nint bitmap = PdfiumNative.FPDFBitmap_CreateEx(
                    region.Width, region.Height, PdfiumNative.FPDFBitmap_BGRA, pixels, stride);
                if (bitmap == 0)
                {
                    throw new InvalidOperationException("PDFium could not wrap the render buffer.");
                }

                try
                {
                    // Rendering the whole scaled page offset by the region's origin makes
                    // PDFium draw only the part that falls inside the bitmap.
                    PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, region.Width, region.Height, new CULong(0xFFFFFFFF));
                    PdfiumNative.FPDF_RenderPageBitmap(
                        bitmap, page, -region.Left, -region.Top, pageWidth, pageHeight, 0, PdfiumNative.FPDF_ANNOT);
                }
                finally
                {
                    PdfiumNative.FPDFBitmap_Destroy(bitmap);
                }
            }
        }
    }

    /// <summary>
    /// Draws a page onto a Windows device context, such as a printer's, as GDI drawing commands.
    /// The rectangle is in device units; rotate is in quarter turns clockwise.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void RenderToDeviceContext(nint deviceContext, int pageIndex, int x, int y, int width, int height, int rotate)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint page = GetLoadedPage(pageIndex);
            PdfiumNative.FPDF_RenderPage(
                deviceContext, page, x, y, width, height, rotate, PdfiumNative.FPDF_ANNOT | PdfiumNative.FPDF_PRINTING);
        }
    }

    /// <summary>Counts the top-level page objects of one kind. Used by fixture tests.</summary>
    public int CountObjects(int pageIndex, PdfiumObjectKind kind)
    {
        lock (PdfiumLibrary.Sync)
        {
            ThrowIfInvalidPage(pageIndex);
            nint page = GetLoadedPage(pageIndex);
            int matches = 0;
            int total = PdfiumNative.FPDFPage_CountObjects(page);
            for (int i = 0; i < total; i++)
            {
                if (PdfiumNative.FPDFPageObj_GetType(PdfiumNative.FPDFPage_GetObject(page, i)) == (int)kind)
                {
                    matches++;
                }
            }

            return matches;
        }
    }

    public int CountAnnotations(int pageIndex)
    {
        lock (PdfiumLibrary.Sync)
        {
            ThrowIfInvalidPage(pageIndex);
            return PdfiumNative.FPDFPage_GetAnnotCount(GetLoadedPage(pageIndex));
        }
    }

    public int AttachmentCount
    {
        get
        {
            lock (PdfiumLibrary.Sync)
            {
                ThrowIfDisposed();
                return PdfiumNative.FPDFDoc_GetAttachmentCount(_document);
            }
        }
    }

    public void Dispose()
    {
        lock (PdfiumLibrary.Sync)
        {
            foreach (var (_, page) in _loadedPages)
            {
                PdfiumNative.FPDF_ClosePage(page);
            }

            _loadedPages.Clear();

            if (_document != 0)
            {
                PdfiumNative.FPDF_CloseDocument(_document);
                _document = 0;
            }

            if (_buffer != null)
            {
                NativeMemory.Free(_buffer);
                _buffer = null;
            }
        }
    }

    /// <summary>Returns a loaded page, most recently used last. Caller holds Sync.</summary>
    private nint GetLoadedPage(int pageIndex)
    {
        ThrowIfInvalidPage(pageIndex);
        int found = _loadedPages.FindIndex(p => p.Index == pageIndex);
        if (found >= 0)
        {
            var entry = _loadedPages[found];
            _loadedPages.RemoveAt(found);
            _loadedPages.Add(entry);
            return entry.Page;
        }

        nint page = PdfiumNative.FPDF_LoadPage(_document, pageIndex);
        if (page == 0)
        {
            throw new InvalidOperationException($"PDFium could not load page {pageIndex}.");
        }

        if (_loadedPages.Count == LoadedPageCapacity)
        {
            PdfiumNative.FPDF_ClosePage(_loadedPages[0].Page);
            _loadedPages.RemoveAt(0);
        }

        _loadedPages.Add((pageIndex, page));
        return page;
    }

    private void ThrowIfInvalidPage(int pageIndex)
    {
        ThrowIfDisposed();
        int count = PdfiumNative.FPDF_GetPageCount(_document);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, count);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_document == 0, this);
}
