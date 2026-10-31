using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.App;

/// <summary>
/// The standard Windows Print dialog (comdlg32 PrintDlgW). It returns the chosen printer, its
/// settings as a DEVMODE (which carries copies and collation), and the page range. The worker
/// does the printing; the app never parses the document.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsPrintDialog
{
    private const uint PD_PAGENUMS = 0x00000002;
    private const uint PD_NOSELECTION = 0x00000004;
    private const uint PD_USEDEVMODECOPIESANDCOLLATE = 0x00040000;

    /// <returns>The job, or null if the user cancelled.</returns>
    public static PrintJob? Show(nint owner, int pageCount, int currentPage, string title)
    {
        ushort lastPage = (ushort)Math.Min(pageCount, ushort.MaxValue);
        var dialog = new PRINTDLGW
        {
            lStructSize = (uint)sizeof(PRINTDLGW),
            hwndOwner = owner,
            Flags = PD_NOSELECTION | PD_USEDEVMODECOPIESANDCOLLATE,
            nFromPage = (ushort)Math.Min(currentPage + 1, lastPage),
            nToPage = (ushort)Math.Min(currentPage + 1, lastPage),
            nMinPage = 1,
            nMaxPage = lastPage,
            nCopies = 1,
        };

        if (!PrintDlgW(&dialog))
        {
            uint error = CommDlgExtendedError();
            if (error != 0)
            {
                throw new Win32Exception((int)error, "The Print dialog failed.");
            }

            return null;
        }

        try
        {
            string printer = ReadPrinterName(dialog.hDevNames);
            byte[] settings = ReadDevMode(dialog.hDevMode);
            int first = 0, last = pageCount - 1;
            if ((dialog.Flags & PD_PAGENUMS) != 0)
            {
                first = Math.Clamp(dialog.nFromPage - 1, 0, pageCount - 1);
                last = Math.Clamp(dialog.nToPage - 1, first, pageCount - 1);
            }

            return new PrintJob(printer, settings, Enumerable.Range(first, last - first + 1).ToArray(), title);
        }
        finally
        {
            GlobalFree(dialog.hDevMode);
            GlobalFree(dialog.hDevNames);
        }
    }

    // DEVNAMES holds character offsets: wDriverOffset, wDeviceOffset, wOutputOffset, wDefault.
    private static string ReadPrinterName(nint devNames)
    {
        byte* block = (byte*)GlobalLock(devNames);
        try
        {
            ushort deviceOffset = ((ushort*)block)[1];
            return new string((char*)block + deviceOffset);
        }
        finally
        {
            GlobalUnlock(devNames);
        }
    }

    // DEVMODEW: dmSize at byte 68 and dmDriverExtra at byte 70; the driver's private data follows.
    private static byte[] ReadDevMode(nint devMode)
    {
        if (devMode == 0)
        {
            return [];
        }

        byte* block = (byte*)GlobalLock(devMode);
        try
        {
            int length = *(ushort*)(block + 68) + *(ushort*)(block + 70);
            return new ReadOnlySpan<byte>(block, length).ToArray();
        }
        finally
        {
            GlobalUnlock(devMode);
        }
    }

    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintDlgW(PRINTDLGW* dialog);

    [LibraryImport("comdlg32.dll")]
    private static partial uint CommDlgExtendedError();

    [LibraryImport("kernel32.dll")]
    private static partial void* GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);

    // 64-bit layout (natural alignment), which is what x64 and arm64 Windows use.
    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTDLGW
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hDevMode;
        public nint hDevNames;
        public nint hDC;
        public uint Flags;
        public ushort nFromPage;
        public ushort nToPage;
        public ushort nMinPage;
        public ushort nMaxPage;
        public ushort nCopies;
        public nint hInstance;
        public nint lCustData;
        public nint lpfnPrintHook;
        public nint lpfnSetupHook;
        public nint lpPrintTemplateName;
        public nint lpSetupTemplateName;
        public nint hPrintTemplate;
        public nint hSetupTemplate;
    }
}
