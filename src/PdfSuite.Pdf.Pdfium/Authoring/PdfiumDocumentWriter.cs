using System.Runtime.InteropServices;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium.Native;

namespace PdfSuite.Pdf.Pdfium.Authoring;

public enum PdfSaveMode
{
    /// <summary>Rewrites the whole file. Unreferenced objects and earlier revisions are dropped.</summary>
    Full,

    /// <summary>Appends changes after the original bytes, which stay in the file.</summary>
    Incremental,
}

/// <summary>An image in 32-bit BGRA layout with no row padding (stride = Width * 4).</summary>
public sealed record BgraImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// Creates or edits documents with PDFium's editing API. The spike uses it to generate
/// test fixtures; the page-operation features will build on the same calls.
/// </summary>
public sealed unsafe class PdfiumDocumentWriter : IDisposable
{
    private nint _document;
    private void* _sourceBuffer;
    private nint _font;

    private PdfiumDocumentWriter(nint document, void* sourceBuffer)
    {
        _document = document;
        _sourceBuffer = sourceBuffer;
    }

    public static PdfiumDocumentWriter CreateNew()
    {
        PdfiumLibrary.EnsureInitialized();
        lock (PdfiumLibrary.Sync)
        {
            nint document = PdfiumNative.FPDF_CreateNewDocument();
            if (document == 0)
            {
                throw new InvalidOperationException("PDFium could not create a document.");
            }

            return new PdfiumDocumentWriter(document, null);
        }
    }

    /// <summary>Opens existing bytes for editing, for example to append an incremental update.</summary>
    public static PdfiumDocumentWriter Load(ReadOnlySpan<byte> data)
    {
        PdfiumLibrary.EnsureInitialized();

        // As in PdfiumEngine.Open, PDFium reads this buffer until the document is closed.
        void* buffer = NativeMemory.Alloc((nuint)Math.Max(data.Length, 1));
        data.CopyTo(new Span<byte>(buffer, data.Length));

        lock (PdfiumLibrary.Sync)
        {
            nint document = PdfiumNative.FPDF_LoadMemDocument64(buffer, (nuint)data.Length, null);
            if (document == 0)
            {
                PdfOpenError error = PdfiumLibrary.LastOpenError();
                NativeMemory.Free(buffer);
                throw new PdfOpenException(error);
            }

            return new PdfiumDocumentWriter(document, buffer);
        }
    }

    public int PageCount
    {
        get
        {
            lock (PdfiumLibrary.Sync)
            {
                return PdfiumNative.FPDF_GetPageCount(Handle);
            }
        }
    }

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_document == 0, this);
            return _document;
        }
    }

    /// <summary>The standard Helvetica font, loaded once per document. Caller holds Sync.</summary>
    internal nint StandardFont
    {
        get
        {
            if (_font == 0)
            {
                _font = PdfiumNative.FPDFText_LoadStandardFont(Handle, "Helvetica");
                if (_font == 0)
                {
                    throw new InvalidOperationException("PDFium could not load the Helvetica standard font.");
                }
            }

            return _font;
        }
    }

    /// <summary>Appends a page of the given size in PDF points.</summary>
    public PdfiumPageWriter AddPage(double width, double height)
    {
        lock (PdfiumLibrary.Sync)
        {
            int index = PdfiumNative.FPDF_GetPageCount(Handle);
            nint page = PdfiumNative.FPDFPage_New(Handle, index, width, height);
            if (page == 0)
            {
                throw new InvalidOperationException($"PDFium could not create page {index}.");
            }

            return new PdfiumPageWriter(this, page);
        }
    }

    public PdfiumPageWriter EditPage(int pageIndex)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint page = PdfiumNative.FPDF_LoadPage(Handle, pageIndex);
            if (page == 0)
            {
                throw new InvalidOperationException($"PDFium could not load page {pageIndex}.");
            }

            return new PdfiumPageWriter(this, page);
        }
    }

    /// <summary>Appends every page of <paramref name="source"/> to the end of this document.</summary>
    public void ImportAllPages(PdfiumDocumentWriter source)
    {
        lock (PdfiumLibrary.Sync)
        {
            int index = PdfiumNative.FPDF_GetPageCount(Handle);
            if (PdfiumNative.FPDF_ImportPages(Handle, source.Handle, null, index) == 0)
            {
                throw new InvalidOperationException("PDFium could not import the pages.");
            }
        }
    }

    /// <summary>
    /// Copies a page of <paramref name="source"/> into this document as a form XObject.
    /// Placing the result on several pages makes them share one content stream.
    /// </summary>
    public PdfiumXObject ImportPageAsXObject(PdfiumDocumentWriter source, int sourcePageIndex)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint xobject = PdfiumNative.FPDF_NewXObjectFromPage(Handle, source.Handle, sourcePageIndex);
            if (xobject == 0)
            {
                throw new InvalidOperationException($"PDFium could not import page {sourcePageIndex} as an XObject.");
            }

            return new PdfiumXObject(xobject);
        }
    }

    public void AddAttachment(string name, ReadOnlySpan<byte> contents)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint attachment;
            fixed (char* namePtr = name)
            {
                attachment = PdfiumNative.FPDFDoc_AddAttachment(Handle, namePtr);
            }

            if (attachment == 0)
            {
                throw new InvalidOperationException($"PDFium could not add the attachment '{name}'.");
            }

            fixed (byte* data = contents)
            {
                if (PdfiumNative.FPDFAttachment_SetFile(attachment, Handle, data, new CULong((uint)contents.Length)) == 0)
                {
                    throw new InvalidOperationException($"PDFium could not store the attachment '{name}'.");
                }
            }
        }
    }

    /// <summary>Renders a page, including annotations, onto a white background.</summary>
    public BgraImage RenderPage(int pageIndex, int pixelWidth, int pixelHeight)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint page = PdfiumNative.FPDF_LoadPage(Handle, pageIndex);
            if (page == 0)
            {
                throw new InvalidOperationException($"PDFium could not load page {pageIndex}.");
            }

            nint bitmap = PdfiumNative.FPDFBitmap_Create(pixelWidth, pixelHeight, 0);
            try
            {
                if (bitmap == 0)
                {
                    throw new InvalidOperationException("PDFium could not allocate the render bitmap.");
                }

                PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, pixelWidth, pixelHeight, new CULong(0xFFFFFFFF));
                PdfiumNative.FPDF_RenderPageBitmap(
                    bitmap, page, 0, 0, pixelWidth, pixelHeight, 0, PdfiumNative.FPDF_ANNOT);

                byte* source = (byte*)PdfiumNative.FPDFBitmap_GetBuffer(bitmap);
                int stride = PdfiumNative.FPDFBitmap_GetStride(bitmap);
                int rowBytes = pixelWidth * 4;
                var pixels = new byte[rowBytes * pixelHeight];
                for (int y = 0; y < pixelHeight; y++)
                {
                    new ReadOnlySpan<byte>(source + (y * stride), rowBytes).CopyTo(pixels.AsSpan(y * rowBytes));
                }

                return new BgraImage(pixelWidth, pixelHeight, pixels);
            }
            finally
            {
                if (bitmap != 0)
                {
                    PdfiumNative.FPDFBitmap_Destroy(bitmap);
                }

                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    public void Save(Stream output, PdfSaveMode mode = PdfSaveMode.Full)
    {
        uint flags = mode == PdfSaveMode.Incremental
            ? PdfiumNative.FPDF_INCREMENTAL
            : PdfiumNative.FPDF_NO_INCREMENTAL;

        lock (PdfiumLibrary.Sync)
        {
            PdfiumSaver.Save(Handle, output, flags);
        }
    }

    public byte[] ToArray(PdfSaveMode mode = PdfSaveMode.Full)
    {
        using var output = new MemoryStream();
        Save(output, mode);
        return output.ToArray();
    }

    public void Dispose()
    {
        lock (PdfiumLibrary.Sync)
        {
            if (_font != 0)
            {
                PdfiumNative.FPDFFont_Close(_font);
                _font = 0;
            }

            if (_document != 0)
            {
                PdfiumNative.FPDF_CloseDocument(_document);
                _document = 0;
            }

            if (_sourceBuffer != null)
            {
                NativeMemory.Free(_sourceBuffer);
                _sourceBuffer = null;
            }
        }
    }
}

/// <summary>A form XObject owned by a document. Disposing it keeps pages that already use it intact.</summary>
public sealed class PdfiumXObject : IDisposable
{
    private nint _xobject;

    internal PdfiumXObject(nint xobject) => _xobject = xobject;

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_xobject == 0, this);
            return _xobject;
        }
    }

    public void Dispose()
    {
        lock (PdfiumLibrary.Sync)
        {
            if (_xobject != 0)
            {
                PdfiumNative.FPDF_CloseXObject(_xobject);
                _xobject = 0;
            }
        }
    }
}
