namespace PdfSuite.Pdf.Abstractions;

/// <summary>
/// Opens PDF documents. Each implementation wraps one engine, and engine-specific
/// types must not appear in this interface or in the application layers above it.
/// </summary>
public interface IPdfEngine
{
    /// <summary>Opens a document from its bytes. The engine keeps its own copy of the data.</summary>
    /// <exception cref="PdfOpenException">The document could not be opened.</exception>
    IPdfDocument Open(ReadOnlySpan<byte> data, string? password = null);
}

/// <summary>An open document. Dispose it to release the engine's resources.</summary>
public interface IPdfDocument : IDisposable
{
    int PageCount { get; }

    PageSize GetPageSize(int pageIndex);

    /// <summary>Returns the page's text as the engine extracts it, in content order.</summary>
    string GetPageText(int pageIndex);
}

/// <summary>Page width and height in PDF points (1/72 inch), as reported by the engine.</summary>
public readonly record struct PageSize(double Width, double Height);
