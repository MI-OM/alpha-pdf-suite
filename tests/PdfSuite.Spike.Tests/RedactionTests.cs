using System.Text;
using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Pdfium.Authoring;
using PdfSuite.Pdf.Qpdf;
using PdfSuite.Pdf.Redaction;

namespace PdfSuite.Spike.Tests;

public sealed class RedactionTests
{
    private readonly PdfiumEngine _engine = new();

    public static TheoryData<string> CanaryFixtures => new(Fixtures.All.Where(f => f.Info.Canary is not null).Select(f => f.Info.File));

    // Every canary fixture, redacted at its manifest target, loses every canary it holds, except
    // where the manifest says the canary legitimately remains (a shared XObject on a kept page).
    [Theory]
    [MemberData(nameof(CanaryFixtures))]
    public void Redact_RemovesEveryCanary(string file)
    {
        FixtureInfo info = Fixtures.All.Single(f => f.Info.File == file).Info;
        byte[] source = Fixtures.All.Single(f => f.Info.File == file).Generate();
        RedactionArea area = TargetOf(info, source);
        string[] canaries = [info.Canary!, .. info.DocumentLevelCanaries ?? []];

        RedactionResult result = Redactor.Redact(source, [area], canaries);

        Assert.True(result.Report.Passed);
        int[] remainsOn = info.CanaryRemainsOnPages ?? [];
        byte[] inspectable = QpdfStructure.Rewrite(result.Pdf, inspectable: true);
        using var output = (PdfiumDocument)_engine.Open(result.Pdf);
        foreach (string canary in canaries)
        {
            bool remains = canary == info.Canary && remainsOn.Length > 0;
            for (int page = 0; page < output.PageCount; page++)
            {
                bool expected = remains && remainsOn.Contains(page);
                Assert.True(expected == output.GetPageText(page).Contains(canary), $"{canary} on page {page + 1}: expected {(expected ? "present" : "absent")}.");
            }

            Assert.True(remains || !PdfText.Contains(inspectable, canary), $"{canary} is still in the file's bytes.");
        }

        Assert.Equal(1, output.CountObjects(area.PageIndex));
        Assert.Equal(0, output.AttachmentCount);
    }

    [Fact]
    public void Redact_SharedXObject_WarnsThatTheCanaryIsKeptElsewhere()
    {
        byte[] source = Fixtures.CanarySharedXObject();

        RedactionResult result = Redactor.Redact(source, [TargetOf("canary-shared-xobject.pdf", source)]);

        Assert.Contains(result.Report.Warnings, w => w.Contains("CANARY-03"));
    }

    [Fact]
    public void Redact_DropsALinkToARedactedPage()
    {
        byte[] source = Fixtures.CanaryDocumentLevel();

        RedactionResult result = Redactor.Redact(source, [TargetOf("canary-document-level.pdf", source)]);

        using var edit = QpdfEdit.Open(result.Pdf);
        QpdfObject link = Assert.Single(edit.Items(edit.Get(edit.Page(1), "/Annots")));
        Assert.False(edit.Has(link, "/Dest"));
        Assert.Equal(2, edit.AllObjects().Count(o => edit.IsDictionary(o) && edit.IsName(edit.Get(o, "/Type"), "/Page")));
    }

    [Fact]
    public void Redact_IsDeterministic()
    {
        byte[] source = Fixtures.CanaryDocumentLevel();
        RedactionArea area = TargetOf("canary-document-level.pdf", source);

        Assert.Equal(Redactor.Redact(source, [area]).Pdf, Redactor.Redact(source, [area]).Pdf);
    }

    [Fact]
    public void Redact_KeepsUnaffectedPagesAsText()
    {
        byte[] source = Fixtures.CanaryDocumentLevel();

        RedactionResult result = Redactor.Redact(source, [TargetOf("canary-document-level.pdf", source)]);

        using var output = _engine.Open(result.Pdf);
        Assert.Equal("", output.GetPageText(0));
        Assert.Contains("Second page, not redacted", output.GetPageText(1));
    }

    // Terms that are also PDF syntax ("/Type /Page", "12 Tf") must not be reported as leaks.
    [Fact]
    public void Verify_DoesNotMistakePdfSyntaxForText()
    {
        byte[] source = Fixtures.CanaryText();
        RedactionArea area = TargetOf("canary-text.pdf", source);
        byte[] redacted = Redactor.Redact(source, [area]).Pdf;

        VerificationReport report = RedactionVerifier.Verify(source, redacted, [area], ["Page", "Type", "Resources", "Tf", "Do", "12", "0"]);

        Assert.True(report.Passed, string.Join(" ", report.Failures.Select(f => f.Message)));
    }

    // Negative controls. Each is a known way to get redaction wrong; the verifier must reject it,
    // through the check meant for that mistake.

    [Fact]
    public void NegativeControl_BoxDrawnOverText_IsRejected()
    {
        byte[] source = Fixtures.CanaryText();
        RedactionArea area = TargetOf("canary-text.pdf", source);
        byte[] boxed;
        using (var document = PdfiumDocumentWriter.Load(source))
        {
            using (var page = document.EditPage(0))
            {
                AddBox(page, area, document.GetPageSize(0));
            }

            boxed = document.ToArray();
        }

        VerificationReport report = RedactionVerifier.Verify(source, boxed, [area]);

        Assert.True(report.Failed(RedactionCheck.TextRemoved));
        Assert.True(report.Failed(RedactionCheck.RedactedPagesAreImages));
        Assert.False(report.Failed(RedactionCheck.AreasCovered));
    }

    // The text object is deleted and a box drawn, but saved as an incremental update: the current
    // revision is clean and the original is still in the file, readable by cutting at the first %%EOF.
    [Fact]
    public void NegativeControl_IncrementalSave_IsRejected()
    {
        byte[] source = Fixtures.CanaryText();
        RedactionArea area = TargetOf("canary-text.pdf", source);
        byte[] updated;
        using (var document = PdfiumDocumentWriter.Load(source))
        {
            using (var page = document.EditPage(0))
            {
                page.RemoveObjectAt(page.ObjectCount - 1);
                AddBox(page, area, document.GetPageSize(0));
            }

            updated = document.ToArray(PdfSaveMode.Incremental);
        }

        using (var current = _engine.Open(updated))
        {
            Assert.DoesNotContain("CANARY-01", current.GetPageText(0));
        }

        VerificationReport report = RedactionVerifier.Verify(source, updated, [area]);

        Assert.True(report.Failed(RedactionCheck.SingleRevision));
        Assert.False(report.Failed(RedactionCheck.TextRemoved));
    }

    [Fact]
    public void NegativeControl_IncrementalUpdateAfterRedaction_IsRejected()
    {
        byte[] source = Fixtures.CanaryDocumentLevel();
        RedactionArea area = TargetOf("canary-document-level.pdf", source);
        byte[] updated;
        using (var document = PdfiumDocumentWriter.Load(Redactor.Redact(source, [area]).Pdf))
        {
            using (var page = document.EditPage(1))
            {
                page.AddText("Reviewed", 72, 100, 12);
            }

            updated = document.ToArray(PdfSaveMode.Incremental);
        }

        VerificationReport report = RedactionVerifier.Verify(source, updated, [area]);

        Assert.Equal([RedactionCheck.SingleRevision], report.Failures.Select(f => f.Check).Distinct());
    }

    [Fact]
    public void NegativeControl_MetadataLeftBehind_IsRejected()
    {
        byte[] source = Fixtures.CanaryText();
        RedactionArea area = TargetOf("canary-text.pdf", source);
        byte[] withTitle = QpdfStructure.SetMetadata(
            Redactor.Redact(source, [area]).Pdf, new Dictionary<string, string?> { ["/Title"] = "CANARY-01" });

        VerificationReport report = RedactionVerifier.Verify(source, withTitle, [area]);

        Assert.True(report.Failed(RedactionCheck.NoDocumentLevelContent));
        Assert.True(report.Failed(RedactionCheck.TextRemoved));
    }

    [Fact]
    public void NegativeControl_AnnotationLeftOnRedactedPage_IsRejected()
    {
        byte[] source = Fixtures.CanaryText();
        RedactionArea area = TargetOf("canary-text.pdf", source);
        byte[] annotated;
        using (var document = PdfiumDocumentWriter.Load(Redactor.Redact(source, [area]).Pdf))
        {
            using (var page = document.EditPage(0))
            {
                page.AddNoteAnnotation("CANARY-01", 400, 600, 420, 620);
            }

            annotated = document.ToArray();
        }

        VerificationReport report = RedactionVerifier.Verify(source, annotated, [area]);

        Assert.True(report.Failed(RedactionCheck.RedactedPagesAreImages));
        Assert.True(report.Failed(RedactionCheck.TextRemoved));
    }

    // A page reachable only through a link, the way PDFium's page copy brings in the target of a link.
    [Fact]
    public void NegativeControl_PageOutsideThePageTree_IsRejected()
    {
        byte[] source = Fixtures.CanaryDocumentLevel();
        RedactionArea area = TargetOf("canary-document-level.pdf", source);
        byte[] withStray;
        using (var edit = QpdfEdit.Open(Redactor.Redact(source, [area]).Pdf))
        {
            QpdfObject stray = edit.NewDictionary();
            edit.Set(stray, "/Type", edit.NewName("/Page"));
            QpdfObject destination = edit.NewArray();
            edit.Append(destination, edit.MakeIndirect(stray));
            edit.Append(destination, edit.NewName("/Fit"));
            QpdfObject link = Assert.Single(edit.Items(edit.Get(edit.Page(1), "/Annots")));
            edit.Set(link, "/Dest", destination);
            withStray = edit.Save();
        }

        VerificationReport report = RedactionVerifier.Verify(source, withStray, [area]);

        Assert.True(report.Failed(RedactionCheck.NoStrayPages));
    }

    [Fact]
    public void NegativeControl_AreaNotFilled_IsRejected()
    {
        byte[] source = Fixtures.CanaryText();
        byte[] redacted = Redactor.Redact(source, [TargetOf("canary-text.pdf", source)]).Pdf;

        // The heading, which is rendered but was never filled.
        var heading = new RedactionArea(0, new PageRect(70, 34, 250, 56));
        VerificationReport report = RedactionVerifier.Verify(source, redacted, [heading]);

        Assert.True(report.Failed(RedactionCheck.AreasCovered));
    }

    [Fact]
    public void ContentStrings_JoinsSplitTextAndSkipsInlineImages()
    {
        byte[] content = Encoding.Latin1.GetBytes(
            "BT /F1 12 Tf (CAN) Tj [<4152> -20 (Y-0\\061)] TJ ET % (comment)\nBI /W 1 /H 1 ID (EI) EI Q");

        Assert.Equal("CANARY-01", Encoding.Latin1.GetString(ContentStrings.Extract(content)));
    }

    private static RedactionArea TargetOf(string file, byte[] source) => TargetOf(Fixtures.All.Single(f => f.Info.File == file).Info, source);

    // The manifest's rectangle is in PDF space (left, bottom, right, top from the bottom-left).
    private static RedactionArea TargetOf(FixtureInfo info, byte[] source)
    {
        using var document = PdfiumDocumentWriter.Load(source);
        int page = info.RedactPage!.Value;
        double height = document.GetPageSize(page).Height;
        double[] r = info.RedactRect!;
        return new RedactionArea(page, new PageRect(r[0], height - r[3], r[2], height - r[1]));
    }

    private static void AddBox(PdfiumPageWriter page, RedactionArea area, PageSize size)
    {
        PageRect r = area.Rect;
        page.AddRectangle(r.Left, size.Height - r.Bottom, r.Right - r.Left, r.Bottom - r.Top, 0, 0, 0);
    }
}
