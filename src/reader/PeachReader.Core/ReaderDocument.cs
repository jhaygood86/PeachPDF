using PeachDrawing.PDF;

namespace PeachReader.Core;

/// <summary>
/// An opened PDF as the reader uses it: its pages and their sizes, the table of contents as a tree, and the links and text of each page,
/// read once and kept. Painting goes straight to <see cref="Source"/> from the platform head, one page at a time under <see cref="PaintGate"/>.
/// </summary>
public sealed class ReaderDocument
{
    private const int TextCacheLimit = 48;

    private readonly object _gate = new();
    private readonly Dictionary<int, IReadOnlyList<PdfSourceLink>> _links = [];
    private readonly Dictionary<int, PdfPageText> _text = [];
    private readonly Queue<int> _textOrder = new();
    private IReadOnlyList<OutlineNode>? _outline;
    private IReadOnlyList<string>? _labels;
    private IReadOnlyDictionary<string, PdfSourceDestination>? _named;

    private ReaderDocument(PdfPageSource source, string key, string title)
    {
        Source = source;
        Key = key;
        Title = title;
        var sizes = new (double Width, double Height)[source.PageCount];
        for (int i = 0; i < sizes.Length; i++) sizes[i] = source.GetPageSize(i);
        PageSizes = sizes;
    }

    /// <summary>Opens a document.</summary>
    /// <param name="bytes">the bytes of the PDF file</param>
    /// <param name="password">the password, or <see langword="null"/> to try opening without one</param>
    /// <param name="fileName">the name of the file, shown as the title when the document has none of its own</param>
    /// <returns>the document</returns>
    /// <exception cref="PdfPasswordException">the file needs a password that was not given or is wrong</exception>
    /// <exception cref="InvalidDataException">the data is not a readable PDF</exception>
    /// <exception cref="NotSupportedException">the file uses encryption that cannot be read</exception>
    public static ReaderDocument Open(byte[] bytes, string? password = null, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var source = PdfPageSource.Open(bytes, password);
        string title = source.GetMetadata().Title is { Length: > 0 } t && !string.IsNullOrWhiteSpace(t)
            ? t.Trim()
            : !string.IsNullOrWhiteSpace(fileName) ? Path.GetFileNameWithoutExtension(fileName) : "Untitled";
        return new ReaderDocument(source, BookmarkStorage.KeyOf(bytes), title);
    }

    /// <summary>The reader behind the document, for painting pages onto a canvas.</summary>
    public PdfPageSource Source { get; }

    /// <summary>Hold this while painting a page: the reader is not safe to paint with from two threads at once.</summary>
    public object PaintGate { get; } = new();

    /// <summary>Identifies the document by its contents, for keeping its bookmarks; see <see cref="BookmarkStorage.KeyOf"/>.</summary>
    public string Key { get; }

    /// <summary>The title in the file, or the file's name without its extension.</summary>
    public string Title { get; }

    /// <summary>The number of pages.</summary>
    public int PageCount => PageSizes.Count;

    /// <summary>The size of each page in points as it is shown, after rotation.</summary>
    public IReadOnlyList<(double Width, double Height)> PageSizes { get; }

    /// <summary>The label of each page ("iv", "A-3", "12"), which is the page number when the file gives no labels.</summary>
    public IReadOnlyList<string> PageLabels
    {
        get
        {
            lock (_gate) return _labels ??= Source.GetPageLabels();
        }
    }

    /// <summary>The label of a page.</summary>
    /// <param name="pageIndex">the zero-based page number</param>
    public string LabelOf(int pageIndex) => pageIndex >= 0 && pageIndex < PageCount ? PageLabels[pageIndex] : (pageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The page a typed number or label stands for: a label first (so "iv" and "A-3" work, and "3" is the page labelled 3), then a plain page number.</summary>
    /// <param name="text">what the person typed</param>
    /// <returns>the zero-based page, or -1 when it matches nothing</returns>
    public int PageOf(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return -1;
        var labels = PageLabels;
        for (int i = 0; i < labels.Count; i++) if (string.Equals(labels[i], text, StringComparison.OrdinalIgnoreCase)) return i;
        return int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int number) && number >= 1 && number <= PageCount ? number - 1 : -1;
    }

    /// <summary>The table of contents as a tree; empty when the file has none.</summary>
    public IReadOnlyList<OutlineNode> Outline
    {
        get
        {
            lock (_gate) return _outline ??= OutlineTree.Build(Source.GetOutline());
        }
    }

    /// <summary>The links of a page, read once.</summary>
    /// <param name="pageIndex">the zero-based page number</param>
    /// <returns>the links with their rectangles in points from the top-left of the page</returns>
    public IReadOnlyList<PdfSourceLink> GetLinks(int pageIndex)
    {
        CheckPage(pageIndex);
        lock (_gate)
        {
            if (!_links.TryGetValue(pageIndex, out var links)) _links[pageIndex] = links = Source.GetLinks(pageIndex);
            return links;
        }
    }

    /// <summary>The text of a page, read when first asked for; the most recent pages are kept.</summary>
    /// <param name="pageIndex">the zero-based page number</param>
    /// <returns>the text with its positions</returns>
    public PdfPageText GetText(int pageIndex)
    {
        CheckPage(pageIndex);
        lock (_gate)
        {
            if (_text.TryGetValue(pageIndex, out var cached)) return cached;
        }

        var text = Source.GetText(pageIndex);          // not under the lock: reading a long page should not stop other pages' links
        lock (_gate)
        {
            if (_text.TryAdd(pageIndex, text))
            {
                _textOrder.Enqueue(pageIndex);
                while (_textOrder.Count > TextCacheLimit) _text.Remove(_textOrder.Dequeue());
            }

            return _text[pageIndex];
        }
    }

    /// <summary>The place a named destination of the file leads to.</summary>
    /// <param name="name">the destination's name</param>
    /// <returns>the place, or <see langword="null"/> when the file has no such name</returns>
    public ReaderLocation? ResolveNamedDestination(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_gate)
        {
            _named ??= Source.GetNamedDestinations();
            return _named.TryGetValue(name, out var d) ? new ReaderLocation(d.PageIndex, d.TopPoints) : null;
        }
    }

    private void CheckPage(int pageIndex)
    {
        if ((uint)pageIndex >= (uint)PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "The document has " + PageCount + " pages.");
    }
}
