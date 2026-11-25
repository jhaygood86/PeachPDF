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

    /// <summary>A comment.</summary>
    Comment,

    /// <summary>A processing instruction.</summary>
    ProcessingInstruction,

    /// <summary>A document type declaration.</summary>
    DocumentType,
}

/// <summary>What a node that is not an element, text or the document carries: kept beside the box that holds its place in the tree.</summary>
internal sealed class SpecialNode(ScriptNodeKind kind, string name, string data, string publicId = "", string systemId = "")
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CssBox, SpecialNode> Table = new();

    public ScriptNodeKind Kind { get; } = kind;
    public string Name { get; } = name;
    public string Data { get; set; } = data;
    public string PublicId { get; } = publicId;
    public string SystemId { get; } = systemId;

    public static CssBox Create(SpecialNode info)
    {
        var box = new CssBox(null, null);
        Table.Add(box, info);
        return box;
    }

    public static SpecialNode? Of(CssBox box) => Table.TryGetValue(box, out var info) ? info : null;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CssBox, object> CData = new();

    public static void MarkCData(CssBox box) => CData.TryAdd(box, box);

    public static bool IsCData(CssBox box) => CData.TryGetValue(box, out _);
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
    public ScriptDocument OwnerDocument { get; internal set; }

    /// <summary>What the node is.</summary>
    public ScriptNodeKind Kind =>
        Box.HtmlTag is not null ? ScriptNodeKind.Element
        : SpecialNode.Of(Box) is { } special ? special.Kind
        : ReferenceEquals(Box, OwnerDocument.RootBox) ? ScriptNodeKind.Document
        : Box.Text is null ? ScriptNodeKind.Fragment
        : ScriptNodeKind.Text;

    /// <summary>The element's name as the document spells it (<c>div</c>, <c>td</c>, <c>linearGradient</c>), or null for a node that is not an element.</summary>
    public string? TagName => Box.HtmlTag?.Name;

    /// <summary>The text of a text node, or null for any other node. Setting it changes the node's text.</summary>
    public string? Data
    {
        get => Kind switch
        {
            ScriptNodeKind.Text => Box.Text ?? string.Empty,
            ScriptNodeKind.Comment or ScriptNodeKind.ProcessingInstruction => SpecialNode.Of(Box)!.Data,
            _ => null,
        };
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var before = Data;
            switch (Kind)
            {
                case ScriptNodeKind.Text:
                    Box.Text = value;
                    break;
                case ScriptNodeKind.Comment or ScriptNodeKind.ProcessingInstruction:
                    SpecialNode.Of(Box)!.Data = value;
                    break;
                default:
                    throw new InvalidOperationException("Only a text, comment or processing instruction node has data.");
            }

            OwnerDocument.NoteChanged();
            OwnerDocument.RaiseMutation(new ScriptMutation(ScriptMutationKind.CharacterData, this, OldValue: before));
        }
    }

    /// <summary>The name of a document type, or the target of a processing instruction; null for any other node.</summary>
    public string? Name => SpecialNode.Of(Box) is { Kind: ScriptNodeKind.DocumentType or ScriptNodeKind.ProcessingInstruction } special ? special.Name : null;

    /// <summary>The public identifier of a document type, or null.</summary>
    public string? PublicId => SpecialNode.Of(Box) is { Kind: ScriptNodeKind.DocumentType } special ? special.PublicId : null;

    /// <summary>The system identifier of a document type, or null.</summary>
    public string? SystemId => SpecialNode.Of(Box) is { Kind: ScriptNodeKind.DocumentType } special ? special.SystemId : null;

    /// <summary>Whether the node is a CDATA section: a text node an XML document marks as such.</summary>
    public bool IsCDataSection => Kind == ScriptNodeKind.Text && SpecialNode.IsCData(Box);

    /// <summary>Whether the node cannot have children: text, a comment, a processing instruction or a document type.</summary>
    public bool IsLeaf => Kind is ScriptNodeKind.Text or ScriptNodeKind.Comment or ScriptNodeKind.ProcessingInstruction or ScriptNodeKind.DocumentType;

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
    private static bool IsPartOfSource(CssBox box) => box.HtmlTag is not null || box.Text is not null || SpecialNode.Of(box) is not null;

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
        var before = Box.HtmlTag!.TryGetAttribute(name);
        Box.HtmlTag!.SetAttribute(name, value);
        OwnerDocument.NoteChanged();
        OwnerDocument.RaiseMutation(new ScriptMutation(ScriptMutationKind.Attributes, this, AttributeName: name, OldValue: before));
    }

    /// <summary>Removes an attribute of the element, if it has it.</summary>
    /// <param name="name">the attribute name</param>
    /// <returns>whether the element had it</returns>
    public bool RemoveAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (Box.HtmlTag is not { } tag || !tag.HasAttribute(name))
            return false;

        var before = tag.TryGetAttribute(name);
        tag.RemoveAttribute(name);
        OwnerDocument.NoteChanged();
        OwnerDocument.RaiseMutation(new ScriptMutation(ScriptMutationKind.Attributes, this, AttributeName: name, OldValue: before));
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
        if (IsLeaf)
            throw new InvalidOperationException("A node of this kind has no children.");
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

        if (!ReferenceEquals(child.OwnerDocument, OwnerDocument))
            OwnerDocument.Adopt(child);

        if (child.Kind == ScriptNodeKind.Fragment)
        {
            var added = child.Children;
            foreach (var moved in child.Box.Boxes.ToArray())
            {
                moved.ParentBox = Box;
                if (reference is not null)
                    moved.SetBeforeBox(reference.Box);
            }

            OwnerDocument.NoteChanged();
            if (added.Count > 0)
                OwnerDocument.RaiseMutation(new ScriptMutation(ScriptMutationKind.ChildList, this, Added: added, Previous: added[0].PreviousSibling, Next: added[^1].NextSibling));

            return child;
        }

        // A node that is somewhere else is taken out of there first, which is a change of its own.
        if (child.Box.ParentBox is not null)
            child.Parent!.RemoveChild(child);

        child.Box.ParentBox = Box;
        if (reference is not null)
            child.Box.SetBeforeBox(reference.Box);

        OwnerDocument.NoteChanged();
        OwnerDocument.RaiseMutation(new ScriptMutation(ScriptMutationKind.ChildList, this, Added: [child], Previous: child.PreviousSibling, Next: child.NextSibling));
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

        var previous = child.PreviousSibling;
        var next = child.NextSibling;
        child.Box.ParentBox = null;
        OwnerDocument.NoteChanged();
        OwnerDocument.RaiseMutation(new ScriptMutation(ScriptMutationKind.ChildList, this, Removed: [child], Previous: previous, Next: next));
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

        if (!ReferenceEquals(existing.Box.ParentBox, Box))
            throw new InvalidOperationException("The node is not a child of this node.");

        // One change: the new node(s) in, the old one out.
        var previous = existing.PreviousSibling;
        var next = existing.NextSibling;
        if (ReferenceEquals(previous, child))
            previous = child.PreviousSibling;
        if (ReferenceEquals(next, child))
            next = child.NextSibling;

        var added = child.Kind == ScriptNodeKind.Fragment ? child.Children : [child];
        OwnerDocument.SuppressMutations++;
        try
        {
            InsertBefore(child, existing);
            RemoveChild(existing);
        }
        finally
        {
            OwnerDocument.SuppressMutations--;
        }

        OwnerDocument.RaiseMutation(new ScriptMutation(ScriptMutationKind.ChildList, this, Added: added, Removed: [existing], Previous: previous, Next: next));
        return existing;
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
        else if (SpecialNode.Of(source) is { } special)
        {
            copy = SpecialNode.Create(new SpecialNode(special.Kind, special.Name, special.Data, special.PublicId, special.SystemId));
        }
        else
        {
            copy = new CssBox(null, null) { Text = source.Text };
            if (SpecialNode.IsCData(source))
                SpecialNode.MarkCData(copy);

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
            if (IsLeaf)
                return Data ?? string.Empty;

            var text = new System.Text.StringBuilder();
            Collect(Box, text);
            return text.ToString();
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (IsLeaf)
            {
                if (Kind != ScriptNodeKind.DocumentType)
                    Data = value;

                return;
            }

            var removed = Children;
            foreach (var child in Box.Boxes.ToArray())
                child.ParentBox = null;

            ScriptNode[] added = [];
            if (value.Length > 0)
                added = [OwnerDocument.Wrap(new CssBox(Box, null) { Text = value })];

            OwnerDocument.NoteChanged();
            if (removed.Count > 0 || added.Length > 0)
                OwnerDocument.RaiseMutation(new ScriptMutation(ScriptMutationKind.ChildList, this, Added: added, Removed: removed));
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
            if (IsLeaf)
                throw new InvalidOperationException("A node of this kind has no markup.");

            var removed = Children;
            foreach (var child in Box.Boxes.ToArray())
                child.ParentBox = null;

            var holder = ParseInContext(value);
            foreach (var child in holder.Boxes.ToArray())
                child.ParentBox = Box;

            OwnerDocument.NoteChanged();
            var added = Children;
            if (removed.Count > 0 || added.Count > 0)
                OwnerDocument.RaiseMutation(new ScriptMutation(ScriptMutationKind.ChildList, this, Added: added, Removed: removed));
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
        ScriptNodeKind.Comment => "#comment",
        ScriptNodeKind.ProcessingInstruction => "#processing-instruction",
        ScriptNodeKind.DocumentType => "#doctype",
        ScriptNodeKind.Fragment => "#fragment",
        _ => "#document",
    };
}
