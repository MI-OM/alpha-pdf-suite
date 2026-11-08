using System.Diagnostics;
using Avalonia;
using Avalonia.Automation.Peers;
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

internal enum ZoomMode
{
    Custom,
    FitWidth,
    FitPage,
}

/// <summary>Per-frame drawing statistics, read by the status bar, the S1 benchmark and the smoke test.</summary>
internal readonly record struct FrameStats(
    int VisibleTiles, int SharpTiles, int PreviewOnlyTiles, int BlankTiles, TimeSpan RenderTime,
    int MatchHighlights = 0, bool CurrentMatchVisible = false);

/// <summary>
/// A continuous vertical view of a document's pages, drawn from tiles that the worker renders.
/// Each visible tile is drawn sharp if cached, otherwise from the page's low-resolution
/// preview if that is cached, otherwise left blank (white). Requests are made for visible
/// tiles first, then for tiles within one viewport above and below; requests for tiles that
/// leave that range are cancelled.
///
/// For assistive technology (spike S2), the view hosts a <see cref="PageElement"/> for each
/// visible page and for the page being read, holding one focusable <see cref="ReadingLine"/>
/// per text line. Up and Down move the reading position line by line, PageUp and PageDown move
/// by page, and the view scrolls to keep the focused line in sight.
///
/// While a search is shown, its matches on visible pages are highlighted, and the current match
/// is drawn in a stronger colour with an outline, so it does not rely on colour alone.
/// </summary>
internal sealed class DocumentView : Control
{
    private const double PageGap = 12;
    private const double DipsPerPoint = 96.0 / 72.0;
    private const double PreviewPixelsPerPoint = 0.3;
    private const int MaxRequestsInFlight = 24;

    private static readonly IBrush Background = new ImmutableSolidColorBrush(Color.FromRgb(0x52, 0x56, 0x59));
    private static readonly IBrush PageBrush = new ImmutableSolidColorBrush(Colors.White);
    private static readonly IBrush MatchBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD5, 0x00));
    private static readonly IBrush CurrentMatchBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0xFF, 0x8C, 0x00));
    private static readonly IPen CurrentMatchPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xB3, 0x40, 0x00)), 2);

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

    private readonly Dictionary<int, IReadOnlyList<TextLine>> _textLines = [];
    private readonly HashSet<int> _unreadableText = [];
    private readonly HashSet<int> _textRequests = [];
    private readonly Dictionary<int, PageElement> _pageElements = [];
    private int? _readingPage;

    private DocumentSearch? _search;
    private SearchHit? _currentHit;
    private int _searchRedrawQueued;

    // Text selection: carets (page, position between characters) at each end, in the order made.
    private static readonly IBrush SelectionBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x60, 0x2B, 0x6C, 0xD9));
    private const int CharacterCacheSize = 48;
    private readonly Dictionary<int, PageCharacters> _characters = [];
    private readonly HashSet<int> _characterRequests = [];
    private (int Page, int Caret)? _selectionAnchor;
    private (int Page, int Caret)? _selectionFocus;
    private (int Page, Point At)? _pendingDrag;
    private bool _dragging;

    public DocumentView()
    {
        Focusable = true;
        ClipToBounds = true;

        // Tab enters the document once and lands on the last reading position, instead of
        // stepping through every line.
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Once);
    }

    /// <summary>Tile edge in device pixels.</summary>
    public int TileSize { get; init; } = 256;

    /// <summary>Whether to build the accessible page and line elements. Off only for the S2 A/B benchmark.</summary>
    public bool ReadingElementsEnabled { get; init; } = true;

    public double Zoom => _zoom;

    public FrameStats LastFrame { get; private set; }

    public int RequestsInFlight => _inFlight.Count;

    public long CacheBytes => _cache.Bytes;

    public DocumentSession? Session => _session;

    public DocumentSearch? Search => _search;

    public SearchHit? CurrentHit => _currentHit;

    /// <summary>Raised on the UI thread when a tile arrives: its latency and the worker's render time.</summary>
    public event Action<TimeSpan, TimeSpan>? TileArrived;

    /// <summary>Raised on the UI thread when what the status bar shows may have changed.</summary>
    public event Action? StatusChanged;

    /// <summary>Raised when the reader asks to copy (Ctrl+C). The window decides, as copying depends on the document's permissions.</summary>
    public event Action? CopyRequested;

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
        foreach (int page in _pageElements.Keys.ToArray())
        {
            RemovePageElement(page);
        }

        _textLines.Clear();
        _unreadableText.Clear();
        _readingPage = null;
        SetSearch(null);
        ClearSelection();
        _characters.Clear();
        _characterRequests.Clear();
        _session = session;
        _offset = default;
        if (session is not null)
        {
            session.StateChanged += OnSessionStateChanged;
        }

        RecomputePageLayout();
        InvalidateAndUpdate();
    }

    /// <summary>How the zoom was chosen. A fit mode is applied again when the view is resized.</summary>
    public ZoomMode ZoomMode { get; private set; } = ZoomMode.Custom;

    public int PageCount => _pageTops.Length;

    /// <summary>Zooms so the page in view fills the view's width, and keeps that as the view is resized.</summary>
    public void FitWidth() => ApplyFit(ZoomMode.FitWidth);

    /// <summary>Zooms so the whole page in view fits, and keeps that as the view is resized.</summary>
    public void FitPage() => ApplyFit(ZoomMode.FitPage);

    // From a command, the page in view is brought to the top; on a resize, the view only rescales.
    private void ApplyFit(ZoomMode mode, bool toPageTop = true)
    {
        if (_session is null || _pageTops.Length == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        int page = CurrentPage;
        PageSize size = _session.PageSizes[page];
        double byWidth = (Bounds.Width - (2 * PageGap)) / (size.Width * DipsPerPoint);
        double byHeight = (Bounds.Height - (2 * PageGap)) / (size.Height * DipsPerPoint);
        SetZoom(mode == ZoomMode.FitPage ? Math.Min(byWidth, byHeight) : byWidth);
        ZoomMode = mode;

        // Pages are centred across the widest page, so a wider page elsewhere (a landscape page in
        // a portrait document) makes the view scroll sideways. Centring it shows the fitted page whole.
        double centreX = Math.Max(0, (_extentWidth - Bounds.Width) / 2);
        double y = toPageTop ? _pageTops[page] - PageGap : _offset.Y;
        ScrollTo(new Vector(centreX, y));
    }

    /// <summary>Sets the zoom (1 = actual size), keeping the document point at the viewport's centre still.</summary>
    public void SetZoom(double zoom)
    {
        ZoomMode = ZoomMode.Custom;
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

    /// <summary>Shows a search's matches, or none. The caller owns the search and disposes it.</summary>
    public void SetSearch(DocumentSearch? search)
    {
        if (_search is not null)
        {
            _search.Changed -= OnSearchChanged;
        }

        _search = search;
        _currentHit = null;
        if (search is not null)
        {
            search.Changed += OnSearchChanged;
        }

        InvalidateVisual();
    }

    /// <summary>Makes a match the current one and scrolls it into view.</summary>
    public void ShowHit(SearchHit hit)
    {
        if (_session is null || hit.Page >= _pageTops.Length || hit.Match.Bounds.Count == 0)
        {
            return;
        }

        _currentHit = hit;
        double scale = DipsPerPoint * _zoom;
        double top = _pageTops[hit.Page] + (hit.Match.Bounds.Min(b => b.Top) * scale);
        double bottom = _pageTops[hit.Page] + (hit.Match.Bounds.Max(b => b.Bottom) * scale);
        double margin = Math.Min(48, Bounds.Height / 4);
        double y = top < _offset.Y + margin || bottom > _offset.Y + Bounds.Height - margin
            ? top - (Bounds.Height / 3)
            : _offset.Y;

        // Only when the page is wider than the view: centre the match across it.
        double x = _offset.X;
        if (_extentWidth > Bounds.Width)
        {
            double pageLeft = (_extentWidth - (_session.PageSizes[hit.Page].Width * scale)) / 2;
            double left = pageLeft + (hit.Match.Bounds.Min(b => b.Left) * scale);
            double right = pageLeft + (hit.Match.Bounds.Max(b => b.Right) * scale);
            if (left < _offset.X + margin || right > _offset.X + Bounds.Width - margin)
            {
                x = ((left + right) / 2) - (Bounds.Width / 2);
            }
        }

        ScrollTo(new Vector(x, y));
    }

    /// <summary>
    /// Moves the reading position (keyboard focus, for screen readers) to the line holding a
    /// match, so reading carries on from where the search ended.
    /// </summary>
    public async Task FocusHitLineAsync(SearchHit hit)
    {
        IReadOnlyList<TextLine> lines = await GetTextLinesAsync(hit.Page) ?? [];
        if (lines.Count == 0 || hit.Match.Bounds.Count == 0)
        {
            await FocusPositionAsync(hit.Page, 0);
            return;
        }

        PageRect first = hit.Match.Bounds[0];
        double middle = (first.Top + first.Bottom) / 2;
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < lines.Count; i++)
        {
            double distance = middle < lines[i].Top ? lines[i].Top - middle
                : middle > lines[i].Bottom ? middle - lines[i].Bottom
                : 0;
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        await FocusPositionAsync(hit.Page, best);
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

        int visible = 0, sharp = 0, previewOnly = 0, blank = 0, highlights = 0;
        bool currentMatchVisible = false;
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

            if (_search?.MatchesOn(page) is { Count: > 0 } matches)
            {
                highlights += DrawMatches(context, page, pageRect, matches, ref currentMatchVisible);
            }

            DrawSelection(context, page, pageRect);
        }

        LastFrame = new FrameStats(
            visible, sharp, previewOnly, blank, Stopwatch.GetElapsedTime(started), highlights, currentMatchVisible);
    }

    /// <summary>Draws a page's matches over its tiles; returns how many rectangles it drew.</summary>
    private int DrawMatches(DrawingContext context, int page, Rect pageRect, IReadOnlyList<TextMatch> matches, ref bool currentMatchVisible)
    {
        double scale = DipsPerPoint * _zoom;
        int drawn = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            bool current = _currentHit is SearchHit hit && hit.Page == page && hit.Index == i;
            foreach (PageRect bounds in matches[i].Bounds)
            {
                var rect = new Rect(
                    pageRect.X + (bounds.Left * scale),
                    pageRect.Y + (bounds.Top * scale),
                    Math.Max(1, (bounds.Right - bounds.Left) * scale),
                    Math.Max(1, (bounds.Bottom - bounds.Top) * scale)).Inflate(1);
                if (!rect.Intersects(new Rect(Bounds.Size)))
                {
                    continue;
                }

                context.DrawRectangle(current ? CurrentMatchBrush : MatchBrush, current ? CurrentMatchPen : null, rect);
                currentMatchVisible |= current;
                drawn++;
            }
        }

        return drawn;
    }

    // Raised on a background thread after every page searched; one redraw per batch is enough.
    private void OnSearchChanged()
    {
        if (Interlocked.Exchange(ref _searchRedrawQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _searchRedrawQueued, 0);
                InvalidateVisual();
            }, DispatcherPriority.Background);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RecomputePageLayout();
        ScrollTo(_offset);
        if (ZoomMode != ZoomMode.Custom)
        {
            ApplyFit(ZoomMode, toPageTop: false);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_session is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (PageAt(e.GetPosition(this), clamp: false) is not (int page, Point at))
        {
            ClearSelection();
            return;
        }

        if (e.ClickCount == 2)
        {
            _ = SelectWordAsync(page, at);
            return;
        }

        ClearSelection();
        _dragging = true;
        e.Pointer.Capture(this);
        _pendingDrag = (page, at);
        _ = StartDragAsync(page, at);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging && PageAt(e.GetPosition(this), clamp: true) is (int page, Point at))
        {
            _pendingDrag = (page, at);
            ExtendSelection();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);

        // A click without a drag selects nothing.
        if (_selectionAnchor == _selectionFocus)
        {
            ClearSelection();
        }
    }

    /// <summary>Whether any text is selected.</summary>
    public bool HasSelection => _selectionAnchor is not null && _selectionFocus is not null && _selectionAnchor != _selectionFocus;

    public void ClearSelection()
    {
        _selectionAnchor = null;
        _selectionFocus = null;
        _pendingDrag = null;
        InvalidateVisual();
    }

    /// <summary>Selects the text of the page in view (decisions for review, D5: not the whole document).</summary>
    public async Task SelectAllOnPageAsync()
    {
        int page = CurrentReadingPosition()?.Page ?? CurrentPage;
        if (await CharactersAsync(page) is { Count: > 0 } characters)
        {
            _selectionAnchor = (page, 0);
            _selectionFocus = (page, characters.Count);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// The selected text, or with nothing selected the focused reading line's, so keyboard and
    /// screen-reader users can copy too. Null if there is nothing to copy.
    /// </summary>
    public async Task<string?> GetSelectedTextAsync()
    {
        if (!HasSelection)
        {
            return CurrentReadingPosition() is (int page, int line) && line >= 0 && _textLines.TryGetValue(page, out var lines) && line < lines.Count
                ? lines[line].Text
                : null;
        }

        var (start, end) = OrderedSelection();
        var pages = new List<string>();
        for (int page = start.Page; page <= end.Page; page++)
        {
            PageCharacters characters = await CharactersAsync(page) ?? PageCharacters.Empty;
            int from = page == start.Page ? start.Caret : 0;
            int to = page == end.Page ? end.Caret : characters.Count;
            pages.Add(TextSelection.Extract(characters, from, to));
        }

        string text = TextSelection.JoinPages(pages);
        return text.Length > 0 ? text : null;
    }

    private ((int Page, int Caret) Start, (int Page, int Caret) End) OrderedSelection()
    {
        var a = _selectionAnchor!.Value;
        var b = _selectionFocus!.Value;
        return a.Page < b.Page || (a.Page == b.Page && a.Caret <= b.Caret) ? (a, b) : (b, a);
    }

    private async Task StartDragAsync(int page, Point at)
    {
        if (await CharactersAsync(page) is not { } characters || !_dragging)
        {
            return;
        }

        int caret = TextSelection.CaretAt(characters, at.X, at.Y);
        _selectionAnchor = (page, caret);
        _selectionFocus ??= (page, caret);
        ExtendSelection();
    }

    // Moves the selection's far end to the pointer, once that page's characters are loaded.
    private void ExtendSelection()
    {
        if (_selectionAnchor is null || _pendingDrag is not (int page, Point at))
        {
            return;
        }

        if (_characters.TryGetValue(page, out PageCharacters? characters))
        {
            _selectionFocus = (page, TextSelection.CaretAt(characters, at.X, at.Y));
            InvalidateVisual();
        }
        else
        {
            _ = LoadCharactersThenExtendAsync(page);
        }
    }

    private async Task LoadCharactersThenExtendAsync(int page)
    {
        await CharactersAsync(page);
        if (_dragging || _pendingDrag?.Page == page)
        {
            ExtendSelection();
        }
    }

    private async Task SelectWordAsync(int page, Point at)
    {
        if (await CharactersAsync(page) is not { Count: > 0 } characters)
        {
            return;
        }

        int caret = TextSelection.CaretAt(characters, at.X, at.Y);
        var (start, end) = TextSelection.WordAt(characters, Math.Min(caret, characters.Count - 1));
        _selectionAnchor = (page, start);
        _selectionFocus = (page, end);
        InvalidateVisual();
    }

    /// <summary>A page's characters, from the cache or the worker; null if they could not be read.</summary>
    private async Task<PageCharacters?> CharactersAsync(int page)
    {
        if (_characters.TryGetValue(page, out PageCharacters? cached))
        {
            return cached;
        }

        DocumentSession? session = _session;
        if (session is null || !_characterRequests.Add(page))
        {
            return null;
        }

        try
        {
            PageCharacters characters = await session.GetCharactersAsync(page);
            if (session != _session)
            {
                return null;
            }

            TrimCharacterCache();
            _characters[page] = characters;
            InvalidateVisual();
            return characters;
        }
        catch (Exception ex) when (ex is WorkerException or WorkerExitedException or OperationCanceledException or InvalidDataException)
        {
            return null;
        }
        finally
        {
            _characterRequests.Remove(page);
        }
    }

    // Keeps the pages at the selection's ends and in view; drops others once the cache is full.
    private void TrimCharacterCache()
    {
        if (_characters.Count < CharacterCacheSize)
        {
            return;
        }

        var keep = new HashSet<int>(VisiblePages());
        if (_selectionAnchor is (int anchorPage, _))
        {
            keep.Add(anchorPage);
        }

        if (_selectionFocus is (int focusPage, _))
        {
            keep.Add(focusPage);
        }

        foreach (int page in _characters.Keys.Where(p => !keep.Contains(p)).Take(_characters.Count - CharacterCacheSize + 1).ToArray())
        {
            _characters.Remove(page);
        }
    }

    /// <summary>
    /// The page under a point in the view and the point in that page's coordinates (points from its
    /// top-left corner). With <paramref name="clamp"/>, a point between or beside pages goes to the
    /// nearest page, so a drag can carry on past a page's edge.
    /// </summary>
    private (int Page, Point At)? PageAt(Point point, bool clamp)
    {
        double scale = DipsPerPoint * _zoom;
        int? best = null;
        double bestDistance = double.MaxValue;
        foreach (int page in VisiblePages())
        {
            Rect rect = PageRect(page);
            if (rect.Contains(point))
            {
                best = page;
                break;
            }

            double distance = point.Y < rect.Top ? rect.Top - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
            if (clamp && distance < bestDistance)
            {
                best = page;
                bestDistance = distance;
            }
        }

        if (best is not int found)
        {
            return null;
        }

        Rect pageRect = PageRect(found);
        PageSize size = _session!.PageSizes[found];
        double x = Math.Clamp((point.X - pageRect.X) / scale, 0, size.Width);
        double y = Math.Clamp((point.Y - pageRect.Y) / scale, 0, size.Height);
        return (found, new Point(x, y));
    }

    private void DrawSelection(DrawingContext context, int page, Rect pageRect)
    {
        if (!HasSelection)
        {
            return;
        }

        var (start, end) = OrderedSelection();
        if (page < start.Page || page > end.Page)
        {
            return;
        }

        if (!_characters.TryGetValue(page, out PageCharacters? characters))
        {
            // A page inside a long selection that scrolled into view: draw it once loaded.
            _ = CharactersAsync(page);
            return;
        }

        int from = page == start.Page ? start.Caret : 0;
        int to = page == end.Page ? end.Caret : characters.Count;
        double scale = DipsPerPoint * _zoom;
        foreach (PageRect r in TextSelection.Highlight(characters, from, to))
        {
            context.FillRectangle(SelectionBrush, new Rect(
                pageRect.X + (r.Left * scale), pageRect.Y + (r.Top * scale), (r.Right - r.Left) * scale, (r.Bottom - r.Top) * scale));
        }
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

    // Keys reach here from the focused line or page too, because key events bubble.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_session is null)
        {
            return;
        }

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        int readingPage = CurrentReadingPosition()?.Page ?? CurrentPage;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Down when !control: _ = MoveReadingAsync(+1); break;
            case Key.Up when !control: _ = MoveReadingAsync(-1); break;
            case Key.PageDown: _ = FocusPositionAsync(Math.Min(readingPage + 1, _session.PageSizes.Count - 1), 0); break;
            case Key.PageUp: _ = FocusPositionAsync(Math.Max(readingPage - 1, 0), 0); break;
            case Key.Home when control: _ = FocusPositionAsync(0, 0); break;
            case Key.End when control: _ = FocusPositionAsync(_session.PageSizes.Count - 1, 0); break;
            case Key.Space: ScrollBy(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -Bounds.Height * 0.9 : Bounds.Height * 0.9); break;
            case Key.OemPlus or Key.Add when control: SetZoom(_zoom * 1.25); break;
            case Key.OemMinus or Key.Subtract when control: SetZoom(_zoom * 0.8); break;

            // The usual PDF reader keys: Ctrl+0 fit page, Ctrl+1 actual size, Ctrl+2 fit width.
            case Key.D0 or Key.NumPad0 when control: FitPage(); break;
            case Key.D1 or Key.NumPad1 when control: SetZoom(1); break;
            case Key.D2 or Key.NumPad2 when control: FitWidth(); break;
            case Key.A when control: _ = SelectAllOnPageAsync(); break;
            case Key.C when control: CopyRequested?.Invoke(); break;
            case Key.Escape when HasSelection: ClearSelection(); break;
            default: e.Handled = false; break;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (PageElement element in _pageElements.Values)
        {
            element.Measure(Size.Infinity);
        }

        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (page, element) in _pageElements)
        {
            element.Scale = DipsPerPoint * _zoom;
            element.Arrange(PageRect(page));
        }

        return finalSize;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DocumentViewPeer(this);

    /// <summary>The focused line or text-less page, as (page, line); line is -1 for a page element.</summary>
    private (int Page, int Line)? CurrentReadingPosition() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() switch
        {
            ReadingLine line => (line.PageIndex, line.Index),
            PageElement page => (page.PageIndex, -1),
            _ => null,
        };

    /// <summary>Moves to the next or previous line, crossing into neighbouring pages.</summary>
    private async Task MoveReadingAsync(int direction)
    {
        if (CurrentReadingPosition() is not (int page, int line))
        {
            // Not reading yet: start at the top of the page in view.
            await FocusPositionAsync(CurrentPage, 0);
            return;
        }

        IReadOnlyList<TextLine> lines = await GetTextLinesAsync(page) ?? [];
        int next = line + direction;
        if (lines.Count > 0 && next >= 0 && next < lines.Count)
        {
            await FocusPositionAsync(page, next);
            return;
        }

        int target = page + direction;
        if (_session is null || target < 0 || target >= _session.PageSizes.Count)
        {
            return;
        }

        IReadOnlyList<TextLine> targetLines = await GetTextLinesAsync(target) ?? [];
        await FocusPositionAsync(target, direction > 0 ? 0 : targetLines.Count - 1);
    }

    /// <summary>Scrolls a line (or a whole text-less page) into view and gives it keyboard focus.</summary>
    private async Task FocusPositionAsync(int page, int line)
    {
        if (_session is null)
        {
            return;
        }

        _readingPage = page;
        IReadOnlyList<TextLine> lines = await GetTextLinesAsync(page) ?? [];
        TextLine? target = lines.Count == 0 ? null : lines[Math.Clamp(line, 0, lines.Count - 1)];
        ScrollIntoView(page, target);
        SyncPageElements();

        if (_pageElements.TryGetValue(page, out PageElement? element))
        {
            Control focusTarget = target is null ? element : element.Lines[lines.ToList().IndexOf(target)];

            // The element is laid out in the next layout pass; focus it after that so it has bounds.
            Dispatcher.UIThread.Post(() => focusTarget.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
        }
    }

    private void ScrollIntoView(int page, TextLine? line)
    {
        double scale = DipsPerPoint * _zoom;
        double top = _pageTops[page] + ((line?.Top ?? 0) * scale);
        double bottom = _pageTops[page] + ((line?.Bottom ?? _session!.PageSizes[page].Height) * scale);
        double margin = Math.Min(48, Bounds.Height / 4);
        if (top < _offset.Y + margin || bottom > _offset.Y + Bounds.Height - margin)
        {
            ScrollTo(new Vector(_offset.X, top - (Bounds.Height / 3)));
        }
    }

    /// <summary>Keeps page elements for the visible pages and the page being read, loading text as needed.</summary>
    private void SyncPageElements()
    {
        if (_session is null || !ReadingElementsEnabled)
        {
            return;
        }

        var wanted = new HashSet<int>(VisiblePages());
        if (_readingPage is int reading)
        {
            wanted.Add(reading);
        }

        bool changed = false;
        foreach (int page in wanted)
        {
            if (_pageElements.ContainsKey(page))
            {
                continue;
            }

            if (_textLines.TryGetValue(page, out IReadOnlyList<TextLine>? lines))
            {
                var element = new PageElement(page, _session.PageSizes.Count, lines, _unreadableText.Contains(page));
                _pageElements[page] = element;
                VisualChildren.Add(element);
                LogicalChildren.Add(element);
                changed = true;
            }
            else if (_textRequests.Add(page))
            {
                _ = LoadTextLinesAsync(page);
            }
        }

        foreach (var (page, element) in _pageElements.ToArray())
        {
            if (!wanted.Contains(page) && !element.IsKeyboardFocusWithin)
            {
                RemovePageElement(page);
                changed = true;
            }
        }

        // This runs after every tile arrives; invalidating layout when nothing changed caused
        // hundreds of extra render passes per second (spike S2 A/B benchmark).
        if (changed)
        {
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    private async Task LoadTextLinesAsync(int page)
    {
        try
        {
            await GetTextLinesAsync(page);
        }
        finally
        {
            _textRequests.Remove(page);
        }

        SyncPageElements();
    }

    private async Task<IReadOnlyList<TextLine>?> GetTextLinesAsync(int page)
    {
        if (_textLines.TryGetValue(page, out IReadOnlyList<TextLine>? cached))
        {
            return cached;
        }

        DocumentSession? session = _session;
        if (session is null)
        {
            return null;
        }

        try
        {
            IReadOnlyList<TextLine> lines = await session.GetTextLinesAsync(page);
            if (session == _session)
            {
                _textLines[page] = lines;
            }

            return lines;
        }
        catch (WorkerException)
        {
            // Record the failure so the page says its text could not be read, rather than
            // claiming it has none, and so it is not requested again.
            if (session == _session)
            {
                _unreadableText.Add(page);
                _textLines[page] = [];
            }

            return [];
        }
        catch (Exception ex) when (ex is WorkerExitedException or OperationCanceledException)
        {
            // Asked again after the session restarts the worker.
            return null;
        }
    }

    private void RemovePageElement(int page)
    {
        if (_pageElements.Remove(page, out PageElement? element))
        {
            VisualChildren.Remove(element);
            LogicalChildren.Remove(element);
        }
    }

    private sealed class DocumentViewPeer(DocumentView owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
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
        if (_pageElements.Count > 0)
        {
            // Page elements move with the scroll offset.
            InvalidateArrange();
        }

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

        SyncPageElements();

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
