using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Qpdf;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>The document outline (bookmarks; roadmap Milestone 2), through the engine and the worker.</summary>
public sealed class OutlineTests : IDisposable
{
    private readonly PdfiumEngine _engine = new();
    private readonly WorkerClient _client = WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()));

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task ReadsTheCanaryFixturesBookmark_TheSameThroughTheWorker()
    {
        byte[] data = Fixtures.CanaryDocumentLevel();
        using IPdfDocument local = _engine.Open(data);
        OutlineItem item = Assert.Single(local.GetOutline());
        Assert.Equal("CANARY-11", item.Title);
        Assert.Equal(0, item.PageIndex);
        Assert.Null(item.Top);   // a /Fit destination has no point on the page
        Assert.Empty(item.Children);

        RemoteDocument remote = await _client.OpenAsync(data, password: null, Ct);
        Assert.Equivalent(local.GetOutline(), await _client.GetOutlineAsync(remote.Id, Ct), strict: true);
    }

    [Fact]
    public async Task ReadsANestedOutline_WithPositions_AndListsWebsiteLinksWithoutATarget()
    {
        byte[] data = WithOutline(Fixtures.SimpleText(), edit =>
        {
            QpdfObject section = Entry(edit, "Section 1.1", Destination(edit, edit.Page(1), "/Fit"));
            QpdfObject chapter1 = Entry(edit, "Chapter 1", Destination(edit, edit.Page(0), "/XYZ", 0, 700, 0));
            Link(edit, chapter1, section);
            QpdfObject chapter2 = Entry(edit, "Chapter 2", Destination(edit, edit.Page(2), "/Fit"));
            QpdfObject website = edit.MakeIndirect(edit.NewDictionary());
            edit.Set(website, "/Title", edit.NewText("Website"));
            QpdfObject action = edit.NewDictionary();
            edit.Set(action, "/S", edit.NewName("/URI"));
            edit.Set(action, "/URI", edit.NewText("https://example.com/"));
            edit.Set(website, "/A", action);
            return [chapter1, chapter2, website];
        });

        RemoteDocument remote = await _client.OpenAsync(data, password: null, Ct);
        IReadOnlyList<OutlineItem> outline = await _client.GetOutlineAsync(remote.Id, Ct);

        Assert.Equal(["Chapter 1", "Chapter 2", "Website"], outline.Select(i => i.Title));
        Assert.Equal(0, outline[0].PageIndex);
        Assert.Equal(792 - 700, outline[0].Top!.Value, 0.5);
        OutlineItem section = Assert.Single(outline[0].Children);
        Assert.Equal(("Section 1.1", 1), (section.Title, section.PageIndex));
        Assert.Equal(2, outline[1].PageIndex);
        Assert.Null(outline[2].PageIndex);   // listed, never followed
    }

    [Fact]
    public void ADocumentWithoutAnOutline_HasAnEmptyOne()
    {
        using IPdfDocument document = _engine.Open(Fixtures.SimpleText());
        Assert.Empty(document.GetOutline());
    }

    // A damaged or hostile outline can loop. Reading it must still finish.
    [Fact]
    public async Task AnOutlineThatLoops_IsReadOnce_AndDoesNotHang()
    {
        byte[] selfSibling = WithOutline(Fixtures.SimpleText(), edit =>
        {
            QpdfObject entry = Entry(edit, "Loop", Destination(edit, edit.Page(0), "/Fit"));
            edit.Set(entry, "/Next", entry);
            return [entry];
        });
        byte[] selfChild = WithOutline(Fixtures.SimpleText(), edit =>
        {
            QpdfObject entry = Entry(edit, "Loop", Destination(edit, edit.Page(0), "/Fit"));
            edit.Set(entry, "/First", entry);
            edit.Set(entry, "/Last", entry);
            return [entry];
        });

        foreach (byte[] data in new[] { selfSibling, selfChild })
        {
            using IPdfDocument document = _engine.Open(data);

            // A timeout here (TimeoutException) means the loop was followed.
            IReadOnlyList<OutlineItem> outline = await Task.Run(document.GetOutline, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
            Assert.Equal("Loop", Assert.Single(outline).Title);
            Assert.True(Count(outline) <= 2, $"The loop was followed: {Count(outline)} entries.");
        }
    }

    private static int Count(IReadOnlyList<OutlineItem> items) => items.Sum(i => 1 + Count(i.Children));

    // Adds an outline whose top-level entries the callback makes, linked as siblings.
    private static byte[] WithOutline(byte[] pdf, Func<QpdfEdit, QpdfObject[]> entries)
    {
        using var edit = QpdfEdit.Open(pdf);
        QpdfObject outlines = edit.MakeIndirect(edit.NewDictionary());
        edit.Set(outlines, "/Type", edit.NewName("/Outlines"));
        QpdfObject[] top = entries(edit);
        Link(edit, outlines, top);
        edit.Set(edit.Root, "/Outlines", outlines);
        return edit.Save();
    }

    private static QpdfObject Entry(QpdfEdit edit, string title, QpdfObject destination)
    {
        QpdfObject entry = edit.MakeIndirect(edit.NewDictionary());
        edit.Set(entry, "/Title", edit.NewText(title));
        edit.Set(entry, "/Dest", destination);
        return entry;
    }

    private static QpdfObject Destination(QpdfEdit edit, QpdfObject page, string fit, params long[] numbers)
    {
        QpdfObject array = edit.NewArray();
        edit.Append(array, page);
        edit.Append(array, edit.NewName(fit));
        foreach (long number in numbers)
        {
            edit.Append(array, edit.NewInteger(number));
        }

        return array;
    }

    // Makes the entries children of the parent, in order, with /Parent, /First, /Last, /Prev and /Next.
    private static void Link(QpdfEdit edit, QpdfObject parent, params QpdfObject[] children)
    {
        edit.Set(parent, "/First", children[0]);
        edit.Set(parent, "/Last", children[^1]);
        edit.Set(parent, "/Count", edit.NewInteger(children.Length));
        for (int i = 0; i < children.Length; i++)
        {
            edit.Set(children[i], "/Parent", parent);
            if (i > 0)
            {
                edit.Set(children[i], "/Prev", children[i - 1]);
            }

            if (i < children.Length - 1)
            {
                edit.Set(children[i], "/Next", children[i + 1]);
            }
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
