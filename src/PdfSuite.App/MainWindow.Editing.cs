using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.App;

/// <summary>
/// Page editing and saving (roadmap Milestones 2 and 3).
/// - Each edit builds a new document in the worker (qpdf) and shows it; the file on disk is not
///   touched until the reader saves.
/// - Undo and Redo keep up to <see cref="UndoDepth"/> earlier versions.
/// - Save a Copy writes to a temporary file, checks what was written, then replaces the target, so
///   a failed save never leaves a half-written file (copy rules §7 and §13).
/// - Closing, opening another document or quitting with unsaved changes asks "Save your changes?".
/// </summary>
internal sealed partial class MainWindow
{
    private const int UndoDepth = 20;
    private const string SaveFailed =
        "We couldn't save the document. Your original file has not been intentionally replaced. Check the destination and available storage, then try again.";

    private readonly List<byte[]> _undo = [];
    private readonly List<byte[]> _redo = [];
    private readonly List<Button> _editButtons = [];
    private Button? _undoButton;
    private Button? _redoButton;
    private byte[]? _currentData;
    private byte[]? _savedData;
    private string? _documentPath;
    private bool _closeConfirmed;

    /// <summary>Whether the document shown differs from the file it was opened from or last saved to.</summary>
    private bool HasUnsavedChanges => _currentData is not null && !ReferenceEquals(_currentData, _savedData);

    private Control BuildEditBar()
    {
        Button Make(string content, string tip, Func<Task> action, bool needsHistory = false)
        {
            var button = new Button { Content = content, Margin = new Thickness(4, 0, 0, 0) };
            ToolTip.SetTip(button, tip);
            button.Click += async (_, _) => await action();
            if (!needsHistory)
            {
                _editButtons.Add(button);
            }

            return button;
        }

        Button rotateLeft = Make("Rotate Left", "Turn this page a quarter turn to the left", () => RotateCurrentPageAsync(-1));
        Button rotateRight = Make("Rotate Right", "Turn this page a quarter turn to the right", () => RotateCurrentPageAsync(1));
        Button delete = Make("Delete Page", $"Delete this page from the document ({Shortcuts.Label("Z")} undoes it)", DeleteCurrentPageAsync);
        _undoButton = Make("Undo", $"Undo the last page change ({Shortcuts.Label("Z")})", UndoAsync, needsHistory: true);
        _redoButton = Make("Redo", $"Redo the change ({Shortcuts.Label("Y")})", RedoAsync, needsHistory: true);
        Button save = Make("Save a Copy…", $"Save the document to a new file ({Shortcuts.Label("Shift+S")})", async () => await SaveCopyAsync());
        rotateLeft.Margin = new Thickness(0);
        _undoButton.Margin = new Thickness(12, 0, 0, 0);
        save.Margin = new Thickness(12, 0, 0, 0);

        var bar = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 6) };
        foreach (Button button in new[] { rotateLeft, rotateRight, delete, _undoButton, _redoButton, save })
        {
            bar.Children.Add(button);
        }

        UpdateEditButtons();
        return bar;
    }

    /// <summary>Called when a document has been opened from a file: it is the saved version, with no history.</summary>
    private void StartEditing(byte[] data, string? path)
    {
        _currentData = data;
        _savedData = data;
        _documentPath = path;
        _undo.Clear();
        _redo.Clear();
        UpdateEditButtons();
    }

    private void StopEditing()
    {
        _currentData = null;
        _savedData = null;
        _documentPath = null;
        _undo.Clear();
        _redo.Clear();
        UpdateEditButtons();
    }

    private void UpdateEditButtons()
    {
        bool open = _view.Session is not null;
        foreach (Button button in _editButtons)
        {
            button.IsEnabled = open;
        }

        _undoButton?.SetValue(IsEnabledProperty, open && _undo.Count > 0);
        _redoButton?.SetValue(IsEnabledProperty, open && _redo.Count > 0);
        UpdateStatus();
    }

    private Task RotateCurrentPageAsync(int quarterTurns)
    {
        if (_view.Session is not DocumentSession session)
        {
            return Task.CompletedTask;
        }

        int page = _view.CurrentPage;
        PageSpec[] pages = [.. Enumerable.Range(0, session.PageSizes.Count).Select(i => new PageSpec(0, i, i == page ? quarterTurns : 0))];
        return EditAsync(pages, page, quarterTurns < 0 ? $"Turned page {page + 1} to the left." : $"Turned page {page + 1} to the right.");
    }

    private Task DeleteCurrentPageAsync()
    {
        if (_view.Session is not DocumentSession session)
        {
            return Task.CompletedTask;
        }

        int count = session.PageSizes.Count;
        if (count == 1)
        {
            ShowBriefly("A document needs at least one page.");
            return Task.CompletedTask;
        }

        int page = _view.CurrentPage;
        PageSpec[] pages = [.. Enumerable.Range(0, count).Where(i => i != page).Select(i => new PageSpec(0, i))];
        return EditAsync(pages, Math.Min(page, count - 2), $"Deleted page {page + 1}. {Shortcuts.Label("Z")} undoes it.");
    }

    private async Task EditAsync(IReadOnlyList<PageSpec> pages, int showPage, string done)
    {
        if (_view.Session is not DocumentSession session || _currentData is not byte[] before)
        {
            return;
        }

        byte[] after;
        try
        {
            after = await session.RebuildPagesAsync(pages);
        }
        catch (PageAssemblyNotAllowedException)
        {
            ShowBriefly("This document does not allow changing its pages.");
            return;
        }
        catch (Exception ex) when (ex is WorkerException or WorkerExitedException or InvalidDataException)
        {
            ShowBriefly("We couldn't change the pages of this document.");
            return;
        }

        if (await ShowVersionAsync(after, session.Password, showPage))
        {
            Push(_undo, before);
            _redo.Clear();
            UpdateEditButtons();
            ShowBriefly(done);
        }
    }

    private async Task UndoAsync()
    {
        if (_undo.Count > 0 && _currentData is byte[] current && _view.Session is DocumentSession session
            && await ShowVersionAsync(_undo[^1], session.Password, _view.CurrentPage))
        {
            _undo.RemoveAt(_undo.Count - 1);
            Push(_redo, current);
            UpdateEditButtons();
        }
    }

    private async Task RedoAsync()
    {
        if (_redo.Count > 0 && _currentData is byte[] current && _view.Session is DocumentSession session
            && await ShowVersionAsync(_redo[^1], session.Password, _view.CurrentPage))
        {
            _redo.RemoveAt(_redo.Count - 1);
            Push(_undo, current);
            UpdateEditButtons();
        }
    }

    private static void Push(List<byte[]> stack, byte[] version)
    {
        stack.Add(version);
        if (stack.Count > UndoDepth)
        {
            stack.RemoveAt(0);
        }
    }

    /// <summary>Opens a version of the document in a new worker and shows it, keeping the zoom, at a page.</summary>
    private async Task<bool> ShowVersionAsync(byte[] data, string? password, int page)
    {
        DocumentSession next;
        try
        {
            next = await DocumentSession.OpenAsync(StartWorker, data, password);
        }
        catch (Exception ex) when (ex is PdfOpenException or WorkerException or WorkerExitedException or Win32Exception or InvalidOperationException)
        {
            ShowBriefly("We couldn't change the pages of this document.");
            return false;
        }

        DocumentSession? previous = _view.Session;
        ClearSearch();
        _searchPanel.Status = string.Empty;
        _view.SetDocument(next);
        _view.ScrollToPage(Math.Clamp(page, 0, next.PageSizes.Count - 1));
        _ = LoadOutlineAsync(next);
        previous?.Dispose();
        _currentData = data;
        _documentSize = data.LongLength;
        return true;
    }

    /// <summary>
    /// Saves the document shown to a file the reader chooses, by default a new one (copy rules §7:
    /// "The original file will remain unchanged"). Returns false if cancelled or failed.
    /// </summary>
    private async Task<bool> SaveCopyAsync()
    {
        if (_currentData is not byte[] data)
        {
            ShowBriefly("Open a PDF to save a copy of it.");
            return false;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a copy",
            SuggestedFileName = SuggestedCopyName(),
            DefaultExtension = "pdf",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("PDF documents") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"], AppleUniformTypeIdentifiers = ["com.adobe.pdf"] }],
        });
        if (file is null)
        {
            return false;
        }

        string? path = file.TryGetLocalPath();
        try
        {
            if (path is not null)
            {
                await SafeFile.WriteReplacingAsync(path, data);
            }
            else
            {
                // No local path (some sandboxed pickers): the file is written in place.
                await using Stream stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await stream.WriteAsync(data);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowMessage(SaveFailed);
            return false;
        }

        _savedData = data;
        _documentName = file.Name;
        if (path is not null)
        {
            _documentPath = path;
            _recent.Add(path);
            FillRecentMenu();
        }

        UpdateEditButtons();
        ShowBriefly($"Saved a copy as {file.Name}.");
        return true;
    }

    private string SuggestedCopyName()
    {
        string name = Path.GetFileNameWithoutExtension(_documentPath ?? _documentName);
        return $"{(name.Length > 0 ? name : "Document")} (edited).pdf";
    }

    /// <summary>With unsaved changes, asks "Save your changes?" (copy rules §7). True when it is safe to go on.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        int choice = await ChoiceDialog.AskAsync(this, "Save your changes?", "You have changes that have not been saved.",
            ["Save", "Discard Changes", "Cancel"], defaultIndex: 0, cancelIndex: 2);
        return choice switch
        {
            0 => await SaveCopyAsync(),
            1 => true,
            _ => false,
        };
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (HasUnsavedChanges && !_closeConfirmed)
        {
            e.Cancel = true;
            _ = ConfirmThenCloseAsync();
        }
    }

    private async Task ConfirmThenCloseAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
