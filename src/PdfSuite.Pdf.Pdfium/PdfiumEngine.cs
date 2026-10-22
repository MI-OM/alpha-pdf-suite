using System.Runtime.InteropServices;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium.Native;

namespace PdfSuite.Pdf.Pdfium;

public sealed unsafe class PdfiumEngine : IPdfEngine
{
    public IPdfDocument Open(ReadOnlySpan<byte> data, string? password = null)
    {
        PdfiumLibrary.EnsureInitialized();

        // PDFium reads from this buffer until the document is closed, so it lives in
        // native memory that neither moves nor is freed before PdfiumDocument.Dispose.
        void* buffer = NativeMemory.Alloc((nuint)Math.Max(data.Length, 1));
        data.CopyTo(new Span<byte>(buffer, data.Length));

        lock (PdfiumLibrary.Sync)
        {
            nint document = PdfiumNative.FPDF_LoadMemDocument64(buffer, (nuint)data.Length, password);
            if (document == 0)
            {
                PdfOpenError error = PdfiumLibrary.LastOpenError();
                NativeMemory.Free(buffer);
                throw new PdfOpenException(error);
            }

            return new PdfiumDocument(document, buffer);
        }
    }
}
