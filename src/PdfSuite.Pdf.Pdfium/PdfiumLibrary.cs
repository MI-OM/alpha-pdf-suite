using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium.Native;

namespace PdfSuite.Pdf.Pdfium;

/// <summary>
/// Process-wide PDFium state. PDFium is not thread-safe, so every call into it is made
/// while holding <see cref="Sync"/>. In the product, PDFium runs only inside the worker
/// process (PRD §8); in-process use is limited to tools and tests.
/// </summary>
internal static class PdfiumLibrary
{
    internal static readonly object Sync = new();

    private static bool s_initialized;

    internal static void EnsureInitialized()
    {
        lock (Sync)
        {
            if (!s_initialized)
            {
                PdfiumNative.FPDF_InitLibrary();
                s_initialized = true;
            }
        }
    }

    internal static PdfOpenError LastOpenError() => (uint)PdfiumNative.FPDF_GetLastError().Value switch
    {
        PdfiumNative.FPDF_ERR_FILE => PdfOpenError.File,
        PdfiumNative.FPDF_ERR_FORMAT => PdfOpenError.Format,
        PdfiumNative.FPDF_ERR_PASSWORD => PdfOpenError.Password,
        PdfiumNative.FPDF_ERR_SECURITY => PdfOpenError.UnsupportedSecurity,
        _ => PdfOpenError.Unknown,
    };
}
