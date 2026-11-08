using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.Pdf.Remote;

/// <summary>One match: its page, its position among that page's matches, and its text and rectangles.</summary>
public readonly record struct SearchHit(int Page, int Index, TextMatch Match);

public enum SearchStatus
{
    /// <summary>Pages are still being searched.</summary>
    Searching,

    /// <summary>Every page has been searched, or could not be (see <see cref="DocumentSearch.FailedPages"/>).</summary>
    Complete,

    /// <summary>Stopped by <see cref="DocumentSearch.Dispose"/>, or because the document was closed.</summary>
    Cancelled,

    /// <summary>Stopped because the worker stopped and could not be restarted, or stopped again on the same page.</summary>
    Failed,
}

/// <summary>
/// Searches a whole document for one query, a page at a time, so the worker serves tile requests
/// between pages. It starts at a given page and wraps round, so the first matches are near the
/// reader. Moving to a match on a page not searched yet searches that page next.
/// </summary>
/// <remarks>
/// Thread-safe. <see cref="Changed"/> is raised on a background thread after each page and when the
/// status changes.
/// </remarks>
public sealed class DocumentSearch : IDisposable
{
    private readonly Func<int, CancellationToken, Task<IReadOnlyList<TextMatch>>> _findOnPage;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly IReadOnlyList<TextMatch>?[] _results;
    private readonly Dictionary<int, TaskCompletionSource<bool>> _waiters = [];
    private readonly Stack<int> _wanted = new();
    private readonly List<int> _failedPages = [];
    private readonly int _startPage;
    private int _cursor;
    private int _pagesSearched;
    private int _matchCount;
    private SearchStatus _status = SearchStatus.Searching;

    /// <param name="findOnPage">Returns the matches on one page, in the order they appear.</param>
    /// <param name="pageCount">The number of pages in the document.</param>
    /// <param name="startPage">The page to search first.</param>
    public DocumentSearch(Func<int, CancellationToken, Task<IReadOnlyList<TextMatch>>> findOnPage, int pageCount, int startPage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        _findOnPage = findOnPage;
        _results = new IReadOnlyList<TextMatch>?[pageCount];
        _startPage = pageCount == 0 ? 0 : Math.Clamp(startPage, 0, pageCount - 1);
        Completion = Task.Run(RunAsync);
    }

    /// <summary>Raised on a background thread after each page is searched and when the status changes.</summary>
    public event Action? Changed;

    /// <summary>Completes when the search ends, whatever its outcome.</summary>
    public Task Completion { get; }

    public int PageCount => _results.Length;

    public int PagesSearched
    {
        get { lock (_gate) { return _pagesSearched; } }
    }

    /// <summary>Matches found so far.</summary>
    public int MatchCount
    {
        get { lock (_gate) { return _matchCount; } }
    }

    public SearchStatus Status
    {
        get { lock (_gate) { return _status; } }
    }

    /// <summary>Pages the engine could not search. They count as having no matches.</summary>
    public IReadOnlyList<int> FailedPages
    {
        get { lock (_gate) { return [.. _failedPages]; } }
    }

    /// <summary>Starts a search of a document's pages, from <paramref name="startPage"/>.</summary>
    /// <exception cref="ArgumentException">The query is empty or only white space.</exception>
    public static DocumentSearch Start(DocumentSession session, string query, bool matchCase, bool wholeWord, int startPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return new DocumentSearch(
            (page, ct) => session.FindTextAsync(page, query, matchCase, wholeWord, ct),
            session.PageSizes.Count,
            startPage);
    }

    /// <summary>The matches on a page, or null if it has not been searched yet.</summary>
    public IReadOnlyList<TextMatch>? MatchesOn(int page)
    {
        lock (_gate)
        {
            return _results[page];
        }
    }

    /// <summary>
    /// The position of <paramref name="hit"/> among all matches, counting from 1, once every page
    /// before its page has been searched; null until then.
    /// </summary>
    public int? OrdinalOf(SearchHit hit)
    {
        lock (_gate)
        {
            int before = 0;
            for (int page = 0; page < hit.Page; page++)
            {
                if (_results[page] is not { } matches)
                {
                    return null;
                }

                before += matches.Count;
            }

            return before + hit.Index + 1;
        }
    }

    /// <summary>
    /// The match after <paramref name="from"/> (or before it, if not <paramref name="forward"/>) in
    /// document order, wrapping round at either end. Without <paramref name="from"/>, the first match
    /// on <paramref name="startPage"/> or after it (or the last match on it or before it).
    /// Waits for the pages it needs. Returns null if there is no match, or if the search stopped
    /// before it reached a page with one.
    /// </summary>
    public async Task<SearchHit?> FindAsync(SearchHit? from, int startPage, bool forward, CancellationToken cancellationToken = default)
    {
        int pageCount = _results.Length;
        if (pageCount == 0)
        {
            return null;
        }

        int step = forward ? 1 : -1;
        int page = from?.Page ?? Math.Clamp(startPage, 0, pageCount - 1);

        // Visit each page once, then the starting page again, for the matches before (or after) 'from' on it.
        for (int visited = 0; visited <= pageCount; visited++)
        {
            IReadOnlyList<TextMatch>? matches = await PageMatchesAsync(page, cancellationToken).ConfigureAwait(false);
            if (matches is { Count: > 0 })
            {
                int? index = PickOnPage(matches.Count, from, page, forward, wrapped: visited == pageCount);
                if (index is int i)
                {
                    return new SearchHit(page, i, matches[i]);
                }
            }

            if (visited == pageCount || (from is null && visited == pageCount - 1))
            {
                break;
            }

            page = ((page + step) % pageCount + pageCount) % pageCount;
        }

        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
    }

    // On the page of 'from', the next match after it (or before it); on any other page, the first (or last).
    // Back on the page of 'from' after a full circle, any match up to and including 'from' itself.
    private static int? PickOnPage(int count, SearchHit? from, int page, bool forward, bool wrapped)
    {
        if (from is not SearchHit hit || hit.Page != page)
        {
            return forward ? 0 : count - 1;
        }

        if (wrapped)
        {
            return forward ? 0 : count - 1;
        }

        int next = hit.Index + (forward ? 1 : -1);
        return next >= 0 && next < count ? next : null;
    }

    /// <summary>The page's matches, searching it next if need be; null if the search stopped first.</summary>
    private async Task<IReadOnlyList<TextMatch>?> PageMatchesAsync(int page, CancellationToken cancellationToken)
    {
        Task<bool> searched;
        lock (_gate)
        {
            if (_results[page] is { } done)
            {
                return done;
            }

            if (_status != SearchStatus.Searching)
            {
                return null;
            }

            if (!_waiters.TryGetValue(page, out TaskCompletionSource<bool>? waiter))
            {
                waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters[page] = waiter;
            }

            _wanted.Push(page);
            searched = waiter.Task;
        }

        return await searched.WaitAsync(cancellationToken).ConfigureAwait(false) ? MatchesOn(page) : null;
    }

    private async Task RunAsync()
    {
        SearchStatus outcome;
        try
        {
            while (TakeNextPage() is int page)
            {
                IReadOnlyList<TextMatch> matches;
                bool failed = false;
                try
                {
                    matches = await _findOnPage(page, _cts.Token).ConfigureAwait(false);
                }
                catch (WorkerException)
                {
                    // The engine could not read this page. Count it, and carry on with the rest.
                    matches = [];
                    failed = true;
                }

                Record(page, matches, failed);
            }

            outcome = SearchStatus.Complete;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            outcome = SearchStatus.Cancelled;
        }
        catch (WorkerExitedException)
        {
            outcome = SearchStatus.Failed;
        }

        TaskCompletionSource<bool>[] waiting;
        lock (_gate)
        {
            _status = outcome;
            waiting = [.. _waiters.Values];
            _waiters.Clear();
        }

        foreach (TaskCompletionSource<bool> waiter in waiting)
        {
            waiter.TrySetResult(false);
        }

        Changed?.Invoke();
    }

    /// <summary>A page someone is waiting for, else the next page in order from the start page.</summary>
    private int? TakeNextPage()
    {
        lock (_gate)
        {
            _cts.Token.ThrowIfCancellationRequested();
            while (_wanted.Count > 0)
            {
                int page = _wanted.Pop();
                if (_results[page] is null)
                {
                    return page;
                }
            }

            while (_cursor < _results.Length)
            {
                int page = (_startPage + _cursor++) % _results.Length;
                if (_results[page] is null)
                {
                    return page;
                }
            }

            return null;
        }
    }

    private void Record(int page, IReadOnlyList<TextMatch> matches, bool failed)
    {
        TaskCompletionSource<bool>? waiter;
        lock (_gate)
        {
            _results[page] = matches;
            _pagesSearched++;
            _matchCount += matches.Count;
            if (failed)
            {
                _failedPages.Add(page);
                _failedPages.Sort();
            }

            _waiters.Remove(page, out waiter);
        }

        waiter?.TrySetResult(true);
        Changed?.Invoke();
    }
}
