using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };

        var bar = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(open, Dock.Left);
        bar.Children.Add(open);
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

        if (_options.Benchmark)
        {
            await new Benchmark(this, _options).RunAsync();
            Close();
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
