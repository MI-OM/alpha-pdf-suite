using System.Runtime.InteropServices;

namespace PdfSuite.Pdf.Qpdf;

/// <summary>A qpdf object handle, valid while the <see cref="QpdfEdit"/> that made it is open.</summary>
public readonly record struct QpdfObject(uint Handle);

/// <summary>
/// Edits a document through qpdf's object API: read, change dictionaries, arrays and streams,
/// write a complete new file. Redaction uses it to strip document-level content; the fixture
/// generator uses it to build structures PDFium cannot write. Every call checks qpdf's error state,
/// so a failed edit throws at once rather than at save time.
/// </summary>
public sealed unsafe class QpdfEdit : IDisposable
{
    private readonly QpdfFile _file;

    private QpdfEdit(QpdfFile file) => _file = file;

    public static QpdfEdit Open(ReadOnlySpan<byte> pdf, string? password = null) => new(QpdfFile.Read(pdf, password));

    /// <summary>
    /// Opens a copy that qpdf has rewritten keeping unreferenced objects. Objects are then numbered
    /// from 1 with generation 0, so <see cref="AllObjects"/> sees everything the file holds, not
    /// just what the trailer reaches. Encryption is removed, so a password is needed for encrypted input.
    /// </summary>
    public static QpdfEdit OpenEverything(ReadOnlySpan<byte> pdf, string? password = null)
    {
        byte[] renumbered;
        using (var file = QpdfFile.Read(pdf, password))
        {
            renumbered = file.Write(
                handle =>
                {
                    QpdfNative.qpdf_set_preserve_unreferenced_objects(handle, 1);
                    QpdfNative.qpdf_set_preserve_encryption(handle, 0);
                },
                encryptedOutput: false);
        }

        return Open(renumbered);
    }

    public QpdfObject Root => Checked(QpdfNative.qpdf_get_root(Handle));

    public QpdfObject Trailer => Checked(QpdfNative.qpdf_get_trailer(Handle));

    public int PageCount => Checked(QpdfNative.qpdf_get_num_pages(Handle));

    private nint Handle => _file.Handle;

    public QpdfObject Page(int index) => Checked(QpdfNative.qpdf_get_page_n(Handle, (nuint)index));

    public QpdfObject Get(QpdfObject dictionary, string key) => Checked(QpdfNative.qpdf_oh_get_key(Handle, dictionary.Handle, key));

    public bool Has(QpdfObject dictionary, string key) => Checked(QpdfNative.qpdf_oh_has_key(Handle, dictionary.Handle, key)) != 0;

    public bool IsDictionary(QpdfObject value) => QpdfNative.qpdf_oh_is_dictionary(Handle, value.Handle) != 0;

    public bool IsArray(QpdfObject value) => QpdfNative.qpdf_oh_is_array(Handle, value.Handle) != 0;

    public bool IsStream(QpdfObject value) => QpdfNative.qpdf_oh_is_stream(Handle, value.Handle) != 0;

    public bool IsString(QpdfObject value) => QpdfNative.qpdf_oh_is_string(Handle, value.Handle) != 0;

    public bool IsNull(QpdfObject value) => QpdfNative.qpdf_oh_is_null(Handle, value.Handle) != 0;

    /// <summary>Whether this is a reference to an object of its own rather than a value held in place.</summary>
    public bool IsIndirect(QpdfObject value) => QpdfNative.qpdf_oh_is_indirect(Handle, value.Handle) != 0;

    /// <summary>Whether <paramref name="value"/> is the name <paramref name="name"/> (written with its slash, "/Page").</summary>
    public bool IsName(QpdfObject value, string name) => QpdfNative.qpdf_oh_is_name_and_equals(Handle, value.Handle, name) != 0;

    /// <summary>The object number of an indirect object; 0 for a direct value.</summary>
    public int ObjectId(QpdfObject value) => Checked(QpdfNative.qpdf_oh_get_object_id(Handle, value.Handle));

    public IReadOnlyList<string> Keys(QpdfObject dictionary)
    {
        var keys = new List<string>();
        QpdfNative.qpdf_oh_begin_dict_key_iter(Handle, dictionary.Handle);
        while (QpdfNative.qpdf_oh_dict_more_keys(Handle) != 0)
        {
            keys.Add(Marshal.PtrToStringUTF8(QpdfNative.qpdf_oh_dict_next_key(Handle))!);
        }

        _file.ThrowIfError();
        return keys;
    }

    /// <summary>
    /// Every indirect object with generation 0, by object number from 1 to the trailer's /Size.
    /// Complete for files opened with <see cref="OpenEverything"/>.
    /// </summary>
    public IEnumerable<QpdfObject> AllObjects()
    {
        int size = Checked(QpdfNative.qpdf_oh_get_int_value_as_int(Handle, Get(Trailer, "/Size").Handle));
        for (int id = 1; id < size; id++)
        {
            QpdfObject value = Checked(QpdfNative.qpdf_get_object_by_id(Handle, id, 0));
            if (!IsNull(value))
            {
                yield return value;
            }
        }
    }

    /// <summary>A string's value as text: PDFDocEncoding and UTF-16 decoded.</summary>
    public string Text(QpdfObject value)
    {
        nint utf8 = QpdfNative.qpdf_oh_get_utf8_value(Handle, value.Handle);
        _file.ThrowIfError();
        return Marshal.PtrToStringUTF8(utf8) ?? "";
    }

    /// <summary>A string's bytes as written, with escapes and hex resolved but no text decoding.</summary>
    public byte[] Bytes(QpdfObject value)
    {
        nuint length;
        byte* bytes = QpdfNative.qpdf_oh_get_binary_string_value(Handle, value.Handle, &length);
        _file.ThrowIfError();
        return new ReadOnlySpan<byte>(bytes, checked((int)length)).ToArray();
    }

    /// <summary>A stream's data with general-purpose filters (such as Flate) decoded; raw if they cannot be.</summary>
    public byte[] StreamData(QpdfObject stream)
    {
        byte* buffer = null;
        nuint length;
        QpdfNative.qpdf_oh_get_stream_data(Handle, stream.Handle, QpdfNative.qpdf_dl_generalized, null, &buffer, &length);
        try
        {
            _file.ThrowIfError();
            return new ReadOnlySpan<byte>(buffer, checked((int)length)).ToArray();
        }
        finally
        {
            if (buffer != null)
            {
                QpdfNative.qpdf_oh_free_buffer(&buffer);
            }
        }
    }

    /// <summary>Replaces a stream's data with <paramref name="data"/>, unfiltered.</summary>
    public void ReplaceStreamData(QpdfObject stream, ReadOnlySpan<byte> data)
    {
        uint none = QpdfNative.qpdf_oh_new_null(Handle);
        _file.ThrowIfError();
        fixed (byte* bytes = data)
        {
            QpdfNative.qpdf_oh_replace_stream_data(Handle, stream.Handle, bytes, (nuint)data.Length, none, none);
        }

        _file.ThrowIfError();
    }

    public IReadOnlyList<QpdfObject> Items(QpdfObject array)
    {
        int count = Checked(QpdfNative.qpdf_oh_get_array_n_items(Handle, array.Handle));
        return [.. Enumerable.Range(0, count).Select(i => Checked(QpdfNative.qpdf_oh_get_array_item(Handle, array.Handle, i)))];
    }

    public void Set(QpdfObject dictionary, string key, QpdfObject value)
    {
        QpdfNative.qpdf_oh_replace_key(Handle, dictionary.Handle, key, value.Handle);
        _file.ThrowIfError();
    }

    public void Remove(QpdfObject dictionary, string key)
    {
        QpdfNative.qpdf_oh_remove_key(Handle, dictionary.Handle, key);
        _file.ThrowIfError();
    }

    public void Append(QpdfObject array, QpdfObject item)
    {
        QpdfNative.qpdf_oh_append_item(Handle, array.Handle, item.Handle);
        _file.ThrowIfError();
    }

    public QpdfObject NewDictionary() => Checked(QpdfNative.qpdf_oh_new_dictionary(Handle));

    public QpdfObject NewArray() => Checked(QpdfNative.qpdf_oh_new_array(Handle));

    public QpdfObject NewName(string name) => Checked(QpdfNative.qpdf_oh_new_name(Handle, name));

    /// <summary>A PDF text string (PDFDocEncoding where possible, otherwise UTF-16BE).</summary>
    public QpdfObject NewText(string text) => Checked(QpdfNative.qpdf_oh_new_unicode_string(Handle, text));

    public QpdfObject NewInteger(long value) => Checked(QpdfNative.qpdf_oh_new_integer(Handle, value));

    /// <summary>A new indirect stream holding <paramref name="data"/> unfiltered; set its dictionary entries with <see cref="StreamDictionary"/>.</summary>
    public QpdfObject NewStream(ReadOnlySpan<byte> data)
    {
        QpdfObject stream = Checked(QpdfNative.qpdf_oh_new_stream(Handle));
        ReplaceStreamData(stream, data);
        return stream;
    }

    public QpdfObject StreamDictionary(QpdfObject stream) => Checked(QpdfNative.qpdf_oh_get_dict(Handle, stream.Handle));

    public QpdfObject MakeIndirect(QpdfObject value) => Checked(QpdfNative.qpdf_make_indirect_object(Handle, value.Handle));

    /// <summary>Writes the edited document as a complete new file (deterministic ID unless encrypted).</summary>
    public byte[] Save(bool inspectable = false) => _file.Write(_ => { }, encryptedOutput: _file.IsEncrypted, inspectable);

    public void Dispose() => _file.Dispose();

    private QpdfObject Checked(uint handle)
    {
        _file.ThrowIfError();
        return new QpdfObject(handle);
    }

    private int Checked(int value)
    {
        _file.ThrowIfError();
        return value;
    }
}
