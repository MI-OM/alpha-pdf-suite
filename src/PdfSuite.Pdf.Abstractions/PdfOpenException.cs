namespace PdfSuite.Pdf.Abstractions;

public enum PdfOpenError
{
    Unknown,
    File,
    Format,
    Password,
    UnsupportedSecurity,
}

public sealed class PdfOpenException : Exception
{
    public PdfOpenException(PdfOpenError error)
        : base(DescribeError(error))
    {
        Error = error;
    }

    public PdfOpenError Error { get; }

    private static string DescribeError(PdfOpenError error) => error switch
    {
        PdfOpenError.File => "The file could not be read.",
        PdfOpenError.Format => "The data is not a valid PDF document, or it is damaged.",
        PdfOpenError.Password => "The document needs a password, or the password is wrong.",
        PdfOpenError.UnsupportedSecurity => "The document uses an unsupported security handler.",
        _ => "The document could not be opened.",
    };
}
