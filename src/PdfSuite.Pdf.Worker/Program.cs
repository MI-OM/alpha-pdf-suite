using System.Globalization;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;

// Spike item S0: proves that PDFium loads and opens a document inside a separate
// process on each OS. Spike item S1 replaces this with the worker's request loop.

if (args is not ["--self-test", var path])
{
    Console.Error.WriteLine("Usage: PdfSuite.Pdf.Worker --self-test <file.pdf>");
    return 2;
}

byte[] data;
try
{
    data = File.ReadAllBytes(path);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Could not read '{path}': {ex.Message}");
    return 1;
}

try
{
    using IPdfDocument document = new PdfiumEngine().Open(data);
    PageSize size = document.GetPageSize(0);
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"{{\"pageCount\":{document.PageCount},\"firstPage\":{{\"width\":{size.Width},\"height\":{size.Height}}}}}"));
    return 0;
}
catch (PdfOpenException ex)
{
    Console.Error.WriteLine($"Could not open '{path}': {ex.Message}");
    return 1;
}
