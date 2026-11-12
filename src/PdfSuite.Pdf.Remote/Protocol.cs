using System.Buffers;
using System.Buffers.Binary;
using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.Pdf.Remote;

/// <summary>
/// Messages between the app and the worker process. Requests travel on the worker's stdin
/// and responses on its stdout. Each frame is
/// [int32 payload length][byte kind][int32 request id][payload], little-endian.
/// </summary>
public enum MessageKind : byte
{
    // Requests
    Open = 1,
    RenderTile = 2,
    Cancel = 3,
    Close = 4,
    GetTextLines = 5,
    Print = 6,
    GetEngineInfo = 7,
    Redact = 8,
    Transform = 9,
    GetMetadata = 10,
    FindText = 11,
    GetDocumentInfo = 12,
    GetCharacters = 13,
    GetOutline = 14,
    RebuildPages = 15,
    AllocateForTest = 90,
    CountDocumentsForTest = 91,

    // Responses
    Opened = 101,
    Tile = 102,
    Ack = 103,
    TextLines = 105,
    Printed = 106,
    EngineInfo = 107,
    Redacted = 108,
    Transformed = 109,
    Metadata = 110,
    TextMatches = 111,
    DocumentInfo = 112,
    Characters = 113,
    Outline = 114,
    Error = 199,
}

public enum WorkerErrorCode
{
    Unknown = 0,
    InvalidRequest = 1,
    DocumentNotFound = 2,
    PageOutOfRange = 3,
    OpenFailed = 4,
    OutOfMemory = 5,
    PrintFailed = 6,
    NotSupported = 7,

    /// <summary>qpdf could not process the document. The detail is qpdf's error code.</summary>
    StructureFailed = 8,

    /// <summary>A source document's permissions forbid the operation. The detail is the source's index.</summary>
    NotAllowed = 9,
}

/// <summary>The whole-file operations a <see cref="MessageKind.Transform"/> request performs with qpdf.</summary>
public enum TransformOperation
{
    Encrypt = 1,
    Decrypt = 2,
    SetMetadata = 3,
    Rewrite = 4,
}

/// <summary>How <see cref="PdfPermissions"/> travel in an encrypt request: one bit per permission.</summary>
public static class PermissionBits
{
    public static int Encode(PdfPermissions permissions) =>
        (permissions.Print ? 1 : 0) | (permissions.CopyText ? 2 : 0) | (permissions.Modify ? 4 : 0)
        | (permissions.AnnotateAndFillForms ? 8 : 0) | (permissions.Assemble ? 16 : 0);

    public static PdfPermissions Decode(int bits) =>
        new((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0);
}

/// <summary>A received frame. The payload is rented from the shared pool; dispose to return it.</summary>
public readonly struct Message : IDisposable
{
    private readonly byte[] _buffer;

    internal Message(MessageKind kind, int requestId, byte[] buffer, int length)
    {
        Kind = kind;
        RequestId = requestId;
        _buffer = buffer;
        Length = length;
    }

    public MessageKind Kind { get; }

    public int RequestId { get; }

    public int Length { get; }

    public ReadOnlySpan<byte> Payload => _buffer.AsSpan(0, Length);

    internal byte[] DetachBuffer() => _buffer;

    public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);
}

public static class MessageIO
{
    // Large enough for any document a user can open in the spike; anything bigger is treated as corrupt framing.
    public const int MaxPayloadLength = 1024 * 1024 * 1024;

    private const int HeaderLength = 9;

    /// <summary>Writes one frame whose payload is <paramref name="first"/> followed by <paramref name="second"/>.</summary>
    public static void Write(Stream output, MessageKind kind, int requestId, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second = default)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteInt32LittleEndian(header, checked(first.Length + second.Length));
        header[4] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(header[5..], requestId);
        output.Write(header);
        output.Write(first);
        output.Write(second);
        output.Flush();
    }

    /// <summary>Reads one frame. Returns false at a clean end of stream (the other side closed).</summary>
    /// <exception cref="EndOfStreamException">The stream ended inside a frame.</exception>
    /// <exception cref="InvalidDataException">The frame header is not valid.</exception>
    public static bool TryRead(Stream input, out Message message)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        int read = input.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false);
        if (read == 0)
        {
            message = default;
            return false;
        }

        if (read < HeaderLength)
        {
            throw new EndOfStreamException("The stream ended inside a frame header.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxPayloadLength)
        {
            throw new InvalidDataException($"Invalid frame length {length}.");
        }

        var kind = (MessageKind)header[4];
        int requestId = BinaryPrimitives.ReadInt32LittleEndian(header[5..]);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        try
        {
            input.ReadExactly(buffer, 0, length);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        message = new Message(kind, requestId, buffer, length);
        return true;
    }
}

/// <summary>Sequential little-endian reader over a payload.</summary>
public ref struct PayloadReader(ReadOnlySpan<byte> payload)
{
    private ReadOnlySpan<byte> _rest = payload;

    public int ReadInt32()
    {
        int value = BinaryPrimitives.ReadInt32LittleEndian(_rest);
        _rest = _rest[4..];
        return value;
    }

    public long ReadInt64()
    {
        long value = BinaryPrimitives.ReadInt64LittleEndian(_rest);
        _rest = _rest[8..];
        return value;
    }

    public double ReadDouble()
    {
        double value = BinaryPrimitives.ReadDoubleLittleEndian(_rest);
        _rest = _rest[8..];
        return value;
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        ReadOnlySpan<byte> value = _rest[..count];
        _rest = _rest[count..];
        return value;
    }

    /// <summary>Reads an [int32 length][UTF-8] string; a length of -1 means null.</summary>
    public string? ReadString()
    {
        int length = ReadInt32();
        return length < 0 ? null : System.Text.Encoding.UTF8.GetString(ReadBytes(length));
    }

    public readonly ReadOnlySpan<byte> Remaining => _rest;
}

/// <summary>Sequential little-endian writer into a caller-provided buffer.</summary>
public ref struct PayloadWriter(Span<byte> buffer)
{
    private readonly Span<byte> _buffer = buffer;
    private int _position;

    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer[_position..], value);
        _position += 4;
    }

    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_buffer[_position..], value);
        _position += 8;
    }

    public void WriteDouble(double value)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(_buffer[_position..], value);
        _position += 8;
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        value.CopyTo(_buffer[_position..]);
        _position += value.Length;
    }

    /// <summary>Writes an [int32 length][UTF-8] string; null is written as length -1.</summary>
    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteInt32(-1);
            return;
        }

        int length = System.Text.Encoding.UTF8.GetBytes(value, _buffer[(_position + 4)..]);
        WriteInt32(length);
        _position += length;
    }

    /// <summary>Bytes needed to write <paramref name="value"/> with <see cref="WriteString"/>.</summary>
    public static int SizeOf(string? value) => 4 + (value is null ? 0 : System.Text.Encoding.UTF8.GetByteCount(value));

    public readonly Span<byte> Written => _buffer[.._position];
}
