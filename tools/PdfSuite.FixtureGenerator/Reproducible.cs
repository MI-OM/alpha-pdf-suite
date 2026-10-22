using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PdfSuite.FixtureGenerator;

/// <summary>
/// Makes fixture bytes reproducible. PDFium stamps new documents and embedded files with the
/// current time (/CreationDate) and every saved file with a random identifier (/ID), and has
/// no API to set either. Both values have a fixed length, so they are overwritten in place:
/// no byte moves, and the cross-reference offsets stay valid.
/// </summary>
internal static partial class Reproducible
{
    private const string FixedDate = "20000101000000";

    [GeneratedRegex(@"/CreationDate\(D:(\d{14})")]
    private static partial Regex CreationDate();

    [GeneratedRegex(@"/ID\s*\[\s*<([0-9A-Fa-f]{32})>\s*<([0-9A-Fa-f]{32})>\s*\]")]
    private static partial Regex FileId();

    public static byte[] Normalize(byte[] pdf)
    {
        // Latin-1 maps each byte to exactly one char and back, so offsets are preserved.
        string original = Encoding.Latin1.GetString(pdf);
        char[] text = original.ToCharArray();

        foreach (Match match in CreationDate().Matches(original))
        {
            FixedDate.CopyTo(0, text, match.Groups[1].Index, FixedDate.Length);
        }

        List<Group> ids = FileId().Matches(original).SelectMany(m => new[] { m.Groups[1], m.Groups[2] }).ToList();
        foreach (Group id in ids)
        {
            new string('0', 32).CopyTo(0, text, id.Index, 32);
        }

        // Each distinct identifier gets a stable replacement derived from the file's content,
        // so the permanent ID that all revisions of an incremental file share stays shared.
        string contentHash = Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(text)));
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Group id in ids)
        {
            if (!replacements.TryGetValue(id.Value, out string? replacement))
            {
                byte[] seed = Encoding.ASCII.GetBytes($"{contentHash}:{replacements.Count}");
                replacement = Convert.ToHexString(SHA256.HashData(seed))[..32];
                replacements.Add(id.Value, replacement);
            }

            replacement.CopyTo(0, text, id.Index, 32);
        }

        return Encoding.Latin1.GetBytes(text);
    }
}
