using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.App;

// Spike shell for S1: open a PDF and scroll or zoom it. Messages follow docs/04-UI-COPY-AND-PRODUCT-RULES.md.
internal sealed class MainWindow : Window
{
    private readonly AppOptions _options;
    private readonly DocumentView _view;
    private readonly TextBlock _status;
    private readonly SearchPanel _searchPanel;
    private string _documentName = string.Empty;
    private string? _message;

    // The search being shown, and what it was started with; a different query or option starts a new one.
    private DocumentSearch? _search;
    private (string Query, bool MatchCase, bool WholeWords) _searchKey;
    private CancellationTokenSource? _findCts;

    public MainWindow(AppOptions options)
    {
        _options = options;
        Title = "PDF Suite (spike)";
        Width = 1000;
        Height = 800;

        _view = new DocumentView { TileSize = options.TileSize, ReadingElementsEnabled = !options.NoReadingElements };
        _view.StatusChanged += UpdateStatus;

        var open = new Button { Content = "Open PDF…" };
        open.Click += async (_, _) => await PickAndOpenAsync();
        var print = new Button { Content = "Print…", Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(print, "Print this document");
        print.Click += async (_, _) => await PrintAsync();
        var search = new Button { Content = "Search", Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(search, "Search this document");
        search.Click += (_, _) => OpenSearch();
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };

        var bar = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(open, Dock.Left);
        DockPanel.SetDock(print, Dock.Left);
        DockPanel.SetDock(search, Dock.Left);
        bar.Children.Add(open);
        bar.Children.Add(print);
        bar.Children.Add(search);
        bar.Children.Add(_status);

        _searchPanel = new SearchPanel { IsVisible = false };
        _searchPanel.FindRequested += forward => _ = FindAsync(forward);
        _searchPanel.QueryChanged += OnSearchQueryChanged;
        _searchPanel.OptionsChanged += OnSearchOptionsChanged;
        _searchPanel.CloseRequested += () => _ = CloseSearchAsync();

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_searchPanel, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_searchPanel);
        root.Children.Add(_view);
        Content = root;

        Opened += async (_, _) => await OnOpenedAsync();
        Closed += (_, _) =>
        {
            ClearSearch();
            _view.Session?.Dispose();
        };
        UpdateStatus();
    }

    public DocumentView View => _view;

    public SearchPanel SearchBar => _searchPanel;

    /// <summary>Time from process start to the window opening.</summary>
    public TimeSpan StartupTime { get; private set; }

    public async Task<bool> OpenFileAsync(string path)
    {
        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("We couldn't open this PDF. The file may be damaged, unsupported, or inaccessible.");
            return false;
        }

        return await OpenAsync(data, Path.GetFileName(path));
    }

    private async Task OnOpenedAsync()
    {
        StartupTime = DateTime.Now - Process.GetCurrentProcess().StartTime;
        if (_options.File is null)
        {
            return;
        }

        if (_options.Benchmark || _options.SmokeTest)
        {
            bool rendered = await new Benchmark(this, _options).RunAsync();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(rendered ? 0 : 1);
            }
        }
        else
        {
            await OpenFileAsync(_options.File);
        }
    }

    private async Task PickAndOpenAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PDF",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("PDF documents")
                {
                    Patterns = ["*.pdf"],
                    MimeTypes = ["application/pdf"],
                    AppleUniformTypeIdentifiers = ["com.adobe.pdf"],
                },
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            await OpenAsync(buffer.ToArray(), files[0].Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("We couldn't open this PDF. The file may be damaged, unsupported, or inaccessible.");
        }
    }

    private async Task<bool> OpenAsync(byte[] data, string name)
    {
        ShowMessage($"Opening {name}…");
        try
        {
            DocumentSession session = await DocumentSession.OpenAsync(StartWorker, data, password: null);
            DocumentSession? previous = _view.Session;
            ClearSearch();
            _searchPanel.Status = string.Empty;
            _view.SetDocument(session);
            previous?.Dispose();
            _documentName = name;
            AutomationProperties.SetName(_view, name);
            ShowMessage(null);
            _view.Focus();
            return true;
        }
        catch (PdfOpenException ex) when (ex.Error == PdfOpenError.Password)
        {
            ShowMessage("This document is password-protected. Password entry is not part of this spike build.");
        }
        catch (Exception ex) when (ex is PdfOpenException or WorkerException)
        {
            // Any other error the worker reports while opening also means this file could not be used.
            ShowMessage("We couldn't open this PDF. The file may be damaged, unsupported, or inaccessible.");
        }
        catch (Exception ex) when (ex is WorkerExitedException or Win32Exception or InvalidOperationException)
        {
            ShowMessage("The rendering process could not start.");
        }

        return false;
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        switch (e.Key)
        {
            case Avalonia.Input.Key.P when modifiers == Avalonia.Input.KeyModifiers.Control:
                e.Handled = true;
                _ = PrintAsync();
                break;
            case Avalonia.Input.Key.F when modifiers == Avalonia.Input.KeyModifiers.Control:
                e.Handled = true;
                OpenSearch();
                break;
            case Avalonia.Input.Key.F3 when modifiers is Avalonia.Input.KeyModifiers.None or Avalonia.Input.KeyModifiers.Shift:
                e.Handled = true;
                if (!_searchPanel.IsVisible)
                {
                    OpenSearch();
                }

                _ = FindAsync(forward: modifiers == Avalonia.Input.KeyModifiers.None);
                break;
        }
    }

    private void OpenSearch()
    {
        _searchPanel.IsVisible = true;
        if (string.IsNullOrWhiteSpace(_searchPanel.Query))
        {
            _searchPanel.Status = "Enter a word or phrase to search this document.";
        }

        _searchPanel.FocusQuery();
    }

    /// <summary>
    /// Hides the search bar and its highlights. Keyboard focus moves to the line holding the current
    /// match, so a screen reader carries on reading from there.
    /// </summary>
    private async Task CloseSearchAsync()
    {
        SearchHit? hit = _view.CurrentHit;
        _searchPanel.IsVisible = false;
        ClearSearch();
        if (hit is SearchHit current)
        {
            await _view.FocusHitLineAsync(current);
        }
        else
        {
            _view.Focus();
        }
    }

    /// <summary>
    /// Moves to the next or previous match. A new query or option starts a new search, from the page
    /// in view, and moves to its first match from there.
    /// </summary>
    private async Task FindAsync(bool forward)
    {
        string query = _searchPanel.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            _searchPanel.Status = "Enter a word or phrase to search this document.";
            return;
        }

        if (_view.Session is not DocumentSession session)
        {
            _searchPanel.Status = "Open a PDF to search it.";
            return;
        }

        // A search that stopped (the worker gave up) is started again, as its message says.
        var key = (query, _searchPanel.MatchCase, _searchPanel.WholeWords);
        if (_search is null || key != _searchKey || _search.Status is SearchStatus.Failed or SearchStatus.Cancelled)
        {
            StartSearch(session, key);
        }

        DocumentSearch search = _search!;
        _findCts?.Cancel();
        var cts = new CancellationTokenSource();
        _findCts = cts;
        UpdateSearchStatus();

        SearchHit? hit;
        try
        {
            hit = await search.FindAsync(_view.CurrentHit, _view.CurrentPage, forward, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // A newer request, a new search, or a new document has taken over.
        if (cts != _findCts || search != _search)
        {
            return;
        }

        if (hit is SearchHit found)
        {
            _view.ShowHit(found);
        }

        UpdateSearchStatus();
    }

    private void StartSearch(DocumentSession session, (string Query, bool MatchCase, bool WholeWords) key)
    {
        ClearSearch();
        var search = DocumentSearch.Start(session, key.Query, key.MatchCase, key.WholeWords, _view.CurrentPage);
        _search = search;
        _searchKey = key;
        search.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            if (search == _search)
            {
                UpdateSearchStatus();
            }
        }, DispatcherPriority.Background);
        _view.SetSearch(search);
    }

    private void ClearSearch()
    {
        _findCts?.Cancel();
        _findCts = null;
        _view.SetSearch(null);
        _search?.Dispose();
        _search = null;
    }

    // Typing a different query clears the old highlights, which no longer match what is in the box.
    private void OnSearchQueryChanged()
    {
        if (_search is not null && _searchPanel.Query != _searchKey.Query)
        {
            ClearSearch();
            _searchPanel.Status = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_searchPanel.Query))
        {
            _searchPanel.Status = "Enter a word or phrase to search this document.";
        }
    }

    // Changing an option searches again at once, if there was a search.
    private void OnSearchOptionsChanged()
    {
        if (_search is not null)
        {
            _ = FindAsync(forward: true);
        }
    }

    private void UpdateSearchStatus()
    {
        if (_search is not DocumentSearch search)
        {
            return;
        }

        int matches = search.MatchCount;
        int? ordinal = _view.CurrentHit is SearchHit hit ? search.OrdinalOf(hit) : null;
        string text = search.Status switch
        {
            SearchStatus.Complete when matches == 0 => "No matches found. Try another search term or change the search options.",
            SearchStatus.Complete when ordinal is int position => $"Match {position:N0} of {matches:N0}.",
            SearchStatus.Complete => $"{Count(matches, "match", "matches")}.",
            SearchStatus.Searching when ordinal is int position => $"Match {position:N0} of {matches:N0} so far. Searching…",
            SearchStatus.Searching => $"{Count(matches, "match", "matches")} so far. Searching…",
            SearchStatus.Failed => $"The search stopped because the rendering process stopped. {Count(matches, "match", "matches")} found. Search again to retry.",
            _ => string.Empty,
        };

        int failed = search.FailedPages.Count;
        if (failed > 0)
        {
            text += $" {Count(failed, "page", "pages")} could not be searched.";
        }

        _searchPanel.Status = text;
    }

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n:N0} {many}";

    // Spike S3: Windows prints through the worker. macOS and Linux are not built yet; the
    // planned route is in docs/research/spike-results.md (S3).
    private async Task PrintAsync()
    {
        if (_view.Session is not DocumentSession session)
        {
            ShowMessage("Open a PDF to print it.");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            ShowMessage("Printing is not available on this operating system in this spike build.");
            return;
        }

        PrintJob? job;
        try
        {
            job = WindowsPrintDialog.Show(TryGetPlatformHandle()?.Handle ?? 0, session.PageSizes.Count, _view.CurrentPage, _documentName);
        }
        catch (Win32Exception)
        {
            ShowMessage("The Print dialog could not be shown.");
            return;
        }

        if (job is null)
        {
            return;
        }

        ShowMessage($"Printing {job.Pages.Count} page(s) to {job.PrinterName}…");
        try
        {
            int printed = await session.PrintAsync(job);
            ShowMessage(printed == 0 ? "Printing was cancelled." : $"Sent {printed} page(s) to {job.PrinterName}.");
        }
        catch (Exception ex) when (ex is WorkerException or WorkerExitedException)
        {
            ShowMessage("We couldn't print the document. Check the printer, then try again.");
        }
    }

    private static WorkerClient StartWorker() =>
        WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath())
        {
            Log = line => Trace.WriteLine($"[worker] {line}"),
        });

    private void ShowMessage(string? message)
    {
        _message = message;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        DocumentSession? session = _view.Session;
        _status.Text = (_message, session?.State) switch
        {
            ({ } message, _) => message,
            (_, null) => "No document open.",
            (_, SessionState.Restarting) => "The rendering process stopped. Restarting…",
            (_, SessionState.Failed) => "The rendering process stopped repeatedly. Close and reopen the document.",
            _ => $"{_documentName}   Page {_view.CurrentPage + 1} of {session!.PageSizes.Count}   {_view.Zoom:P0}",
        };
    }
}
