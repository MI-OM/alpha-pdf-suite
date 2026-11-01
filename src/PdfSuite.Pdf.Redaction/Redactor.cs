using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium.Authoring;
using PdfSuite.Pdf.Qpdf;

namespace PdfSuite.Pdf.Redaction;

/// <summary>An area to remove: a rectangle in points from the top-left corner of the page as displayed.</summary>
public readonly record struct RedactionArea(int PageIndex, PageRect Rect);

public sealed record RedactionResult(byte[] Pdf, VerificationReport Report);

/// <summary>The rebuilt file did not pass <see cref="RedactionVerifier"/>, so it was not returned.</summary>
public sealed class RedactionFailedException(VerificationReport report)
    : Exception("The redacted file failed verification: " + string.Join(" ", report.Failures.Select(f => f.Message)))
{
    public VerificationReport Report { get; } = report;
}

/// <summary>
/// Redaction by rebuilding (spike S5). The output is a new document, never an edit of the source:
/// pages with redaction areas are rendered to an image with the areas filled black, and every other
/// page is copied in unchanged. Nothing outside the pages is carried over (see
/// <see cref="RedactionPolicy"/>). The result is written once, by qpdf, as a single revision, then
/// checked by <see cref="RedactionVerifier"/>; a file that fails is never returned.
/// </summary>
/// <remarks>
/// Rendering costs the redacted pages their text, links and sharpness, which is the price of
/// knowing that nothing under an area survives. Keeping text outside the areas needs glyph-level
/// editing of content streams, which is a v2 question (spike results, S5).
/// </remarks>
public static class Redactor
{
    /// <summary>Resolution of redacted pages: 3 pixels per point is 216 dpi.</summary>
    public const double PixelsPerPoint = 3;

    /// <summary>Most pixels rendered for one page (160 MB as BGRA); very large pages get a lower resolution.</summary>
    public const double MaxPixelsPerPage = 40_000_000;

    /// <param name="extraTerms">Text the verifier must also find nowhere in the output, such as a name redacted from an image.</param>
    /// <exception cref="RedactionFailedException">The rebuilt file failed verification.</exception>
    public static RedactionResult Redact(ReadOnlySpan<byte> pdf, IReadOnlyList<RedactionArea> areas, IReadOnlyList<string>? extraTerms = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(areas.Count);
        byte[] scrubbed = Scrub(Rebuild(pdf, areas));
        VerificationReport report = RedactionVerifier.Verify(pdf, scrubbed, areas, extraTerms);
        return report.Passed ? new RedactionResult(scrubbed, report) : throw new RedactionFailedException(report);
    }

    private static byte[] Rebuild(ReadOnlySpan<byte> pdf, IReadOnlyList<RedactionArea> areas)
    {
        using var source = PdfiumDocumentWriter.Load(pdf);
        int pageCount = source.PageCount;
        foreach (RedactionArea area in areas)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(area.PageIndex, nameof(areas));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(area.PageIndex, pageCount, nameof(areas));
            if (area.Rect.Right <= area.Rect.Left || area.Rect.Bottom <= area.Rect.Top)
            {
                throw new ArgumentException($"The area on page {area.PageIndex + 1} is empty.", nameof(areas));
            }
        }

        ILookup<int, PageRect> areasByPage = areas.ToLookup(a => a.PageIndex, a => a.Rect);
        using var output = PdfiumDocumentWriter.CreateNew();
        int page = 0;
        while (page < pageCount)
        {
            if (areasByPage.Contains(page))
            {
                AddRenderedPage(source, page, areasByPage[page], output);
                page++;
                continue;
            }

            // Unaffected pages are copied in runs, which keeps shared resources shared.
            int first = page;
            while (page < pageCount && !areasByPage.Contains(page))
            {
                page++;
            }

            output.ImportPages(source, $"{first + 1}-{page}");
        }

        return output.ToArray();
    }

    private static void AddRenderedPage(PdfiumDocumentWriter source, int pageIndex, IEnumerable<PageRect> areas, PdfiumDocumentWriter output)
    {
        PageSize size = source.GetPageSize(pageIndex);
        double scale = Math.Min(PixelsPerPoint, Math.Sqrt(MaxPixelsPerPage / (size.Width * size.Height)));
        int width = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(size.Height * scale));

        // Annotations are rendered too, so their appearance stays while their data goes.
        BgraImage image = source.RenderPage(pageIndex, width, height);
        foreach (PageRect area in areas)
        {
            Fill(image, area, width / size.Width, height / size.Height);
        }

        using PdfiumPageWriter page = output.AddPage(size.Width, size.Height);
        page.AddImage(image, 0, 0, size.Width, size.Height);
    }

    // Rounds outwards, so a pixel the area only partly covers is filled too.
    private static void Fill(BgraImage image, PageRect area, double scaleX, double scaleY)
    {
        int left = Math.Clamp((int)Math.Floor(area.Left * scaleX), 0, image.Width);
        int right = Math.Clamp((int)Math.Ceiling(area.Right * scaleX), 0, image.Width);
        int top = Math.Clamp((int)Math.Floor(area.Top * scaleY), 0, image.Height);
        int bottom = Math.Clamp((int)Math.Ceiling(area.Bottom * scaleY), 0, image.Height);
        for (int y = top; y < bottom; y++)
        {
            Span<byte> row = image.Pixels.AsSpan(((y * image.Width) + left) * 4, (right - left) * 4);
            for (int x = 0; x < row.Length; x += 4)
            {
                row[x] = 0;
                row[x + 1] = 0;
                row[x + 2] = 0;
                row[x + 3] = 255;
            }
        }
    }

    /// <summary>Applies <see cref="RedactionPolicy"/> and writes the single revision.</summary>
    private static byte[] Scrub(byte[] pdf)
    {
        using var edit = QpdfEdit.Open(pdf);
        edit.Remove(edit.Trailer, "/Info");

        // qpdf keeps the first half of an existing file ID, and PDFium's is random. Without it qpdf
        // derives both halves from the content, so the same input gives the same bytes.
        edit.Remove(edit.Trailer, "/ID");
        foreach (string key in edit.Keys(edit.Root).Except(RedactionPolicy.CatalogKeys))
        {
            edit.Remove(edit.Root, key);
        }

        List<QpdfObject> pages = [.. Enumerable.Range(0, edit.PageCount).Select(edit.Page)];
        HashSet<int> pageIds = [.. pages.Select(edit.ObjectId)];
        foreach (QpdfObject page in pages)
        {
            foreach (string key in RedactionPolicy.PageKeysRemoved)
            {
                edit.Remove(page, key);
            }

            QpdfObject annotations = edit.Get(page, "/Annots");
            if (!edit.IsArray(annotations))
            {
                continue;
            }

            foreach (QpdfObject annotation in edit.Items(annotations).Where(edit.IsDictionary))
            {
                foreach (string key in RedactionPolicy.AnnotationKeysRemoved)
                {
                    edit.Remove(annotation, key);
                }

                // A copied page brings along whatever it refers to. PDFium already drops a reference
                // to a page it is not copying (spike results, S5), so a link to a redacted page arrives
                // without its target; this rule does not depend on that. Anything a removed link
                // pulled in becomes unreachable, and qpdf's write leaves it out.
                if (!LinkStaysInside(edit, annotation, pageIds))
                {
                    edit.Remove(annotation, "/Dest");
                    edit.Remove(annotation, "/A");
                }
            }
        }

        return edit.Save();
    }

    // A link may lead to a page of the output or to a web address. Anything else (a page that was
    // redacted or left out, JavaScript, launching a file, a chain of actions) loses its link.
    private static bool LinkStaysInside(QpdfEdit edit, QpdfObject annotation, HashSet<int> pageIds)
    {
        if (edit.Has(annotation, "/Dest") && !DestinationInside(edit, edit.Get(annotation, "/Dest"), pageIds))
        {
            return false;
        }

        if (!edit.Has(annotation, "/A"))
        {
            return true;
        }

        QpdfObject action = edit.Get(annotation, "/A");
        if (!edit.IsDictionary(action) || edit.Has(action, "/Next"))
        {
            return false;
        }

        QpdfObject type = edit.Get(action, "/S");
        return edit.IsName(type, "/URI")
            || (edit.IsName(type, "/GoTo") && DestinationInside(edit, edit.Get(action, "/D"), pageIds));
    }

    // Named destinations are fine: the name tree they would resolve through is gone.
    private static bool DestinationInside(QpdfEdit edit, QpdfObject destination, HashSet<int> pageIds) =>
        !edit.IsArray(destination)
        || (edit.Items(destination) is [QpdfObject page, ..] && pageIds.Contains(edit.ObjectId(page)));
}

/// <summary>What a redacted file may keep outside its pages. The redactor applies it; the verifier checks it.</summary>
internal static class RedactionPolicy
{
    /// <summary>
    /// The only catalog entries kept. Everything else goes: XMP metadata, open actions and
    /// document JavaScript, bookmarks, named destinations and attachments, forms, the structure
    /// tree, layers, and viewer settings.
    /// </summary>
    public static readonly string[] CatalogKeys = ["/Type", "/Pages"];

    /// <summary>
    /// Removed from every page, including pages copied unchanged: page actions, page metadata,
    /// private application data, article beads, and the link into the removed structure tree.
    /// </summary>
    public static readonly string[] PageKeysRemoved = ["/AA", "/Metadata", "/PieceInfo", "/B", "/StructParents"];

    /// <summary>
    /// Removed from annotations on copied pages: the page link, reply links, actions, and the
    /// structure-tree link. Each can refer to objects outside the page.
    /// </summary>
    public static readonly string[] AnnotationKeysRemoved = ["/P", "/IRT", "/AA", "/StructParent"];
}
