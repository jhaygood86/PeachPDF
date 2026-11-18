using PeachDrawing.PDF;

namespace PeachReader.Core;

/// <summary>One entry of a document's table of contents, with the entries nested under it.</summary>
public sealed class OutlineNode
{
    private readonly List<OutlineNode> _children = [];

    internal OutlineNode(string title, int? pageIndex, double topPoints, string? uri, bool isOpen, OutlineNode? parent)
    {
        Title = title;
        PageIndex = pageIndex;
        TopPoints = topPoints;
        Uri = uri;
        IsOpen = isOpen;
        Parent = parent;
    }

    /// <summary>The text of the entry.</summary>
    public string Title { get; }

    /// <summary>The page the entry leads to, or <see langword="null"/> when it leads somewhere else (a web address) or nowhere.</summary>
    public int? PageIndex { get; }

    /// <summary>How far below the top of <see cref="PageIndex"/> the entry points, in points.</summary>
    public double TopPoints { get; }

    /// <summary>The web address the entry opens, when it opens one instead of a page.</summary>
    public string? Uri { get; }

    /// <summary>Whether the file asks for the entry to be shown expanded.</summary>
    public bool IsOpen { get; }

    /// <summary>The entry this one is nested under, or <see langword="null"/> for a top-level entry.</summary>
    public OutlineNode? Parent { get; }

    /// <summary>The entries nested under this one, in order.</summary>
    public IReadOnlyList<OutlineNode> Children => _children;

    /// <summary>The place the entry leads to inside the document, or <see langword="null"/> when it has none.</summary>
    public ReaderLocation? Location => PageIndex is { } page ? new ReaderLocation(page, TopPoints) : null;

    internal OutlineNode Add(OutlineNode child)
    {
        _children.Add(child);
        return child;
    }
}

/// <summary>Builds and searches the tree of a document's table of contents.</summary>
public static class OutlineTree
{
    /// <summary>
    /// Turns the flat, reading-order list <see cref="PdfPageSource.GetOutline"/> gives into a tree. An entry is nested under the nearest earlier
    /// entry of a lower level, so a file that skips a level (a level 3 right after a level 1) still comes out as a tree.
    /// </summary>
    /// <param name="entries">the entries in reading order, each with its nesting level</param>
    /// <returns>the top-level entries</returns>
    public static IReadOnlyList<OutlineNode> Build(IReadOnlyList<PdfBookmark> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var roots = new List<OutlineNode>();
        var open = new Stack<(int Level, OutlineNode Node)>();
        foreach (var entry in entries)
        {
            while (open.Count > 0 && open.Peek().Level >= entry.Level) open.Pop();
            var parent = open.Count > 0 ? open.Peek().Node : null;
            var node = new OutlineNode(entry.Title ?? "", entry.PageIndex, entry.TopPoints, entry.Uri, entry.IsOpen, parent);
            if (parent is null) roots.Add(node); else parent.Add(node);
            open.Push((entry.Level, node));
        }

        return roots;
    }

    /// <summary>Every entry of the tree, each followed by its descendants.</summary>
    /// <param name="roots">the top-level entries</param>
    /// <returns>all entries in reading order</returns>
    public static IEnumerable<OutlineNode> Flatten(IEnumerable<OutlineNode> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    /// <summary>
    /// The entry to highlight while a page is on screen: the last entry, in reading order, that leads to a page no later than
    /// <paramref name="pageIndex"/> (the section the page belongs to).
    /// </summary>
    /// <param name="roots">the top-level entries</param>
    /// <param name="pageIndex">the page being read</param>
    /// <returns>the entry, or <see langword="null"/> when the page comes before the first one</returns>
    public static OutlineNode? CurrentFor(IEnumerable<OutlineNode> roots, int pageIndex)
    {
        OutlineNode? current = null;
        int best = int.MinValue;
        foreach (var node in Flatten(roots))
        {
            if (node.PageIndex is not { } page || page > pageIndex) continue;
            if (page >= best) { best = page; current = node; }
        }

        return current;
    }
}
