using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Saving without data loss (PRD §11; roadmap Milestone 3: "operations do not unexpectedly
/// overwrite source files"; copy rules §13). Every save in the app goes through SafeFile.
/// </summary>
public sealed class SafeFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pdf-suite-safe-file-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task WritesExactlyTheBytes_AndLeavesNothingElse()
    {
        string path = Path.Combine(_folder, "new.pdf");
        byte[] data = [.. Enumerable.Range(0, 100_000).Select(i => (byte)(i * 7))];

        await SafeFile.WriteReplacingAsync(path, data);

        Assert.Equal(data, await File.ReadAllBytesAsync(path, Ct));
        Assert.Equal(["new.pdf"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ReplacesALongerFile_Completely()
    {
        string path = Path.Combine(_folder, "existing.pdf");
        await File.WriteAllBytesAsync(path, new byte[50_000], Ct);
        byte[] data = [1, 2, 3];

        await SafeFile.WriteReplacingAsync(path, data);

        Assert.Equal(data, await File.ReadAllBytesAsync(path, Ct));
        Assert.Single(Directory.GetFiles(_folder));
    }

    // Whatever goes wrong, the target is as it was and no temporary file is left behind.
    [Fact]
    public async Task AFailedSave_LeavesTheTargetAndTheFolderAsTheyWere()
    {
        string blocked = Path.Combine(_folder, "a folder.pdf");
        Directory.CreateDirectory(blocked);
        string inside = Path.Combine(blocked, "kept.txt");
        await File.WriteAllTextAsync(inside, "kept", Ct);

        // The app treats both as a failed save: Windows reports this one as access denied.
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => SafeFile.WriteReplacingAsync(blocked, [1, 2, 3]));
        Assert.True(failure is IOException or UnauthorizedAccessException, $"Unexpected {failure.GetType().Name}.");

        Assert.Equal("kept", await File.ReadAllTextAsync(inside, Ct));
        Assert.Empty(Directory.GetFiles(_folder));

        await Assert.ThrowsAnyAsync<IOException>(() => SafeFile.WriteReplacingAsync(Path.Combine(_folder, "missing", "x.pdf"), [1]));
        Assert.False(Directory.Exists(Path.Combine(_folder, "missing")));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
