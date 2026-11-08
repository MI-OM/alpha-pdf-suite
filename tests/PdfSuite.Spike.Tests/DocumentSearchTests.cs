using System.Collections.Concurrent;
using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Remote;

namespace PdfSuite.Spike.Tests;

/// <summary>
/// Whole-document search (roadmap Milestone 1): the order pages are searched in, moving between
/// matches, and what happens when a page or the worker fails. Most tests use a scripted page
/// searcher; the last ones search real documents through the worker.
/// </summary>
public sealed class DocumentSearchTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task FindsEveryMatch_InDocumentOrder_AndCountsThem()
    {
        var pages = new FakePages(2, 0, 1, 0, 3);
        using var search = new DocumentSearch(pages.FindAsync, pages.Count, startPage: 0);
        await search.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(SearchStatus.Complete, search.Status);
        Assert.Equal(6, search.MatchCount);
        Assert.Equal(5, search.PagesSearched);

        var visited = new List<(int Page, int Index, int? Ordinal)>();
        SearchHit? hit = null;
        for (int i = 0; i < 7; i++)
        {
            hit = await search.FindAsync(hit, startPage: 0, forward: true, Ct);
            Assert.NotNull(hit);
            visited.Add((hit.Value.Page, hit.Value.Index, search.OrdinalOf(hit.Value)));
        }

        // Six matches in order, then round to the first again.
        Assert.Equal([(0, 0, 1), (0, 1, 2), (2, 0, 3), (4, 0, 4), (4, 1, 5), (4, 2, 6), (0, 0, 1)], visited);
        Assert.Equal("p4m2", (await search.FindAsync(null, 4, forward: false, Ct))?.Match.Text);
    }

    [Fact]
    public async Task SearchesFromTheStartPage_AndWrapsRound()
    {
        var pages = new FakePages(0, 0, 0, 0, 0);
        using var search = new DocumentSearch(pages.FindAsync, pages.Count, startPage: 3);
        await search.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal([3, 4, 0, 1, 2], pages.Requested);
        Assert.Equal(0, search.MatchCount);
        Assert.Null(await search.FindAsync(null, 3, forward: true, Ct));
    }

    [Fact]
    public async Task FindsTheFirstMatchFromTheStartPage_BeforeEarlierPagesAreSearched()
    {
        var pages = new FakePages(0, 1, 0, 0, 1, 0);
        pages.Hold(0);
        using var search = new DocumentSearch(pages.FindAsync, pages.Count, startPage: 3);

        SearchHit? hit = await search.FindAsync(null, startPage: 3, forward: true, Ct).WaitAsync(Timeout, Ct);

        Assert.Equal(4, hit?.Page);
        Assert.Equal(SearchStatus.Searching, search.Status);

        // Its position is not known until the pages before it have been searched.
        Assert.Null(search.OrdinalOf(hit!.Value));
        pages.Release(0);
        await search.Completion.WaitAsync(Timeout, Ct);
        Assert.Equal(2, search.OrdinalOf(hit.Value));
    }

    [Fact]
    public async Task PreviousAndNext_WrapRoundTheEnds()
    {
        var pages = new FakePages(1, 0, 2);
        using var search = new DocumentSearch(pages.FindAsync, pages.Count, startPage: 0);
        await search.Completion.WaitAsync(Timeout, Ct);

        SearchHit first = (await search.FindAsync(null, 0, forward: true, Ct))!.Value;
        SearchHit last = (await search.FindAsync(first, 0, forward: false, Ct))!.Value;
        Assert.Equal((2, 1), (last.Page, last.Index));

        SearchHit wrapped = (await search.FindAsync(last, 0, forward: true, Ct))!.Value;
        Assert.Equal((0, 0), (wrapped.Page, wrapped.Index));

        // A single match is both the next and the previous one.
        var single = new FakePages(0, 1, 0);
        using var one = new DocumentSearch(single.FindAsync, single.Count, startPage: 0);
        await one.Completion.WaitAsync(Timeout, Ct);
        SearchHit only = (await one.FindAsync(null, 0, forward: true, Ct))!.Value;
        Assert.Equal(only, await one.FindAsync(only, 0, forward: true, Ct));
        Assert.Equal(only, await one.FindAsync(only, 0, forward: false, Ct));
    }

    [Fact]
    public async Task MovingToAPageNotSearchedYet_SearchesItNext()
    {
        var pages = new FakePages(0, 0, 0, 0, 0, 2);
        pages.Hold(1);
        using var search = new DocumentSearch(pages.FindAsync, pages.Count, startPage: 0);
        await pages.WaitUntilRequested(1).WaitAsync(Timeout, Ct);

        // From page 0 backwards: page 0 has no match, so the last page is next.
        Task<SearchHit?> previous = search.FindAsync(null, startPage: 0, forward: false, Ct);
        pages.Release(1);
        SearchHit? hit = await previous.WaitAsync(Timeout, Ct);

        Assert.Equal((5, 1), (hit?.Page, hit?.Index));
        Assert.Equal([0, 1, 5], pages.Requested.Take(3));
        await search.Completion.WaitAsync(Timeout, Ct);
        Assert.Equal(6, pages.Requested.Count);
    }

    [Fact]
    public async Task Dispose_StopsTheSearch()
    {
        var pages = new FakePages(0, 0, 0, 0, 1);
        pages.Hold(2);
        var search = new DocumentSearch(pages.FindAsync, pages.Count, startPage: 0);
        await pages.WaitUntilRequested(2).WaitAsync(Timeout, Ct);
        Task<SearchHit?> waiting = search.FindAsync(null, startPage: 4, forward: true, Ct);

        search.Dispose();
        await search.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(SearchStatus.Cancelled, search.Status);
        Assert.Null(await waiting.WaitAsync(Timeout, Ct));
        Assert.Equal([0, 1, 2], pages.Requested);
    }

    [Fact]
    public async Task APageTheEngineCannotRead_IsCountedAndSkipped()
    {
        var pages = new FakePages(1, 5, 1);
        pages.FailWith(1, new WorkerException(WorkerErrorCode.Unknown, "Could not load the page."));
        using var search = new DocumentSearch(pages.FindAsync, pages.Count, startPage: 0);
        await search.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(SearchStatus.Complete, search.Status);
        Assert.Equal([1], search.FailedPages);
        Assert.Equal(2, search.MatchCount);
        SearchHit first = (await search.FindAsync(null, 0, forward: true, Ct))!.Value;
        Assert.Equal(2, (await search.FindAsync(first, 0, forward: true, Ct))?.Page);
    }

    [Fact]
    public async Task WhenTheWorkerCannotCarryOn_TheSearchFails_AndKeepsWhatItFound()
    {
        var pages = new FakePages(1, 0, 0, 0, 1);
        pages.FailWith(2, new WorkerExitedException());
        using var search = new DocumentSearch(pages.FindAsync, pages.Count, startPage: 0);
        await search.Completion.WaitAsync(Timeout, Ct);

        Assert.Equal(SearchStatus.Failed, search.Status);
        Assert.Equal([0, 1, 2], pages.Requested);
        Assert.Equal(1, search.MatchCount);
        Assert.Equal(0, (await search.FindAsync(null, 0, forward: true, Ct))?.Page);

        // Pages 3 and 4 were never searched, so the match on page 4 cannot be reached: moving on from
        // page 3 skips them and wraps round to the match on page 0.
        Assert.Null(search.MatchesOn(4));
        Assert.Equal(0, (await search.FindAsync(null, 3, forward: true, Ct))?.Page);
    }

    [Fact]
    public async Task SearchesAWholeDocument_ThroughTheWorker()
    {
        byte[] data = Fixtures.LargeMixed(pageCount: 120);
        using DocumentSession session = await DocumentSession.OpenAsync(StartWorker, data, password: null, Ct);

        using (DocumentSearch heading = DocumentSearch.Start(session, "Page 75 of 120", matchCase: false, wholeWord: false, startPage: 0))
        {
            await heading.Completion.WaitAsync(Timeout, Ct);
            Assert.Equal(SearchStatus.Complete, heading.Status);
            Assert.Equal(1, heading.MatchCount);
            SearchHit hit = (await heading.FindAsync(null, 0, forward: true, Ct))!.Value;
            Assert.Equal(74, hit.Page);
            Assert.Equal(1, heading.OrdinalOf(hit));
        }

        using DocumentSearch word = DocumentSearch.Start(session, "quarterly", matchCase: false, wholeWord: true, startPage: 60);
        await word.Completion.WaitAsync(Timeout, Ct);
        Assert.Equal(EngineMatchCount(data, "quarterly"), word.MatchCount);
        Assert.Empty(word.FailedPages);
    }

    // Roadmap Milestone 1: killing the worker must not lose work. The session restarts it and the
    // search carries on from the page it was on.
    [Fact]
    public async Task ASearch_CarriesOnAfterTheWorkerCrashes()
    {
        byte[] data = Fixtures.LargeMixed(pageCount: 200);
        using DocumentSession session = await DocumentSession.OpenAsync(StartWorker, data, password: null, Ct);
        int firstWorker = session.WorkerProcessId;

        var killed = new TaskCompletionSource();
        using DocumentSearch search = DocumentSearch.Start(session, "quarterly", matchCase: false, wholeWord: true, startPage: 0);
        search.Changed += () =>
        {
            if (search.PagesSearched == 10 && killed.TrySetResult())
            {
                session.KillWorker();
            }
        };

        await search.Completion.WaitAsync(TimeSpan.FromSeconds(60), Ct);

        Assert.True(killed.Task.IsCompleted, "The worker was not killed during the search.");
        Assert.Equal(SearchStatus.Complete, search.Status);
        Assert.Equal(200, search.PagesSearched);
        Assert.Empty(search.FailedPages);
        Assert.Equal(EngineMatchCount(data, "quarterly"), search.MatchCount);
        Assert.NotEqual(firstWorker, session.WorkerProcessId);
    }

    [Fact]
    public async Task Start_RejectsAnEmptyQuery()
    {
        using DocumentSession session = await DocumentSession.OpenAsync(StartWorker, Fixtures.SimpleText(), password: null, Ct);
        Assert.Throws<ArgumentException>(() => DocumentSearch.Start(session, "   ", matchCase: false, wholeWord: false, startPage: 0));
    }

    private static int EngineMatchCount(byte[] data, string query)
    {
        var engine = new PdfiumEngine();
        using IPdfDocument document = engine.Open(data);
        int total = 0;
        for (int page = 0; page < document.PageCount; page++)
        {
            total += document.FindText(page, query, wholeWord: true).Count;
        }

        return total;
    }

    private static WorkerClient StartWorker() => WorkerClient.Start(new WorkerOptions(WorkerOptions.DefaultWorkerPath()));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Pages with a set number of matches each. Records the order pages are asked for; a page can
    /// be held until released, or made to throw.
    /// </summary>
    private sealed class FakePages(params int[] matchesPerPage)
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _held = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _requested = new();
        private readonly ConcurrentDictionary<int, Exception> _failures = new();
        private readonly ConcurrentQueue<int> _order = new();

        public int Count => matchesPerPage.Length;

        public List<int> Requested => [.. _order];

        public void Hold(int page) => _held[page] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(int page) => _held[page].TrySetResult();

        public void FailWith(int page, Exception exception) => _failures[page] = exception;

        public Task WaitUntilRequested(int page) => RequestSignal(page).Task;

        public async Task<IReadOnlyList<TextMatch>> FindAsync(int page, CancellationToken cancellationToken)
        {
            _order.Enqueue(page);
            RequestSignal(page).TrySetResult();
            if (_held.TryGetValue(page, out TaskCompletionSource? hold))
            {
                await hold.Task.WaitAsync(cancellationToken);
            }

            await Task.Yield();
            if (_failures.TryGetValue(page, out Exception? failure))
            {
                throw failure;
            }

            return [.. Enumerable.Range(0, matchesPerPage[page]).Select(i =>
                new TextMatch($"p{page}m{i}", [new PageRect(72, 100 + (i * 20), 120, 112 + (i * 20))]))];
        }

        private TaskCompletionSource RequestSignal(int page) =>
            _requested.GetOrAdd(page, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
