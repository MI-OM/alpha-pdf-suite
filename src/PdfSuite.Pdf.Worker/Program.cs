using System.Globalization;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Qpdf;
using PdfSuite.Pdf.Worker;

// The worker process: the only place where PDF documents are parsed (PRD §8).
//   --serve [--test-commands] [--memory-limit <bytes>]
//                               serve requests from the app on stdin/stdout; on Linux the
//                               worker caps its own memory first (LinuxMemoryLimit)
//   --self-test <file.pdf>      open a file and print its page count and qpdf's version (used by CI)

switch (args)
{
    case ["--serve", .. var options]:
        {
            int limitAt = Array.IndexOf(options, "--memory-limit");
            if (OperatingSystem.IsLinux() && limitAt >= 0 && limitAt + 1 < options.Length)
            {
                LinuxMemoryLimit.Apply(long.Parse(options[limitAt + 1], CultureInfo.InvariantCulture));
            }

            // stdout carries protocol frames only; diagnostics go to stderr.
            using Stream input = Console.OpenStandardInput();
            using Stream output = new BufferedStream(Console.OpenStandardOutput(), 1 << 16);
            return new WorkerHost(input, output, allowTestCommands: options.Contains("--test-commands")).Run();
        }

    case ["--self-test", var path]:
        return SelfTest(path);

    default:
        Console.Error.WriteLine("Usage: PdfSuite.Pdf.Worker --serve [--test-commands] [--memory-limit <bytes>] | --self-test <file.pdf>");
        return 2;
}

static int SelfTest(string path)
{
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
            $"{{\"pageCount\":{document.PageCount},\"firstPage\":{{\"width\":{size.Width},\"height\":{size.Height}}},\"qpdfVersion\":\"{QpdfStructure.LibraryVersion}\"}}"));
        return 0;
    }
    catch (PdfOpenException ex)
    {
        Console.Error.WriteLine($"Could not open '{path}': {ex.Message}");
        return 1;
    }
}
