using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.Pdf.Remote;

public sealed record WorkerOptions(string WorkerPath)
{
    /// <summary>
    /// Memory cap for the worker. Windows and Linux refuse allocations over it, and the worker keeps
    /// serving. macOS enforces no such limit, so there the worker is stopped if its footprint goes over.
    /// </summary>
    public long? MemoryLimitBytes { get; init; } = 1536L * 1024 * 1024;

    /// <summary>A request older than this is treated as a hung worker, which is then killed.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public bool EnableTestCommands { get; init; }

    /// <summary>Receives the worker's stderr lines. The worker never writes document content there.</summary>
    public Action<string>? Log { get; init; }

    public static string DefaultWorkerPath() =>
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "PdfSuite.Pdf.Worker.exe" : "PdfSuite.Pdf.Worker");
}

public sealed record RemoteDocument(int Id, IReadOnlyList<PageSize> PageSizes);

/// <summary>
/// A print job (Windows only in the spike). <paramref name="DevMode"/> holds the printer settings
/// from the Print dialog, or is empty for the printer's defaults. <paramref name="OutputFile"/>
/// is for print-to-file drivers such as Microsoft Print to PDF.
/// </summary>
public sealed record PrintJob(string PrinterName, byte[] DevMode, IReadOnlyList<int> Pages, string Title, string? OutputFile = null);

public sealed record WorkerExit(int? ExitCode);

public class WorkerException(WorkerErrorCode code, string message) : Exception(message)
{
    public WorkerErrorCode Code { get; } = code;
}

public sealed class WorkerExitedException()
    : Exception("The rendering process stopped.");

/// <summary>A rendered tile. Its pixels live in a pooled buffer; dispose to return it.</summary>
public sealed class RenderedTile : IDisposable
{
    private byte[]? _buffer;
    private readonly int _offset;

    internal RenderedTile(byte[] buffer, int offset, int width, int height, TimeSpan renderTime)
    {
        _buffer = buffer;
        _offset = offset;
        Width = width;
        Height = height;
        RenderTime = renderTime;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Time the worker spent rendering, excluding queueing and transfer.</summary>
    public TimeSpan RenderTime { get; }

    public ReadOnlySpan<byte> Pixels
    {
        get
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            return _buffer.AsSpan(_offset, Width * Height * 4);
        }
    }

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>
/// Starts a worker process and talks to it. Requests may be sent from any thread; results
/// complete on the thread pool. When the worker exits unexpectedly, every pending request
/// fails with <see cref="WorkerExitedException"/> and <see cref="Exited"/> is raised.
/// </summary>
public sealed class WorkerClient : IDisposable
{
    private readonly Process _process;
    private readonly IDisposable? _job;
    private readonly Stream _toWorker;
    private readonly Stream _fromWorker;
    private readonly WorkerOptions _options;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<int, Pending> _pending = new();
    private readonly Timer _watchdog;
    private readonly Timer? _memoryWatch;
    private int _nextRequestId;
    private volatile bool _disposed;
    private volatile bool _exited;

    private WorkerClient(Process process, IDisposable? job, WorkerOptions options)
    {
        _process = process;
        _job = job;
        _options = options;
        _toWorker = process.StandardInput.BaseStream;
        _fromWorker = new BufferedStream(process.StandardOutput.BaseStream, 1 << 16);
        new Thread(ReadResponses) { IsBackground = true, Name = "Worker response reader" }.Start();
        _watchdog = new Timer(_ => CheckForHungRequests(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        if (OperatingSystem.IsMacOS() && options.MemoryLimitBytes is not null)
        {
            _memoryWatch = new Timer(_ => CheckMemory(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
        }
    }

    public event Action<WorkerClient, WorkerExit>? Exited;

    public int ProcessId => _process.Id;

    public bool HasExited => _exited;

    public static WorkerClient Start(WorkerOptions options)
    {
        var startInfo = new ProcessStartInfo(options.WorkerPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--serve");
        if (options.EnableTestCommands)
        {
            startInfo.ArgumentList.Add("--test-commands");
        }

        // Linux: the worker caps its own memory before it reads any request. Windows: the Job
        // Object below. macOS: the client watches the worker's footprint (CheckMemory).
        if (OperatingSystem.IsLinux() && options.MemoryLimitBytes is long limit)
        {
            startInfo.ArgumentList.Add("--memory-limit");
            startInfo.ArgumentList.Add(limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start the worker '{options.WorkerPath}'.");

        IDisposable? job = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var windowsJob = WindowsJobObject.Create(options.MemoryLimitBytes);
                job = windowsJob;
                windowsJob.Assign(process);
            }
        }
        catch
        {
            job?.Dispose();
            process.Kill();
            process.Dispose();
            throw;
        }

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                options.Log?.Invoke(e.Data);
            }
        };
        process.BeginErrorReadLine();
        return new WorkerClient(process, job, options);
    }

    /// <exception cref="PdfOpenException">The worker could not open the document.</exception>
    public async Task<RemoteDocument> OpenAsync(ReadOnlyMemory<byte> data, string? password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int id = NextRequestId();
        Pending pending = Register(id);

        // Cancelling unregisters the request, so the watchdog does not mistake it for a hang.
        // If the worker had already started opening, the late answer closes the document (Discard).
        Message response;
        using (cancellationToken.Register(() => CancelRequest(id)))
        {
            // A large document takes a moment to write, so it is sent off the caller's thread.
            await Task.Run(() => SendOpen(id, data, password), CancellationToken.None).ConfigureAwait(false);
            response = await pending.Completion.Task.ConfigureAwait(false);
        }

        using var owned = response;
        ThrowIfError(response);
        Expect(response, MessageKind.Opened);

        var reader = new PayloadReader(response.Payload);
        int documentId = reader.ReadInt32();
        int pageCount = reader.ReadInt32();
        var sizes = new PageSize[pageCount];
        for (int i = 0; i < pageCount; i++)
        {
            sizes[i] = new PageSize(reader.ReadDouble(), reader.ReadDouble());
        }

        return new RemoteDocument(documentId, sizes);
    }

    public async Task<RenderedTile> RenderTileAsync(
        int documentId, int pageIndex, double pixelsPerPoint, PixelRegion region, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int id = NextRequestId();
        Pending pending = Register(id);
        SendRenderTile(id, documentId, pageIndex, pixelsPerPoint, region);

        Message response;
        using (cancellationToken.Register(() => CancelRequest(id)))
        {
            response = await pending.Completion.Task.ConfigureAwait(false);
        }

        try
        {
            ThrowIfError(response);
            Expect(response, MessageKind.Tile);
            var reader = new PayloadReader(response.Payload);
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            long renderMicroseconds = reader.ReadInt64();
            if (width * height * 4 != reader.Remaining.Length)
            {
                throw new InvalidDataException("The tile size does not match its pixel data.");
            }

            // The tile takes over the pooled buffer, so the pixels are not copied again.
            return new RenderedTile(response.DetachBuffer(), 16, width, height, TimeSpan.FromMicroseconds(renderMicroseconds));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async Task<IReadOnlyList<TextLine>> GetTextLinesAsync(int documentId, int pageIndex, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int id = NextRequestId();
        Pending pending = Register(id);
        SendPageRequest(MessageKind.GetTextLines, id, documentId, pageIndex);

        Message response;
        using (cancellationToken.Register(() => CancelRequest(id)))
        {
            response = await pending.Completion.Task.ConfigureAwait(false);
        }

        using var owned = response;
        ThrowIfError(response);
        Expect(response, MessageKind.TextLines);

        var reader = new PayloadReader(response.Payload);
        var lines = new TextLine[reader.ReadInt32()];
        for (int i = 0; i < lines.Length; i++)
        {
            double left = reader.ReadDouble(), top = reader.ReadDouble(), right = reader.ReadDouble(), bottom = reader.ReadDouble();
            string text = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
            lines[i] = new TextLine(text, left, top, right, bottom);
        }

        return lines;
    }

    /// <summary>Returns the qpdf version the worker loaded. The worker loads qpdf to answer.</summary>
    public async Task<string> GetQpdfVersionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int id = NextRequestId();
        Pending pending = Register(id);
        Send(MessageKind.GetEngineInfo, id, []);

        Message response;
        using (cancellationToken.Register(() => CancelRequest(id)))
        {
            response = await pending.Completion.Task.ConfigureAwait(false);
        }

        using var owned = response;
        ThrowIfError(response);
        Expect(response, MessageKind.EngineInfo);
        return new PayloadReader(response.Payload).ReadString() ?? string.Empty;
    }

    /// <summary>
    /// Prints pages through the worker. Returns the number of pages sent to the printer, or 0 if
    /// the user cancelled when the job started (for example in a print-to-file Save dialog).
    /// </summary>
    public async Task<int> PrintAsync(int documentId, PrintJob job, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int id = NextRequestId();

        // Printing takes much longer than one tile, so the hang watchdog gets a matching allowance.
        Pending pending = Register(id, TimeSpan.FromSeconds(30 + (10 * job.Pages.Count)));

        byte[] payload = new byte[8 + (4 * job.Pages.Count) + PayloadWriter.SizeOf(job.PrinterName)
            + PayloadWriter.SizeOf(job.Title) + PayloadWriter.SizeOf(job.OutputFile) + 4 + job.DevMode.Length];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(documentId);
        writer.WriteInt32(job.Pages.Count);
        foreach (int page in job.Pages)
        {
            writer.WriteInt32(page);
        }

        writer.WriteString(job.PrinterName);
        writer.WriteString(job.Title);
        writer.WriteString(job.OutputFile);
        writer.WriteInt32(job.DevMode.Length);
        writer.WriteBytes(job.DevMode);
        Send(MessageKind.Print, id, payload);

        Message response;
        using (cancellationToken.Register(() => CancelRequest(id)))
        {
            response = await pending.Completion.Task.ConfigureAwait(false);
        }

        using var owned = response;
        ThrowIfError(response);
        Expect(response, MessageKind.Printed);
        return new PayloadReader(response.Payload).ReadInt32();
    }

    public async Task CloseDocumentAsync(int documentId)
    {
        int id = NextRequestId();
        Pending pending = Register(id);
        Span<byte> payload = stackalloc byte[4];
        new PayloadWriter(payload).WriteInt32(documentId);
        Send(MessageKind.Close, id, payload);
        using Message response = await pending.Completion.Task.ConfigureAwait(false);
        ThrowIfError(response);
    }

    /// <summary>
    /// Redacts a document by rebuilding it, and verifies the result (ADR-0006). The worker handles
    /// one request at a time, and this one can take seconds on a long document, so it holds up
    /// rendering for any document open in the same worker.
    /// </summary>
    /// <param name="extraTerms">Text the verifier must also find nowhere in the output.</param>
    /// <exception cref="RedactionFailedException">The rebuilt file failed verification, so none was returned.</exception>
    /// <exception cref="PdfOpenException">The source could not be opened, for example because it is encrypted.</exception>
    public async Task<RedactionResult> RedactAsync(
        ReadOnlyMemory<byte> pdf, IReadOnlyList<RedactionArea> areas, IReadOnlyList<string>? extraTerms = null, CancellationToken cancellationToken = default)
    {
        extraTerms ??= [];
        byte[] header = new byte[8 + (36 * areas.Count) + extraTerms.Sum(PayloadWriter.SizeOf)];
        var writer = new PayloadWriter(header);
        writer.WriteInt32(areas.Count);
        foreach (RedactionArea area in areas)
        {
            writer.WriteInt32(area.PageIndex);
            writer.WriteDouble(area.Rect.Left);
            writer.WriteDouble(area.Rect.Top);
            writer.WriteDouble(area.Rect.Right);
            writer.WriteDouble(area.Rect.Bottom);
        }

        writer.WriteInt32(extraTerms.Count);
        foreach (string term in extraTerms)
        {
            writer.WriteString(term);
        }

        // Rendering, rebuilding and verifying: 5.5 s for 2 pages of a 2,000-page, 4.9 MB file (spike S5).
        int pages = areas.Select(a => a.PageIndex).Distinct().Count();
        TimeSpan timeout = TimeSpan.FromSeconds(60 + (10 * pages) + (pdf.Length / (1024 * 1024)));
        using Message response = await RequestAsync(MessageKind.Redact, header, pdf, timeout, cancellationToken).ConfigureAwait(false);
        ThrowIfError(response);
        Expect(response, MessageKind.Redacted);

        var reader = new PayloadReader(response.Payload);
        bool passed = reader.ReadInt32() != 0;
        var failures = new VerificationFailure[reader.ReadInt32()];
        for (int i = 0; i < failures.Length; i++)
        {
            failures[i] = new VerificationFailure((RedactionCheck)reader.ReadInt32(), reader.ReadString() ?? string.Empty);
        }

        var warnings = new string[reader.ReadInt32()];
        for (int i = 0; i < warnings.Length; i++)
        {
            warnings[i] = reader.ReadString() ?? string.Empty;
        }

        var report = new VerificationReport(failures, warnings);
        return passed ? new RedactionResult(reader.Remaining.ToArray(), report) : throw new RedactionFailedException(report);
    }

    /// <summary>Encrypts with AES-256 through qpdf in the worker.</summary>
    /// <param name="password">The input's password, if it is already encrypted.</param>
    /// <exception cref="PdfOpenException">The input needs a password, or the one given is wrong.</exception>
    /// <exception cref="WorkerException">qpdf could not process the document (<see cref="WorkerErrorCode.StructureFailed"/>).</exception>
    public Task<byte[]> EncryptAsync(
        ReadOnlyMemory<byte> pdf, string userPassword, string ownerPassword, PdfPermissions permissions, string? password = null, CancellationToken cancellationToken = default)
    {
        byte[] header = TransformHeader(TransformOperation.Encrypt, password, PayloadWriter.SizeOf(userPassword) + PayloadWriter.SizeOf(ownerPassword) + 4, out int offset);
        var writer = new PayloadWriter(header.AsSpan(offset));
        writer.WriteString(userPassword);
        writer.WriteString(ownerPassword);
        writer.WriteInt32(PermissionBits.Encode(permissions));
        return TransformAsync(header, pdf, cancellationToken);
    }

    /// <inheritdoc cref="EncryptAsync"/>
    /// <summary>Removes encryption through qpdf in the worker.</summary>
    public Task<byte[]> DecryptAsync(ReadOnlyMemory<byte> pdf, string password, CancellationToken cancellationToken = default) =>
        TransformAsync(TransformHeader(TransformOperation.Decrypt, password, 0, out _), pdf, cancellationToken);

    /// <inheritdoc cref="EncryptAsync"/>
    /// <summary>Sets document information keys ("/Title" and so on) through qpdf in the worker; a null value removes the key.</summary>
    public Task<byte[]> SetMetadataAsync(
        ReadOnlyMemory<byte> pdf, IReadOnlyDictionary<string, string?> values, string? password = null, CancellationToken cancellationToken = default)
    {
        int size = 4 + values.Sum(v => PayloadWriter.SizeOf(v.Key) + PayloadWriter.SizeOf(v.Value));
        byte[] header = TransformHeader(TransformOperation.SetMetadata, password, size, out int offset);
        var writer = new PayloadWriter(header.AsSpan(offset));
        writer.WriteInt32(values.Count);
        foreach (var (key, value) in values)
        {
            writer.WriteString(key);
            writer.WriteString(value);
        }

        return TransformAsync(header, pdf, cancellationToken);
    }

    /// <inheritdoc cref="EncryptAsync"/>
    /// <summary>Rewrites the whole file through qpdf in the worker, dropping earlier revisions and unreferenced objects.</summary>
    public Task<byte[]> RewriteAsync(ReadOnlyMemory<byte> pdf, string? password = null, CancellationToken cancellationToken = default) =>
        TransformAsync(TransformHeader(TransformOperation.Rewrite, password, 0, out _), pdf, cancellationToken);

    /// <inheritdoc cref="EncryptAsync"/>
    /// <summary>Reads document information keys through qpdf in the worker. Keys the document does not have are left out.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(
        ReadOnlyMemory<byte> pdf, IReadOnlyList<string> keys, string? password = null, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[PayloadWriter.SizeOf(password) + 4 + keys.Sum(PayloadWriter.SizeOf)];
        var writer = new PayloadWriter(header);
        writer.WriteString(password);
        writer.WriteInt32(keys.Count);
        foreach (string key in keys)
        {
            writer.WriteString(key);
        }

        using Message response = await RequestAsync(MessageKind.GetMetadata, header, pdf, StructureTimeout(pdf), cancellationToken).ConfigureAwait(false);
        ThrowIfError(response);
        Expect(response, MessageKind.Metadata);

        var reader = new PayloadReader(response.Payload);
        var values = new Dictionary<string, string>();
        for (int count = reader.ReadInt32(); count > 0; count--)
        {
            values[reader.ReadString() ?? string.Empty] = reader.ReadString() ?? string.Empty;
        }

        return values;
    }

    /// <summary>Test command: how many documents the worker has open.</summary>
    public async Task<int> CountDocumentsForTestAsync()
    {
        int id = NextRequestId();
        Pending pending = Register(id);
        Send(MessageKind.CountDocumentsForTest, id, []);
        using Message response = await pending.Completion.Task.ConfigureAwait(false);
        ThrowIfError(response);
        return new PayloadReader(response.Payload).ReadInt32();
    }

    /// <summary>Test command: asks the worker to commit this much memory, hold it for a time, then free it.</summary>
    public async Task AllocateForTestAsync(int megabytes, int holdMilliseconds = 0)
    {
        int id = NextRequestId();
        Pending pending = Register(id);
        Span<byte> payload = stackalloc byte[8];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(megabytes);
        writer.WriteInt32(holdMilliseconds);
        Send(MessageKind.AllocateForTest, id, payload);
        using Message response = await pending.Completion.Task.ConfigureAwait(false);
        ThrowIfError(response);
    }

    /// <summary>Kills the worker. Pending requests fail and <see cref="Exited"/> is raised.</summary>
    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watchdog.Dispose();
        _memoryWatch?.Dispose();
        try
        {
            // Closing stdin tells the worker to finish and exit.
            _toWorker.Dispose();
            if (!_process.WaitForExit(TimeSpan.FromSeconds(2)))
            {
                Kill();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Kill();
        }

        _job?.Dispose();
        _process.Dispose();
    }

    private void SendOpen(int id, ReadOnlyMemory<byte> data, string? password)
    {
        byte[]? passwordBytes = password is null ? null : Encoding.UTF8.GetBytes(password);
        byte[] header = new byte[4 + (passwordBytes?.Length ?? 0)];
        var writer = new PayloadWriter(header);
        writer.WriteInt32(passwordBytes?.Length ?? -1);
        writer.WriteBytes(passwordBytes);
        Send(MessageKind.Open, id, header, data.Span);
    }

    /// <summary>Sends a request whose payload is a header and then a document, and waits for the answer.</summary>
    private async Task<Message> RequestAsync(
        MessageKind kind, byte[] header, ReadOnlyMemory<byte> body, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int id = NextRequestId();
        Pending pending = Register(id, timeout);
        using (cancellationToken.Register(() => CancelRequest(id)))
        {
            // A large document takes a moment to write, so it is sent off the caller's thread.
            await Task.Run(() => Send(kind, id, header, body.Span), CancellationToken.None).ConfigureAwait(false);
            return await pending.Completion.Task.ConfigureAwait(false);
        }
    }

    // [int32 operation][password], then room for the operation's own fields, which start at offset.
    private static byte[] TransformHeader(TransformOperation operation, string? password, int fieldsSize, out int offset)
    {
        offset = 4 + PayloadWriter.SizeOf(password);
        byte[] header = new byte[offset + fieldsSize];
        var writer = new PayloadWriter(header);
        writer.WriteInt32((int)operation);
        writer.WriteString(password);
        return header;
    }

    private async Task<byte[]> TransformAsync(byte[] header, ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        using Message response = await RequestAsync(MessageKind.Transform, header, pdf, StructureTimeout(pdf), cancellationToken).ConfigureAwait(false);
        ThrowIfError(response);
        Expect(response, MessageKind.Transformed);
        return response.Payload.ToArray();
    }

    // qpdf reads and writes the whole file; allow for large ones.
    private static TimeSpan StructureTimeout(ReadOnlyMemory<byte> pdf) => TimeSpan.FromSeconds(30 + (pdf.Length / (1024 * 1024)));

    // A late answer to a cancelled request. An open that completed anyway would leave its document
    // open in the worker until the worker exits (spike finding 13), so it is closed here.
    private void Discard(Message message)
    {
        try
        {
            if (message.Kind == MessageKind.Opened)
            {
                Span<byte> payload = stackalloc byte[4];
                new PayloadWriter(payload).WriteInt32(new PayloadReader(message.Payload).ReadInt32());
                Send(MessageKind.Close, 0, payload);
            }
        }
        catch (WorkerExitedException)
        {
            // The worker has gone, and the document with it.
        }
        finally
        {
            message.Dispose();
        }
    }

    private void SendRenderTile(int id, int documentId, int pageIndex, double pixelsPerPoint, PixelRegion region)
    {
        Span<byte> payload = stackalloc byte[32];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(documentId);
        writer.WriteInt32(pageIndex);
        writer.WriteDouble(pixelsPerPoint);
        writer.WriteInt32(region.Left);
        writer.WriteInt32(region.Top);
        writer.WriteInt32(region.Width);
        writer.WriteInt32(region.Height);
        Send(MessageKind.RenderTile, id, writer.Written);
    }

    private void SendPageRequest(MessageKind kind, int id, int documentId, int pageIndex)
    {
        Span<byte> payload = stackalloc byte[8];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(documentId);
        writer.WriteInt32(pageIndex);
        Send(kind, id, writer.Written);
    }

    private void CancelRequest(int id)
    {
        if (!_pending.TryRemove(id, out Pending? pending))
        {
            return;
        }

        pending.Completion.TrySetCanceled();
        try
        {
            Span<byte> payload = stackalloc byte[4];
            new PayloadWriter(payload).WriteInt32(id);
            Send(MessageKind.Cancel, 0, payload);
        }
        catch (WorkerExitedException)
        {
            // Nothing to cancel in a worker that has gone.
        }
    }

    private void Send(MessageKind kind, int id, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second = default)
    {
        if (_exited || _disposed)
        {
            Fail(id);
            throw new WorkerExitedException();
        }

        try
        {
            lock (_writeLock)
            {
                MessageIO.Write(_toWorker, kind, id, first, second);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Fail(id);
            throw new WorkerExitedException();
        }
    }

    private void ReadResponses()
    {
        try
        {
            while (MessageIO.TryRead(_fromWorker, out Message message))
            {
                if (_pending.TryRemove(message.RequestId, out Pending? pending))
                {
                    if (!pending.Completion.TrySetResult(message))
                    {
                        Discard(message);
                    }
                }
                else
                {
                    // The request was cancelled after the worker had started it, or needs no answer.
                    Discard(message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException or ObjectDisposedException)
        {
            _options.Log?.Invoke($"Worker connection ended: {ex.GetType().Name}");
        }
        finally
        {
            OnWorkerGone();
        }
    }

    private void OnWorkerGone()
    {
        _exited = true;
        foreach (int id in _pending.Keys)
        {
            Fail(id);
        }

        if (_disposed)
        {
            return;
        }

        int? exitCode = null;
        try
        {
            if (_process.WaitForExit(TimeSpan.FromSeconds(2)))
            {
                exitCode = _process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            // This runs on the reader thread, where an escaping exception would crash the app.
            // Reading the exit code can race with Dispose (seen as UnauthorizedAccessException);
            // the exit code is only diagnostic, so report it as unknown.
            _options.Log?.Invoke($"Could not read the worker's exit code: {ex.GetType().Name}");
        }

        Exited?.Invoke(this, new WorkerExit(exitCode));
    }

    // macOS enforces neither RLIMIT_DATA nor RLIMIT_AS for mapped memory, so the cap there is a
    // check of the worker's footprint every 250 ms. It is softer than the Windows and Linux
    // limits: the worker is stopped rather than refused one allocation, and it can overshoot
    // between checks. A stopped worker is restarted by its session like any crash.
    private void CheckMemory()
    {
        if (!OperatingSystem.IsMacOS() || _exited || _disposed || _options.MemoryLimitBytes is not long limit)
        {
            return;
        }

        try
        {
            if (MacMemoryFootprint.Of(_process.Id) is long footprint && footprint > limit)
            {
                _options.Log?.Invoke($"The worker used {footprint / (1024 * 1024)} MB, over its {limit / (1024 * 1024)} MB limit; stopping it.");
                Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // The process object was disposed between the check above and reading its id.
        }
    }

    private void CheckForHungRequests()
    {
        long now = Stopwatch.GetTimestamp();
        foreach (Pending pending in _pending.Values)
        {
            TimeSpan timeout = pending.Timeout ?? _options.RequestTimeout;
            if (Stopwatch.GetElapsedTime(pending.StartedAt, now) > timeout)
            {
                _options.Log?.Invoke($"A request took longer than {timeout.TotalSeconds:0} s; stopping the worker.");
                Kill();
                return;
            }
        }
    }

    private int NextRequestId() => Interlocked.Increment(ref _nextRequestId);

    private Pending Register(int id, TimeSpan? timeout = null)
    {
        var pending = new Pending(Stopwatch.GetTimestamp(), timeout);
        _pending[id] = pending;
        return pending;
    }

    private void Fail(int id)
    {
        if (_pending.TryRemove(id, out Pending? pending))
        {
            pending.Completion.TrySetException(new WorkerExitedException());
        }
    }

    private static void ThrowIfError(in Message response)
    {
        if (response.Kind != MessageKind.Error)
        {
            return;
        }

        var reader = new PayloadReader(response.Payload);
        var code = (WorkerErrorCode)reader.ReadInt32();
        int detail = reader.ReadInt32();
        string message = Encoding.UTF8.GetString(reader.Remaining);
        if (code == WorkerErrorCode.OpenFailed)
        {
            throw new PdfOpenException((PdfOpenError)detail);
        }

        throw new WorkerException(code, message);
    }

    private static void Expect(in Message response, MessageKind kind)
    {
        if (response.Kind != kind)
        {
            throw new InvalidDataException($"Expected a {kind} response but received {response.Kind}.");
        }
    }

    private sealed class Pending(long startedAt, TimeSpan? timeout)
    {
        public long StartedAt { get; } = startedAt;

        /// <summary>Overrides the default hang timeout for slow requests such as printing.</summary>
        public TimeSpan? Timeout { get; } = timeout;

        public TaskCompletionSource<Message> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
