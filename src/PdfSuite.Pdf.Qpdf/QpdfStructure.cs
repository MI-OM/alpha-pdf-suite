using System.Runtime.InteropServices;

namespace PdfSuite.Pdf.Qpdf;

/// <summary>What a user may do with an encrypted document without the owner password.</summary>
public sealed record PdfPermissions(
    bool Print = true,
    bool CopyText = true,
    bool Modify = false,
    bool AnnotateAndFillForms = true,
    bool Assemble = false);

/// <summary>A qpdf failure. <see cref="Code"/> is qpdf's error code (for example 4 = wrong password).</summary>
public sealed class QpdfException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// Structural operations through qpdf (spike S4): the things PDFium cannot write, such as
/// encryption and document metadata, and full rewrites. Every operation reads the input and
/// writes a complete new file. A full write keeps only objects reachable from the trailer, so
/// earlier incremental revisions and unreferenced objects are dropped. Unencrypted output gets a
/// deterministic file ID, so the same input and operation give the same bytes; qpdf does not allow
/// deterministic IDs on encrypted output.
/// </summary>
public static unsafe class QpdfStructure
{
    private const int PasswordError = 4;

    public static bool IsEncrypted(ReadOnlySpan<byte> pdf)
    {
        // qpdf can tell without the password; a wrong-password error still means encrypted.
        try
        {
            using var file = QpdfFile.Read(pdf, password: null);
            return file.IsEncrypted;
        }
        catch (QpdfException ex) when (ex.Code == PasswordError)
        {
            return true;
        }
    }

    /// <summary>Encrypts with AES-256 (PDF 2.0 security handler revision 6).</summary>
    /// <param name="password">The input's password, if it is already encrypted.</param>
    public static byte[] Encrypt(ReadOnlySpan<byte> pdf, string userPassword, string ownerPassword, PdfPermissions permissions, string? password = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerPassword);
        using var file = QpdfFile.Read(pdf, password);
        return file.Write(
            handle => QpdfNative.qpdf_set_r6_encryption_parameters2(
                handle, userPassword, ownerPassword,
                allowAccessibility: 1, // Screen readers keep access; required by PDF 2.0 for revision 6.
                allowExtract: Flag(permissions.CopyText),
                allowAssemble: Flag(permissions.Assemble),
                allowAnnotateAndForm: Flag(permissions.AnnotateAndFillForms),
                allowFormFilling: Flag(permissions.AnnotateAndFillForms),
                allowModifyOther: Flag(permissions.Modify),
                print: permissions.Print ? QpdfNative.qpdf_r3p_full : QpdfNative.qpdf_r3p_none,
                encryptMetadata: 1),
            encryptedOutput: true);
    }

    /// <summary>Removes encryption. Needs the user or owner password.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> pdf, string password)
    {
        using var file = QpdfFile.Read(pdf, password);
        return file.Write(handle => QpdfNative.qpdf_set_preserve_encryption(handle, 0), encryptedOutput: false);
    }

    /// <summary>Reads document information keys such as "/Title"; absent keys are left out.</summary>
    public static IReadOnlyDictionary<string, string> GetMetadata(ReadOnlySpan<byte> pdf, IEnumerable<string> keys, string? password = null)
    {
        using var file = QpdfFile.Read(pdf, password);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        uint info = file.InfoDictionary(create: false);
        if (info != 0)
        {
            foreach (string key in keys)
            {
                uint value = QpdfNative.qpdf_oh_get_key(file.Handle, info, key);
                if (QpdfNative.qpdf_oh_is_string(file.Handle, value) != 0)
                {
                    values[key] = Marshal.PtrToStringUTF8(QpdfNative.qpdf_oh_get_utf8_value(file.Handle, value))!;
                }
            }
        }

        file.ThrowIfError();
        return values;
    }

    /// <summary>Sets document information keys ("/Title" and so on); a null value removes the key. Encryption is kept.</summary>
    public static byte[] SetMetadata(ReadOnlySpan<byte> pdf, IReadOnlyDictionary<string, string?> values, string? password = null)
    {
        using var file = QpdfFile.Read(pdf, password);
        uint info = file.InfoDictionary(create: true);
        foreach (var (key, value) in values)
        {
            if (value is null)
            {
                QpdfNative.qpdf_oh_remove_key(file.Handle, info, key);
            }
            else
            {
                QpdfNative.qpdf_oh_replace_key(file.Handle, info, key, QpdfNative.qpdf_oh_new_unicode_string(file.Handle, value));
            }
        }

        file.ThrowIfError();
        return file.Write(_ => { }, encryptedOutput: file.IsEncrypted);
    }

    /// <summary>
    /// Rewrites the whole file, keeping any encryption. With <paramref name="inspectable"/>,
    /// streams are decoded and object streams are not used, so the output's bytes can be searched
    /// for leftover content (see <see cref="PdfText.Contains"/>).
    /// </summary>
    public static byte[] Rewrite(ReadOnlySpan<byte> pdf, string? password = null, bool inspectable = false)
    {
        using var file = QpdfFile.Read(pdf, password);
        return file.Write(
            handle =>
            {
                if (inspectable)
                {
                    QpdfNative.qpdf_set_compress_streams(handle, 0);
                    QpdfNative.qpdf_set_decode_level(handle, QpdfNative.qpdf_dl_all);
                    QpdfNative.qpdf_set_object_stream_mode(handle, QpdfNative.qpdf_o_disable);
                }
            },
            encryptedOutput: file.IsEncrypted);
    }

    private static int Flag(bool value) => value ? 1 : 0;

    /// <summary>One qpdf_data handle over a private native copy of the input, which qpdf reads lazily.</summary>
    private sealed class QpdfFile : IDisposable
    {
        private nint _handle;
        private void* _buffer;

        private QpdfFile(nint handle, void* buffer)
        {
            _handle = handle;
            _buffer = buffer;
        }

        public nint Handle => _handle;

        public bool IsEncrypted => QpdfNative.qpdf_is_encrypted(_handle) != 0;

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
            uint trailer = QpdfNative.qpdf_get_trailer(_handle);
            uint info = QpdfNative.qpdf_oh_get_key(_handle, trailer, "/Info");
            if (QpdfNative.qpdf_oh_is_dictionary(_handle, info) != 0)
            {
                return info;
            }

            if (!create)
            {
                return 0;
            }

            info = QpdfNative.qpdf_make_indirect_object(_handle, QpdfNative.qpdf_oh_new_dictionary(_handle));
            QpdfNative.qpdf_oh_replace_key(_handle, trailer, "/Info", info);
            return info;
        }

        /// <param name="encryptedOutput">
        /// Whether the output will be encrypted. qpdf forbids deterministic IDs then, and the
        /// setter that would complain throws a C++ exception instead of returning an error.
        /// </param>
        public byte[] Write(Action<nint> configure, bool encryptedOutput)
        {
            Check(QpdfNative.qpdf_init_write_memory(_handle));
            if (!encryptedOutput)
            {
                QpdfNative.qpdf_set_deterministic_ID(_handle, 1);
            }

            configure(_handle);
            Check(QpdfNative.qpdf_write(_handle));
            int length = checked((int)QpdfNative.qpdf_get_buffer_length(_handle));
            return new ReadOnlySpan<byte>(QpdfNative.qpdf_get_buffer(_handle), length).ToArray();
        }

        /// <summary>Throws if an object-handle call reported an error.</summary>
        public void ThrowIfError()
        {
            if (QpdfNative.qpdf_has_error(_handle) != 0)
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
}
