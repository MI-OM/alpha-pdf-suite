using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Qpdf;
using PdfSuite.Pdf.Redaction;
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
                case MessageKind.GetTextLines:
                    GetTextLines(request);
                    break;
                case MessageKind.FindText:
                    FindText(request);
                    break;
                case MessageKind.GetDocumentInfo:
                    GetDocumentInfo(request);
                    break;
                case MessageKind.GetCharacters:
                    GetCharacters(request);
                    break;
                case MessageKind.GetOutline:
                    GetOutline(request);
                    break;
                case MessageKind.RebuildPages:
                    RebuildPages(request);
                    break;
                case MessageKind.Print:
                    Print(request);
                    break;
                case MessageKind.GetEngineInfo:
                    GetEngineInfo(request);
                    break;
                case MessageKind.Redact:
                    Redact(request);
                    break;
                case MessageKind.Transform:
                    Transform(request);
                    break;
                case MessageKind.GetMetadata:
                    GetMetadata(request);
                    break;
                case MessageKind.CountDocumentsForTest when allowTestCommands:
                    Span<byte> count = stackalloc byte[4];
                    new PayloadWriter(count).WriteInt32(_documents.Count);
                    MessageIO.Write(output, MessageKind.Ack, request.RequestId, count);
                    break;
                case MessageKind.AllocateForTest when allowTestCommands:
                    AllocateForTest(request);
                    break;
                default:
                    SendError(request.RequestId, WorkerErrorCode.InvalidRequest, 0, $"Unsupported request {request.Kind}.");
                    break;
            }
        }
        catch (PageAssemblyNotAllowedException ex)
        {
            SendError(request.RequestId, WorkerErrorCode.NotAllowed, ex.SourceIndex, ex.Message);
        }
        catch (PdfOpenException ex)
        {
            SendError(request.RequestId, WorkerErrorCode.OpenFailed, (int)ex.Error, ex.Message);
        }
        catch (QpdfException ex) when (ex.IsPasswordError)
        {
            SendError(request.RequestId, WorkerErrorCode.OpenFailed, (int)PdfOpenError.Password, ex.Message);
        }
        catch (QpdfException ex)
        {
            // qpdf's own message can quote the file's contents, so only its error code is passed on.
            SendError(request.RequestId, WorkerErrorCode.StructureFailed, ex.Code, $"qpdf could not process the document (error {ex.Code}).");
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
        catch (System.ComponentModel.Win32Exception ex)
        {
            SendError(request.RequestId, WorkerErrorCode.PrintFailed, ex.NativeErrorCode, ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Messages here come from this codebase and never include document content.
            SendError(request.RequestId, WorkerErrorCode.Unknown, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            // One bad request must not take the worker down, for example a native function missing
            // from a PDFium build (EntryPointNotFoundException). Report the type only: the message
            // could quote document content.
            Console.Error.WriteLine($"Unexpected {ex.GetType().FullName} handling {request.Kind}.");
            SendError(request.RequestId, WorkerErrorCode.Unknown, 0, $"Unexpected {ex.GetType().Name}.");
        }
    }

    private void Open(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        int passwordLength = reader.ReadInt32();
        string? password = passwordLength < 0 ? null : Encoding.UTF8.GetString(reader.ReadBytes(passwordLength));
        IPdfDocument document = _engine.Open(reader.Remaining, password);
        int pageCount = document.PageCount;
        byte[] payload = new byte[8 + (pageCount * 16)];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(_nextDocumentId);
        writer.WriteInt32(pageCount);
        try
        {
            for (int i = 0; i < pageCount; i++)
            {
                PageSize size = document.GetPageSize(i);
                writer.WriteDouble(size.Width);
                writer.WriteDouble(size.Height);
            }
        }
        catch (InvalidOperationException)
        {
            // PDFium loaded the file but cannot read one of its pages, so it is damaged. Found by
            // fuzzing: reported as a generic error, it was one the app did not expect at open.
            document.Dispose();
            throw new PdfOpenException(PdfOpenError.Format);
        }

        if (document is PdfiumDocument pdfium)
        {
            // Asked only when a text line meets a layer, which most documents never do (finding 32).
            pdfium.FindHiddenLayers = pdf => HiddenLayers(pdf, password);
        }

        _documents[_nextDocumentId++] = document;
        MessageIO.Write(output, MessageKind.Opened, request.RequestId, payload);
    }

    // A file qpdf cannot read may still open in PDFium; its layers then all count as visible.
    private static IReadOnlySet<string> HiddenLayers(ReadOnlySpan<byte> pdf, string? password)
    {
        try
        {
            return QpdfStructure.HiddenLayerNames(pdf, password);
        }
        catch (QpdfException)
        {
            return new HashSet<string>();
        }
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

    // One page per request, so tile requests can be served between the pages of a long search.
    // Payload: [docId][page][int32 flags: 1 match case, 2 whole word][query].
    // Response: [int32 n] then per match [text][int32 m][m x 4 x double rectangle].
    private void FindText(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        IPdfDocument document = _documents[reader.ReadInt32()];
        int pageIndex = reader.ReadInt32();
        int flags = reader.ReadInt32();
        string query = reader.ReadString() ?? string.Empty;
        IReadOnlyList<TextMatch> matches = document.FindText(pageIndex, query, matchCase: (flags & 1) != 0, wholeWord: (flags & 2) != 0);

        byte[] payload = new byte[4 + matches.Sum(m => PayloadWriter.SizeOf(m.Text) + 4 + (32 * m.Bounds.Count))];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(matches.Count);
        foreach (TextMatch match in matches)
        {
            writer.WriteString(match.Text);
            writer.WriteInt32(match.Bounds.Count);
            foreach (PageRect rect in match.Bounds)
            {
                writer.WriteDouble(rect.Left);
                writer.WriteDouble(rect.Top);
                writer.WriteDouble(rect.Right);
                writer.WriteDouble(rect.Bottom);
            }
        }

        MessageIO.Write(output, MessageKind.TextMatches, request.RequestId, payload);
    }

    // Payload: [int32 sources] then per source [password][int32 length]; [int32 pages] then per page
    // [int32 source][int32 page][int32 quarter turns][double blank width, NaN if not blank][double blank height];
    // then the sources' bytes, one after another. Response: Transformed, with the new document.
    // Each source's permissions are what PDFium says this password gives (all of them with the owner
    // password); qpdf alone cannot tell the owner password from the user's.
    private void RebuildPages(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        var headers = new (string? Password, int Length)[reader.ReadInt32()];
        for (int i = 0; i < headers.Length; i++)
        {
            headers[i] = (reader.ReadString(), reader.ReadInt32());
        }

        var pages = new PageSpec[reader.ReadInt32()];
        for (int i = 0; i < pages.Length; i++)
        {
            int source = reader.ReadInt32(), page = reader.ReadInt32(), turns = reader.ReadInt32();
            double width = reader.ReadDouble(), height = reader.ReadDouble();
            pages[i] = double.IsNaN(width) ? new PageSpec(source, page, turns) : PageSpec.Blank(new PageSize(width, height));
        }

        var sources = new PageSource[headers.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            byte[] pdf = reader.ReadBytes(headers[i].Length).ToArray();
            bool allowed;
            using (IPdfDocument document = _engine.Open(pdf, headers[i].Password))
            {
                allowed = document.GetDocumentInfo().Permissions.Assemble;
            }

            sources[i] = new PageSource(pdf, headers[i].Password, OwnerAccess: allowed);
        }

        MessageIO.Write(output, MessageKind.Transformed, request.RequestId, QpdfPages.Rebuild(sources, pages));
    }

    // Payload: [docId]. Response: [int32 n] then n entries in document order, each
    // [int32 depth][title][int32 page, -1 for none][double top, NaN for none].
    private void GetOutline(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        var flat = new List<(int Depth, OutlineItem Item)>();
        void Flatten(IReadOnlyList<OutlineItem> items, int depth)
        {
            foreach (OutlineItem item in items)
            {
                flat.Add((depth, item));
                Flatten(item.Children, depth + 1);
            }
        }

        Flatten(_documents[reader.ReadInt32()].GetOutline(), 0);
        byte[] payload = new byte[4 + flat.Sum(f => 16 + PayloadWriter.SizeOf(f.Item.Title))];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(flat.Count);
        foreach (var (depth, item) in flat)
        {
            writer.WriteInt32(depth);
            writer.WriteString(item.Title);
            writer.WriteInt32(item.PageIndex ?? -1);
            writer.WriteDouble(item.Top ?? double.NaN);
        }

        MessageIO.Write(output, MessageKind.Outline, request.RequestId, payload);
    }

    // Payload: [docId][page]. Response: [text][int32 n] then n x [4 x double box].
    private void GetCharacters(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        IPdfDocument document = _documents[reader.ReadInt32()];
        PageCharacters characters = document.GetCharacters(reader.ReadInt32());

        byte[] payload = new byte[PayloadWriter.SizeOf(characters.Text) + 4 + (32 * characters.Boxes.Count)];
        var writer = new PayloadWriter(payload);
        writer.WriteString(characters.Text);
        writer.WriteInt32(characters.Boxes.Count);
        foreach (PageRect box in characters.Boxes)
        {
            writer.WriteDouble(box.Left);
            writer.WriteDouble(box.Top);
            writer.WriteDouble(box.Right);
            writer.WriteDouble(box.Bottom);
        }

        MessageIO.Write(output, MessageKind.Characters, request.RequestId, payload);
    }

    // Payload: [docId]. Response: [version][int32 flags: 1 encrypted, 2 tagged][int32 permission bits]
    // [int32 n] then n x [key][value].
    private void GetDocumentInfo(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        DocumentInfo info = _documents[reader.ReadInt32()].GetDocumentInfo();

        byte[] payload = new byte[PayloadWriter.SizeOf(info.PdfVersion) + 12
            + info.Metadata.Sum(m => PayloadWriter.SizeOf(m.Key) + PayloadWriter.SizeOf(m.Value))];
        var writer = new PayloadWriter(payload);
        writer.WriteString(info.PdfVersion);
        writer.WriteInt32((info.Encrypted ? 1 : 0) | (info.Tagged ? 2 : 0));
        writer.WriteInt32(PermissionBits.Encode(info.Permissions));
        writer.WriteInt32(info.Metadata.Count);
        foreach (var (key, value) in info.Metadata)
        {
            writer.WriteString(key);
            writer.WriteString(value);
        }

        MessageIO.Write(output, MessageKind.DocumentInfo, request.RequestId, payload);
    }

    // Payload: [int32 count] then per line [4 x double bounds][int32 UTF-8 length][UTF-8 text].
    private void GetTextLines(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        IPdfDocument document = _documents[reader.ReadInt32()];
        IReadOnlyList<TextLine> lines = document.GetTextLines(reader.ReadInt32());

        byte[][] texts = lines.Select(l => Encoding.UTF8.GetBytes(l.Text)).ToArray();
        byte[] payload = new byte[4 + texts.Sum(t => 36 + t.Length)];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            writer.WriteDouble(lines[i].Left);
            writer.WriteDouble(lines[i].Top);
            writer.WriteDouble(lines[i].Right);
            writer.WriteDouble(lines[i].Bottom);
            writer.WriteInt32(texts[i].Length);
            writer.WriteBytes(texts[i]);
        }

        MessageIO.Write(output, MessageKind.TextLines, request.RequestId, payload);
    }

    // Payload: [qpdf version] via WriteString. Loading qpdf here proves its native library is
    // where the worker expects it, which packaging smoke tests rely on (spike S6).
    private void GetEngineInfo(in Message request)
    {
        string qpdfVersion = QpdfStructure.LibraryVersion;
        byte[] payload = new byte[PayloadWriter.SizeOf(qpdfVersion)];
        new PayloadWriter(payload).WriteString(qpdfVersion);
        MessageIO.Write(output, MessageKind.EngineInfo, request.RequestId, payload);
    }

    // Redaction runs here, not in the app, because it parses the untrusted source with both PDFium
    // and qpdf (spike finding 37). Payload: [int32 n][n x (int32 page, 4 x double rect)]
    // [int32 m][m x term] then the source PDF. Response: [int32 passed][int32 n][n x (int32 check,
    // message)][int32 m][m x warning] then the redacted PDF, which is empty if verification failed.
    // Messages and warnings can quote the document's text: they go to the app for the user, and
    // are never logged.
    private void Redact(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        var areas = new RedactionArea[reader.ReadInt32()];
        for (int i = 0; i < areas.Length; i++)
        {
            int page = reader.ReadInt32();
            areas[i] = new RedactionArea(page, new PageRect(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()));
        }

        var terms = new string[reader.ReadInt32()];
        for (int i = 0; i < terms.Length; i++)
        {
            terms[i] = reader.ReadString() ?? string.Empty;
        }

        byte[] redacted;
        VerificationReport report;
        try
        {
            RedactionResult result = Redactor.Redact(reader.Remaining, areas, terms);
            (redacted, report) = (result.Pdf, result.Report);
        }
        catch (RedactionFailedException ex)
        {
            (redacted, report) = ([], ex.Report);
        }

        byte[] header = new byte[12 + report.Failures.Sum(f => 4 + PayloadWriter.SizeOf(f.Message)) + report.Warnings.Sum(PayloadWriter.SizeOf)];
        var writer = new PayloadWriter(header);
        writer.WriteInt32(report.Passed ? 1 : 0);
        writer.WriteInt32(report.Failures.Count);
        foreach (VerificationFailure failure in report.Failures)
        {
            writer.WriteInt32((int)failure.Check);
            writer.WriteString(failure.Message);
        }

        writer.WriteInt32(report.Warnings.Count);
        foreach (string warning in report.Warnings)
        {
            writer.WriteString(warning);
        }

        MessageIO.Write(output, MessageKind.Redacted, request.RequestId, header, redacted);
    }

    // Whole-file operations through qpdf, which must not run in the app: some qpdf calls throw C++
    // exceptions that end the process on Linux and macOS (spike finding 28).
    // Payload: [int32 operation][password] then the operation's fields, then the PDF:
    //   Encrypt:     [user password][owner password][int32 permission bits]
    //   SetMetadata: [int32 n][n x (key, value or null to remove)]
    //   Decrypt, Rewrite: nothing more
    // Response: the new PDF.
    private void Transform(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        var operation = (TransformOperation)reader.ReadInt32();
        string? password = reader.ReadString();
        byte[] result;
        switch (operation)
        {
            case TransformOperation.Encrypt:
                {
                    string user = reader.ReadString() ?? string.Empty, owner = reader.ReadString() ?? string.Empty;
                    PdfPermissions permissions = PermissionBits.Decode(reader.ReadInt32());
                    result = QpdfStructure.Encrypt(reader.Remaining, user, owner, permissions, password);
                    break;
                }

            case TransformOperation.Decrypt:
                result = QpdfStructure.Decrypt(reader.Remaining, password ?? string.Empty);
                break;
            case TransformOperation.SetMetadata:
                {
                    var values = new Dictionary<string, string?>();
                    for (int count = reader.ReadInt32(); count > 0; count--)
                    {
                        values[reader.ReadString() ?? string.Empty] = reader.ReadString();
                    }

                    result = QpdfStructure.SetMetadata(reader.Remaining, values, password);
                    break;
                }

            case TransformOperation.Rewrite:
                result = QpdfStructure.Rewrite(reader.Remaining, password);
                break;
            default:
                SendError(request.RequestId, WorkerErrorCode.InvalidRequest, 0, $"Unsupported operation {operation}.");
                return;
        }

        MessageIO.Write(output, MessageKind.Transformed, request.RequestId, result);
    }

    // Payload: [password][int32 n][n x key] then the PDF. Response: [int32 n][n x (key, value)],
    // for the requested keys the document has.
    private void GetMetadata(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        string? password = reader.ReadString();
        var keys = new string[reader.ReadInt32()];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = reader.ReadString() ?? string.Empty;
        }

        IReadOnlyDictionary<string, string> values = QpdfStructure.GetMetadata(reader.Remaining, keys, password);
        byte[] payload = new byte[4 + values.Sum(v => PayloadWriter.SizeOf(v.Key) + PayloadWriter.SizeOf(v.Value))];
        var writer = new PayloadWriter(payload);
        writer.WriteInt32(values.Count);
        foreach (var (key, value) in values)
        {
            writer.WriteString(key);
            writer.WriteString(value);
        }

        MessageIO.Write(output, MessageKind.Metadata, request.RequestId, payload);
    }

    // Payload: [docId][int32 n][n x int32 page][printer][title][output][int32 length][DEVMODE bytes]; strings via WriteString.
    private void Print(in Message request)
    {
        if (!OperatingSystem.IsWindows())
        {
            SendError(request.RequestId, WorkerErrorCode.NotSupported, 0, "Printing is implemented on Windows only in the spike.");
            return;
        }

        var reader = new PayloadReader(request.Payload);
        var document = (PdfiumDocument)_documents[reader.ReadInt32()];
        var pages = new int[reader.ReadInt32()];
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i] = reader.ReadInt32();
            ArgumentOutOfRangeException.ThrowIfNegative(pages[i]);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pages[i], document.PageCount);
        }

        string printer = reader.ReadString() ?? throw new ArgumentException("No printer named.");
        string title = reader.ReadString() ?? "Document";
        string? outputFile = reader.ReadString();
        ReadOnlySpan<byte> devMode = reader.ReadBytes(reader.ReadInt32());

        int printed = GdiPrinter.Print(document, printer, devMode, pages, title, outputFile);

        Span<byte> payload = stackalloc byte[4];
        new PayloadWriter(payload).WriteInt32(printed);
        MessageIO.Write(output, MessageKind.Printed, request.RequestId, payload);
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

    // Test command: commits the requested memory page by page, so a memory limit is hit for real,
    // then holds it for the given time, so a limit that is checked by polling can see it.
    private unsafe void AllocateForTest(in Message request)
    {
        var reader = new PayloadReader(request.Payload);
        int megabytes = reader.ReadInt32();
        int holdMilliseconds = reader.ReadInt32();
        nuint bytes = (nuint)megabytes * 1024 * 1024;
        byte* memory = (byte*)NativeMemory.Alloc(bytes);
        try
        {
            for (nuint offset = 0; offset < bytes; offset += 4096)
            {
                memory[offset] = 1;
            }

            Thread.Sleep(holdMilliseconds);
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
