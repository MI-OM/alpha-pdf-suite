using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Pdf.Worker;

/// <summary>
/// Serves requests from the app over stdin/stdout. One thread reads requests; every engine
/// call happens on the thread that calls <see cref="Run"/>, in arrival order, because PDFium
/// is not thread-safe. A cancelled request is skipped if it has not started. The worker exits
/// when stdin closes, so it never outlives the app.
/// </summary>
internal sealed class WorkerHost(Stream input, Stream output, bool allowTestCommands)
{
    private readonly BlockingCollection<Message> _queue = new();
    private readonly ConcurrentDictionary<int, byte> _cancelled = new();
    private readonly Dictionary<int, IPdfDocument> _documents = [];
    private readonly IPdfEngine _engine = new PdfiumEngine();
    private int _nextDocumentId = 1;
    private int _lastStartedRequestId;

    public int Run()
    {
        new Thread(ReadRequests) { IsBackground = true, Name = "Request reader" }.Start();

        foreach (Message request in _queue.GetConsumingEnumerable())
        {
            using (request)
            {
                Volatile.Write(ref _lastStartedRequestId, request.RequestId);
                if (_cancelled.TryRemove(request.RequestId, out _))
                {
                    continue;
                }

                Handle(request);
            }
        }

        foreach (IPdfDocument document in _documents.Values)
        {
            document.Dispose();
        }

        return 0;
    }

    private void ReadRequests()
    {
        try
        {
            while (MessageIO.TryRead(input, out Message message))
            {
                if (message.Kind != MessageKind.Cancel)
                {
                    _queue.Add(message);
                    continue;
                }

                int target = new PayloadReader(message.Payload).ReadInt32();
                message.Dispose();

                // A cancel for a request that has already started is too late; recording it would only leak.
                if (target > Volatile.Read(ref _lastStartedRequestId))
                {
                    _cancelled[target] = 0;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            Console.Error.WriteLine($"Request stream failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _queue.CompleteAdding();
        }
    }

    private void Handle(in Message request)
    {
        try
        {
            switch (request.Kind)
            {
                case MessageKind.Open:
                    Open(request);
                    break;
                case MessageKind.RenderTile:
                    RenderTile(request);
                    break;
                case MessageKind.Close:
                    Close(request);
                    break;
                case MessageKind.AllocateForTest when allowTestCommands:
                    AllocateForTest(request);
                    break;
                default:
                    SendError(request.RequestId, WorkerErrorCode.InvalidRequest, 0, $"Unsupported request {request.Kind}.");
                    break;
            }
        }
        catch (PdfOpenException ex)
        {
            SendError(request.RequestId, WorkerErrorCode.OpenFailed, (int)ex.Error, ex.Message);
        }
        catch (KeyNotFoundException)
        {
            SendError(request.RequestId, WorkerErrorCode.DocumentNotFound, 0, "No open document has that id.");
        }
        catch (ArgumentOutOfRangeException)
        {
            SendError(request.RequestId, WorkerErrorCode.PageOutOfRange, 0, "The page or region is out of range.");
        }
        catch (OutOfMemoryException)
        {
            SendError(request.RequestId, WorkerErrorCode.OutOfMemory, 0, "The worker ran out of memory.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Messages here come from this codebase and never include document content.
            SendError(request.RequestId, WorkerErrorCode.Unknown, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Open(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        int passwordLength = reader.ReadInt32();
        string? password = passwordLength < 0 ? null : Encoding.UTF8.GetString(reader.ReadBytes(passwordLength));
        IPdfDocument document = _engine.Open(reader.Remaining, password);

        int id = _nextDocumentId++;
        _documents[id] = document;
        int pageCount = document.PageCount;

        byte[] payload = new byte[8 + (pageCount * 16)];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(id);
        writer.WriteInt32(pageCount);
        for (int i = 0; i < pageCount; i++)
        {
            PageSize size = document.GetPageSize(i);
            writer.WriteDouble(size.Width);
            writer.WriteDouble(size.Height);
        }

        MessageIO.Write(output, MessageKind.Opened, request.RequestId, payload);
    }

    private void RenderTile(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        IPdfDocument document = _documents[reader.ReadInt32()];
        int pageIndex = reader.ReadInt32();
        double pixelsPerPoint = reader.ReadDouble();
        var region = new PixelRegion(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

        // Tiles are capped so that one request cannot ask for an enormous allocation.
        if (region.Width is <= 0 or > 4096 || region.Height is <= 0 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Tile size out of range.");
        }

        int byteCount = region.Width * region.Height * 4;
        byte[] pixels = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            long started = Stopwatch.GetTimestamp();
            document.RenderRegion(pageIndex, pixelsPerPoint, region, pixels.AsSpan(0, byteCount));
            long microseconds = (long)Stopwatch.GetElapsedTime(started).TotalMicroseconds;

            Span<byte> header = stackalloc byte[16];
            var writer = new PayloadWriter(header);
            writer.WriteInt32(region.Width);
            writer.WriteInt32(region.Height);
            writer.WriteInt64(microseconds);
            MessageIO.Write(output, MessageKind.Tile, request.RequestId, header, pixels.AsSpan(0, byteCount));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    private void Close(in Message request)
    {
        int id = new PayloadReader(request.Payload).ReadInt32();
        if (_documents.Remove(id, out IPdfDocument? document))
        {
            document.Dispose();
        }

        MessageIO.Write(output, MessageKind.Ack, request.RequestId, []);
    }

    // Test command: commits the requested memory page by page, so a memory limit is hit for real.
    private unsafe void AllocateForTest(in Message request)
    {
        int megabytes = new PayloadReader(request.Payload).ReadInt32();
        nuint bytes = (nuint)megabytes * 1024 * 1024;
        byte* memory = (byte*)NativeMemory.Alloc(bytes);
        try
        {
            for (nuint offset = 0; offset < bytes; offset += 4096)
            {
                memory[offset] = 1;
            }
        }
        finally
        {
            NativeMemory.Free(memory);
        }

        MessageIO.Write(output, MessageKind.Ack, request.RequestId, []);
    }

    private void SendError(int requestId, WorkerErrorCode code, int detail, string message)
    {
        byte[] text = Encoding.UTF8.GetBytes(message);
        byte[] payload = new byte[8 + text.Length];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32((int)code);
        writer.WriteInt32(detail);
        writer.WriteBytes(text);
        MessageIO.Write(output, MessageKind.Error, requestId, payload);
    }
}
