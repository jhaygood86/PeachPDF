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

    internal AccessibilityTree(AccessibilityNode root, Dictionary<PeachBrackets.Html.Dom.CssBox, AccessibilityNode>? byBox = null)
    {
        Root = root;
        _byBox = byBox;
        Nodes = root.DescendantsAndSelf().ToArray();
        _byId = new Dictionary<string, AccessibilityNode>(Nodes.Count, StringComparer.Ordinal);
        foreach (var node in Nodes)
            _byId.TryAdd(node.Id, node);
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
