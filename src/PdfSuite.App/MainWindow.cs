using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.App;

// Spike shell for S1: open a PDF and scroll or zoom it. Messages follow docs/04-UI-COPY-AND-PRODUCT-RULES.md.
internal sealed class MainWindow : Window
{
    private readonly AppOptions _options;
    private readonly DocumentView _view;
    private readonly TextBlock _status;
    private string _documentName = string.Empty;
    private string? _message;

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
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };

        var bar = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(open, Dock.Left);
        DockPanel.SetDock(print, Dock.Left);
        bar.Children.Add(open);
        bar.Children.Add(print);
        bar.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_view);
        Content = root;

        Opened += async (_, _) => await OnOpenedAsync();
        Closed += (_, _) => _view.Session?.Dispose();
        UpdateStatus();
    }

    public DocumentView View => _view;

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
        catch (PdfOpenException)
        {
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
        if (!e.Handled && e.Key == Avalonia.Input.Key.P && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control)
        {
            e.Handled = true;
            _ = PrintAsync();
        }
    }

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
