using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Threading;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.App;

/// <summary>Per-frame drawing statistics, read by the status bar and the S1 benchmark.</summary>
internal readonly record struct FrameStats(
    int VisibleTiles, int SharpTiles, int PreviewOnlyTiles, int BlankTiles, TimeSpan RenderTime);

/// <summary>
/// A continuous vertical view of a document's pages, drawn from tiles that the worker renders.
/// Each visible tile is drawn sharp if cached, otherwise from the page's low-resolution
/// preview if that is cached, otherwise left blank (white). Requests are made for visible
/// tiles first, then for tiles within one viewport above and below; requests for tiles that
/// leave that range are cancelled.
/// </summary>
internal sealed class DocumentView : Control
{
    private const double PageGap = 12;
    private const double DipsPerPoint = 96.0 / 72.0;
    private const double PreviewPixelsPerPoint = 0.3;
    private const int MaxRequestsInFlight = 24;

    private static readonly IBrush Background = new ImmutableSolidColorBrush(Color.FromRgb(0x52, 0x56, 0x59));
    private static readonly IBrush PageBrush = new ImmutableSolidColorBrush(Colors.White);

    private readonly TileCache _cache = new(capacityBytes: 384L * 1024 * 1024);
    private readonly Dictionary<TileKey, CancellationTokenSource> _inFlight = [];
    private readonly HashSet<TileKey> _failed = [];
    private DocumentSession? _session;
    private double[] _pageTops = [];
    private double _extentHeight;
    private double _extentWidth;
    private double _zoom = 1;
    private Vector _offset;
    private long _frame;
    private bool _updateQueued;

    public DocumentView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>Tile edge in device pixels.</summary>
    public int TileSize { get; init; } = 256;

    public double Zoom => _zoom;

    public FrameStats LastFrame { get; private set; }

    public int RequestsInFlight => _inFlight.Count;

    public long CacheBytes => _cache.Bytes;

    public DocumentSession? Session => _session;

    /// <summary>Raised on the UI thread when a tile arrives: its latency and the worker's render time.</summary>
    public event Action<TimeSpan, TimeSpan>? TileArrived;

    /// <summary>Raised on the UI thread when what the status bar shows may have changed.</summary>
    public event Action? StatusChanged;

    public int CurrentPage
    {
        get
        {
            int index = Array.BinarySearch(_pageTops, _offset.Y + (Bounds.Height / 2));
            return Math.Clamp(index >= 0 ? index : ~index - 1, 0, Math.Max(_pageTops.Length - 1, 0));
        }
    }

    public void SetDocument(DocumentSession? session)
    {
        if (_session is not null)
        {
            _session.StateChanged -= OnSessionStateChanged;
        }

        CancelAll();
        _cache.Clear();
        _failed.Clear();
        _session = session;
        _offset = default;
        if (session is not null)
        {
            session.StateChanged += OnSessionStateChanged;
        }

        RecomputePageLayout();
        InvalidateAndUpdate();
    }

    /// <summary>Sets the zoom (1 = actual size), keeping the document point at the viewport's centre still.</summary>
    public void SetZoom(double zoom)
    {
        zoom = Math.Clamp(zoom, 0.1, 16);
        if (_session is null || zoom == _zoom)
        {
            _zoom = zoom;
            return;
        }

        double ratio = zoom / _zoom;
        var centre = new Vector(_offset.X + (Bounds.Width / 2), _offset.Y + (Bounds.Height / 2));
        _zoom = zoom;
        RecomputePageLayout();
        ScrollTo(new Vector((centre.X * ratio) - (Bounds.Width / 2), (centre.Y * ratio) - (Bounds.Height / 2)));
    }

    public void ScrollTo(Vector offset)
    {
        double maxX = Math.Max(0, _extentWidth - Bounds.Width);
        double maxY = Math.Max(0, _extentHeight - Bounds.Height);
        _offset = new Vector(Math.Clamp(offset.X, 0, maxX), Math.Clamp(offset.Y, 0, maxY));
        InvalidateAndUpdate();
    }

    public void ScrollBy(double dy) => ScrollTo(_offset + new Vector(0, dy));

    public void ScrollToPage(int pageIndex)
    {
        if (_pageTops.Length > 0)
        {
            ScrollTo(new Vector(_offset.X, _pageTops[Math.Clamp(pageIndex, 0, _pageTops.Length - 1)] - PageGap));
        }
    }

    public bool AtEnd => _offset.Y >= Math.Max(0, _extentHeight - Bounds.Height) - 0.5;

    public override void Render(DrawingContext context)
    {
        long started = Stopwatch.GetTimestamp();
        _frame++;
        context.FillRectangle(Background, new Rect(Bounds.Size));
        if (_session is null)
        {
            return;
        }

        int visible = 0, sharp = 0, previewOnly = 0, blank = 0;
        double scaling = RenderScaling;
        double pixelsPerPoint = DipsPerPoint * _zoom * scaling;
        long scaleKey = TileKey.ToScaleKey(pixelsPerPoint);

        foreach (int page in VisiblePages())
        {
            Rect pageRect = PageRect(page);
            context.FillRectangle(PageBrush, pageRect);

            var key = new TileKey(page, TileKey.ToScaleKey(PreviewPixelsPerPoint), -1, -1);
            bool hasPreview = _cache.TryGet(key, _frame, out WriteableBitmap preview);
            if (hasPreview)
            {
                context.DrawImage(preview, new Rect(preview.Size), pageRect);
            }

            foreach (var (tile, region) in VisibleTiles(page, pixelsPerPoint, scaleKey))
            {
                visible++;
                if (_cache.TryGet(tile, _frame, out WriteableBitmap bitmap))
                {
                    sharp++;
                    var destination = new Rect(
                        pageRect.X + (region.Left / scaling),
                        pageRect.Y + (region.Top / scaling),
                        region.Width / scaling,
                        region.Height / scaling);
                    context.DrawImage(bitmap, new Rect(0, 0, region.Width, region.Height), destination);
                }
                else if (hasPreview)
                {
                    previewOnly++;
                }
                else
                {
                    blank++;
                }
            }
        }

        LastFrame = new FrameStats(visible, sharp, previewOnly, blank, Stopwatch.GetElapsedTime(started));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RecomputePageLayout();
        ScrollTo(_offset);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SetZoom(_zoom * (e.Delta.Y > 0 ? 1.25 : 0.8));
        }
        else
        {
            ScrollTo(_offset - new Vector(e.Delta.X * 100, e.Delta.Y * 100));
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Down: ScrollBy(60); break;
            case Key.Up: ScrollBy(-60); break;
            case Key.PageDown or Key.Space: ScrollBy(Bounds.Height * 0.9); break;
            case Key.PageUp: ScrollBy(-Bounds.Height * 0.9); break;
            case Key.Home: ScrollTo(new Vector(_offset.X, 0)); break;
            case Key.End: ScrollTo(new Vector(_offset.X, double.MaxValue)); break;
            case Key.OemPlus or Key.Add when control: SetZoom(_zoom * 1.25); break;
            case Key.OemMinus or Key.Subtract when control: SetZoom(_zoom * 0.8); break;
            case Key.D0 or Key.NumPad0 when control: SetZoom(1); break;
            default: e.Handled = false; break;
        }
    }

    private double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    private void RecomputePageLayout()
    {
        IReadOnlyList<PageSize> sizes = _session?.PageSizes ?? [];
        _pageTops = new double[sizes.Count];
        double y = PageGap;
        double widest = 0;
        for (int i = 0; i < sizes.Count; i++)
        {
            _pageTops[i] = y;
            y += (sizes[i].Height * DipsPerPoint * _zoom) + PageGap;
            widest = Math.Max(widest, sizes[i].Width * DipsPerPoint * _zoom);
        }

        _extentHeight = y;
        _extentWidth = widest + (2 * PageGap);
    }

    private Rect PageRect(int page)
    {
        PageSize size = _session!.PageSizes[page];
        double width = size.Width * DipsPerPoint * _zoom;
        double height = size.Height * DipsPerPoint * _zoom;
        double x = _extentWidth <= Bounds.Width
            ? (Bounds.Width - width) / 2
            : ((_extentWidth - width) / 2) - _offset.X;
        return new Rect(x, _pageTops[page] - _offset.Y, width, height);
    }

    private IEnumerable<int> VisiblePages() => PagesBetween(_offset.Y, _offset.Y + Bounds.Height);

    private IEnumerable<int> PagesBetween(double top, double bottom)
    {
        if (_pageTops.Length == 0)
        {
            yield break;
        }

        int index = Array.BinarySearch(_pageTops, top);
        int first = Math.Max(0, index >= 0 ? index : ~index - 1);
        for (int page = first; page < _pageTops.Length && _pageTops[page] < bottom; page++)
        {
            yield return page;
        }
    }

    /// <summary>The tiles of one page that intersect the band [top, bottom] (document DIPs), with their pixel regions.</summary>
    private IEnumerable<(TileKey Key, PixelRegion Region)> TilesBetween(int page, double pixelsPerPoint, long scaleKey, double top, double bottom)
    {
        double scaling = RenderScaling;
        Rect pageRect = PageRect(page);
        var (pageWidth, pageHeight) = _session!.PageSizes[page].ToPixels(pixelsPerPoint);

        // Band and viewport in this page's device pixels.
        double pageTop = _pageTops[page];
        int left = (int)Math.Floor(Math.Max(0, -pageRect.X) * scaling);
        int right = (int)Math.Ceiling(Math.Min(pageRect.Width, Bounds.Width - pageRect.X) * scaling);
        int upper = (int)Math.Floor(Math.Max(0, top - pageTop) * scaling);
        int lower = (int)Math.Ceiling(Math.Min(pageRect.Height, bottom - pageTop) * scaling);
        if (right <= left || lower <= upper)
        {
            yield break;
        }

        int tile = TileSize;
        for (int ty = upper / tile; ty <= Math.Min((lower - 1) / tile, (pageHeight - 1) / tile); ty++)
        {
            for (int tx = left / tile; tx <= Math.Min((right - 1) / tile, (pageWidth - 1) / tile); tx++)
            {
                int x = tx * tile;
                int y = ty * tile;
                yield return (new TileKey(page, scaleKey, tx, ty),
                    new PixelRegion(x, y, Math.Min(tile, pageWidth - x), Math.Min(tile, pageHeight - y)));
            }
        }
    }

    private IEnumerable<(TileKey Key, PixelRegion Region)> VisibleTiles(int page, double pixelsPerPoint, long scaleKey) =>
        TilesBetween(page, pixelsPerPoint, scaleKey, _offset.Y, _offset.Y + Bounds.Height);

    private void InvalidateAndUpdate()
    {
        InvalidateVisual();
        StatusChanged?.Invoke();
        if (!_updateQueued)
        {
            _updateQueued = true;
            Dispatcher.UIThread.Post(UpdateRequests, DispatcherPriority.Background);
        }
    }

    /// <summary>Cancels requests that are no longer wanted and starts the most wanted missing tiles.</summary>
    private void UpdateRequests()
    {
        _updateQueued = false;
        if (_session is null || _session.State != SessionState.Ready)
        {
            return;
        }

        double pixelsPerPoint = DipsPerPoint * _zoom * RenderScaling;
        long scaleKey = TileKey.ToScaleKey(pixelsPerPoint);
        double margin = Bounds.Height;
        double viewTop = _offset.Y;
        double viewBottom = _offset.Y + Bounds.Height;

        // In order of priority: previews of visible pages (cheap, and they fill gaps at once),
        // visible tiles, then tiles and previews within one viewport above and below.
        var wanted = new List<(TileKey Key, PixelRegion Region, double PixelsPerPoint)>();
        foreach (int page in PagesBetween(viewTop, viewBottom))
        {
            wanted.Add(Preview(page));
        }

        foreach (int page in PagesBetween(viewTop, viewBottom))
        {
            foreach (var (key, region) in TilesBetween(page, pixelsPerPoint, scaleKey, viewTop, viewBottom))
            {
                wanted.Add((key, region, pixelsPerPoint));
            }
        }

        foreach (int page in PagesBetween(viewTop - margin, viewBottom + margin))
        {
            wanted.Add(Preview(page));
            foreach (var (key, region) in TilesBetween(page, pixelsPerPoint, scaleKey, viewTop - margin, viewBottom + margin))
            {
                wanted.Add((key, region, pixelsPerPoint));
            }
        }

        var wantedKeys = new HashSet<TileKey>(wanted.Select(w => w.Key));
        foreach (var (key, cts) in _inFlight.ToArray())
        {
            if (!wantedKeys.Contains(key))
            {
                cts.Cancel();
                _inFlight.Remove(key);
            }
        }

        foreach (var (key, region, scale) in wanted)
        {
            if (_inFlight.Count >= MaxRequestsInFlight)
            {
                break;
            }

            if (!_cache.Contains(key) && !_inFlight.ContainsKey(key) && !_failed.Contains(key))
            {
                var cts = new CancellationTokenSource();
                _inFlight[key] = cts;
                _ = RequestTileAsync(_session, key, scale, region, cts);
            }
        }

        StatusChanged?.Invoke();
    }

    private (TileKey Key, PixelRegion Region, double PixelsPerPoint) Preview(int page)
    {
        var (width, height) = _session!.PageSizes[page].ToPixels(PreviewPixelsPerPoint);
        return (new TileKey(page, TileKey.ToScaleKey(PreviewPixelsPerPoint), -1, -1),
            new PixelRegion(0, 0, Math.Min(width, 4096), Math.Min(height, 4096)),
            PreviewPixelsPerPoint);
    }

    private async Task RequestTileAsync(
        DocumentSession session, TileKey key, double pixelsPerPoint, PixelRegion region, CancellationTokenSource cts)
    {
        long requested = Stopwatch.GetTimestamp();
        bool requestAgainNow = true;
        try
        {
            // Started on the UI thread, so the code after the await runs there too.
            using RenderedTile tile = await session.RenderTileAsync(key.Page, pixelsPerPoint, region, cts.Token);
            if (session != _session)
            {
                return;
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(tile.Width, tile.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (ILockedFramebuffer frame = bitmap.Lock())
            {
                CopyPixels(tile, frame);
            }

            _cache.Add(key, bitmap, (long)tile.Width * tile.Height * 4, _frame);
            TileArrived?.Invoke(Stopwatch.GetElapsedTime(requested), tile.RenderTime);
        }
        catch (OperationCanceledException)
        {
            // No longer wanted.
        }
        catch (WorkerExitedException)
        {
            // The session restarts the worker and raises StateChanged(Ready); requesting again
            // before then would only send requests to the worker that has gone.
            requestAgainNow = false;
        }
        catch (WorkerException)
        {
            // Do not retry a tile the engine could not render; it stays blank.
            _failed.Add(key);
        }
        finally
        {
            if (_inFlight.TryGetValue(key, out CancellationTokenSource? current) && current == cts)
            {
                _inFlight.Remove(key);
            }

            cts.Dispose();
            if (session == _session && requestAgainNow)
            {
                InvalidateAndUpdate();
            }
        }
    }

    private static unsafe void CopyPixels(RenderedTile tile, ILockedFramebuffer frame)
    {
        ReadOnlySpan<byte> source = tile.Pixels;
        int rowBytes = tile.Width * 4;
        for (int y = 0; y < tile.Height; y++)
        {
            source.Slice(y * rowBytes, rowBytes)
                .CopyTo(new Span<byte>((byte*)frame.Address + ((long)y * frame.RowBytes), rowBytes));
        }
    }

    private void OnSessionStateChanged(SessionState state) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (state == SessionState.Ready)
            {
                // Requests that were in flight failed with the old worker; ask again.
                CancelAll();
            }

            InvalidateAndUpdate();
        });

    private void CancelAll()
    {
        foreach (CancellationTokenSource cts in _inFlight.Values)
        {
            cts.Cancel();
        }

        _inFlight.Clear();
    }
}
