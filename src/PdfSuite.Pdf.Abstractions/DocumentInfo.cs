using System.Globalization;

namespace PdfSuite.Pdf.Abstractions;

/// <summary>
/// What a document says about itself, for the Document Properties view. <see cref="Metadata"/>
/// holds the document information keys it has (Title, Author, Subject, Keywords, Creator,
/// Producer, CreationDate, ModDate). <see cref="Permissions"/> are what this reader may do as the
/// document was opened: everything for an unencrypted document, or one opened with its owner password.
/// </summary>
public sealed record DocumentInfo(
    IReadOnlyDictionary<string, string> Metadata,
    string? PdfVersion,
    bool Encrypted,
    PdfPermissions Permissions,
    bool Tagged)
{
    /// <summary>The document information keys read, in the order they are shown.</summary>
    public static IReadOnlyList<string> Keys { get; } =
        ["Title", "Author", "Subject", "Keywords", "Creator", "Producer", "CreationDate", "ModDate"];

    public static PdfPermissions AllPermitted { get; } = new(Print: true, CopyText: true, Modify: true, AnnotateAndFillForms: true, Assemble: true);

    /// <summary>
    /// Reads the user access permissions of a standard security handler (ISO 32000-1 Table 22): bit 3
    /// print, 4 modify, 5 copy text and graphics, 6 annotate and fill forms, 11 assemble.
    /// </summary>
    public static PdfPermissions FromPermissionBits(uint bits) => new(
        Print: (bits & (1u << 2)) != 0,
        CopyText: (bits & (1u << 4)) != 0,
        Modify: (bits & (1u << 3)) != 0,
        AnnotateAndFillForms: (bits & (1u << 5)) != 0,
        Assemble: (bits & (1u << 10)) != 0);
}

/// <summary>Dates in PDF's own format (ISO 32000-1 §7.9.4), such as D:20261107143000+01'00'.</summary>
public static class PdfDate
{
    /// <summary>
    /// Parses a PDF date. Everything after the year is optional, as the standard allows; a date
    /// without a time zone is taken as UTC. Returns false for anything else.
    /// </summary>
    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> s = text.AsSpan().Trim();
        if (s.StartsWith("D:", StringComparison.Ordinal))
        {
            s = s[2..];
        }

        int i = 0;
        if (!Number(s, ref i, 4, out int year))
        {
            return false;
        }

        int month = 1, day = 1, hour = 0, minute = 0, second = 0;
        _ = Number(s, ref i, 2, out month, 1) && Number(s, ref i, 2, out day, 1) && Number(s, ref i, 2, out hour, 0)
            && Number(s, ref i, 2, out minute, 0) && Number(s, ref i, 2, out second, 0);

        TimeSpan offset = TimeSpan.Zero;
        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            int sign = s[i] == '-' ? -1 : 1;
            i++;
            if (!Number(s, ref i, 2, out int offsetHours))
            {
                return false;
            }

            if (i < s.Length && s[i] == '\'')
            {
                i++;
            }

            Number(s, ref i, 2, out int offsetMinutes, 0);
            offset = sign * new TimeSpan(offsetHours, offsetMinutes, 0);
        }

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(Math.Clamp(year, 1, 9999), month)
            || hour > 23 || minute > 59 || second > 59 || offset.Duration() > TimeSpan.FromHours(14) || year is < 1 or > 9999)
        {
            return false;
        }

        value = new DateTimeOffset(year, month, day, hour, minute, second, offset);
        return true;
    }

    private static bool Number(ReadOnlySpan<char> s, ref int i, int digits, out int value, int fallback = -1)
    {
        if (i + digits <= s.Length && int.TryParse(s.Slice(i, digits), NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            i += digits;
            return true;
        }

        value = fallback;
        return false;
    }
}
