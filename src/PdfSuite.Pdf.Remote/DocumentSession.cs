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
        var (client, documentId) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.RenderTileAsync(documentId, pageIndex, pixelsPerPoint, region, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TextLine>> GetTextLinesAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        var (client, documentId) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetTextLinesAsync(documentId, pageIndex, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetQpdfVersionAsync(CancellationToken cancellationToken = default)
    {
        var (client, _) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.GetQpdfVersionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> PrintAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        var (client, documentId) = await ReadyClientAsync(cancellationToken).ConfigureAwait(false);
        return await client.PrintAsync(documentId, job, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Waits out any restart, then returns the current worker and its id for the document.</summary>
    private async Task<(WorkerClient Client, int DocumentId)> ReadyClientAsync(CancellationToken cancellationToken)
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

            return (_client, _remote.Id);
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
                return;
            }

            _recentRestarts.Enqueue(now);
            SetState(SessionState.Restarting);

            // Starting a process takes a moment; do it off this lock so callers are not blocked.
            _ready = Task.Run(RestartAsync);
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
}
