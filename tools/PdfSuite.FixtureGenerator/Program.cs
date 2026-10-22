using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PdfSuite.FixtureGenerator;

// Writes every fixture plus manifest.json. Usage: PdfSuite.FixtureGenerator [--out <directory>]
string outputDirectory = args is ["--out", var directory] ? directory : Path.Combine("fixtures", "generated");
Directory.CreateDirectory(outputDirectory);

foreach (Fixture fixture in Fixtures.All)
{
    var stopwatch = Stopwatch.StartNew();
    byte[] bytes = fixture.Generate();
    File.WriteAllBytes(Path.Combine(outputDirectory, fixture.Info.File), bytes);
    string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
    Console.WriteLine($"{fixture.Info.File,-28} {bytes.Length,12:N0} bytes  sha256 {hash[..16]}…  {stopwatch.ElapsedMilliseconds,6} ms");
}

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
};
string manifestPath = Path.Combine(outputDirectory, "manifest.json");
File.WriteAllText(manifestPath, JsonSerializer.Serialize(Fixtures.All.Select(f => f.Info), options));
Console.WriteLine($"Wrote {Fixtures.All.Count} fixtures and {manifestPath}");
return 0;
