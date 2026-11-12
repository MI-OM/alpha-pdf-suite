namespace PdfSuite.Pdf.Abstractions;

/// <summary>
/// Writing a file so that a failure never leaves it half-written (copy rules §13, "Save failed"). The app
/// saves every document this way; it is here, not in the app, so it is tested on every OS.
/// </summary>
public static class SafeFile
{
    /// <summary>
    /// Writes the bytes to a temporary file in the same folder, flushes it to disk, reads it back and
    /// compares, then moves it over the target in one step. The target is untouched if any of that
    /// fails, and the temporary file is removed.
    /// </summary>
    /// <exception cref="InvalidDataException">What was read back differs from what was written.</exception>
    public static async Task WriteReplacingAsync(string path, byte[] data)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string temporary = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await stream.WriteAsync(data);
                stream.Flush(flushToDisk: true);
            }

            byte[] written = await File.ReadAllBytesAsync(temporary);
            if (!written.AsSpan().SequenceEqual(data))
            {
                throw new InvalidDataException("The file written differs from the document.");
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
