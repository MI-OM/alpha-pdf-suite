using System.Text;

namespace PdfSuite.Pdf.Qpdf;

/// <summary>
/// Searches raw PDF bytes for a piece of text in each form a PDF may hold it. Use it on the output
/// of an inspectable <see cref="QpdfStructure.Rewrite"/>, where streams are decoded.
/// </summary>
/// <remarks>
/// A plain byte search is not enough. PDFium, for one, writes text as hex strings in TJ arrays
/// (<c>[&lt;43414E4152592D3031&gt;] TJ</c> for "CANARY-01"), so searching for the literal text
/// would never find it, and a leak check built on that would always pass. This covers the literal,
/// hex in either case, and UTF-16BE (literal and hex). It does not decode font encodings that remap
/// character codes, which S5's verifier must handle separately with text extraction.
/// </remarks>
public static class PdfText
{
    public static bool Contains(ReadOnlySpan<byte> pdf, string text)
    {
        string bytes = Encoding.Latin1.GetString(pdf);
        byte[] ascii = Encoding.Latin1.GetBytes(text);
        byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        string[] forms =
        [
            text,
            Convert.ToHexString(ascii),
            Encoding.Latin1.GetString(utf16),
            Convert.ToHexString(utf16),
        ];

        return forms.Any(form => bytes.Contains(form, StringComparison.OrdinalIgnoreCase));
    }
}
