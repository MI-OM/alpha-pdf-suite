using System.Diagnostics;
using System.Text;
using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>Spike S1: the app-to-worker rendering pipeline, run against a real worker process.</summary>
public sealed class WorkerPipelineTests : IDisposable
{
    private readonly WorkerClient _client = StartWorker();

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Open_ReturnsPageSizes()
    {
        RemoteDocument document = await _client.OpenAsync(Fixtures.CanaryText(), password: null, Ct);

        Assert.Equal([new PageSize(612, 792)], document.PageSizes);
    }

    [Fact]
    public async Task Open_InvalidData_ThrowsFormatError()
    {
        var error = await Assert.ThrowsAsync<PdfOpenException>(
            () => _client.OpenAsync(Encoding.ASCII.GetBytes("not a pdf"), password: null, Ct));

        Assert.Equal(PdfOpenError.Format, error.Error);
    }

    [Fact]
    public async Task GetTextLines_ReturnsTheSameLinesAsTheEngine()
    {
        byte[] data = Fixtures.CanaryText();
        RemoteDocument document = await _client.OpenAsync(data, password: null, Ct);

        IReadOnlyList<TextLine> remote = await _client.GetTextLinesAsync(document.Id, 0, Ct);

        using IPdfDocument local = new PdfSuite.Pdf.Pdfium.PdfiumEngine().Open(data);
        Assert.Equal(local.GetTextLines(0), remote);
        Assert.Contains(remote, l => l.Text.Contains("CANARY-01"));
    }

    // Spike S6: packaging smoke tests use this to check that qpdf's library ships next to the worker.
    [Fact]
    public async Task GetQpdfVersion_ReportsTheLibraryTheWorkerLoaded()
    {
        string version = await _client.GetQpdfVersionAsync(Ct);

        Assert.Equal(PdfSuite.Pdf.Qpdf.QpdfStructure.LibraryVersion, version);
        Assert.StartsWith("12.4.", version);
    }

    [Fact]
    public async Task RenderTile_DrawsTextOnlyWhereThePageHasIt()
    {
        RemoteDocument document = await _client.OpenAsync(Fixtures.CanaryText(), password: null, Ct);

        // "CANARY-01" is set at 12 pt with its baseline at (180, 500) in PDF space, which is
        // y = 792 - 500 = 292 from the top at 1 pixel per point.
        using RenderedTile text = await _client.RenderTileAsync(document.Id, 0, 1.0, new PixelRegion(176, 278, 80, 18), Ct);
        using RenderedTile empty = await _client.RenderTileAsync(document.Id, 0, 1.0, new PixelRegion(400, 120, 100, 100), Ct);

        Assert.True(DarkPixels(text) > 50, $"Expected text in the canary region; found {DarkPixels(text)} dark pixels.");
        Assert.Equal(0, DarkPixels(empty));
    }

    // A tile that is placed wrongly differs from the full render everywhere. A correctly placed
    // tile differs only where a glyph straddles the tile edge: PDFium snaps each glyph to whole
    // pixels from the tile's own origin, so a straddling glyph can be anti-aliased slightly
    // differently. Measured at 1.5x: all differing pixels lie within 12 px of an edge, about
    // 0.25% of the page, with no visible seam (spike results, S1).
    [Fact]
    public async Task RenderTile_MatchesTheFullPageExceptForGlyphsOnTileEdges()
    {
        RemoteDocument document = await _client.OpenAsync(Fixtures.SimpleText(), password: null, Ct);
        const double scale = 1.5;
        var (width, height) = document.PageSizes[0].ToPixels(scale);
        using RenderedTile full = await _client.RenderTileAsync(document.Id, 0, scale, new PixelRegion(0, 0, width, height), Ct);

        const int tile = 256;
        const int edgeBand = 12;
        int edgeDifferences = 0;
        for (int y = 0; y < height; y += tile)
        {
            for (int x = 0; x < width; x += tile)
            {
                var region = new PixelRegion(x, y, Math.Min(tile, width - x), Math.Min(tile, height - y));
                using RenderedTile part = await _client.RenderTileAsync(document.Id, 0, scale, region, Ct);
                for (int row = 0; row < region.Height; row++)
                {
                    for (int column = 0; column < region.Width; column++)
                    {
                        int expected = ((((y + row) * width) + x + column) * 4);
                        int actual = ((row * region.Width) + column) * 4;
                        if (full.Pixels.Slice(expected, 4).SequenceEqual(part.Pixels.Slice(actual, 4)))
                        {
                            continue;
                        }

                        bool nearEdge = Math.Min(column, tile - 1 - column) < edgeBand || Math.Min(row, tile - 1 - row) < edgeBand;
                        Assert.True(nearEdge, $"Tile ({x}, {y}) differs from the full render at ({column}, {row}), away from its edges.");
                        edgeDifferences++;
                    }
                }
            }
        }

        Assert.True(edgeDifferences < width * height / 100, $"{edgeDifferences} pixels differ near tile edges; expected under 1%.");
    }

    [Fact]
    public async Task CancelledRequests_CompleteAsCancelled_AndTheWorkerKeepsServing()
    {
        RemoteDocument document = await _client.OpenAsync(Fixtures.LargeMixed(pageCount: 40), password: null, Ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var requests = Enumerable.Range(0, 40)
            .Select(page => _client.RenderTileAsync(document.Id, page, 4.0, new PixelRegion(0, 0, 1024, 1024), cts.Token))
            .ToList();
        cts.Cancel();

        int cancelled = 0;
        foreach (Task<RenderedTile> request in requests)
        {
            try
            {
                (await request).Dispose();
            }
            catch (OperationCanceledException)
            {
                cancelled++;
            }
        }

        Assert.True(cancelled > 0, "Expected at least some requests to be cancelled before they ran.");
        using RenderedTile after = await _client.RenderTileAsync(document.Id, 0, 1.0, new PixelRegion(0, 0, 64, 64), Ct);
        Assert.Equal(64, after.Width);
    }

    [Fact]
    public async Task KillingTheWorker_FailsPendingRequests_AndRaisesExited()
    {
        RemoteDocument document = await _client.OpenAsync(Fixtures.LargeMixed(pageCount: 40), password: null, Ct);
        var exited = new TaskCompletionSource<WorkerExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Exited += (_, exit) => exited.TrySetResult(exit);

        var requests = Enumerable.Range(0, 40)
            .Select(page => _client.RenderTileAsync(document.Id, page, 4.0, new PixelRegion(0, 0, 1024, 1024), Ct))
            .ToList();
        _client.Kill();

        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        int failed = 0;
        foreach (Task<RenderedTile> request in requests)
        {
            try
            {
                (await request).Dispose();
            }
            catch (WorkerExitedException)
            {
                failed++;
            }
        }

        Assert.True(failed > 0, "Expected requests still pending at the kill to fail.");
        Assert.True(_client.HasExited);
        await Assert.ThrowsAsync<WorkerExitedException>(
            () => _client.RenderTileAsync(document.Id, 0, 1.0, new PixelRegion(0, 0, 64, 64), Ct));
    }

    [Fact]
    public async Task Session_RestartsTheWorkerAfterACrash_AndKeepsRendering()
    {
        using DocumentSession session = await DocumentSession.OpenAsync(StartWorker, Fixtures.CanaryText(), password: null, Ct);
        int firstWorker = session.WorkerProcessId;
        Task ready = NextState(session, SessionState.Ready);

        session.KillWorker();
        await ready.WaitAsync(TimeSpan.FromSeconds(15), Ct);

        Assert.NotEqual(firstWorker, session.WorkerProcessId);
        using RenderedTile tile = await session.RenderTileAsync(0, 1.0, new PixelRegion(176, 278, 80, 18), Ct);
        Assert.True(DarkPixels(tile) > 50);
    }

    [Fact]
    public async Task Session_StopsRestartingAfterRepeatedCrashes()
    {
        using DocumentSession session = await DocumentSession.OpenAsync(StartWorker, Fixtures.CanaryText(), password: null, Ct);
        for (int crash = 1; crash <= 3; crash++)
        {
            Task ready = NextState(session, SessionState.Ready);
            session.KillWorker();
            await ready.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        }

        Task failed = NextState(session, SessionState.Failed);
        session.KillWorker();
        await failed.WaitAsync(TimeSpan.FromSeconds(15), Ct);

        await Assert.ThrowsAsync<WorkerExitedException>(() => session.RenderTileAsync(0, 1.0, new PixelRegion(0, 0, 64, 64), Ct));
    }

    [Fact]
    public async Task WorkerMemoryLimit_IsEnforcedOnEveryOs()
    {
        using WorkerClient limited = WorkerClient.Start(
            new WorkerOptions(WorkerOptions.DefaultWorkerPath()) { EnableTestCommands = true, MemoryLimitBytes = 256L * 1024 * 1024 });

        if (OperatingSystem.IsMacOS())
        {
            // macOS: the client sees the footprint go over the limit and stops the worker.
            await Assert.ThrowsAsync<WorkerExitedException>(() => limited.AllocateForTestAsync(megabytes: 768, holdMilliseconds: 5000));
            Assert.True(limited.HasExited);
            return;
        }

        // Windows (Job Object) and Linux (RLIMIT_DATA): the allocation is refused.
        var error = await Assert.ThrowsAsync<WorkerException>(() => limited.AllocateForTestAsync(megabytes: 512));
        Assert.Equal(WorkerErrorCode.OutOfMemory, error.Code);

        // The refusal is recoverable: the worker is still alive and serving.
        await limited.AllocateForTestAsync(megabytes: 32);
        Assert.False(limited.HasExited);
    }

    // Spike S3. Prints through the worker to Microsoft Print to PDF with an output file, so no
    // dialog appears, then checks the printed PDF. Text arrives as glyph outlines (PDFium cannot
    // print GDI text in these builds), so the check is pages, size, and ink where the text is.
    // Skips where that printer is not installed.
    [Fact]
    public async Task Print_ToMicrosoftPrintToPdf_ProducesEveryPageAtTheRightSize()
    {
        Assert.SkipUnless(PrintToPdfInstalled(), "Needs Windows with the Microsoft Print to PDF printer (spike S3).");

        RemoteDocument document = await _client.OpenAsync(Fixtures.SimpleText(), password: null, Ct);
        string output = Path.Combine(Path.GetTempPath(), $"pdfsuite-s3-{Guid.NewGuid():N}.pdf");
        try
        {
            int printed = await _client.PrintAsync(
                document.Id, new PrintJob("Microsoft Print to PDF", [], [0, 1, 2], "PDF Suite S3 test", output), Ct);
            Assert.Equal(3, printed);

            using IPdfDocument result = new PdfSuite.Pdf.Pdfium.PdfiumEngine().Open(await WaitForPrintedFileAsync(output));
            Assert.Equal(3, result.PageCount);
            Assert.Equal(new PageSize(612, 792), result.GetPageSize(0));

            // The heading "Simple text page 1" is at 16 pt with its baseline 72 pt from the top.
            var heading = new PixelRegion(72, 52, 200, 24);
            var margin = new PixelRegion(400, 20, 150, 40);
            Assert.True(DarkPixels(result, heading) > 200, "Expected the printed heading where the original has it.");
            Assert.Equal(0, DarkPixels(result, margin));
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void DisposingTheClient_EndsTheWorkerProcess()
    {
        WorkerClient client = StartWorker();
        int processId = client.ProcessId;

        client.Dispose();

        Assert.Throws<ArgumentException>(() => Process.GetProcessById(processId));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool PrintToPdfInstalled() =>
        OperatingSystem.IsWindows()
        && Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\Microsoft Print to PDF") is not null;

    // The spooler writes the file after EndDoc returns, so wait until it is complete and readable.
    private static async Task<byte[]> WaitForPrintedFileAsync(string path)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            try
            {
                if (File.Exists(path))
                {
                    byte[] bytes = await File.ReadAllBytesAsync(path, Ct);
                    if (bytes.AsSpan().LastIndexOf("%%EOF"u8) > 0)
                    {
                        return bytes;
                    }
                }
            }
            catch (IOException)
            {
                // Still being written.
            }

            await Task.Delay(250, Ct);
        }

        throw new TimeoutException($"The printed file '{path}' did not appear within 60 s.");
    }

    private static WorkerClient StartWorker() =>
        WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()) { EnableTestCommands = true });

    private static Task NextState(DocumentSession session, SessionState wanted)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(SessionState state)
        {
            if (state == wanted)
            {
                session.StateChanged -= Handler;
                reached.TrySetResult();
            }
        }

        session.StateChanged += Handler;
        return reached.Task;
    }

    private static int DarkPixels(IPdfDocument document, PixelRegion region)
    {
        var pixels = new byte[region.Width * region.Height * 4];
        document.RenderRegion(0, 1.0, region, pixels);
        int dark = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 128 && pixels[i + 1] < 128 && pixels[i + 2] < 128)
            {
                dark++;
            }
        }

        return dark;
    }

    private static int DarkPixels(RenderedTile tile)
    {
        ReadOnlySpan<byte> pixels = tile.Pixels;
        int dark = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 128 && pixels[i + 1] < 128 && pixels[i + 2] < 128)
            {
                dark++;
            }
        }

        return dark;
    }
}
