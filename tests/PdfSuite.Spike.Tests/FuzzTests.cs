using System.Diagnostics;
using System.Text;
using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Redaction;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Malformed input (PRD §8; roadmap Milestone 6). Inputs come from a fixed seed, so every run
/// tries the same cases and a failure can be reproduced.
/// </summary>
public sealed class FuzzTests
{
    private const int Seed = 20261106;

    // The redaction verifier's content-stream lexer reads untrusted bytes (ADR-0006). Inputs are
    // biased towards the characters it treats specially, so every branch is reached often.
    [Fact]
    public void ContentStrings_AnyInput_NeverThrowsAndNeverGrows()
    {
        var random = new Random(Seed);
        byte[] alphabet = "()<>\\%BIDE \n\r\t0178afF[]/TJj"u8.ToArray();
        var clock = Stopwatch.StartNew();
        for (int n = 0; n < 20_000; n++)
        {
            byte[] input = new byte[random.Next(0, 512)];
            for (int i = 0; i < input.Length; i++)
            {
                input[i] = random.Next(4) == 0 ? (byte)random.Next(256) : alphabet[random.Next(alphabet.Length)];
            }

            // Every byte out comes from at least one byte in: escapes and hex digits only shrink.
            Assert.True(ContentStrings.Extract(input).Length <= input.Length, $"Case {n} grew.");
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"20,000 cases took {clock.Elapsed.TotalSeconds:0.0} s; the lexer should be linear.");
    }

    // Random bytes written as a literal string (with escapes) and as a hex string come back exactly.
    [Fact]
    public void ContentStrings_RoundTripsEncodedStrings()
    {
        var random = new Random(Seed);
        for (int n = 0; n < 5_000; n++)
        {
            byte[] value = new byte[random.Next(0, 64)];
            random.NextBytes(value);

            var content = new StringBuilder("BT /F1 12 Tf (");
            foreach (byte b in value)
            {
                content.Append(b switch
                {
                    (byte)'(' or (byte)')' or (byte)'\\' => "\\" + (char)b,
                    < 32 or >= 127 => "\\" + Convert.ToString(b, 8).PadLeft(3, '0'),
                    _ => ((char)b).ToString(),
                });
            }

            content.Append(") Tj <").Append(Convert.ToHexString(value)).Append("> Tj ET");
            byte[] extracted = ContentStrings.Extract(Encoding.Latin1.GetBytes(content.ToString()));

            Assert.Equal([.. value, .. value], extracted);
        }
    }

    // Mutated fixtures go through a real worker: open, render, extract text, and rewrite with
    // qpdf. Every request must end in a result, a clean error, or a worker crash the client
    // contains. A crash is counted, not failed: it is an engine bug, and containing it is the
    // worker's job (ADR-0003). Anything else, such as a protocol fault, fails the test.
    [Fact]
    public async Task Worker_MutatedDocuments_AreAnsweredOrContained()
    {
        byte[][] seeds = [Fixtures.SimpleText(), Fixtures.CanaryText(), Fixtures.CanaryImage(), Fixtures.CanaryAnnotation(), Fixtures.CanaryDocumentLevel(), Fixtures.ImageOnlyMiddlePage()];
        var random = new Random(Seed);
        var outcomes = new Dictionary<string, int>();
        void Count(string outcome) => outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;

        WorkerClient client = StartWorker();
        try
        {
            for (int n = 0; n < 300; n++)
            {
                byte[] document = Mutate(seeds[n % seeds.Length], random);
                try
                {
                    await ExerciseAsync(client, document, Count);
                }
                catch (WorkerExitedException)
                {
                    Count($"worker crashed (case {n})");
                    client.Dispose();
                    client = StartWorker();
                }
            }
        }
        finally
        {
            client.Dispose();
        }

        string summary = string.Join("; ", outcomes.OrderBy(o => o.Key).Select(o => $"{o.Key}: {o.Value}"));
        TestContext.Current.TestOutputHelper?.WriteLine($"Mutated documents: {summary}");
        Assert.True(outcomes.GetValueOrDefault("opened") > 0, $"No mutated document opened, so rendering was never exercised. {summary}");
    }

    private static async Task ExerciseAsync(WorkerClient client, byte[] document, Action<string> count)
    {
        // An open must end in a document or a PdfOpenException, which the app explains. Any other
        // error fails the test: the fuzzer found damaged pages reported as a generic error, which
        // the app did not expect at open.
        RemoteDocument? opened = null;
        try
        {
            opened = await client.OpenAsync(document, password: null, Ct);
            count("opened");
        }
        catch (PdfOpenException ex)
        {
            count($"open refused ({ex.Error})");
        }

        if (opened is not null)
        {
            try
            {
                if (opened.PageSizes.Count > 0)
                {
                    (int width, int height) = opened.PageSizes[0].ToPixels(0.25);
                    using RenderedTile tile = await client.RenderTileAsync(
                        opened.Id, 0, 0.25, new PixelRegion(0, 0, Math.Clamp(width, 1, 512), Math.Clamp(height, 1, 512)), Ct);
                    await client.GetTextLinesAsync(opened.Id, 0, Ct);
                }
            }
            catch (WorkerException ex)
            {
                // The view treats a page it cannot draw or read as failed, and carries on.
                count($"page error ({ex.Code})");
            }

            await client.CloseDocumentAsync(opened.Id);
        }

        try
        {
            await client.RewriteAsync(document, cancellationToken: Ct);
            count("rewritten by qpdf");
        }
        catch (PdfOpenException)
        {
            count("rewrite refused (password)");
        }
        catch (WorkerException ex)
        {
            count($"rewrite refused ({ex.Code})");
        }
    }

    // A few edits of the kinds that break PDF structure: random bytes, changed digits (offsets and
    // lengths), and inserted, deleted or duplicated ranges.
    private static byte[] Mutate(byte[] seed, Random random)
    {
        var bytes = new List<byte>(seed);
        for (int edits = random.Next(1, 9); edits > 0 && bytes.Count > 16; edits--)
        {
            int at = random.Next(bytes.Count);
            switch (random.Next(5))
            {
                case 0:
                    bytes[at] = (byte)random.Next(256);
                    break;
                case 1:
                    int digit = bytes.FindIndex(at, b => b is >= (byte)'0' and <= (byte)'9');
                    if (digit >= 0)
                    {
                        bytes[digit] = (byte)('0' + random.Next(10));
                    }

                    break;
                case 2:
                    bytes.InsertRange(at, Enumerable.Range(0, random.Next(1, 64)).Select(_ => (byte)random.Next(256)));
                    break;
                case 3:
                    bytes.RemoveRange(at, Math.Min(random.Next(1, 256), bytes.Count - at));
                    break;
                default:
                    int length = Math.Min(random.Next(1, 256), bytes.Count - at);
                    bytes.InsertRange(random.Next(bytes.Count), bytes.GetRange(at, length));
                    break;
            }
        }

        return [.. bytes];
    }

    private static WorkerClient StartWorker() => WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
