using System.Text;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Qpdf;

namespace PdfSuite.Pdf.Redaction;

/// <summary>
/// Checks a redacted file against its source without trusting how it was made (spike S5). It
/// reads the output with both PDFium and qpdf: PDFium for what a reader shows (page text, objects,
/// rendering), qpdf for what the file holds (every object, including unreferenced ones, every
/// string, and every stream). Negative controls call it directly to show each check catches the
/// mistake it is for.
/// </summary>
/// <remarks>
/// The secrets it looks for are the source's text under each area, plus any extra terms. A secret
/// that also appears on a page that was not redacted is expected in the output, so it is reported
/// as a warning and not checked. Text drawn as pixels or vector art cannot be found this way; for
/// that, the areas must render black and the redacted pages must be nothing but an image.
/// </remarks>
public static class RedactionVerifier
{
    private const double CoverageCheckPixelsPerPoint = 2;

    public static VerificationReport Verify(
        ReadOnlySpan<byte> source, ReadOnlySpan<byte> output, IReadOnlyList<RedactionArea> areas, IReadOnlyList<string>? extraTerms = null)
    {
        var failures = new List<VerificationFailure>();
        var warnings = new List<string>();
        void Fail(RedactionCheck check, string message) => failures.Add(new(check, message));

        CheckSingleRevision(output, Fail);

        var engine = new PdfiumEngine();
        using var before = (PdfiumDocument)engine.Open(source);
        using var after = (PdfiumDocument)engine.Open(output);
        HashSet<int> redactedPages = [.. areas.Select(a => a.PageIndex)];

        if (after.PageCount != before.PageCount)
        {
            Fail(RedactionCheck.PagesMatchSource, $"The output has {after.PageCount} pages; the source has {before.PageCount}.");
        }
        else
        {
            for (int page = 0; page < before.PageCount; page++)
            {
                PageSize expected = before.GetPageSize(page), actual = after.GetPageSize(page);
                if (Math.Abs(expected.Width - actual.Width) > 0.01 || Math.Abs(expected.Height - actual.Height) > 0.01)
                {
                    Fail(RedactionCheck.PagesMatchSource, $"Page {page + 1} is {actual.Width:0.##} x {actual.Height:0.##} pt; in the source it is {expected.Width:0.##} x {expected.Height:0.##} pt.");
                }
            }
        }

        foreach (int page in redactedPages.Where(p => p < after.PageCount).Order())
        {
            int objects = after.CountObjects(page);
            int images = after.CountObjects(page, PdfiumObjectKind.Image);
            int annotations = after.CountAnnotations(page);
            if (objects != 1 || images != 1 || annotations != 0)
            {
                Fail(RedactionCheck.RedactedPagesAreImages, $"Redacted page {page + 1} has {objects} objects ({images} images) and {annotations} annotations; it should be one image.");
            }
        }

        FileContents contents = InspectStructure(output, Fail);
        CheckSecrets(before, after, redactedPages, contents, Secrets(before, areas, extraTerms), Fail, warnings);
        CheckCoverage(after, areas, Fail);

        return new VerificationReport(failures, warnings);
    }

    // Every full save ends with one %%EOF; each incremental update adds another. Anything after the
    // last one is ignored by readers but still in the file.
    private static void CheckSingleRevision(ReadOnlySpan<byte> output, Action<RedactionCheck, string> fail)
    {
        int markers = 0, end = -1;
        for (int at = output.IndexOf("%%EOF"u8); at >= 0;)
        {
            markers++;
            end = at + 5;
            int next = output[end..].IndexOf("%%EOF"u8);
            at = next < 0 ? -1 : end + next;
        }

        if (markers != 1)
        {
            fail(RedactionCheck.SingleRevision, $"The file has {markers} end-of-file markers; a single revision has one.");
        }

        if (end >= 0 && output[end..].IndexOfAnyExcept(" \t\r\n\0"u8) >= 0)
        {
            fail(RedactionCheck.SingleRevision, $"The file has {output.Length - end} bytes after its last end-of-file marker.");
        }
    }

    private static FileContents InspectStructure(ReadOnlySpan<byte> output, Action<RedactionCheck, string> fail)
    {
        using var edit = QpdfEdit.OpenEverything(output);
        if (edit.Has(edit.Trailer, "/Info"))
        {
            fail(RedactionCheck.NoDocumentLevelContent, "The file has document information (/Info).");
        }

        List<string> catalog = [.. edit.Keys(edit.Root).Except(RedactionPolicy.CatalogKeys)];
        if (catalog.Count > 0)
        {
            fail(RedactionCheck.NoDocumentLevelContent, $"The catalog has {string.Join(", ", catalog)}.");
        }

        List<QpdfObject> pages = [.. Enumerable.Range(0, edit.PageCount).Select(edit.Page)];
        for (int i = 0; i < pages.Count; i++)
        {
            List<string> keys = [.. RedactionPolicy.PageKeysRemoved.Where(key => edit.Has(pages[i], key))];
            if (keys.Count > 0)
            {
                fail(RedactionCheck.NoDocumentLevelContent, $"Page {i + 1} has {string.Join(", ", keys)}.");
            }
        }

        HashSet<int> pageIds = [.. pages.Select(edit.ObjectId)];
        int stray = edit.AllObjects().Count(o =>
            edit.IsDictionary(o) && edit.IsName(edit.Get(o, "/Type"), "/Page") && !pageIds.Contains(edit.ObjectId(o)));
        if (stray > 0)
        {
            fail(RedactionCheck.NoStrayPages, $"The file holds {stray} page objects that are not in its page tree.");
        }

        return FileContents.Read(edit, pages);
    }

    private static List<string> Secrets(PdfiumDocument before, IReadOnlyList<RedactionArea> areas, IReadOnlyList<string>? extraTerms) =>
        [.. areas.SelectMany(a => before.GetTextInArea(a.PageIndex, a.Rect))
            .Concat(extraTerms ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static void CheckSecrets(
        PdfiumDocument before, PdfiumDocument after, HashSet<int> redactedPages, FileContents contents,
        List<string> secrets, Action<RedactionCheck, string> fail, List<string> warnings)
    {
        string keptText = Normalize(string.Join("\n", Enumerable.Range(0, before.PageCount).Where(p => !redactedPages.Contains(p)).Select(before.GetPageText)));
        string outputText = Normalize(string.Join("\n", Enumerable.Range(0, after.PageCount).Select(after.GetPageText)));
        foreach (string secret in secrets)
        {
            string normalized = Normalize(secret);
            if (keptText.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"\"{secret}\" also appears on pages that were not redacted, so it is expected in the output and was not checked.");
            }
            else if (outputText.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                fail(RedactionCheck.TextRemoved, $"\"{secret}\" is still page text.");
            }
            else if (contents.Contains(secret) || contents.Contains(normalized))
            {
                fail(RedactionCheck.TextRemoved, $"\"{secret}\" is still in the file's strings or streams.");
            }
        }
    }

    // Extracted text can hold a soft hyphen where the file has '-' (PDFium does this for its own
    // TrueType embedding) and a no-break space where it has ' '. Search for what the file holds.
    private static string Normalize(string text) => text.Replace('­', '-').Replace(' ', ' ');

    // Renders each area, a point in from its edges to stay clear of resampling at the boundary.
    private static void CheckCoverage(PdfiumDocument after, IReadOnlyList<RedactionArea> areas, Action<RedactionCheck, string> fail)
    {
        const double s = CoverageCheckPixelsPerPoint;
        foreach (RedactionArea area in areas.Where(a => a.PageIndex < after.PageCount))
        {
            PageRect r = area.Rect;
            double inset = Math.Min(r.Right - r.Left, r.Bottom - r.Top) > 4 ? 1 : 0;
            int left = (int)Math.Ceiling((r.Left + inset) * s), top = (int)Math.Ceiling((r.Top + inset) * s);
            int width = (int)Math.Floor((r.Right - inset) * s) - left, height = (int)Math.Floor((r.Bottom - inset) * s) - top;
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            var pixels = new byte[width * height * 4];
            after.RenderRegion(area.PageIndex, s, new PixelRegion(left, top, width, height), pixels);
            int light = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) > 64)
                {
                    light++;
                }
            }

            if (light > 0)
            {
                fail(RedactionCheck.AreasCovered, $"{light} of {width * height} pixels in an area on page {area.PageIndex + 1} are not black.");
            }
        }
    }

    /// <summary>
    /// Every piece of text a file holds, outside images and font programs: strings in any object,
    /// string operands of content streams, and the data of other streams (metadata, attachments,
    /// scripts). Searching these instead of the raw bytes avoids matching PDF syntax: a redacted
    /// "Page" or "12" would otherwise be found in "/Type /Page" or "12 Tf".
    /// </summary>
    private sealed class FileContents
    {
        private readonly List<string> _text = [];

        public static FileContents Read(QpdfEdit edit, IReadOnlyList<QpdfObject> pages)
        {
            var contents = new FileContents();
            HashSet<int> pageContentIds = [];
            foreach (QpdfObject page in pages)
            {
                QpdfObject value = edit.Get(page, "/Contents");
                IReadOnlyList<QpdfObject> streams = edit.IsArray(value) ? edit.Items(value) : [value];
                foreach (QpdfObject stream in streams)
                {
                    pageContentIds.Add(edit.ObjectId(stream));
                }
            }

            foreach (QpdfObject value in edit.AllObjects())
            {
                contents.Visit(edit, value, pageContentIds);
            }

            // The trailer's /ID is random bytes, not text.
            foreach (string key in edit.Keys(edit.Trailer).Where(k => k != "/ID"))
            {
                QpdfObject value = edit.Get(edit.Trailer, key);
                if (!edit.IsIndirect(value))
                {
                    contents.Visit(edit, value, pageContentIds);
                }
            }

            return contents;
        }

        public bool Contains(string secret) => _text.Any(t => t.Contains(secret, StringComparison.OrdinalIgnoreCase));

        // Indirect objects inside a value are skipped: AllObjects visits each once.
        private void Visit(QpdfEdit edit, QpdfObject value, HashSet<int> pageContentIds)
        {
            if (edit.IsString(value))
            {
                _text.Add(edit.Text(value));
                AddBytes(edit.Bytes(value));
            }
            else if (edit.IsArray(value))
            {
                foreach (QpdfObject item in edit.Items(value).Where(i => !edit.IsIndirect(i)))
                {
                    Visit(edit, item, pageContentIds);
                }
            }
            else if (edit.IsDictionary(value))
            {
                VisitDictionary(edit, value, pageContentIds);
            }
            else if (edit.IsStream(value))
            {
                QpdfObject dictionary = edit.StreamDictionary(value);
                VisitDictionary(edit, dictionary, pageContentIds);
                if (IsPixelsOrFontProgram(edit, dictionary))
                {
                    return;
                }

                byte[] data = edit.StreamData(value);
                bool isContent = pageContentIds.Contains(edit.ObjectId(value))
                    || edit.IsName(edit.Get(dictionary, "/Subtype"), "/Form")
                    || edit.Has(dictionary, "/PatternType");
                AddBytes(isContent ? ContentStrings.Extract(data) : data);
            }
        }

        private void VisitDictionary(QpdfEdit edit, QpdfObject dictionary, HashSet<int> pageContentIds)
        {
            foreach (string key in edit.Keys(dictionary))
            {
                QpdfObject item = edit.Get(dictionary, key);
                if (!edit.IsIndirect(item))
                {
                    Visit(edit, item, pageContentIds);
                }
            }
        }

        // Images are checked by rendering. Font programs and ICC profiles are binary, and the names
        // and notices inside them are not document text.
        private static bool IsPixelsOrFontProgram(QpdfEdit edit, QpdfObject dictionary)
        {
            QpdfObject subtype = edit.Get(dictionary, "/Subtype");
            return edit.IsName(subtype, "/Image")
                || edit.IsName(subtype, "/Type1C") || edit.IsName(subtype, "/CIDFontType0C") || edit.IsName(subtype, "/OpenType")
                || edit.Has(dictionary, "/Length1") || edit.Has(dictionary, "/Length2") || edit.Has(dictionary, "/Length3")
                || edit.Has(dictionary, "/N");
        }

        // Single-byte text, and UTF-16BE at either byte alignment.
        private void AddBytes(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return;
            }

            _text.Add(Encoding.Latin1.GetString(bytes));
            _text.Add(Encoding.BigEndianUnicode.GetString(bytes));
            _text.Add(Encoding.BigEndianUnicode.GetString(bytes, 1, bytes.Length - 1));
        }
    }
}
