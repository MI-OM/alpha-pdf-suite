using PdfSuite.Pdf.Pdfium.Native;

namespace PdfSuite.Pdf.Pdfium.Authoring;

/// <summary>
/// Adds content to one page. Coordinates are PDF points with the origin at the bottom-left.
/// Changes reach the page's content stream on <see cref="Commit"/> or <see cref="Dispose"/>.
/// </summary>
public sealed unsafe class PdfiumPageWriter : IDisposable
{
    private readonly PdfiumDocumentWriter _owner;
    private nint _page;
    private bool _dirty;

    internal PdfiumPageWriter(PdfiumDocumentWriter owner, nint page)
    {
        _owner = owner;
        _page = page;
    }

    public int ObjectCount
    {
        get
        {
            lock (PdfiumLibrary.Sync)
            {
                return PdfiumNative.FPDFPage_CountObjects(Page);
            }
        }
    }

    private nint Page
    {
        get
        {
            ObjectDisposedException.ThrowIf(_page == 0, this);
            return _page;
        }
    }

    /// <summary>Adds one line of Helvetica text with its baseline starting at (x, y).</summary>
    public void AddText(string text, double x, double y, float fontSize)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint textObject = PdfiumNative.FPDFPageObj_CreateTextObj(_owner.Handle, _owner.StandardFont, fontSize);
            if (textObject == 0)
            {
                throw new InvalidOperationException("PDFium could not create a text object.");
            }

            fixed (char* value = text)
            {
                if (PdfiumNative.FPDFText_SetText(textObject, value) == 0)
                {
                    PdfiumNative.FPDFPageObj_Destroy(textObject);
                    throw new InvalidOperationException("PDFium could not set the text of a text object.");
                }
            }

            PdfiumNative.FPDFPageObj_Transform(textObject, 1, 0, 0, 1, x, y);
            Insert(textObject);
        }
    }

    public void AddRectangle(double x, double y, double width, double height, byte red, byte green, byte blue)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint path = PdfiumNative.FPDFPageObj_CreateNewRect((float)x, (float)y, (float)width, (float)height);
            if (path == 0)
            {
                throw new InvalidOperationException("PDFium could not create a rectangle.");
            }

            if (PdfiumNative.FPDFPageObj_SetFillColor(path, red, green, blue, 255) == 0
                || PdfiumNative.FPDFPath_SetDrawMode(path, PdfiumNative.FPDF_FILLMODE_ALTERNATE, 0) == 0)
            {
                PdfiumNative.FPDFPageObj_Destroy(path);
                throw new InvalidOperationException("PDFium could not style a rectangle.");
            }

            Insert(path);
        }
    }

    /// <summary>Places an image, scaled to fill the rectangle (x, y, width, height). Alpha is ignored.</summary>
    public void AddImage(BgraImage image, double x, double y, double width, double height)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint imageObject = PdfiumNative.FPDFPageObj_NewImageObj(_owner.Handle);
            if (imageObject == 0)
            {
                throw new InvalidOperationException("PDFium could not create an image object.");
            }

            nint bitmap = PdfiumNative.FPDFBitmap_Create(image.Width, image.Height, 0);
            try
            {
                if (bitmap == 0)
                {
                    throw new InvalidOperationException("PDFium could not allocate an image bitmap.");
                }

                byte* target = (byte*)PdfiumNative.FPDFBitmap_GetBuffer(bitmap);
                int stride = PdfiumNative.FPDFBitmap_GetStride(bitmap);
                int rowBytes = image.Width * 4;
                for (int row = 0; row < image.Height; row++)
                {
                    image.Pixels.AsSpan(row * rowBytes, rowBytes).CopyTo(new Span<byte>(target + (row * stride), rowBytes));
                }

                if (PdfiumNative.FPDFImageObj_SetBitmap(null, 0, imageObject, bitmap) == 0
                    || PdfiumNative.FPDFImageObj_SetMatrix(imageObject, width, 0, 0, height, x, y) == 0)
                {
                    throw new InvalidOperationException("PDFium could not set the image data.");
                }
            }
            catch
            {
                PdfiumNative.FPDFPageObj_Destroy(imageObject);
                throw;
            }
            finally
            {
                if (bitmap != 0)
                {
                    PdfiumNative.FPDFBitmap_Destroy(bitmap);
                }
            }

            Insert(imageObject);
        }
    }

    /// <summary>Places a form XObject with its origin at (x, y).</summary>
    public void AddFormXObject(PdfiumXObject xobject, double x, double y, double scale = 1)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint formObject = PdfiumNative.FPDF_NewFormObjectFromXObject(xobject.Handle);
            if (formObject == 0)
            {
                throw new InvalidOperationException("PDFium could not create a form object.");
            }

            PdfiumNative.FPDFPageObj_Transform(formObject, scale, 0, 0, scale, x, y);
            Insert(formObject);
        }
    }

    /// <summary>Adds a sticky-note (Text) annotation whose comment is <paramref name="contents"/>.</summary>
    public void AddNoteAnnotation(string contents, double left, double bottom, double right, double top)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint annotation = PdfiumNative.FPDFPage_CreateAnnot(Page, PdfiumNative.FPDF_ANNOT_TEXT);
            if (annotation == 0)
            {
                throw new InvalidOperationException("PDFium could not create an annotation.");
            }

            try
            {
                var rect = new FS_RECTF { Left = (float)left, Top = (float)top, Right = (float)right, Bottom = (float)bottom };
                bool ok = PdfiumNative.FPDFAnnot_SetRect(annotation, &rect) != 0;
                fixed (char* value = contents)
                {
                    ok &= PdfiumNative.FPDFAnnot_SetStringValue(annotation, "Contents", value) != 0;
                }

                if (!ok)
                {
                    throw new InvalidOperationException("PDFium could not set the annotation's rectangle or contents.");
                }
            }
            finally
            {
                PdfiumNative.FPDFPage_CloseAnnot(annotation);
            }
        }
    }

    public void RemoveObjectAt(int index)
    {
        lock (PdfiumLibrary.Sync)
        {
            nint pageObject = PdfiumNative.FPDFPage_GetObject(Page, index);
            if (pageObject == 0 || PdfiumNative.FPDFPage_RemoveObject(Page, pageObject) == 0)
            {
                throw new InvalidOperationException($"PDFium could not remove page object {index}.");
            }

            // After removal the caller owns the object.
            PdfiumNative.FPDFPageObj_Destroy(pageObject);
            _dirty = true;
        }
    }

    /// <summary>Regenerates the page's content stream from its page objects.</summary>
    public void Commit()
    {
        lock (PdfiumLibrary.Sync)
        {
            if (_dirty)
            {
                if (PdfiumNative.FPDFPage_GenerateContent(Page) == 0)
                {
                    throw new InvalidOperationException("PDFium could not generate the page content.");
                }

                _dirty = false;
            }
        }
    }

    public void Dispose()
    {
        lock (PdfiumLibrary.Sync)
        {
            if (_page == 0)
            {
                return;
            }

            try
            {
                Commit();
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(_page);
                _page = 0;
            }
        }
    }

    private void Insert(nint pageObject)
    {
        PdfiumNative.FPDFPage_InsertObject(Page, pageObject);
        _dirty = true;
    }
}
