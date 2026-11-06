using System.Text;
using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Qpdf;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Spike finding 16: reading lines follow a tagged page's structure tree, not the order the file
/// draws them. The page here draws a running header (an artifact), then the second paragraph,
/// then the first; its structure tree lists the first paragraph before the second.
/// </summary>
public sealed class ReadingOrderTests
{
    private const string Content = """
        /Artifact BMC BT /F1 9 Tf 72 760 Td (Running header) Tj ET EMC
        /P <</MCID 1>> BDC BT /F1 12 Tf 72 700 Td (Second paragraph) Tj ET EMC
        /P <</MCID 0>> BDC BT /F1 12 Tf 72 730 Td (First paragraph) Tj ET EMC
        """;

    private readonly PdfiumEngine _engine = new();

    [Fact]
    public void TaggedPage_IsReadInStructureOrder_WithUntaggedLinesKeptInPlace()
    {
        using IPdfDocument document = _engine.Open(Page(tagged: true));

        Assert.Equal(["Running header", "First paragraph", "Second paragraph"], document.GetTextLines(0).Select(l => l.Text));
    }

    // Negative control: without the structure tree, the same page is read in drawing order.
    [Fact]
    public void UntaggedPage_IsReadInDrawingOrder()
    {
        using IPdfDocument document = _engine.Open(Page(tagged: false));

        Assert.Equal(["Running header", "Second paragraph", "First paragraph"], document.GetTextLines(0).Select(l => l.Text));
    }

    // Page 1 of simple-text.pdf, with its content replaced, and optionally a structure tree.
    private static byte[] Page(bool tagged)
    {
        using var edit = QpdfEdit.Open(Fixtures.SimpleText());
        QpdfObject page = edit.Page(0);
        edit.Set(page, "/Contents", edit.NewStream(Encoding.ASCII.GetBytes(Content)));

        QpdfObject font = edit.NewDictionary();
        edit.Set(font, "/Type", edit.NewName("/Font"));
        edit.Set(font, "/Subtype", edit.NewName("/Type1"));
        edit.Set(font, "/BaseFont", edit.NewName("/Helvetica"));
        QpdfObject fonts = edit.NewDictionary();
        edit.Set(fonts, "/F1", font);
        QpdfObject resources = edit.NewDictionary();
        edit.Set(resources, "/Font", fonts);
        edit.Set(page, "/Resources", resources);

        if (tagged)
        {
            QpdfObject root = edit.MakeIndirect(edit.NewDictionary());
            edit.Set(root, "/Type", edit.NewName("/StructTreeRoot"));
            QpdfObject document = Element(edit, "/Document", root, page, mcid: null);
            QpdfObject first = Element(edit, "/P", document, page, mcid: 0);
            QpdfObject second = Element(edit, "/P", document, page, mcid: 1);
            edit.Set(document, "/K", Array(edit, first, second));
            edit.Set(root, "/K", document);

            // The parent tree maps the page's marked content back to its elements.
            QpdfObject parentTree = edit.NewDictionary();
            edit.Set(parentTree, "/Nums", Array(edit, edit.NewInteger(0), Array(edit, first, second)));
            edit.Set(root, "/ParentTree", parentTree);
            edit.Set(page, "/StructParents", edit.NewInteger(0));
            edit.Set(edit.Root, "/StructTreeRoot", root);

            // A file is tagged only if it says so; PDFium ignores the structure tree otherwise.
            QpdfObject markInfo = edit.NewDictionary();
            edit.Set(markInfo, "/Marked", edit.NewBoolean(true));
            edit.Set(edit.Root, "/MarkInfo", markInfo);
        }

        return edit.Save();
    }

    private static QpdfObject Element(QpdfEdit edit, string type, QpdfObject parent, QpdfObject page, int? mcid)
    {
        QpdfObject element = edit.MakeIndirect(edit.NewDictionary());
        edit.Set(element, "/Type", edit.NewName("/StructElem"));
        edit.Set(element, "/S", edit.NewName(type));
        edit.Set(element, "/P", parent);
        edit.Set(element, "/Pg", page);
        if (mcid is int id)
        {
            edit.Set(element, "/K", edit.NewInteger(id));
        }

        return element;
    }

    private static QpdfObject Array(QpdfEdit edit, params QpdfObject[] items)
    {
        QpdfObject array = edit.NewArray();
        foreach (QpdfObject item in items)
        {
            edit.Append(array, item);
        }

        return array;
    }
}
