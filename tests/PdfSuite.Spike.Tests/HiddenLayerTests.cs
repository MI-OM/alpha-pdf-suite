using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Qpdf;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Spike finding 32: text in a layer that is off by default is not read to screen-reader users.
/// canary-document-level.pdf draws CANARY-13 in such a layer, named "Hidden layer".
/// </summary>
public sealed class HiddenLayerTests : IDisposable
{
    private const string Layer = "Hidden layer";
    private readonly WorkerClient _client = WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()));

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task ReadingLines_LeaveOutTextInALayerThatIsOff()
    {
        byte[] source = Fixtures.CanaryDocumentLevel();
        RemoteDocument document = await _client.OpenAsync(source, password: null, Ct);

        IReadOnlyList<TextLine> lines = await _client.GetTextLinesAsync(document.Id, 0, Ct);

        Assert.DoesNotContain(lines, l => l.Text.Contains("CANARY-13"));
        Assert.NotEmpty(lines);

        // Text extraction for redaction still finds it: hidden text must be removed too.
        using IPdfDocument local = new PdfiumEngine().Open(source);
        Assert.Contains("CANARY-13", local.GetPageText(0));
    }

    // Negative control: the same file with the layer switched on is read in full.
    [Fact]
    public async Task ReadingLines_KeepTextInALayerThatIsOn()
    {
        byte[] layerOn = WithDefaultConfiguration(Fixtures.CanaryDocumentLevel(), (edit, defaults, layer) => edit.Set(defaults, "/OFF", edit.NewArray()));
        RemoteDocument document = await _client.OpenAsync(layerOn, password: null, Ct);

        IReadOnlyList<TextLine> lines = await _client.GetTextLinesAsync(document.Id, 0, Ct);

        Assert.Contains(lines, l => l.Text.Contains("CANARY-13"));
    }

    [Fact]
    public void HiddenLayerNames_FollowsTheDefaultConfiguration()
    {
        byte[] fixture = Fixtures.CanaryDocumentLevel();

        // /BaseState /ON (the default), with the layer listed in /OFF.
        Assert.Equal([Layer], QpdfStructure.HiddenLayerNames(fixture));

        // /BaseState /OFF: hidden unless listed in /ON.
        Assert.Equal([Layer], QpdfStructure.HiddenLayerNames(WithDefaultConfiguration(fixture, (edit, defaults, layer) =>
        {
            edit.Set(defaults, "/BaseState", edit.NewName("/OFF"));
            edit.Remove(defaults, "/OFF");
        })));
        Assert.Empty(QpdfStructure.HiddenLayerNames(WithDefaultConfiguration(fixture, (edit, defaults, layer) =>
        {
            edit.Set(defaults, "/BaseState", edit.NewName("/OFF"));
            QpdfObject on = edit.NewArray();
            edit.Append(on, layer);
            edit.Set(defaults, "/ON", on);
        })));

        // No layers at all.
        Assert.Empty(QpdfStructure.HiddenLayerNames(Fixtures.SimpleText()));
    }

    // A second layer with the same name that is on: the name is ambiguous, so nothing is hidden.
    [Fact]
    public void HiddenLayerNames_LeavesOutANameThatAVisibleLayerAlsoUses()
    {
        byte[] twoLayers = WithDefaultConfiguration(Fixtures.CanaryDocumentLevel(), (edit, defaults, layer) =>
        {
            QpdfObject twin = edit.MakeIndirect(edit.NewDictionary());
            edit.Set(twin, "/Type", edit.NewName("/OCG"));
            edit.Set(twin, "/Name", edit.NewText(Layer));
            edit.Append(edit.Get(edit.Get(edit.Root, "/OCProperties"), "/OCGs"), twin);
        });

        Assert.Empty(QpdfStructure.HiddenLayerNames(twoLayers));
    }

    private static byte[] WithDefaultConfiguration(byte[] pdf, Action<QpdfEdit, QpdfObject, QpdfObject> change)
    {
        using var edit = QpdfEdit.Open(pdf);
        QpdfObject properties = edit.Get(edit.Root, "/OCProperties");
        QpdfObject layer = edit.Items(edit.Get(properties, "/OCGs"))[0];
        change(edit, edit.Get(properties, "/D"), layer);
        return edit.Save();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
