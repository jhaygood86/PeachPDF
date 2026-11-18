using PeachDrawing.PDF;

namespace PeachReader.Core;

/// <summary>One occurrence of the text a reader searched for.</summary>
/// <param name="Page">the zero-based page number</param>
/// <param name="Range">the characters of the page's text it covers</param>
public readonly record struct SearchHit(int Page, PdfTextRange Range);

/// <summary>How far a search has got, for a progress display.</summary>
/// <param name="PagesSearched">the pages read so far</param>
/// <param name="PageCount">the pages in the document</param>
/// <param name="Hits">the occurrences found so far</param>
public readonly record struct SearchProgress(int PagesSearched, int PageCount, int Hits);

/// <summary>The occurrences a search found and which of them the reader is looking at.</summary>
public sealed class SearchResults
{
    private readonly List<SearchHit> _hits;
    private readonly int[] _perPage;

    internal SearchResults(string query, PdfTextSearchOptions options, List<SearchHit> hits, int pageCount)
    {
        Query = query;
        Options = options;
        _hits = hits;
        _perPage = new int[pageCount];
        foreach (var hit in hits) _perPage[hit.Page]++;
        CurrentIndex = hits.Count > 0 ? 0 : -1;
    }

    /// <summary>The text that was searched for.</summary>
    public string Query { get; }

    /// <summary>How it was compared.</summary>
    public PdfTextSearchOptions Options { get; }

    /// <summary>All the occurrences, in reading order.</summary>
    public IReadOnlyList<SearchHit> Hits => _hits;

    /// <summary>The number of occurrences.</summary>
    public int Count => _hits.Count;

    /// <summary>The index in <see cref="Hits"/> of the occurrence the reader is on, or -1 when there are none.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>The occurrence the reader is on.</summary>
    public SearchHit? Current => CurrentIndex >= 0 ? _hits[CurrentIndex] : null;

    /// <summary>The number of occurrences on a page, for a thumbnail's badge.</summary>
    /// <param name="page">the zero-based page number</param>
    /// <returns>the count</returns>
    public int CountOnPage(int page) => (uint)page < (uint)_perPage.Length ? _perPage[page] : 0;

    /// <summary>The occurrences on a page, for highlighting.</summary>
    /// <param name="page">the zero-based page number</param>
    /// <returns>the occurrences in order</returns>
    public IEnumerable<SearchHit> HitsOnPage(int page) => _hits.Where(h => h.Page == page);

    /// <summary>Moves to the next occurrence, wrapping from the last to the first.</summary>
    /// <returns>the occurrence now current, or <see langword="null"/> when there are none</returns>
    public SearchHit? Next()
    {
        if (_hits.Count == 0) return null;
        CurrentIndex = (CurrentIndex + 1) % _hits.Count;
        return _hits[CurrentIndex];
    }

    /// <summary>Moves to the previous occurrence, wrapping from the first to the last.</summary>
    /// <returns>the occurrence now current, or <see langword="null"/> when there are none</returns>
    public SearchHit? Previous()
    {
        if (_hits.Count == 0) return null;
        CurrentIndex = (CurrentIndex - 1 + _hits.Count) % _hits.Count;
        return _hits[CurrentIndex];
    }

    /// <summary>Makes current the first occurrence on or after a page, as when searching from where the reader is; the first overall when there is none after it.</summary>
    /// <param name="page">the page to start from</param>
    /// <returns>the occurrence now current, or <see langword="null"/> when there are none</returns>
    public SearchHit? MoveToFirstFrom(int page)
    {
        if (_hits.Count == 0) return null;
        int at = _hits.FindIndex(h => h.Page >= page);
        CurrentIndex = at >= 0 ? at : 0;
        return _hits[CurrentIndex];
    }
}

/// <summary>Searches a whole document for text.</summary>
public static class DocumentSearch
{
    /// <summary>Searches every page, reading each page's text on a worker thread and stopping early when cancelled.</summary>
    /// <param name="document">the document to search</param>
    /// <param name="query">the text to find; a blank one finds nothing</param>
    /// <param name="options">how to compare</param>
    /// <param name="progress">told after each page, or <see langword="null"/></param>
    /// <param name="cancellationToken">stops the search</param>
    /// <returns>the occurrences, with the first one current</returns>
    public static Task<SearchResults> RunAsync(ReaderDocument document, string query, PdfTextSearchOptions options = PdfTextSearchOptions.None,
        IProgress<SearchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return RunAsync(document.GetText, document.PageCount, query, options, progress, cancellationToken);
    }

    /// <summary>Searches pages given by a function; the form <see cref="RunAsync(ReaderDocument, string, PdfTextSearchOptions, IProgress{SearchProgress}, CancellationToken)"/> uses.</summary>
    /// <param name="textOf">gives the text of a page</param>
    /// <param name="pageCount">the number of pages</param>
    /// <param name="query">the text to find; a blank one finds nothing</param>
    /// <param name="options">how to compare</param>
    /// <param name="progress">told after each page, or <see langword="null"/></param>
    /// <param name="cancellationToken">stops the search</param>
    /// <returns>the occurrences, with the first one current</returns>
    public static Task<SearchResults> RunAsync(Func<int, PdfPageText> textOf, int pageCount, string query,
        PdfTextSearchOptions options = PdfTextSearchOptions.None, IProgress<SearchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textOf);
        ArgumentNullException.ThrowIfNull(query);
        return Task.Run(() =>
        {
            var hits = new List<SearchHit>();
            if (!string.IsNullOrWhiteSpace(query))
            {
                for (int page = 0; page < pageCount; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var range in textOf(page).Find(query, options)) hits.Add(new SearchHit(page, range));
                    progress?.Report(new SearchProgress(page + 1, pageCount, hits.Count));
                }
            }

            return new SearchResults(query, options, hits, pageCount);
        }, cancellationToken);
    }
}
