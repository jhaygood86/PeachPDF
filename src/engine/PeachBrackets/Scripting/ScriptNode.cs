using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.ScriptHosting;

/// <summary>What a <see cref="ScriptNode"/> stands for.</summary>
public enum ScriptNodeKind
{
    /// <summary>The document itself: the parent of the <c>html</c> element.</summary>
    Document,

    /// <summary>An element.</summary>
    Element,

    /// <summary>A run of text.</summary>
    Text,

    /// <summary>A holder of nodes that is not in the tree: when it is inserted, its children are inserted in its place.</summary>
    Fragment,
}

/// <summary>
/// One node of a document a <see cref="IScriptHost"/> is running scripts for: the document, an element or a run of text. It is the view of the tree a
/// script changes, with the operations the DOM gives a script (navigation, attributes, structure, text and markup, selectors).
/// </summary>
/// <remarks>
/// A node belongs to the <see cref="ScriptDocument"/> that made it, and there is one object for each node: two references to the same node are the same object.
/// Comments are not kept in the tree.
/// </remarks>
public sealed class ScriptNode
{
    private static readonly StylesheetParser SelectorParser = new();

    internal ScriptNode(ScriptDocument document, CssBox box)
    {
        OwnerDocument = document;
        Box = box;
    }

    internal CssBox Box { get; }

    /// <summary>The document the node belongs to.</summary>
    public ScriptDocument OwnerDocument { get; }

    /// <summary>What the node is.</summary>
    public ScriptNodeKind Kind =>
        Box.HtmlTag is not null ? ScriptNodeKind.Element
        : ReferenceEquals(Box, OwnerDocument.RootBox) ? ScriptNodeKind.Document
        : Box.Text is null ? ScriptNodeKind.Fragment
        : ScriptNodeKind.Text;

    /// <summary>The element's name as the document spells it (<c>div</c>, <c>td</c>, <c>linearGradient</c>), or null for a node that is not an element.</summary>
    public string? TagName => Box.HtmlTag?.Name;

    /// <summary>The text of a text node, or null for any other node. Setting it changes the node's text.</summary>
    public string? Data
    {
        get => Kind == ScriptNodeKind.Text ? Box.Text ?? string.Empty : null;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Kind != ScriptNodeKind.Text)
                throw new InvalidOperationException("Only a text node has data.");

            Box.Text = value;
            OwnerDocument.NoteChanged();
        }
    }

    /// <summary>The parent node, or null for the document and for a node that is not in the tree.</summary>
    public ScriptNode? Parent => Box.ParentBox is { } parent ? OwnerDocument.Wrap(parent) : null;

    /// <summary>The node's children, in order. Text a node holds directly is a child like any other.</summary>
    public IReadOnlyList<ScriptNode> Children => [.. Box.Boxes.Where(IsPartOfSource).Select(OwnerDocument.Wrap)];

    /// <summary>The element children of the node, in order.</summary>
    public IReadOnlyList<ScriptNode> ChildElements => [.. Box.Boxes.Where(b => b.HtmlTag is not null && IsPartOfSource(b)).Select(OwnerDocument.Wrap)];

    /// <summary>The first child, or null.</summary>
    public ScriptNode? FirstChild => Children.FirstOrDefault();

    /// <summary>The last child, or null.</summary>
    public ScriptNode? LastChild => Children.LastOrDefault();

    /// <summary>The sibling before this node, or null.</summary>
    public ScriptNode? PreviousSibling => Sibling(-1);

    /// <summary>The sibling after this node, or null.</summary>
    public ScriptNode? NextSibling => Sibling(1);

    /// <summary>Whether the node is in the document's tree.</summary>
    public bool IsConnected
    {
        get
        {
            for (var box = Box; box is not null; box = box.ParentBox)
            {
                if (ReferenceEquals(box, OwnerDocument.RootBox))
                    return true;
            }

            return false;
        }
    }

    // A text box the parser kept for markup it dropped is empty: it is not a node of the document.
    private static bool IsPartOfSource(CssBox box) => box.HtmlTag is not null || box.Text is not null;

    private ScriptNode? Sibling(int step)
    {
        if (Box.ParentBox is not { } parent)
            return null;

        var siblings = parent.Boxes.Where(IsPartOfSource).ToList();
        var index = siblings.IndexOf(Box) + step;
        return index >= 0 && index < siblings.Count ? OwnerDocument.Wrap(siblings[index]) : null;
    }

    private void RequireElement()
    {
        if (Kind != ScriptNodeKind.Element)
            throw new InvalidOperationException("Only an element has attributes.");
    }

    /// <summary>The names of the element's attributes, in the order they were set.</summary>
    public IReadOnlyList<string> AttributeNames => Box.HtmlTag?.Attributes is { } attributes ? [.. attributes.Keys] : [];

    /// <summary>The value of an attribute, or null when the element has none by that name. The name is matched without regard to case.</summary>
    /// <param name="name">the attribute name</param>
    /// <returns>the value, or null</returns>
    public string? GetAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Box.HtmlTag?.TryGetAttribute(name);
    }

    /// <summary>Whether the element has an attribute by that name.</summary>
    /// <param name="name">the attribute name</param>
    /// <returns>whether it has</returns>
    public bool HasAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Box.HtmlTag?.HasAttribute(name) ?? false;
    }

    /// <summary>Sets an attribute of the element.</summary>
    /// <param name="name">the attribute name</param>
    /// <param name="value">the value</param>
    /// <exception cref="InvalidOperationException">the node is not an element</exception>
    public void SetAttribute(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        RequireElement();
        Box.HtmlTag!.SetAttribute(name, value);
        OwnerDocument.NoteChanged();
    }

    /// <summary>Removes an attribute of the element, if it has it.</summary>
    /// <param name="name">the attribute name</param>
    /// <returns>whether the element had it</returns>
    public bool RemoveAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (Box.HtmlTag is not { } tag || !tag.HasAttribute(name))
            return false;

        tag.RemoveAttribute(name);
        OwnerDocument.NoteChanged();
        return true;
    }

    /// <summary>The element's <c>id</c> attribute, or the empty string.</summary>
    public string Id => GetAttribute("id") ?? string.Empty;

    /// <summary>The element's classes, in order.</summary>
    public IReadOnlyList<string> ClassNames =>
        GetAttribute("class")?.Split([' ', '\t', '\r', '\n', '\f'], StringSplitOptions.RemoveEmptyEntries) ?? [];

    /// <summary>Appends <paramref name="child"/> to this node's children, moving it from where it is.</summary>
    /// <param name="child">the node to add</param>
    /// <returns>the node added</returns>
    /// <exception cref="InvalidOperationException">the child is this node or one of its ancestors, or this node cannot have children</exception>
    public ScriptNode AppendChild(ScriptNode child) => InsertBefore(child, null);

    /// <summary>Inserts <paramref name="child"/> before <paramref name="reference"/>, moving it from where it is.</summary>
    /// <param name="child">the node to insert</param>
    /// <param name="reference">the child to insert before, or null to append</param>
    /// <returns>the node inserted</returns>
    /// <exception cref="InvalidOperationException">the child is this node or one of its ancestors, the reference is not a child of this node, or this node cannot have children</exception>
    public ScriptNode InsertBefore(ScriptNode child, ScriptNode? reference)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ReferenceEquals(child.OwnerDocument, OwnerDocument))
            throw new InvalidOperationException("The node belongs to another document.");
        if (Kind == ScriptNodeKind.Text)
            throw new InvalidOperationException("A text node has no children.");
        if (child.Kind == ScriptNodeKind.Document)
            throw new InvalidOperationException("The document cannot be a child.");
        if (child.Kind == ScriptNodeKind.Fragment && IsInside(child.Box))
            throw new InvalidOperationException("A node cannot be put inside itself.");
        if (reference is not null && !ReferenceEquals(reference.Box.ParentBox, Box))
            throw new InvalidOperationException("The reference node is not a child of this node.");

        for (var ancestor = Box; ancestor is not null; ancestor = ancestor.ParentBox)
        {
            if (ReferenceEquals(ancestor, child.Box))
                throw new InvalidOperationException("A node cannot be put inside itself.");
        }

        if (ReferenceEquals(reference?.Box, child.Box))
            return child;

        if (child.Kind == ScriptNodeKind.Fragment)
        {
            foreach (var moved in child.Box.Boxes.ToArray())
            {
                moved.ParentBox = Box;
                if (reference is not null)
                    moved.SetBeforeBox(reference.Box);
            }

            OwnerDocument.NoteChanged();
            return child;
        }

        child.Box.ParentBox = Box;
        if (reference is not null)
            child.Box.SetBeforeBox(reference.Box);

        OwnerDocument.NoteChanged();
        return child;
    }

    private bool IsInside(CssBox box)
    {
        for (var ancestor = Box; ancestor is not null; ancestor = ancestor.ParentBox)
        {
            if (ReferenceEquals(ancestor, box))
                return true;
        }

        return false;
    }

    /// <summary>Takes a child out of this node.</summary>
    /// <param name="child">the child</param>
    /// <returns>the child removed</returns>
    /// <exception cref="InvalidOperationException">the node is not a child of this node</exception>
    public ScriptNode RemoveChild(ScriptNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ReferenceEquals(child.Box.ParentBox, Box))
            throw new InvalidOperationException("The node is not a child of this node.");

        child.Box.ParentBox = null;
        OwnerDocument.NoteChanged();
        return child;
    }

    /// <summary>Puts <paramref name="child"/> where <paramref name="existing"/> is, and takes <paramref name="existing"/> out.</summary>
    /// <param name="child">the new node</param>
    /// <param name="existing">the child to replace</param>
    /// <returns>the child replaced</returns>
    /// <exception cref="InvalidOperationException">the node is not a child of this node</exception>
    public ScriptNode ReplaceChild(ScriptNode child, ScriptNode existing)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(existing);
        if (ReferenceEquals(child, existing))
            return existing;

        InsertBefore(child, existing);
        return RemoveChild(existing);
    }

    /// <summary>Takes the node out of its parent, if it has one.</summary>
    public void Remove() => Parent?.RemoveChild(this);

    /// <summary>Makes a copy of the node that is not in the tree.</summary>
    /// <param name="deep">whether to copy the node's descendants too</param>
    /// <returns>the copy</returns>
    public ScriptNode CloneNode(bool deep) => OwnerDocument.Wrap(Clone(Box, deep));

    private static CssBox Clone(CssBox source, bool deep)
    {
        CssBox copy;
        if (source.HtmlTag is { } tag)
        {
            var attributes = tag.Attributes is null ? null : new Dictionary<string, string>(tag.Attributes, StringComparer.OrdinalIgnoreCase);
            copy = CssBox.CreateBox(new HtmlTag(tag.Name, tag.IsSingle, attributes));
            if (deep)
            {
                foreach (var child in source.Boxes.Where(IsPartOfSource))
                    Clone(child, true).ParentBox = copy;
            }
        }
        else
        {
            copy = new CssBox(null, null) { Text = source.Text };
            if (deep && source.Text is null)
            {
                foreach (var child in source.Boxes.Where(IsPartOfSource))
                    Clone(child, true).ParentBox = copy;
            }
        }

        return copy;
    }

    /// <summary>The text of the node and everything inside it. Setting it replaces the node's children with one text node (an element) or changes the text (a text node).</summary>
    public string TextContent
    {
        get
        {
            if (Kind == ScriptNodeKind.Text)
                return Box.Text ?? string.Empty;

            var text = new System.Text.StringBuilder();
            Collect(Box, text);
            return text.ToString();
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Kind == ScriptNodeKind.Text)
            {
                Data = value;
                return;
            }

            foreach (var child in Box.Boxes.ToArray())
                child.ParentBox = null;

            if (value.Length > 0)
                new CssBox(Box, null) { Text = value };

            OwnerDocument.NoteChanged();
        }
    }

    private static void Collect(CssBox box, System.Text.StringBuilder text)
    {
        foreach (var child in box.Boxes)
        {
            if (child.HtmlTag is null && child.Text is { } own)
                text.Append(own);
            else if (child.HtmlTag is not null)
                Collect(child, text);
        }
    }

    /// <summary>The HTML of the node's children. Setting it parses the markup and replaces the children with what it makes.</summary>
    public string InnerHtml
    {
        get => ScriptSerializer.SerializeChildren(Box);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Kind == ScriptNodeKind.Text)
                throw new InvalidOperationException("A text node has no markup.");

            foreach (var child in Box.Boxes.ToArray())
                child.ParentBox = null;

            var holder = ParseInContext(value);
            foreach (var child in holder.Boxes.ToArray())
                child.ParentBox = Box;

            OwnerDocument.NoteChanged();
        }
    }

    // The fragment parsing algorithm needs the element the markup goes into: a <td> is dropped outside a table, so a table part's own markup is parsed
    // inside the ancestors it needs and the parsed element's children are what is taken.
    private CssBox ParseInContext(string markup)
    {
        var chain = (TagName?.ToLowerInvariant()) switch
        {
            "tr" => new[] { "table", "tbody", "tr" },
            "tbody" or "thead" or "tfoot" => new[] { "table", TagName!.ToLowerInvariant() },
            "table" => new[] { "table" },
            "colgroup" => new[] { "table", "colgroup" },
            "select" => new[] { "select" },
            "optgroup" => new[] { "select", "optgroup" },
            "head" or "html" => [],
            _ => [],
        };

        if (chain.Length == 0)
            return HtmlParser.ParseDocument(markup, out _, null, document: false, scripting: true);

        var wrapped = HtmlParser.ParseDocument(
            string.Concat(chain.Select(t => $"<{t}>")) + markup, out _, null, document: false, scripting: true);

        var holder = wrapped;
        foreach (var _ in chain)
            holder = holder.Boxes.FirstOrDefault(b => b.HtmlTag is not null) ?? holder;

        return holder;
    }

    /// <summary>The HTML of the node itself and its children.</summary>
    public string OuterHtml => ScriptSerializer.SerializeNode(Box);

    /// <summary>The first element at or below this node that the CSS selector matches, or null.</summary>
    /// <param name="selector">a selector list</param>
    /// <returns>the element, or null</returns>
    /// <exception cref="ArgumentException">the selector is not valid</exception>
    public ScriptNode? QuerySelector(string selector) => QuerySelectorAll(selector, firstOnly: true).FirstOrDefault();

    /// <summary>The elements below this node that the CSS selector matches, in document order.</summary>
    /// <param name="selector">a selector list</param>
    /// <returns>the elements</returns>
    /// <exception cref="ArgumentException">the selector is not valid</exception>
    public IReadOnlyList<ScriptNode> QuerySelectorAll(string selector) => QuerySelectorAll(selector, firstOnly: false);

    private List<ScriptNode> QuerySelectorAll(string selector, bool firstOnly)
    {
        var parsed = Parse(selector);
        var found = new List<ScriptNode>();

        bool Walk(CssBox box)
        {
            foreach (var child in box.Boxes.ToArray())
            {
                if (child.HtmlTag is null)
                    continue;

                if (CssData.SelectorMatches(parsed, child))
                {
                    found.Add(OwnerDocument.Wrap(child));
                    if (firstOnly)
                        return true;
                }

                if (Walk(child))
                    return true;
            }

            return false;
        }

        Walk(Box);
        return found;
    }

    /// <summary>Whether the element matches the CSS selector.</summary>
    /// <param name="selector">a selector list</param>
    /// <returns>whether it matches</returns>
    /// <exception cref="ArgumentException">the selector is not valid</exception>
    public bool Matches(string selector) => Box.HtmlTag is not null && CssData.SelectorMatches(Parse(selector), Box);

    /// <summary>The closest element at or above this one that the CSS selector matches, or null.</summary>
    /// <param name="selector">a selector list</param>
    /// <returns>the element, or null</returns>
    /// <exception cref="ArgumentException">the selector is not valid</exception>
    public ScriptNode? Closest(string selector)
    {
        var parsed = Parse(selector);
        for (var box = Box; box is not null; box = box.ParentBox)
        {
            if (box.HtmlTag is not null && CssData.SelectorMatches(parsed, box))
                return OwnerDocument.Wrap(box);
        }

        return null;
    }

    private static ISelector Parse(string selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return SelectorParser.ParseSelector(selector) ?? throw new ArgumentException($"'{selector}' is not a valid selector.", nameof(selector));
    }

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        ScriptNodeKind.Element => $"<{TagName}>",
        ScriptNodeKind.Text => "#text",
        ScriptNodeKind.Fragment => "#fragment",
        _ => "#document",
    };
}
