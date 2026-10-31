using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;

namespace PdfSuite.Pdf.Worker;

/// <summary>
/// Prints pages on Windows by drawing them onto a printer device context with PDFium. The app
/// shows the Print dialog and passes the printer's name and settings (DEVMODE); the document is
/// parsed only here, in the worker. Pages are drawn as GDI commands rather than bitmaps.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class GdiPrinter
{
    private const int HORZRES = 8;
    private const int VERTRES = 10;
    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;
    private const int PHYSICALWIDTH = 110;
    private const int PHYSICALHEIGHT = 111;
    private const int PHYSICALOFFSETX = 112;
    private const int PHYSICALOFFSETY = 113;
    private const int ERROR_CANCELLED = 1223;

    /// <summary>
    /// Prints the pages, each centred on the paper at actual size, shrunk to fit the printable
    /// area if larger, and turned a quarter if its orientation differs from the paper's.
    /// </summary>
    /// <param name="devMode">The printer settings from the Print dialog, or empty for the printer's defaults.</param>
    /// <param name="outputFile">For print-to-file drivers; null to print normally.</param>
    /// <returns>The number of pages sent; 0 if the user cancelled when the job started.</returns>
    public static int Print(
        PdfiumDocument document, string printerName, ReadOnlySpan<byte> devMode, IReadOnlyList<int> pages, string title, string? outputFile)
    {
        nint dc;
        fixed (byte* settings = devMode)
        {
            dc = CreateDCW(null, printerName, null, devMode.IsEmpty ? null : settings);
        }

        if (dc == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not open the printer '{printerName}'.");
        }

        try
        {
            int started;
            fixed (char* name = title)
            fixed (char* output = outputFile)
            {
                var info = new DOCINFOW { cbSize = sizeof(DOCINFOW), lpszDocName = name, lpszOutput = output };
                started = StartDocW(dc, &info);
            }

            if (started <= 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == ERROR_CANCELLED)
                {
                    // The user cancelled, for example in a print-to-file driver's Save dialog.
                    return 0;
                }

                throw new Win32Exception(error, "The printer did not accept the document.");
            }

            try
            {
                foreach (int pageIndex in pages)
                {
                    if (StartPage(dc) <= 0)
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError(), "The printer did not accept a page.");
                    }

                    PrintPage(document, dc, pageIndex);
                    if (EndPage(dc) <= 0)
                    {
                        throw new Win32Exception(Marshal.GetLastPInvokeError(), "The printer did not finish a page.");
                    }
                }

                if (EndDoc(dc) <= 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "The printer did not finish the document.");
                }
            }
            catch
            {
                AbortDoc(dc);
                throw;
            }

            return pages.Count;
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    private static void PrintPage(PdfiumDocument document, nint dc, int pageIndex)
    {
        PageSize size = document.GetPageSize(pageIndex);
        int paperWidth = GetDeviceCaps(dc, PHYSICALWIDTH);
        int paperHeight = GetDeviceCaps(dc, PHYSICALHEIGHT);
        bool rotate = (size.Width > size.Height) != (paperWidth > paperHeight);
        double widthPoints = rotate ? size.Height : size.Width;
        double heightPoints = rotate ? size.Width : size.Height;

        double width = widthPoints * GetDeviceCaps(dc, LOGPIXELSX) / 72.0;
        double height = heightPoints * GetDeviceCaps(dc, LOGPIXELSY) / 72.0;
        double shrink = Math.Min(1, Math.Min(GetDeviceCaps(dc, HORZRES) / width, GetDeviceCaps(dc, VERTRES) / height));
        width *= shrink;
        height *= shrink;

        // Device coordinates start at the printable area's corner, not the paper's.
        int x = (int)Math.Round(((paperWidth - width) / 2) - GetDeviceCaps(dc, PHYSICALOFFSETX));
        int y = (int)Math.Round(((paperHeight - height) / 2) - GetDeviceCaps(dc, PHYSICALOFFSETY));
        document.RenderToDeviceContext(dc, pageIndex, x, y, (int)Math.Round(width), (int)Math.Round(height), rotate ? 1 : 0);
    }

    [LibraryImport("gdi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateDCW(string? driver, string device, string? port, void* devMode);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int StartDocW(nint dc, DOCINFOW* info);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int StartPage(nint dc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int EndPage(nint dc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int EndDoc(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial int AbortDoc(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDeviceCaps(nint dc, int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct DOCINFOW
    {
        public int cbSize;
        public char* lpszDocName;
        public char* lpszOutput;
        public char* lpszDatatype;
        public uint fwType;
    }
}
