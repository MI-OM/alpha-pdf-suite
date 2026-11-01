using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Pdfium.Authoring;
using PdfSuite.Pdf.Pdfium.Native;
using PdfSuite.Pdf.Qpdf;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Spike S5's glyph-level probe, a question for v2: can PDFium remove only the characters under an
/// area and keep the rest of the line as text in its original font? Each case writes one line in
/// one font, removes "CANARY-01" from the middle by rebuilding the text object around it, and
/// measures what changed. Explicit, so CI does not run it: the embedded-font cases need a system
/// font, and the findings are recorded in docs/research/spike-results.md.
/// </summary>
public sealed unsafe class GlyphProbeTests
{
    private const string Line = "Name: CANARY-01 Phone: 555-0100";
    private const string Secret = "CANARY-01";
    private const string WindowsFont = @"C:\Windows\Fonts\arial.ttf";

    private sealed record ProbeResult(
        string Font, bool Embedded, bool SetTextWorked, string Text, bool SecretGone,
        double MaxShift, int ChangedPixelsOutside, int InkLeftInside, int BytesBefore, int BytesAfter);

    [Fact(Explicit = true)]
    public void Probe_StandardFont()
    {
        ProbeResult result = Probe(Build(fontData: null, cid: false));

        Report(result);
        Assert.True(result.SecretGone);
    }

    [Fact(Explicit = true)]
    public void Probe_EmbeddedTrueType()
    {
        Assert.SkipUnless(File.Exists(WindowsFont), $"Needs {WindowsFont}.");

        Report(Probe(Build(File.ReadAllBytes(WindowsFont), cid: false)));
    }

    [Fact(Explicit = true)]
    public void Probe_EmbeddedTrueTypeAsCid()
    {
        Assert.SkipUnless(File.Exists(WindowsFont), $"Needs {WindowsFont}.");

        Report(Probe(Build(File.ReadAllBytes(WindowsFont), cid: true)));
    }

    private static void Report(ProbeResult r) =>
        Console.WriteLine(
            $"PROBE {r.Font} (embedded: {r.Embedded}): set text {(r.SetTextWorked ? "worked" : "FAILED")}; text now \"{r.Text.Trim()}\"; "
            + $"secret gone: {r.SecretGone}; max shift of kept characters {r.MaxShift:0.###} pt; "
            + $"{r.ChangedPixelsOutside} changed pixels outside the area, {r.InkLeftInside} inked pixels inside; "
            + $"{r.BytesBefore} -> {r.BytesAfter} bytes.");

    // One Letter page with Line at (72, 500), 12 pt, in the standard Helvetica or the given TrueType font.
    private static byte[] Build(byte[]? fontData, bool cid)
    {
        using var document = PdfiumDocumentWriter.CreateNew();
        lock (PdfiumLibrary.Sync)
        {
            nint font = document.StandardFont;
            if (fontData is not null)
            {
                fixed (byte* data = fontData)
                {
                    font = PdfiumNative.FPDFText_LoadFont(document.Handle, data, (uint)fontData.Length, PdfiumNative.FPDF_FONT_TRUETYPE, cid ? 1 : 0);
                }

                Assert.NotEqual(0, font);
            }

            nint page = PdfiumNative.FPDFPage_New(document.Handle, 0, 612, 792);
            nint text = PdfiumNative.FPDFPageObj_CreateTextObj(document.Handle, font, 12);
            fixed (char* value = Line)
            {
                Assert.NotEqual(0, PdfiumNative.FPDFText_SetText(text, value));
            }

            PdfiumNative.FPDFPageObj_Transform(text, 1, 0, 0, 1, 72, 500);
            PdfiumNative.FPDFPage_InsertObject(page, text);
            Assert.NotEqual(0, PdfiumNative.FPDFPage_GenerateContent(page));
            PdfiumNative.FPDF_ClosePage(page);
            if (fontData is not null)
            {
                PdfiumNative.FPDFFont_Close(font);
            }
        }

        return document.ToArray();
    }

    // Replaces the text object holding the secret with up to two new ones in the same font, size
    // and orientation, holding the characters before and after it, each placed at the origin its
    // first character had.
    private static ProbeResult Probe(byte[] source)
    {
        string fontName;
        bool embedded, setTextWorked = true;
        List<(double X, double Y)> keptBefore;
        PageRect secretBox;
        using (var document = PdfiumDocumentWriter.Load(source))
        {
            lock (PdfiumLibrary.Sync)
            {
                nint page = PdfiumNative.FPDF_LoadPage(document.Handle, 0);
                nint textPage = PdfiumNative.FPDFText_LoadPage(page);
                string text = PageText(textPage);
                int start = text.IndexOf(Secret, StringComparison.Ordinal), end = start + Secret.Length;
                nint original = PdfiumNative.FPDFText_GetTextObject(textPage, start);
                List<int> own = [.. Enumerable.Range(0, text.Length).Where(i => PdfiumNative.FPDFText_GetTextObject(textPage, i) == original)];
                List<int> before = [.. own.Where(i => i < start)], after = [.. own.Where(i => i >= end)];
                Assert.True(start >= 0, $"The secret is not in the page text: \"{text}\".");
                keptBefore = KeptOrigins(textPage, text);
                secretBox = Box(textPage, start, end);

                nint font = PdfiumNative.FPDFTextObj_GetFont(original);
                float size;
                FS_MATRIX matrix;
                PdfiumNative.FPDFTextObj_GetFontSize(original, &size);
                PdfiumNative.FPDFPageObj_GetMatrix(original, &matrix);
                embedded = PdfiumNative.FPDFFont_GetIsEmbedded(font) == 1;
                fontName = BaseFontName(font);

                foreach (List<int> segment in new[] { before, after }.Where(s => s.Count > 0))
                {
                    nint replacement = PdfiumNative.FPDFPageObj_CreateTextObj(document.Handle, font, size);
                    string value = string.Concat(segment.Select(i => text[i]));
                    fixed (char* chars = value)
                    {
                        setTextWorked &= PdfiumNative.FPDFText_SetText(replacement, chars) != 0;
                    }

                    (matrix.E, matrix.F) = ((float)Origin(textPage, segment[0]).X, (float)Origin(textPage, segment[0]).Y);
                    PdfiumNative.FPDFPageObj_SetMatrix(replacement, &matrix);
                    PdfiumNative.FPDFPage_InsertObject(page, replacement);
                }

                PdfiumNative.FPDFPage_RemoveObject(page, original);
                PdfiumNative.FPDFPageObj_Destroy(original);
                PdfiumNative.FPDFText_ClosePage(textPage);
                Assert.NotEqual(0, PdfiumNative.FPDFPage_GenerateContent(page));
                PdfiumNative.FPDF_ClosePage(page);
            }

            byte[] result = document.ToArray();
            return Measure(source, result, fontName, embedded, setTextWorked, keptBefore, secretBox);
        }
    }

    private static ProbeResult Measure(
        byte[] source, byte[] result, string font, bool embedded, bool setTextWorked, List<(double X, double Y)> keptBefore, PageRect secretBox)
    {
        var engine = new PdfiumEngine();
        using var sourceDocument = (PdfiumDocument)engine.Open(source);
        using var resultDocument = (PdfiumDocument)engine.Open(result);
        string text = resultDocument.GetPageText(0).Replace('­', '-');
        bool secretGone = !text.Contains(Secret) && !PdfText.Contains(QpdfStructure.Rewrite(result, inspectable: true), Secret);

        // Where the kept characters after the secret ended up, against where they were.
        double maxShift = double.NaN;
        using (var document = PdfiumDocumentWriter.Load(result))
        {
            lock (PdfiumLibrary.Sync)
            {
                nint page = PdfiumNative.FPDF_LoadPage(document.Handle, 0);
                nint textPage = PdfiumNative.FPDFText_LoadPage(page);
                List<(double X, double Y)> keptAfter = KeptOrigins(textPage, PageText(textPage));
                if (keptAfter.Count == keptBefore.Count && keptAfter.Count > 0)
                {
                    maxShift = keptBefore.Zip(keptAfter, (p, q) => Math.Max(Math.Abs(q.X - p.X), Math.Abs(q.Y - p.Y))).Max();
                }

                PdfiumNative.FPDFText_ClosePage(textPage);
                PdfiumNative.FPDF_ClosePage(page);
            }
        }

        // Pixels at 2 px/pt. The area is the secret's character boxes, padded by 2 pt for antialiasing.
        const double s = 2;
        var region = new PixelRegion(0, 0, (int)(612 * s), (int)(792 * s));
        byte[] a = new byte[region.Width * region.Height * 4], b = new byte[a.Length];
        sourceDocument.RenderRegion(0, s, region, a);
        resultDocument.RenderRegion(0, s, region, b);
        int changedOutside = 0, inkInside = 0;
        for (int y = 0; y < region.Height; y++)
        {
            for (int x = 0; x < region.Width; x++)
            {
                int i = ((y * region.Width) + x) * 4;
                double px = x / s, py = y / s;
                bool inside = px >= secretBox.Left - 2 && px <= secretBox.Right + 2 && py >= secretBox.Top - 2 && py <= secretBox.Bottom + 2;
                if (inside)
                {
                    inkInside += b[i] < 128 ? 1 : 0;
                }
                else if (Math.Abs(a[i] - b[i]) > 48 || Math.Abs(a[i + 1] - b[i + 1]) > 48 || Math.Abs(a[i + 2] - b[i + 2]) > 48)
                {
                    changedOutside++;
                }
            }
        }

        return new ProbeResult(font, embedded, setTextWorked, text, secretGone, maxShift, changedOutside, inkInside, source.Length, result.Length);
    }

    // Origins of the visible characters kept after the secret ("Phone: 555-0100" without its space),
    // skipping spaces, which PDFium sometimes generates and sometimes doubles; empty if not found.
    private static List<(double X, double Y)> KeptOrigins(nint textPage, string text)
    {
        int start = text.IndexOf("Phone", StringComparison.Ordinal);
        return start < 0 ? [] : [.. Enumerable.Range(start, text.Length - start).Where(i => !char.IsWhiteSpace(text[i])).Take(14).Select(i => Origin(textPage, i))];
    }

    // The union of character boxes [start, end), in points from the top-left of a Letter page.
    private static PageRect Box(nint textPage, int start, int end)
    {
        double left = double.MaxValue, right = double.MinValue, bottom = double.MaxValue, top = double.MinValue;
        for (int i = start; i < end; i++)
        {
            double l, r, b, t;
            PdfiumNative.FPDFText_GetCharBox(textPage, i, &l, &r, &b, &t);
            (left, right, bottom, top) = (Math.Min(left, l), Math.Max(right, r), Math.Min(bottom, b), Math.Max(top, t));
        }

        return new PageRect(left, 792 - top, right, 792 - bottom);
    }

    // PDFium's simple TrueType embedding maps '-' to U+00AD (soft hyphen) in extracted text.
    private static string PageText(nint textPage) =>
        new([.. Enumerable.Range(0, PdfiumNative.FPDFText_CountChars(textPage))
            .Select(i => (char)PdfiumNative.FPDFText_GetUnicode(textPage, i))
            .Select(c => c == '­' ? '-' : c)]);

    private static (double X, double Y) Origin(nint textPage, int index)
    {
        double x = 0, y = 0;
        PdfiumNative.FPDFText_GetCharOrigin(textPage, index, &x, &y);
        return (x, y);
    }

    private static string BaseFontName(nint font)
    {
        var buffer = new byte[256];
        fixed (byte* bytes = buffer)
        {
            nuint length = PdfiumNative.FPDFFont_GetBaseFontName(font, bytes, (nuint)buffer.Length);
            return length > 1 ? System.Text.Encoding.UTF8.GetString(buffer, 0, (int)length - 1) : "?";
        }
    }
}
