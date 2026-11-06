using System.Reflection;
using System.Runtime.InteropServices;

namespace PdfSuite.Pdf.Qpdf;

// Project-owned bindings for the subset of qpdf's C API (qpdf-c.h, qpdf 12.4.2) used here.
// QPDF_BOOL and QPDF_ERROR_CODE are int. Strings are UTF-8. A qpdf_data handle is not thread-safe.
// Caution: some void-returning functions can throw a C++ exception instead of reporting an error.
// It surfaces as SEHException on Windows and aborts the process on Linux and macOS. Known case:
// setting encryption while deterministic IDs are on. Callers must avoid those combinations.
internal static unsafe partial class QpdfNative
{
    private const string Library = "qpdf";

    // QPDF_ERROR_CODE bits.
    internal const int QPDF_ERRORS = 1 << 1;

    // qpdf_stream_decode_level_e.
    internal const int qpdf_dl_generalized = 1;
    internal const int qpdf_dl_all = 3;

    // qpdf_object_stream_e.
    internal const int qpdf_o_disable = 0;

    // qpdf_r3_print_e.
    internal const int qpdf_r3p_full = 0;
    internal const int qpdf_r3p_none = 2;

    private static int s_resolverInstalled;

    /// <summary>Installs the resolver that finds qpdf's library next to the app. Call before any qpdf call.</summary>
    internal static void EnsureResolver()
    {
        if (Interlocked.Exchange(ref s_resolverInstalled, 1) == 0)
        {
            NativeLibrary.SetDllImportResolver(typeof(QpdfNative).Assembly, Resolve);
        }
    }

    [LibraryImport(Library)]
    internal static partial byte* qpdf_get_qpdf_version();

    [LibraryImport(Library)]
    internal static partial nint qpdf_init();

    [LibraryImport(Library)]
    internal static partial void qpdf_cleanup(nint* qpdf);

    [LibraryImport(Library)]
    internal static partial void qpdf_silence_errors(nint qpdf);

    [LibraryImport(Library)]
    internal static partial void qpdf_set_suppress_warnings(nint qpdf, int value);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int qpdf_read_memory(nint qpdf, string description, byte* buffer, ulong size, string? password);

    [LibraryImport(Library)]
    internal static partial int qpdf_is_encrypted(nint qpdf);

    // Object-handle API. qpdf_oh is an unsigned int; handles are freed by qpdf_cleanup. These calls
    // trap errors and report them through qpdf_has_error. (qpdf_set_info_key is not used: it stores
    // the bytes it is given, so UTF-8 text would be misread by every PDF reader.)
    [LibraryImport(Library)]
    internal static partial uint qpdf_get_trailer(nint qpdf);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial uint qpdf_oh_get_key(nint qpdf, uint dictionary, string key);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_is_dictionary(nint qpdf, uint handle);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_is_string(nint qpdf, uint handle);

    [LibraryImport(Library)]
    internal static partial uint qpdf_oh_new_dictionary(nint qpdf);

    [LibraryImport(Library)]
    internal static partial uint qpdf_make_indirect_object(nint qpdf, uint handle);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void qpdf_oh_replace_key(nint qpdf, uint dictionary, string key, uint item);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void qpdf_oh_remove_key(nint qpdf, uint dictionary, string key);

    // Makes a PDF text string: PDFDocEncoding where possible, otherwise UTF-16BE.
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial uint qpdf_oh_new_unicode_string(nint qpdf, string utf8);

    // Returns UTF-8 owned by qpdf, valid until the next call on this handle.
    [LibraryImport(Library)]
    internal static partial nint qpdf_oh_get_utf8_value(nint qpdf, uint handle);

    [LibraryImport(Library)]
    internal static partial int qpdf_has_error(nint qpdf);

    [LibraryImport(Library)]
    internal static partial uint qpdf_get_root(nint qpdf);

    [LibraryImport(Library)]
    internal static partial int qpdf_get_num_pages(nint qpdf);

    [LibraryImport(Library)]
    internal static partial uint qpdf_get_page_n(nint qpdf, nuint zeroBasedIndex);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int qpdf_oh_has_key(nint qpdf, uint dictionary, string key);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_is_array(nint qpdf, uint handle);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_get_array_n_items(nint qpdf, uint array);

    [LibraryImport(Library)]
    internal static partial uint qpdf_oh_get_array_item(nint qpdf, uint array, int index);

    [LibraryImport(Library)]
    internal static partial uint qpdf_oh_new_array(nint qpdf);

    [LibraryImport(Library)]
    internal static partial void qpdf_oh_append_item(nint qpdf, uint array, uint item);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial uint qpdf_oh_new_name(nint qpdf, string name);

    [LibraryImport(Library)]
    internal static partial uint qpdf_oh_new_integer(nint qpdf, long value);

    [LibraryImport(Library)]
    internal static partial uint qpdf_oh_new_bool(nint qpdf, int value);

    [LibraryImport(Library)]
    internal static partial uint qpdf_oh_new_null(nint qpdf);

    [LibraryImport(Library)]
    internal static partial uint qpdf_oh_new_stream(nint qpdf);

    [LibraryImport(Library)]
    internal static partial void qpdf_oh_replace_stream_data(nint qpdf, uint stream, byte* data, nuint length, uint filter, uint decodeParms);

    [LibraryImport(Library)]
    internal static partial uint qpdf_oh_get_dict(nint qpdf, uint stream);

    // Returns a null object, not an error, for an object number that is not in the file.
    [LibraryImport(Library)]
    internal static partial uint qpdf_get_object_by_id(nint qpdf, int objectId, int generation);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_get_object_id(nint qpdf, uint handle);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_is_null(nint qpdf, uint handle);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_is_stream(nint qpdf, uint handle);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_is_indirect(nint qpdf, uint handle);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int qpdf_oh_is_name_and_equals(nint qpdf, uint handle, string name);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_get_int_value_as_int(nint qpdf, uint handle);

    // Returns bytes owned by qpdf, valid until the next call on this handle.
    [LibraryImport(Library)]
    internal static partial byte* qpdf_oh_get_binary_string_value(nint qpdf, uint handle, nuint* length);

    // Only one dictionary can be iterated at a time; each key is valid until the next call.
    [LibraryImport(Library)]
    internal static partial void qpdf_oh_begin_dict_key_iter(nint qpdf, uint dictionary);

    [LibraryImport(Library)]
    internal static partial int qpdf_oh_dict_more_keys(nint qpdf);

    [LibraryImport(Library)]
    internal static partial nint qpdf_oh_dict_next_key(nint qpdf);

    // The buffer is allocated by qpdf and must be released with qpdf_oh_free_buffer.
    [LibraryImport(Library)]
    internal static partial int qpdf_oh_get_stream_data(nint qpdf, uint stream, int decodeLevel, int* filtered, byte** buffer, nuint* length);

    [LibraryImport(Library)]
    internal static partial void qpdf_oh_free_buffer(byte** buffer);

    [LibraryImport(Library)]
    internal static partial void qpdf_set_preserve_unreferenced_objects(nint qpdf, int value);

    [LibraryImport(Library)]
    internal static partial int qpdf_init_write_memory(nint qpdf);

    [LibraryImport(Library)]
    internal static partial int qpdf_write(nint qpdf);

    [LibraryImport(Library)]
    internal static partial nuint qpdf_get_buffer_length(nint qpdf);

    [LibraryImport(Library)]
    internal static partial byte* qpdf_get_buffer(nint qpdf);

    [LibraryImport(Library)]
    internal static partial void qpdf_set_preserve_encryption(nint qpdf, int value);

    [LibraryImport(Library)]
    internal static partial void qpdf_set_deterministic_ID(nint qpdf, int value);

    [LibraryImport(Library)]
    internal static partial void qpdf_set_compress_streams(nint qpdf, int value);

    [LibraryImport(Library)]
    internal static partial void qpdf_set_decode_level(nint qpdf, int level);

    [LibraryImport(Library)]
    internal static partial void qpdf_set_object_stream_mode(nint qpdf, int mode);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void qpdf_set_r6_encryption_parameters2(
        nint qpdf, string userPassword, string ownerPassword,
        int allowAccessibility, int allowExtract, int allowAssemble, int allowAnnotateAndForm,
        int allowFormFilling, int allowModifyOther, int print, int encryptMetadata);

    [LibraryImport(Library)]
    internal static partial nint qpdf_get_error(nint qpdf);

    [LibraryImport(Library)]
    internal static partial int qpdf_get_error_code(nint qpdf, nint error);

    [LibraryImport(Library)]
    internal static partial nint qpdf_get_error_full_text(nint qpdf, nint error);

    /// <summary>
    /// Loads qpdf from the app directory by its real file name: release archives name it with a
    /// version (qpdf30.dll, libqpdf.so.30.4.2, libqpdf.30.4.2.dylib), and the symlinks that give
    /// it a plain name do not survive extraction. On Linux the libraries qpdf's archive bundles
    /// (GnuTLS and its dependencies, libjpeg) are loaded first, so the loader finds them by soname.
    /// </summary>
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library)
        {
            return 0;
        }

        string directory = AppContext.BaseDirectory;
        string pattern = OperatingSystem.IsWindows() ? "qpdf*.dll"
            : OperatingSystem.IsMacOS() ? "libqpdf*.dylib"
            : "libqpdf.so*";
        string? qpdf = Directory.EnumerateFiles(directory, pattern).MaxBy(path => new FileInfo(path).Length);
        if (qpdf is null)
        {
            throw new DllNotFoundException($"qpdf's library ({pattern}) is not in {directory}. Run tools/qpdf/fetch-qpdf.ps1, then rebuild.");
        }

        if (OperatingSystem.IsLinux())
        {
            PreloadBundledLibraries(directory, qpdf);
        }

        return NativeLibrary.Load(qpdf);
    }

    // The libraries qpdf's Linux archive bundles (qpdf 12.4.2).
    private static readonly string[] BundledLinuxLibraries =
        ["libunistring", "libidn2", "libtasn1", "libffi", "libp11-kit", "libnettle", "libhogweed", "libgnutls", "libjpeg"];

    // Loads the bundled libraries, retrying until no more succeed, so dependencies load before dependants.
    private static void PreloadBundledLibraries(string directory, string qpdf)
    {
        var pending = Directory.EnumerateFiles(directory, "*.so*")
            .Where(path => path != qpdf && BundledLinuxLibraries.Any(name => Path.GetFileName(path).StartsWith(name + ".so", StringComparison.Ordinal)))
            .ToList();
        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            foreach (string path in pending.ToArray())
            {
                if (NativeLibrary.TryLoad(path, out _))
                {
                    pending.Remove(path);
                    progress = true;
                }
            }
        }
    }
}
