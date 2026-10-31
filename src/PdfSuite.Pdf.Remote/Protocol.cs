using System.Buffers;
using System.Buffers.Binary;

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
    AllocateForTest = 90,

    // Responses
    Opened = 101,
    Tile = 102,
    Ack = 103,
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

    public readonly Span<byte> Written => _buffer[.._position];
}
