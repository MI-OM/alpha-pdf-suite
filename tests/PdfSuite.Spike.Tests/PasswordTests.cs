using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Qpdf;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Password-protected documents through a session, as the app's password prompt opens them
/// (roadmap Milestone 1: "handle password-protected documents gracefully").
/// </summary>
public sealed class PasswordTests
{
    private const string Password = "open-sesame";

    [Fact]
    public async Task Session_RefusesAMissingOrWrongPassword_AndOpensWithTheRightOne()
    {
        byte[] encrypted = QpdfStructure.Encrypt(Fixtures.SimpleText(), Password, "owner-pass", new PdfPermissions());

        foreach (string? wrong in new[] { null, "wrong" })
        {
            var refused = await Assert.ThrowsAsync<PdfOpenException>(() => DocumentSession.OpenAsync(StartWorker, encrypted, wrong, Ct));
            Assert.Equal(PdfOpenError.Password, refused.Error);
        }

        using DocumentSession session = await DocumentSession.OpenAsync(StartWorker, encrypted, Password, Ct);
        Assert.Equal(3, session.PageSizes.Count);
        Assert.Contains(await session.GetTextLinesAsync(0, Ct), line => line.Text.Contains("Simple text page 1", StringComparison.Ordinal));
    }

    // The session keeps the password in memory so a restarted worker can reopen the document
    // without asking again (decisions for review, D2).
    [Fact]
    public async Task Session_ReopensAProtectedDocument_AfterTheWorkerCrashes()
    {
        byte[] encrypted = QpdfStructure.Encrypt(Fixtures.SimpleText(), Password, "owner-pass", new PdfPermissions());
        using DocumentSession session = await DocumentSession.OpenAsync(StartWorker, encrypted, Password, Ct);
        var ready = new TaskCompletionSource();
        session.StateChanged += state =>
        {
            if (state == SessionState.Ready)
            {
                ready.TrySetResult();
            }
        };

        session.KillWorker();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);

        Assert.Equal(SessionState.Ready, session.State);
        IReadOnlyList<TextMatch> matches = await session.FindTextAsync(2, "Simple text page 3", cancellationToken: Ct);
        Assert.Single(matches);
    }

    private static WorkerClient StartWorker() => WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
