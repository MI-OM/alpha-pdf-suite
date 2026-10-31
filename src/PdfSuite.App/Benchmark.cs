using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.App;

/// <summary>
/// Spike S1 measurements (docs/06-SPIKE-PLAN.md). Opens the file, waits for the first page,
/// then scrolls at a constant speed at several zoom levels, recording frame intervals, how
/// many frames showed blank or preview-only tiles, tile latency, and app plus worker memory.
/// With --kill-worker, a final phase kills the worker mid-scroll and times the recovery.
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

    public async Task RunAsync()
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
            await WriteResultsAsync(new { error = "The document could not be opened." });
            return;
        }

        double openMs = open.Elapsed.TotalMilliseconds;
        await WaitForFrameAsync(s => s.VisibleTiles > 0 && s.SharpTiles == s.VisibleTiles, TimeSpan.FromSeconds(30));
        double firstPageMs = open.Elapsed.TotalMilliseconds;

        using var sampler = new Timer(_ => SampleMemory(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));

        int pages = _view.Session!.PageSizes.Count;
        var phases = new List<object>
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
        await WriteResultsAsync(new
        {
            spikeItem = "S1",
            recordedAt = DateTimeOffset.Now,
            environment = new
            {
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.OSArchitecture.ToString(),
                processors = Environment.ProcessorCount,
                renderScaling = TopLevel.GetTopLevel(_view)?.RenderScaling,
                viewportDips = new { width = _view.Bounds.Width, height = _view.Bounds.Height },
                rendering = options.Software ? "software" : "default (GPU where available)",
                tileSize = _view.TileSize,
                build = typeof(Benchmark).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
                    .OfType<System.Reflection.AssemblyConfigurationAttribute>().FirstOrDefault()?.Configuration,
            },
            document = new { file = Path.GetFileName(options.File), pages },
            startup = new
            {
                processStartToWindowMs = Math.Round(startup.TotalMilliseconds),
                openMs = Math.Round(openMs),
                openToFirstPageSharpMs = Math.Round(firstPageMs),
                processStartToFirstPageSharpMs = Math.Round(startup.TotalMilliseconds + firstPageMs),
            },
            phases,
            tiles = new
            {
                count = _tileLatenciesMs.Count,
                latencyMs = Percentiles(_tileLatenciesMs),
                workerRenderMs = Percentiles(_tileRenderMs),
            },
            memory = new
            {
                peakAppWorkingSetMb = Mb(_peakAppWorkingSet),
                peakAppPrivateMb = Mb(_peakAppPrivate),
                peakWorkerWorkingSetMb = Mb(_peakWorkerWorkingSet),
                peakWorkerPrivateMb = Mb(_peakWorkerPrivate),
                peakCombinedWorkingSetMb = Mb(_peakCombinedWorkingSet),
            },
        });
    }

    private async Task<object> RunPhaseAsync(string name, double zoom, double seconds, int startPage, bool killWorker = false)
    {
        var settle = Stopwatch.StartNew();
        _view.SetZoom(zoom);
        _view.ScrollToPage(startPage);
        bool settled = await WaitForFrameAsync(s => s.VisibleTiles > 0 && s.SharpTiles == s.VisibleTiles, TimeSpan.FromSeconds(10));
        double settleMs = settle.Elapsed.TotalMilliseconds;

        var intervals = new List<double>();
        var renderMs = new List<double>();
        int frames = 0, framesWithBlank = 0, framesWithPreviewOnly = 0;
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
                intervals.Add(dt);
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

        return new
        {
            name,
            zoom,
            settledBeforeScroll = settled,
            zoomAndJumpToSharpMs = Math.Round(settleMs),
            seconds = Math.Round(elapsed, 1),
            frames,
            frameIntervalMs = Percentiles(intervals),
            framesOver33Ms = intervals.Count(i => i > 33.4),
            uiThreadDrawMs = Percentiles(renderMs),
            framesWithBlankTilesPercent = Percent(framesWithBlank, frames),
            framesWithPreviewOnlyTilesPercent = Percent(framesWithPreviewOnly, frames),
            workerKilledAtSeconds = killedAt is null ? (double?)null : Math.Round(killedAt.Value, 2),
            recoveryMs = killedAt is not null && recoveredAt is not null ? Math.Round((recoveredAt.Value - killedAt.Value) * 1000) : (double?)null,
            appStillRunning = true,
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

    private async Task WriteResultsAsync(object results)
    {
        string path = options.OutputPath
            ?? Path.Combine("artifacts", "benchmarks", $"s1-{DateTime.Now:yyyyMMdd-HHmmss}-{(options.Software ? "software" : "gpu")}-tile{_view.TileSize}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Percentiles(List<double> values)
    {
        if (values.Count == 0)
        {
            return new { count = 0 };
        }

        double[] sorted = [.. values.Order()];
        double At(double p) => Math.Round(sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)], 2);
        return new { count = sorted.Length, p50 = At(0.50), p95 = At(0.95), p99 = At(0.99), max = Math.Round(sorted[^1], 2) };
    }

    private static double Percent(int part, int whole) => whole == 0 ? 0 : Math.Round(100.0 * part / whole, 1);

    private static double Mb(long bytes) => Math.Round(bytes / (1024.0 * 1024.0), 1);
}
