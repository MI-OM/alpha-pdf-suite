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

/// <summary>Works out which optional-content layers are off by default, from the document's bytes.</summary>
public delegate IReadOnlySet<string> HiddenLayerFinder(ReadOnlySpan<byte> pdf);

public sealed unsafe class PdfiumDocument : IPdfDocument
{
    // Loading a page parses its content, so recently used pages stay loaded. Tiles of the
    // same page arrive in bursts; a handful of pages covers a screenful at any zoom.
    private const int LoadedPageCapacity = 8;

    private readonly List<(int Index, nint Page)> _loadedPages = new(LoadedPageCapacity);
    private readonly int _length;
    private nint _document;
    private void* _buffer;
    private IReadOnlySet<string>? _hiddenLayers;

    internal PdfiumDocument(nint document, void* buffer, int length)
    {
        _document = document;
        _buffer = buffer;
        _length = length;
    }

    /// <summary>
    /// Finds the names of the layers that are off when the document opens. Text in those layers is
    /// left out of <see cref="GetTextLines"/>, so a screen reader does not read what a sighted user
    /// cannot see (spike finding 32). PDFium has no API for layer visibility, so the caller supplies
    /// this, for example by reading /OCProperties with qpdf. It runs at most once, and only when a
    /// text line meets a layer. Without it, every layer counts as visible. The other text APIs keep
    /// hidden text, because redaction must find it.
    /// </summary>
    public HiddenLayerFinder? FindHiddenLayers { get; set; }

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
                bool[]? hidden = FindHiddenCharacters(textPage, count);
                var lines = new List<TextLine>();
                int start = 0;
                for (int i = 0; i <= count; i++)
                {
                    if (i < count && text[i] is not ('\r' or '\n'))
                    {
                        continue;
                    }

                    if (i > start && VisibleLine(textPage, text, start, i, hidden, pageHeight) is { } line)
                    {
                        lines.Add(line);
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

    /// <summary>
    /// The runs of text whose characters sit inside <paramref name="area"/> (character centres
    /// inside the rectangle), split wherever a character outside it or a line break intervenes.
    /// Redaction uses this to learn what text an area covers before it is removed.
    /// </summary>
    public IReadOnlyList<string> GetTextInArea(int pageIndex, PageRect area)
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
                var runs = new List<string>();
                var current = new System.Text.StringBuilder();
                int count = PdfiumNative.FPDFText_CountChars(textPage);
                for (int i = 0; i <= count; i++)
                {
                    bool inside = false;
                    char c = '\0';
                    if (i < count)
                    {
                        c = (char)PdfiumNative.FPDFText_GetUnicode(textPage, i);
                        double left, right, bottom, top;
                        if (c is not ('\r' or '\n') && PdfiumNative.FPDFText_GetCharBox(textPage, i, &left, &right, &bottom, &top) != 0)
                        {
                            inside = area.Contains((left + right) / 2, pageHeight - ((top + bottom) / 2));
                        }
                    }

                    if (inside)
                    {
                        current.Append(c);
                    }
                    else if (current.Length > 0)
                    {
                        string run = current.ToString().Trim();
                        if (run.Count(ch => !char.IsWhiteSpace(ch)) >= 2)
                        {
                            runs.Add(run);
                        }

                        current.Clear();
                    }
                }

                return runs;
            }
            finally
            {
                PdfiumNative.FPDFText_ClosePage(textPage);
            }
        }
    }

    // A line made of the characters in [start, end) that are not hidden: usually all of them, as
    // one run. Hidden characters split the line into runs, whose text and bounds are joined.
    private static TextLine? VisibleLine(nint textPage, char[] text, int start, int end, bool[]? hidden, double pageHeight)
    {
        var visible = new System.Text.StringBuilder();
        (double Left, double Top, double Right, double Bottom)? bounds = null;
        int run = start;
        for (int k = start; k <= end; k++)
        {
            if (k < end && hidden?[k] != true)
            {
                continue;
            }

            if (k > run)
            {
                visible.Append(text, run, k - run);
                if (TryGetBounds(textPage, run, k - run, pageHeight) is { } b)
                {
                    bounds = bounds is { } u
                        ? (Math.Min(u.Left, b.Left), Math.Min(u.Top, b.Top), Math.Max(u.Right, b.Right), Math.Max(u.Bottom, b.Bottom))
                        : b;
                }
            }

            run = k + 1;
        }

        string lineText = visible.ToString().Trim();
        return lineText.Length > 0 && bounds is { } r ? new TextLine(lineText, r.Left, r.Top, r.Right, r.Bottom) : null;
    }

    // Which characters are in a layer that is off by default, or null when none are. Characters
    // PDFium generates, such as line breaks, have no text object and count as visible.
    private bool[]? FindHiddenCharacters(nint textPage, int count)
    {
        if (FindHiddenLayers is null)
        {
            return null;
        }

        bool[]? hidden = null;
        var byObject = new Dictionary<nint, bool>();
        for (int k = 0; k < count; k++)
        {
            nint textObject = PdfiumNative.FPDFText_GetTextObject(textPage, k);
            if (textObject == 0)
            {
                continue;
            }

            if (!byObject.TryGetValue(textObject, out bool isHidden))
            {
                isHidden = InHiddenLayer(textObject);
                byObject[textObject] = isHidden;
            }

            if (isHidden)
            {
                hidden ??= new bool[count];
                hidden[k] = true;
            }
        }

        return hidden;
    }

    // "/OC /Layer BDC ... EMC" gives the objects inside an "OC" mark whose parameters are the
    // layer's dictionary, so its /Name is readable. Not covered, and so counted as visible:
    // membership dictionaries (OCMDs), which have no /Name; form XObjects and images hidden by their
    // own /OC entry; and two layers that share a name, one of them on (the finder leaves such names out).
    private bool InHiddenLayer(nint pageObject)
    {
        int marks = PdfiumNative.FPDFPageObj_CountMarks(pageObject);
        for (int m = 0; m < marks; m++)
        {
            nint mark = PdfiumNative.FPDFPageObj_GetMark(pageObject, new CULong((uint)m));
            if (mark == 0 || MarkText(mark, key: null) != "OC" || MarkText(mark, key: "Name") is not { } layer)
            {
                continue;
            }

            _hiddenLayers ??= FindHiddenLayers!(new ReadOnlySpan<byte>(_buffer, _length));
            if (_hiddenLayers.Contains(layer))
            {
                return true;
            }
        }

        return false;
    }

    // The mark's name (key null) or one of its string parameters, or null if there is none.
    private static string? MarkText(nint mark, string? key)
    {
        CULong size;
        int found = key is null
            ? PdfiumNative.FPDFPageObjMark_GetName(mark, null, default, &size)
            : PdfiumNative.FPDFPageObjMark_GetParamStringValue(mark, key, null, default, &size);
        if (found == 0 || size.Value < 2)
        {
            return null;
        }

        var buffer = new char[(int)size.Value / 2];
        fixed (char* p = buffer)
        {
            found = key is null
                ? PdfiumNative.FPDFPageObjMark_GetName(mark, p, size, &size)
                : PdfiumNative.FPDFPageObjMark_GetParamStringValue(mark, key, p, size, &size);
        }

        return found == 0 ? null : new string(buffer).TrimEnd('\0');
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

    /// <summary>Counts the top-level page objects of every kind.</summary>
    public int CountObjects(int pageIndex)
    {
        lock (PdfiumLibrary.Sync)
        {
            ThrowIfInvalidPage(pageIndex);
            return PdfiumNative.FPDFPage_CountObjects(GetLoadedPage(pageIndex));
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

    /// <summary>A document information value such as "Title" or "Author"; empty if absent.</summary>
    public string GetMetadata(string tag)
    {
        lock (PdfiumLibrary.Sync)
        {
            ThrowIfDisposed();
            int needed = (int)PdfiumNative.FPDF_GetMetaText(_document, tag, null, new CULong(0)).Value;
            if (needed <= 2)
            {
                return string.Empty;
            }

            var buffer = new char[needed / 2];
            fixed (char* p = buffer)
            {
                PdfiumNative.FPDF_GetMetaText(_document, tag, p, new CULong((uint)needed));
            }

            return new string(buffer, 0, buffer.Length - 1);
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
