using System.Runtime.InteropServices;

namespace PdfSuite.Pdf.Qpdf;

/// <summary>One qpdf_data handle over a private native copy of the input, which qpdf reads lazily.</summary>
internal sealed unsafe class QpdfFile : IDisposable
{
    internal const int PasswordError = 4;

    private nint _handle;
    private void* _buffer;

    private QpdfFile(nint handle, void* buffer)
    {
        _handle = handle;
        _buffer = buffer;
    }

    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            return _handle;
        }
    }

    public bool IsEncrypted => QpdfNative.qpdf_is_encrypted(Handle) != 0;

    public static QpdfFile Read(ReadOnlySpan<byte> pdf, string? password)
    {
        QpdfNative.EnsureResolver();
        nint handle = QpdfNative.qpdf_init();
        void* buffer = NativeMemory.Alloc((nuint)Math.Max(pdf.Length, 1));
        pdf.CopyTo(new Span<byte>(buffer, pdf.Length));
        var file = new QpdfFile(handle, buffer);
        try
        {
            // Errors are reported to the caller as exceptions, not printed to stderr.
            QpdfNative.qpdf_silence_errors(handle);
            QpdfNative.qpdf_set_suppress_warnings(handle, 1);
            file.Check(QpdfNative.qpdf_read_memory(handle, "document", (byte*)buffer, (ulong)pdf.Length, password));
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>The trailer's /Info dictionary; 0 if absent and not created.</summary>
    public uint InfoDictionary(bool create)
    {
        uint trailer = QpdfNative.qpdf_get_trailer(Handle);
        uint info = QpdfNative.qpdf_oh_get_key(Handle, trailer, "/Info");
        if (QpdfNative.qpdf_oh_is_dictionary(Handle, info) != 0)
        {
            return info;
        }

        if (!create)
        {
            return 0;
        }

        info = QpdfNative.qpdf_make_indirect_object(Handle, QpdfNative.qpdf_oh_new_dictionary(Handle));
        QpdfNative.qpdf_oh_replace_key(Handle, trailer, "/Info", info);
        return info;
    }

    /// <param name="encryptedOutput">
    /// Whether the output will be encrypted. qpdf forbids deterministic IDs then, and the setter
    /// that would complain throws a C++ exception instead of returning an error.
    /// </param>
    /// <param name="inspectable">Decode streams and avoid object streams, so the bytes can be searched.</param>
    public byte[] Write(Action<nint> configure, bool encryptedOutput, bool inspectable = false)
    {
        Check(QpdfNative.qpdf_init_write_memory(Handle));
        if (!encryptedOutput)
        {
            QpdfNative.qpdf_set_deterministic_ID(Handle, 1);
        }

        if (inspectable)
        {
            QpdfNative.qpdf_set_compress_streams(Handle, 0);
            QpdfNative.qpdf_set_decode_level(Handle, QpdfNative.qpdf_dl_all);
            QpdfNative.qpdf_set_object_stream_mode(Handle, QpdfNative.qpdf_o_disable);
        }

        configure(Handle);
        Check(QpdfNative.qpdf_write(Handle));
        int length = checked((int)QpdfNative.qpdf_get_buffer_length(Handle));
        return new ReadOnlySpan<byte>(QpdfNative.qpdf_get_buffer(Handle), length).ToArray();
    }

    /// <summary>Throws if an object-handle call reported an error.</summary>
    public void ThrowIfError()
    {
        if (QpdfNative.qpdf_has_error(Handle) != 0)
        {
            Check(QpdfNative.QPDF_ERRORS);
        }
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            nint handle = _handle;
            QpdfNative.qpdf_cleanup(&handle);
            _handle = 0;
        }

        if (_buffer != null)
        {
            NativeMemory.Free(_buffer);
            _buffer = null;
        }
    }

    private void Check(int result)
    {
        if ((result & QpdfNative.QPDF_ERRORS) == 0)
        {
            return;
        }

        nint error = QpdfNative.qpdf_get_error(_handle);
        int code = QpdfNative.qpdf_get_error_code(_handle, error);
        string text = Marshal.PtrToStringUTF8(QpdfNative.qpdf_get_error_full_text(_handle, error)) ?? "qpdf failed.";
        throw code == PasswordError
            ? new QpdfException(code, "The document needs a password, or the password is wrong.")
            : new QpdfException(code, text);
    }
}
