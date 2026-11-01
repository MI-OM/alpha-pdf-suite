using System.Text;
using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;

// PDFium is not thread-safe. The adapter serializes calls, but running tests one at a
// time also keeps timings and native-memory behaviour easy to read.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace PdfSuite.Spike.Tests;

public sealed class FixtureTests
{
    private readonly PdfiumEngine _engine = new();

    [Fact]
    public void LargeDocument_HasExpectedPageCountSizesAndText()
    {
        using var document = _engine.Open(Fixtures.LargeMixed());

        Assert.Equal(Fixtures.LargePageCount, document.PageCount);
        Assert.Equal(new PageSize(612, 792), document.GetPageSize(0));
        Assert.Equal(new PageSize(792, 612), document.GetPageSize(9));
        Assert.Equal(new PageSize(595, 842), document.GetPageSize(49));
        Assert.Contains("Page 1 of 2000", document.GetPageText(0));
        Assert.Contains("Page 2000 of 2000", document.GetPageText(1999));
    }

    [Fact]
    public void LargeDocument_HasImagesEvery25thPage()
    {
        using var document = (PdfiumDocument)_engine.Open(Fixtures.LargeMixed(pageCount: 30));

        Assert.Equal(1, document.CountObjects(0, PdfiumObjectKind.Image));
        Assert.Equal(0, document.CountObjects(1, PdfiumObjectKind.Image));
        Assert.Equal(1, document.CountObjects(25, PdfiumObjectKind.Image));
    }

    [Fact]
    public void CanaryText_IsExtractableText()
    {
        using var document = _engine.Open(Fixtures.CanaryText());

        Assert.Contains("CANARY-01", document.GetPageText(0));
    }

    [Fact]
    public void TextLines_HaveBoundsWhereTheTextIs()
    {
        using var document = _engine.Open(Fixtures.CanaryText());

        IReadOnlyList<TextLine> lines = document.GetTextLines(0);

        Assert.Equal("Canary fixture: text", lines[0].Text);
        TextLine canary = Assert.Single(lines, l => l.Text.Contains("CANARY-01"));

        // "CANARY-01" is set at 12 pt from x = 180 with its baseline at y = 500 (bottom-left origin),
        // which is 292 pt from the top.
        Assert.InRange(canary.Right, 240, 260);
        Assert.InRange(canary.Top, 280, 292);
        Assert.InRange(canary.Bottom, 290, 298);
    }

    // Characterises PDFium, not a goal: lines come back in content-stream order. The canary line is
    // drawn above the filler but written after it, so it comes last. Reading order for untagged PDFs
    // needs the structure tree or layout analysis (spike results, finding 16).
    [Fact]
    public void TextLines_FollowContentOrderNotVisualOrder()
    {
        using var document = _engine.Open(Fixtures.CanaryText());

        IReadOnlyList<TextLine> lines = document.GetTextLines(0);

        Assert.Contains("CANARY-01", lines[^1].Text);
        Assert.True(lines[^1].Top < lines[1].Top, "The last line returned is visually above the second.");
    }

    [Fact]
    public void TextLines_AreEmptyForAnImageOnlyPage()
    {
        using var document = _engine.Open(Fixtures.ImageOnlyMiddlePage());

        Assert.Equal(6, document.GetTextLines(0).Count);
        Assert.Empty(document.GetTextLines(1));
        Assert.Equal("Text page 2", document.GetTextLines(2)[0].Text);
    }

    [Fact]
    public void TextLines_DoNotIncludeTextThatIsOnlyPixels()
    {
        using var document = _engine.Open(Fixtures.CanaryImage());

        IReadOnlyList<TextLine> lines = document.GetTextLines(0);

        Assert.Contains(lines, l => l.Text == "Canary fixture: image");
        Assert.DoesNotContain(lines, l => l.Text.Contains("CANARY"));
    }

    [Fact]
    public void CanaryImage_IsPixelsOnly()
    {
        using var document = (PdfiumDocument)_engine.Open(Fixtures.CanaryImage());

        Assert.DoesNotContain("CANARY-02", document.GetPageText(0));
        Assert.Equal(1, document.CountObjects(0, PdfiumObjectKind.Image));
    }

    [Fact]
    public void CanarySharedXObject_AppearsOnBothPagesAsFormObjects()
    {
        using var document = (PdfiumDocument)_engine.Open(Fixtures.CanarySharedXObject());

        Assert.Equal(2, document.PageCount);
        for (int page = 0; page < 2; page++)
        {
            Assert.Contains("CANARY-03", document.GetPageText(page));
            Assert.Equal(1, document.CountObjects(page, PdfiumObjectKind.Form));
        }
    }

    [Fact]
    public void CanaryAnnotation_HasOneAnnotation()
    {
        using var document = (PdfiumDocument)_engine.Open(Fixtures.CanaryAnnotation());

        Assert.Equal(1, document.CountAnnotations(0));
    }

    [Fact]
    public void CanaryAttachment_HasOneAttachment()
    {
        using var document = (PdfiumDocument)_engine.Open(Fixtures.CanaryAttachment());

        Assert.Equal(1, document.AttachmentCount);
    }

    [Fact]
    public void CanaryIncremental_OnlyTheFirstRevisionContainsTheCanary()
    {
        byte[] bytes = Fixtures.CanaryIncremental();

        using (var current = _engine.Open(bytes))
        {
            string text = current.GetPageText(0);
            Assert.DoesNotContain("CANARY-06", text);
            Assert.Contains("REVISED", text);
        }

        // The first revision ends at the first %%EOF marker.
        int firstEof = IndexOf(bytes, "%%EOF"u8);
        Assert.True(firstEof > 0, "The file has no %%EOF marker.");
        Assert.True(IndexOf(bytes.AsSpan(firstEof + 5), "%%EOF"u8) >= 0, "The file has no incremental update.");

        using var firstRevision = _engine.Open(bytes.AsSpan(0, firstEof + 5));
        Assert.Contains("CANARY-06", firstRevision.GetPageText(0));
    }

    [Fact]
    public void CanaryDocumentLevel_HasEveryCanaryWhereItShouldBe()
    {
        byte[] bytes = Fixtures.CanaryDocumentLevel();
        byte[] inspectable = PdfSuite.Pdf.Qpdf.QpdfStructure.Rewrite(bytes, inspectable: true);

        foreach (string canary in new[] { "CANARY-08", "CANARY-09", "CANARY-10", "CANARY-11", "CANARY-12", "CANARY-13" })
        {
            Assert.True(PdfSuite.Pdf.Qpdf.PdfText.Contains(inspectable, canary), $"{canary} is missing from the fixture.");
        }

        using var document = (PdfiumDocument)_engine.Open(bytes);
        Assert.Equal("CANARY-08", document.GetMetadata("Title"));
        Assert.True(document.CountObjects(0, PdfiumObjectKind.Path) > 100, "CANARY-07 should be drawn as many rectangles.");
        Assert.DoesNotContain("CANARY-07", document.GetPageText(0));
    }

    [Fact]
    public void Manifest_EveryCanaryHasARedactionTarget()
    {
        foreach (FixtureInfo info in Fixtures.All.Select(f => f.Info).Where(i => i.Canary is not null))
        {
            Assert.NotNull(info.CanaryLocation);
            Assert.NotNull(info.RedactPage);
            Assert.NotNull(info.RedactRect);
            Assert.Equal(4, info.RedactRect.Length);
        }
    }

    [Fact]
    public void Open_InvalidData_ThrowsFormatError()
    {
        var error = Assert.Throws<PdfOpenException>(() => _engine.Open(Encoding.ASCII.GetBytes("not a pdf")));

        Assert.Equal(PdfOpenError.Format, error.Error);
    }

    // Each fixture is generated twice. The pair covers full saves, an embedded file's
    // own /CreationDate, an incremental update with two trailers, imported chunks, and a qpdf rewrite.
    [Theory]
    [InlineData("canary-text.pdf")]
    [InlineData("canary-attachment.pdf")]
    [InlineData("canary-incremental.pdf")]
    [InlineData("canary-document-level.pdf")]
    [InlineData("simple-text.pdf")]
    public void Generation_IsByteForByteReproducible(string file)
    {
        Fixture fixture = Fixtures.All.Single(f => f.Info.File == file);

        Assert.Equal(fixture.Generate(), fixture.Generate());
    }

    [Fact]
    public void Generation_LargeChunkedDocument_IsReproducible()
    {
        Assert.Equal(Fixtures.LargeMixed(pageCount: 250), Fixtures.LargeMixed(pageCount: 250));
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) => haystack.IndexOf(needle);
}
