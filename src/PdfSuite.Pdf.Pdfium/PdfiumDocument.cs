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
            nint page = LoadPage(pageIndex);
            nint textPage = PdfiumNative.FPDFText_LoadPage(page);
            try
            {
                if (textPage == 0)
                {
                    throw new InvalidOperationException($"PDFium could not extract text from page {pageIndex}.");
                }

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
                if (textPage != 0)
                {
                    PdfiumNative.FPDFText_ClosePage(textPage);
                }

                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    /// <summary>Counts the top-level page objects of one kind. Used by fixture tests.</summary>
    public int CountObjects(int pageIndex, PdfiumObjectKind kind)
    {
        lock (PdfiumLibrary.Sync)
        {
            ThrowIfInvalidPage(pageIndex);
            nint page = LoadPage(pageIndex);
            try
            {
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
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    public int CountAnnotations(int pageIndex)
    {
        lock (PdfiumLibrary.Sync)
        {
            ThrowIfInvalidPage(pageIndex);
            nint page = LoadPage(pageIndex);
            try
            {
                return PdfiumNative.FPDFPage_GetAnnotCount(page);
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
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

    private nint LoadPage(int pageIndex)
    {
        nint page = PdfiumNative.FPDF_LoadPage(_document, pageIndex);
        if (page == 0)
        {
            throw new InvalidOperationException($"PDFium could not load page {pageIndex}.");
        }

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
