using System.Text.Json;
using System.Text.Json.Nodes;

namespace PdfSuite.App;

/// <summary>
/// The recently opened documents (Open Recent in docs/04-UI-COPY-AND-PRODUCT-RULES.md §3): up to
/// <see cref="Capacity"/> full paths, newest first, in a small JSON file in the user's application
/// data folder. Only paths are kept, never contents or previews, and nothing leaves the device
/// (decisions for review, D8). A file that cannot be read or written is treated as an empty list.
/// </summary>
internal sealed class RecentFiles
{
    public const int Capacity = 10;

    private readonly string _path;
    private readonly List<string> _paths = [];

    public RecentFiles(string directory)
    {
        _path = Path.Combine(directory, "recent-files.json");
        Load();
    }

    /// <summary>
    /// The application data folder, or PDF_SUITE_SETTINGS_DIR if set, which tests use to keep a
    /// person's own list untouched.
    /// </summary>
    public static string DefaultDirectory() =>
        Environment.GetEnvironmentVariable("PDF_SUITE_SETTINGS_DIR") is { Length: > 0 } directory
            ? directory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PDF Suite (spike)");

    public IReadOnlyList<string> Paths => _paths;

    public void Add(string path)
    {
        string full = Path.GetFullPath(path);
        _paths.RemoveAll(p => SamePath(p, full));
        _paths.Insert(0, full);
        if (_paths.Count > Capacity)
        {
            _paths.RemoveRange(Capacity, _paths.Count - Capacity);
        }

        Save();
    }

    public void Remove(string path)
    {
        if (_paths.RemoveAll(p => SamePath(p, path)) > 0)
        {
            Save();
        }
    }

    /// <summary>Forgets every document, and deletes the file.</summary>
    public void Clear()
    {
        _paths.Clear();
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The list in memory is empty either way.
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private void Load()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(_path)) is JsonArray array)
            {
                foreach (JsonNode? node in array)
                {
                    if (node?.GetValueKind() == JsonValueKind.String && node.GetValue<string>() is { } path
                        && Path.IsPathFullyQualified(path) && !_paths.Exists(p => SamePath(p, path)) && _paths.Count < Capacity)
                    {
                        _paths.Add(path);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _paths.Clear();
        }
    }

    // Written to a temporary file and moved into place, so a crash cannot leave half a list.
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            var array = new JsonArray();
            foreach (string path in _paths)
            {
                array.Add((JsonNode?)JsonValue.Create(path));
            }

            File.WriteAllText(temporary, array.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembering a document is not worth interrupting the reader for.
        }
    }
}
