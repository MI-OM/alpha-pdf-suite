using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PdfSuite.Pdf.Pdfium.Native;

namespace PdfSuite.Pdf.Pdfium;

internal static unsafe class PdfiumSaver
{
    /// <summary>Writes the document to <paramref name="output"/>. Caller holds PdfiumLibrary.Sync.</summary>
    internal static void Save(nint document, Stream output, uint flags)
    {
        var state = new SaveState(output);
        var handle = GCHandle.Alloc(state);
        try
        {
            var fileWrite = new FPDF_FILEWRITE
            {
                Version = 1,
                WriteBlock = &WriteBlock,
                State = GCHandle.ToIntPtr(handle),
            };

            bool saved = PdfiumNative.FPDF_SaveAsCopy(document, &fileWrite, new CULong(flags)) != 0;

            // A write failure surfaces as a failed save; rethrow the original exception.
            if (state.Error is not null)
            {
                throw new IOException("Writing the PDF failed.", state.Error);
            }

            if (!saved)
            {
                throw new IOException("PDFium could not save the document.");
            }
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WriteBlock(FPDF_FILEWRITE* self, void* data, CULong size)
    {
        var state = (SaveState)GCHandle.FromIntPtr(self->State).Target!;
        try
        {
            state.Output.Write(new ReadOnlySpan<byte>(data, checked((int)size.Value)));
            return 1;
        }
        catch (Exception ex)
        {
            // Exceptions must not cross into native code. Save() rethrows this one.
            state.Error ??= ex;
            return 0;
        }
    }

    private sealed class SaveState(Stream output)
    {
        public Stream Output { get; } = output;

        public Exception? Error { get; set; }
    }
}
