using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.App;

/// <summary>
/// Spike S1 measurements (docs/06-SPIKE-PLAN.md). Opens the file, waits for the first page,
/// then scrolls at a constant speed at several zoom levels, recording frame intervals, how
/// many frames showed blank or preview-only tiles, tile latency, and app plus worker memory.
/// With --kill-worker, a final phase kills the worker mid-scroll and times the recovery.
/// With --smoke-test (spike S6), it records the startup times once the first page is sharp, checks
/// that the worker loads qpdf, then searches the whole document for the last page's first line and
/// moves to that match; packaging CI uses it to launch an installed package.
/// Results go to a JSON file. The window must stay visible: a hidden window gets no frames.
/// </summary>
internal sealed class Benchmark(MainWindow window, AppOptions options)
{
    private const double ScrollSpeed = 3000; // DIPs per second

    private readonly DocumentView _view = window.View;
    private readonly List<double> _tileLatenciesMs = [];
    private readonly List<double> _tileRenderMs = [];
    private long _peakAppWorkingSet;
    private long _peakAppPrivate;
    private long _peakWorkerWorkingSet;
    private long _peakWorkerPrivate;
    private long _peakCombinedWorkingSet;

    /// <summary>
    /// Returns false if the document did not open, its first page never became sharp, or (smoke
    /// test only) the worker could not load qpdf or the search did not find and show its match.
    /// </summary>
    public async Task<bool> RunAsync()
    {
        _view.TileArrived += (latency, render) =>
        {
            _tileLatenciesMs.Add(latency.TotalMilliseconds);
            _tileRenderMs.Add(render.TotalMilliseconds);
        };

        TimeSpan startup = window.StartupTime;
        var open = Stopwatch.StartNew();
        if (!await window.OpenFileAsync(options.File!))
        {
            await WriteResultsAsync(new JsonObject { ["error"] = "The document could not be opened." });
            return false;
        }

        double openMs = open.Elapsed.TotalMilliseconds;
        bool firstPageSharp = await WaitForFrameAsync(s => s.VisibleTiles > 0 && s.SharpTiles == s.VisibleTiles, TimeSpan.FromSeconds(30));
        double firstPageMs = open.Elapsed.TotalMilliseconds;

        if (options.SmokeTest)
        {
            // The worker loads qpdf to answer, which checks that packaging put it where the worker looks.
            string? qpdfVersion = null, qpdfError = null;
            try
            {
                qpdfVersion = await _view.Session!.GetQpdfVersionAsync();
            }
            catch (Exception ex) when (ex is WorkerException or WorkerExitedException)
            {
                qpdfError = ex.Message;
            }

            var (searchPassed, search) = await SmokeSearchAsync();

            await WriteResultsAsync(new JsonObject
            {
                ["spikeItem"] = "S6",
                ["recordedAt"] = DateTimeOffset.Now,
                ["environment"] = EnvironmentInfo(),
                ["document"] = new JsonObject { ["file"] = Path.GetFileName(options.File), ["pages"] = _view.Session!.PageSizes.Count },
                ["firstPageSharp"] = firstPageSharp,
                ["qpdfVersion"] = qpdfVersion,
                ["qpdfError"] = qpdfError,
                ["startup"] = StartupInfo(startup, openMs, firstPageMs),
                ["search"] = search,
            });
            return firstPageSharp && qpdfVersion is not null && searchPassed;
        }

        using var sampler = new Timer(_ => SampleMemory(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));

        int pages = _view.Session!.PageSizes.Count;
        var phases = new JsonArray
        {
            await RunPhaseAsync("scroll at 100%", zoom: 1, seconds: 15, startPage: 0),
            await RunPhaseAsync("scroll at 25%", zoom: 0.25, seconds: 8, startPage: Math.Min(300, pages - 1)),
            await RunPhaseAsync("scroll at 400%", zoom: 4, seconds: 8, startPage: Math.Min(600, pages - 1)),
            await RunPhaseAsync("scroll at 800%", zoom: 8, seconds: 8, startPage: Math.Min(900, pages - 1)),
        };

        if (options.KillWorker)
        {
            phases.Add(await RunPhaseAsync("kill worker mid-scroll at 100%", zoom: 1, seconds: 8, startPage: Math.Min(1200, pages - 1), killWorker: true));
        }

        SampleMemory();
        await WriteResultsAsync(new JsonObject
        {
            ["spikeItem"] = "S1",
            ["recordedAt"] = DateTimeOffset.Now,
            ["environment"] = EnvironmentInfo(),
            ["document"] = new JsonObject { ["file"] = Path.GetFileName(options.File), ["pages"] = pages },
            ["startup"] = StartupInfo(startup, openMs, firstPageMs),
            ["phases"] = phases,
            ["tiles"] = new JsonObject
            {
                ["count"] = _tileLatenciesMs.Count,
                ["latencyMs"] = Percentiles(_tileLatenciesMs),
                ["workerRenderMs"] = Percentiles(_tileRenderMs),
            },
            ["memory"] = new JsonObject
            {
                ["peakAppWorkingSetMb"] = Mb(_peakAppWorkingSet),
                ["peakAppPrivateMb"] = Mb(_peakAppPrivate),
                ["peakWorkerWorkingSetMb"] = Mb(_peakWorkerWorkingSet),
                ["peakWorkerPrivateMb"] = Mb(_peakWorkerPrivate),
                ["peakCombinedWorkingSetMb"] = Mb(_peakCombinedWorkingSet),
            },
        });
        return firstPageSharp;
    }

    /// <summary>
    /// Searches every page, from the first, for the first line of text on the last page, then
    /// moves to the match on the last page and waits until it is drawn on a sharp page. A
    /// document whose last page has no text skips the search.
    /// </summary>
    private async Task<(bool Passed, string Summary)> SmokeSearchAsync()
    {
        DocumentSession session = _view.Session!;
        int last = session.PageSizes.Count - 1;
        string? query;
        try
        {
            query = (await session.GetTextLinesAsync(last)).Select(l => l.Text.Trim()).FirstOrDefault(t => t.Length > 0);
        }
        catch (Exception ex) when (ex is WorkerException or WorkerExitedException)
        {
            return (false, $"FAILED: the last page's text could not be read ({ex.Message})");
        }

        if (query is null)
        {
            return (true, "skipped: the last page has no text");
        }

        var clock = Stopwatch.StartNew();
        using DocumentSearch search = DocumentSearch.Start(session, query, matchCase: false, wholeWord: false, startPage: 0);
        await search.Completion;
        long searchMs = clock.ElapsedMilliseconds;
        string found = string.Create(CultureInfo.InvariantCulture,
            $"{search.MatchCount} match(es) for [{query}] in {search.PagesSearched} pages in {searchMs} ms");
        if (search.Status != SearchStatus.Complete || search.FailedPages.Count > 0)
        {
            return (false, $"FAILED: {found}; status {search.Status}, {search.FailedPages.Count} page(s) not searched");
        }

        if (await search.FindAsync(null, last, forward: true) is not SearchHit hit || hit.Page != last)
        {
            return (false, $"FAILED: {found}; none on the last page");
        }

        clock.Restart();
        _view.SetSearch(search);
        _view.ShowHit(hit);
        bool shown = await WaitForFrameAsync(s => s.CurrentMatchVisible && s.VisibleTiles > 0 && s.SharpTiles == s.VisibleTiles, TimeSpan.FromSeconds(15));
        _view.SetSearch(null);
        return shown
            ? (true, string.Create(CultureInfo.InvariantCulture, $"{found}; shown in {clock.ElapsedMilliseconds} ms"))
            : (false, $"FAILED: {found}; the match was not shown");
    }

    private JsonObject EnvironmentInfo() => new()
    {
        ["os"] = RuntimeInformation.OSDescription,
        ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
        ["processors"] = Environment.ProcessorCount,
        ["renderScaling"] = TopLevel.GetTopLevel(_view)?.RenderScaling,
        ["viewportDips"] = new JsonObject { ["width"] = _view.Bounds.Width, ["height"] = _view.Bounds.Height },
        ["rendering"] = options.Software ? "software" : "default (GPU where available)",
        ["tileSize"] = _view.TileSize,
        ["readingElements"] = _view.ReadingElementsEnabled,
        ["build"] = typeof(Benchmark).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .OfType<System.Reflection.AssemblyConfigurationAttribute>().FirstOrDefault()?.Configuration,
        // NativeAOT has no JIT. A JIT build may still contain ReadyToRun code; the packaging script records that.
        ["codeGeneration"] = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT",
    };

    private static JsonObject StartupInfo(TimeSpan startup, double openMs, double firstPageMs) => new()
    {
        ["processStartToWindowMs"] = Math.Round(startup.TotalMilliseconds),
        ["openMs"] = Math.Round(openMs),
        ["openToFirstPageSharpMs"] = Math.Round(firstPageMs),
        ["processStartToFirstPageSharpMs"] = Math.Round(startup.TotalMilliseconds + firstPageMs),
    };

    private async Task<JsonNode> RunPhaseAsync(string name, double zoom, double seconds, int startPage, bool killWorker = false)
    {
        var settle = Stopwatch.StartNew();
        _view.SetZoom(zoom);
        _view.ScrollToPage(startPage);
        bool settled = await WaitForFrameAsync(s => s.VisibleTiles > 0 && s.SharpTiles == s.VisibleTiles, TimeSpan.FromSeconds(10));
        double settleMs = settle.Elapsed.TotalMilliseconds;

        var intervals = new List<double>();
        var renderMs = new List<double>();
        var stalls = new JsonArray();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long allocated = GC.GetTotalAllocatedBytes();
        TimeSpan gcPause = GC.GetTotalPauseDuration();
        int frames = 0, framesWithBlank = 0, framesWithPreviewOnly = 0, extraTicks = 0;
        double elapsed = 0;
        double? killedAt = null, recoveredAt = null;
        int tilesBeforeKill = 0;
        TimeSpan? last = null;
        var done = new TaskCompletionSource();
        TopLevel topLevel = TopLevel.GetTopLevel(_view)!;

        void OnFrame(TimeSpan now)
        {
            if (last is TimeSpan previous)
            {
                double dt = (now - previous).TotalMilliseconds;

                // Avalonia also runs animation-frame callbacks on render passes caused by
                // invalidation, not only once per display frame. Callbacks under 2 ms apart are
                // counted as extra ticks, not frames, so they do not dilute the frame statistics.
                if (dt < 2)
                {
                    extraTicks++;
                }
                else
                {
                    intervals.Add(dt);
                    if (dt > 50)
                    {
                        // When stalls happen, and whether a GC ran, separates warm-up from steady state.
                        JsonNode stall = new JsonObject
                        {
                            ["atSeconds"] = Math.Round(elapsed, 2),
                            ["ms"] = Math.Round(dt, 1),
                            ["gcCountSoFar"] = GC.CollectionCount(0) - gen0,
                            ["gcPauseMsSoFar"] = Math.Round((GC.GetTotalPauseDuration() - gcPause).TotalMilliseconds, 1),
                        };
                        stalls.Add(stall);
                    }
                }

                elapsed += dt / 1000;
                _view.ScrollBy(ScrollSpeed * dt / 1000);
            }

            last = now;
            FrameStats stats = _view.LastFrame;
            frames++;
            renderMs.Add(stats.RenderTime.TotalMilliseconds);
            if (stats.BlankTiles > 0)
            {
                framesWithBlank++;
            }
            else if (stats.PreviewOnlyTiles > 0)
            {
                framesWithPreviewOnly++;
            }

            if (killWorker && killedAt is null && elapsed >= seconds / 2)
            {
                tilesBeforeKill = _tileLatenciesMs.Count;
                _view.Session!.KillWorker();
                killedAt = elapsed;
            }
            else if (killedAt is not null && recoveredAt is null
                && _view.Session!.State == SessionState.Ready && _tileLatenciesMs.Count > tilesBeforeKill)
            {
                recoveredAt = elapsed;
            }

            if (elapsed >= seconds || _view.AtEnd)
            {
                done.TrySetResult();
            }
            else
            {
                topLevel.RequestAnimationFrame(OnFrame);
            }
        }

        topLevel.RequestAnimationFrame(OnFrame);
        await done.Task;

        return new JsonObject
        {
            ["name"] = name,
            ["zoom"] = zoom,
            ["settledBeforeScroll"] = settled,
            ["zoomAndJumpToSharpMs"] = Math.Round(settleMs),
            ["seconds"] = Math.Round(elapsed, 1),
            ["frames"] = frames,
            ["extraTicks"] = extraTicks,
            ["frameIntervalMs"] = Percentiles(intervals),
            ["stallsOver50Ms"] = stalls,
            ["gc"] = new JsonObject
            {
                ["gen0"] = GC.CollectionCount(0) - gen0,
                ["gen1"] = GC.CollectionCount(1) - gen1,
                ["gen2"] = GC.CollectionCount(2) - gen2,
                ["pauseMs"] = Math.Round((GC.GetTotalPauseDuration() - gcPause).TotalMilliseconds, 1),
                ["allocatedMb"] = Math.Round((GC.GetTotalAllocatedBytes() - allocated) / (1024.0 * 1024.0), 1),
            },
            ["framesOver33Ms"] = intervals.Count(i => i > 33.4),
            ["uiThreadDrawMs"] = Percentiles(renderMs),
            ["framesWithBlankTilesPercent"] = Percent(framesWithBlank, frames),
            ["framesWithPreviewOnlyTilesPercent"] = Percent(framesWithPreviewOnly, frames),
            ["workerKilledAtSeconds"] = killedAt is null ? null : Math.Round(killedAt.Value, 2),
            ["recoveryMs"] = killedAt is not null && recoveredAt is not null ? Math.Round((recoveredAt.Value - killedAt.Value) * 1000) : null,
            ["appStillRunning"] = true,
        };
    }

    private async Task<bool> WaitForFrameAsync(Func<FrameStats, bool> condition, TimeSpan timeout)
    {
        var result = new TaskCompletionSource<bool>();
        var clock = Stopwatch.StartNew();
        TopLevel topLevel = TopLevel.GetTopLevel(_view)!;

        void OnFrame(TimeSpan _)
        {
            if (condition(_view.LastFrame))
            {
                result.TrySetResult(true);
            }
            else if (clock.Elapsed > timeout)
            {
                result.TrySetResult(false);
            }
            else
            {
                _view.InvalidateVisual();
                topLevel.RequestAnimationFrame(OnFrame);
            }
        }

        topLevel.RequestAnimationFrame(OnFrame);
        return await result.Task;
    }

    private void SampleMemory()
    {
        try
        {
            using Process app = Process.GetCurrentProcess();
            long appWorkingSet = app.WorkingSet64;
            _peakAppWorkingSet = Math.Max(_peakAppWorkingSet, appWorkingSet);
            _peakAppPrivate = Math.Max(_peakAppPrivate, app.PrivateMemorySize64);

            if (_view.Session is { } session)
            {
                using Process worker = Process.GetProcessById(session.WorkerProcessId);
                _peakWorkerWorkingSet = Math.Max(_peakWorkerWorkingSet, worker.WorkingSet64);
                _peakWorkerPrivate = Math.Max(_peakWorkerPrivate, worker.PrivateMemorySize64);
                _peakCombinedWorkingSet = Math.Max(_peakCombinedWorkingSet, appWorkingSet + worker.WorkingSet64);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The worker exited between samples (the kill phase).
        }
    }

    private async Task WriteResultsAsync(JsonObject results)
    {
        string path = options.OutputPath
            ?? Path.Combine("artifacts", "benchmarks", $"s1-{DateTime.Now:yyyyMMdd-HHmmss}-{(options.Software ? "software" : "gpu")}-tile{_view.TileSize}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, results.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonObject Percentiles(List<double> values)
    {
        if (values.Count == 0)
        {
            return new JsonObject { ["count"] = 0 };
        }

        double[] sorted = [.. values.Order()];
        double At(double p) => Math.Round(sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)], 2);
        return new JsonObject
        {
            ["count"] = sorted.Length,
            ["p50"] = At(0.50),
            ["p95"] = At(0.95),
            ["p99"] = At(0.99),
            ["max"] = Math.Round(sorted[^1], 2),
        };
    }

    private static double Percent(int part, int whole) => whole == 0 ? 0 : Math.Round(100.0 * part / whole, 1);

    private static double Mb(long bytes) => Math.Round(bytes / (1024.0 * 1024.0), 1);
}
