using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.Pdf.Remote;

public enum SessionState
{
    Ready,
    Restarting,
    Failed,
}

/// <summary>
/// One open document backed by a worker process. If the worker exits unexpectedly, the
/// session starts a new one and reopens the document; requests made meanwhile wait for it.
/// Repeated crashes in a short time end the session in <see cref="SessionState.Failed"/>,
/// so a document that crashes the engine cannot keep restarting it.
/// </summary>
public sealed class DocumentSession : IDisposable
{
    private const int MaxRestarts = 3;
    private static readonly TimeSpan RestartWindow = TimeSpan.FromSeconds(30);

    private readonly Func<WorkerClient> _startWorker;
    private readonly byte[] _data;
    private readonly string? _password;
    private readonly object _gate = new();
    private readonly Queue<DateTime> _recentRestarts = new();
    private WorkerClient _client;
    private RemoteDocument _remote;
    private Task _ready = Task.CompletedTask;

    // Completed once the session has dealt with the current worker's exit: a restart has begun,
    // or the session has failed. A request that saw the worker go waits for it before asking again.
    private TaskCompletionSource _exitHandled = NewExitSignal();
    private bool _disposed;

    private DocumentSession(Func<WorkerClient> startWorker, byte[] data, string? password, WorkerClient client, RemoteDocument remote)
    {
        _startWorker = startWorker;
        _data = data;
        _password = password;
        _client = client;
        _remote = remote;
        PageSizes = remote.PageSizes;
        client.Exited += OnWorkerExited;
    }

    /// <summary>Raised on a background thread when the session's state changes.</summary>
    public event Action<SessionState>? StateChanged;

    public IReadOnlyList<PageSize> PageSizes { get; }

    public SessionState State { get; private set; } = SessionState.Ready;

    public int WorkerProcessId
    {
        get
        {
            lock (_gate)
            {
                return _client.ProcessId;
            }
        }
    }

    /// <exception cref="PdfOpenException">The document could not be opened.</exception>
    public static async Task<DocumentSession> OpenAsync(
        Func<WorkerClient> startWorker, byte[] data, string? password, CancellationToken cancellationToken = default)
    {
        WorkerClient client = startWorker();
        try
        {
            RemoteDocument remote = await client.OpenAsync(data, password, cancellationToken).ConfigureAwait(false);
            return new DocumentSession(startWorker, data, password, client, remote);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<RenderedTile> RenderTileAsync(
        int pageIndex, double pixelsPerPoint, PixelRegion region, CancellationToken cancellationToken = default)
    {
        var (client, documentId, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.RenderTileAsync(documentId, pageIndex, pixelsPerPoint, region, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TextLine>> GetTextLinesAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        var (client, documentId, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetTextLinesAsync(documentId, pageIndex, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds text on one page. If the worker stops during the request, this waits for the session
    /// to restart it and asks once more, so a search can carry on past a crash. A page that stops
    /// the worker twice ends in <see cref="WorkerExitedException"/>.
    /// </summary>
    public async Task<IReadOnlyList<TextMatch>> FindTextAsync(
        int pageIndex, string query, bool matchCase = false, bool wholeWord = false, CancellationToken cancellationToken = default)
    {
        var (client, documentId, exitHandled) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await client.FindTextAsync(documentId, pageIndex, query, matchCase, wholeWord, cancellationToken).ConfigureAwait(false);
        }
        catch (WorkerExitedException)
        {
            // The client fails its requests before it raises Exited, so the session may not know yet
            // that the worker has gone. Asking again now would reach the same, dead worker.
            await exitHandled.WaitAsync(cancellationToken).ConfigureAwait(false);
            (client, documentId, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
            return await client.FindTextAsync(documentId, pageIndex, query, matchCase, wholeWord, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The document's bytes as opened, for saving a copy or for page operations.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>
    /// A new document from this one's pages (source 0) and any others', built in the worker; this
    /// session's document is not changed. Its password is used, and its encryption kept.
    /// </summary>
    public async Task<byte[]> RebuildPagesAsync(IReadOnlyList<PageSpec> pages, IReadOnlyList<PageSource>? others = null, CancellationToken cancellationToken = default)
    {
        var (client, _, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        List<PageSource> sources = [new PageSource(_data, _password), .. others ?? []];
        return await client.RebuildPagesAsync(sources, pages, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The password the document was opened with, to reopen an edited copy; null if none.</summary>
    public string? Password => _password;

    public async Task<IReadOnlyList<OutlineItem>> GetOutlineAsync(CancellationToken cancellationToken = default)
    {
        var (client, documentId, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetOutlineAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PageCharacters> GetCharactersAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        var (client, documentId, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetCharactersAsync(documentId, pageIndex, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentInfo> GetDocumentInfoAsync(CancellationToken cancellationToken = default)
    {
        var (client, documentId, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetDocumentInfoAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetQpdfVersionAsync(CancellationToken cancellationToken = default)
    {
        var (client, _, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetQpdfVersionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        var (client, documentId, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.PrintAsync(documentId, job, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits out any restart, then returns the current worker, its id for the document, and a task
    /// that completes once the session has dealt with that worker's exit.
    /// </summary>
    private async Task<(WorkerClient Client, int DocumentId, Task ExitHandled)> ReadyClientAsync(CancellationToken cancellationToken)
    {
        Task ready;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ready = _ready;
        }

        await ready.WaitAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (State == SessionState.Failed)
            {
                throw new WorkerExitedException();
            }

            return (_client, _remote.Id, _exitHandled.Task);
        }
    }

    /// <summary>Kills the current worker, as a crash would. For tests and the S1 benchmark.</summary>
    public void KillWorker()
    {
        lock (_gate)
        {
            _client.Kill();
        }
    }

    public void Dispose()
    {
        WorkerClient client;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            client = _client;

            // Requests waiting to ask again wake up, and then find the session disposed.
            _exitHandled.TrySetResult();
        }

        client.Dispose();
    }

    private void OnWorkerExited(WorkerClient client, WorkerExit exit)
    {
        lock (_gate)
        {
            if (_disposed || client != _client)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            while (_recentRestarts.Count > 0 && now - _recentRestarts.Peek() > RestartWindow)
            {
                _recentRestarts.Dequeue();
            }

            if (_recentRestarts.Count >= MaxRestarts)
            {
                SetState(SessionState.Failed);
                _exitHandled.TrySetResult();
                return;
            }

            _recentRestarts.Enqueue(now);
            SetState(SessionState.Restarting);

            // Starting a process takes a moment; do it off this lock so callers are not blocked.
            _ready = Task.Run(RestartAsync);
            _exitHandled.TrySetResult();
        }
    }

    private async Task RestartAsync()
    {
        WorkerClient? client = null;
        try
        {
            client = _startWorker();
            RemoteDocument remote = await client.OpenAsync(_data, _password).ConfigureAwait(false);
            WorkerClient old;
            lock (_gate)
            {
                if (_disposed)
                {
                    client.Dispose();
                    return;
                }

                old = _client;
                _client = client;
                _remote = remote;
                _exitHandled = NewExitSignal();
                client.Exited += OnWorkerExited;
            }

            old.Dispose();
            SetState(SessionState.Ready);
        }
        catch (Exception ex) when (ex is WorkerExitedException or WorkerException or PdfOpenException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            client?.Dispose();
            SetState(SessionState.Failed);
        }
    }

    private void SetState(SessionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private static TaskCompletionSource NewExitSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
