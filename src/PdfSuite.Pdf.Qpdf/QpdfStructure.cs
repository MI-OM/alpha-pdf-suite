using System.Runtime.InteropServices;
using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.Pdf.Qpdf;

/// <summary>A qpdf failure. <see cref="Code"/> is qpdf's error code (for example 4 = wrong password).</summary>
public sealed class QpdfException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;

    /// <summary>The document needs a password, or the one given is wrong.</summary>
    public bool IsPasswordError => Code == QpdfFile.PasswordError;
}

/// <summary>
/// Structural operations through qpdf (spike S4): the things PDFium cannot write, such as
/// encryption and document metadata, and full rewrites. Every operation reads the input and
/// writes a complete new file. A full write keeps only objects reachable from the trailer, so
/// earlier incremental revisions and unreferenced objects are dropped. Unencrypted output gets a
/// deterministic file ID, so the same input and operation give the same bytes; qpdf does not allow
/// deterministic IDs on encrypted output.
/// </summary>
public static class QpdfStructure
{
    /// <summary>The version of the loaded qpdf library. Loads it on first use.</summary>
    public static unsafe string LibraryVersion
    {
        get
        {
            QpdfNative.EnsureResolver();
            return System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)QpdfNative.qpdf_get_qpdf_version()) ?? string.Empty;
        }
    }

    public static bool IsEncrypted(ReadOnlySpan<byte> pdf)
    {
        // qpdf can tell without the password; a wrong-password error still means encrypted.
        try
        {
            using var file = QpdfFile.Read(pdf, password: null);
            return file.IsEncrypted;
        }
        catch (QpdfException ex) when (ex.Code == QpdfFile.PasswordError)
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
        return file.Write(_ => { }, encryptedOutput: file.IsEncrypted, inspectable);
    }

    private static int Flag(bool value) => value ? 1 : 0;
}
