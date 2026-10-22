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
    // own /CreationDate, an incremental update with two trailers, and imported chunks.
    [Theory]
    [InlineData("canary-text.pdf")]
    [InlineData("canary-attachment.pdf")]
    [InlineData("canary-incremental.pdf")]
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
