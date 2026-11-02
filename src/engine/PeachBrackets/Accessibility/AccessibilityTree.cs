using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Accessibility;

/// <summary>
/// What a document is to an assistive technology: a tree of <see cref="AccessibilityNode"/>s that follows the document, with the role, name and
/// state each part has. A host that has an accessibility tree of its own (UI Automation on Windows, an accessibility node provider on Android)
/// builds it from this.
/// </summary>
public sealed class AccessibilityTree
{
    private readonly Dictionary<string, AccessibilityNode> _byId;

    private readonly Dictionary<PeachBrackets.Html.Dom.CssBox, AccessibilityNode>? _byBox;
    private readonly Dictionary<AccessibilityNode, int> _documentPosition;
    private readonly Dictionary<AccessibilityNode, int> _focusPosition;

    internal AccessibilityTree(AccessibilityNode root, Dictionary<PeachBrackets.Html.Dom.CssBox, AccessibilityNode>? byBox = null)
    {
        Root = root;
        _byBox = byBox;
        Nodes = root.DescendantsAndSelf().ToArray();
        _byId = new Dictionary<string, AccessibilityNode>(Nodes.Count, StringComparer.Ordinal);
        _documentPosition = new Dictionary<AccessibilityNode, int>(Nodes.Count, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < Nodes.Count; i++)
        {
            _byId.TryAdd(Nodes[i].Id, Nodes[i]);
            _documentPosition[Nodes[i]] = i;
        }

        // Sequential focus navigation (HTML, "sequential focus navigation order"): what has a positive tabindex comes first, lowest first and in
        // document order among equals; then everything else that can be focused with the keyboard in document order. A tabindex below zero can be
        // focused by a script or a click but is not a stop of the sequence.
        var stops = Nodes.Where(n => n.IsFocusable && (n.TabIndex ?? 0) >= 0).ToArray();
        FocusOrder = [.. stops.Where(n => n.TabIndex > 0).OrderBy(n => n.TabIndex), .. stops.Where(n => (n.TabIndex ?? 0) == 0)];
        _focusPosition = new Dictionary<AccessibilityNode, int>(FocusOrder.Count, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < FocusOrder.Count; i++)
            _focusPosition[FocusOrder[i]] = i;
    }

    /// <summary>The nodes the Tab key steps through, in the order it does: those with a positive <c>tabindex</c> first, then the rest in document order.</summary>
    public IReadOnlyList<AccessibilityNode> FocusOrder { get; }

    /// <summary>
    /// The tab stop after or before a node. From a node that is not a stop (it has a negative <c>tabindex</c>, or cannot be focused), the next stop
    /// in document order after (or before) it.
    /// </summary>
    /// <param name="current">the node that has the focus, or null if none has, to get the first (or last) stop</param>
    /// <param name="forward">true for Tab, false for Shift+Tab</param>
    /// <returns>the stop, or null if there is none in that direction, which is where the focus leaves the document</returns>
    public AccessibilityNode? NextFocus(AccessibilityNode? current, bool forward)
    {
        if (FocusOrder.Count == 0)
            return null;

        if (current is null)
            return forward ? FocusOrder[0] : FocusOrder[^1];

        if (_focusPosition.TryGetValue(current, out var position))
        {
            var next = forward ? position + 1 : position - 1;
            return next >= 0 && next < FocusOrder.Count ? FocusOrder[next] : null;
        }

        if (!_documentPosition.TryGetValue(current, out var at))
            return null;

        return forward
            ? FocusOrder.Where(n => _documentPosition[n] > at).OrderBy(n => _documentPosition[n]).FirstOrDefault()
            : FocusOrder.Where(n => _documentPosition[n] < at).OrderByDescending(n => _documentPosition[n]).FirstOrDefault();
    }

    /// <summary>The document: its children are the top-level parts of the page.</summary>
    public AccessibilityNode Root { get; }

    /// <summary>Every node, in reading order, the document first.</summary>
    public IReadOnlyList<AccessibilityNode> Nodes { get; }

    /// <summary>The node an element's box became, or null if it has none (it means nothing to a reader, or is not shown).</summary>
    internal AccessibilityNode? NodeFor(PeachBrackets.Html.Dom.CssBox box) => _byBox?.GetValueOrDefault(box);

    /// <summary>The node with an <see cref="AccessibilityNode.Id"/>, or null if there is none.</summary>
    /// <param name="id">the identifier</param>
    /// <returns>the node, or null</returns>
    public AccessibilityNode? FindById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// The innermost node at a point of a page: the last in reading order of those whose rectangle on that page holds the point, which is the one
    /// drawn on top.
    /// </summary>
    /// <param name="pageIndex">the zero-based index of the page</param>
    /// <param name="x">the distance from the left edge of the sheet, in points</param>
    /// <param name="y">the distance from the top edge of the sheet, in points</param>
    /// <returns>the node, or null if the point is over nothing in the tree</returns>
    public AccessibilityNode? NodeAt(int pageIndex, double x, double y)
    {
        AccessibilityNode? found = null;
        foreach (var node in Nodes)
        {
            foreach (var bounds in node.Bounds)
            {
                if (bounds.PageIndex == pageIndex && x >= bounds.Bounds.Left && x < bounds.Bounds.Right && y >= bounds.Bounds.Top && y < bounds.Bounds.Bottom)
                {
                    found = node;
                    break;
                }
            }
        }

        return found;
    }
}
