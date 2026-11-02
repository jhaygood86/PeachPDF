using PeachDrawing.Core;
using System.Collections.Generic;

namespace PeachBrackets.Accessibility;

/// <summary>The state of something that can be on, off, or in between: <c>aria-checked</c>, <c>aria-pressed</c>.</summary>
public enum ToggleState
{
    /// <summary>The node has no such state.</summary>
    None,

    /// <summary>Off: not checked, not pressed.</summary>
    False,

    /// <summary>On: checked, pressed.</summary>
    True,

    /// <summary>Neither on nor off, as when a check box heads a group of which some are checked.</summary>
    Mixed,
}

/// <summary>How urgently a change to a region should be announced: <c>aria-live</c>.</summary>
public enum LiveSetting
{
    /// <summary>Changes are not announced.</summary>
    Off,

    /// <summary>Changes are announced when the reader is idle.</summary>
    Polite,

    /// <summary>Changes are announced at once.</summary>
    Assertive,
}

/// <summary>Where part of a node is: the page it is on and its rectangle there.</summary>
/// <param name="PageIndex">the zero-based index of the page</param>
/// <param name="Bounds">the rectangle on that page, in points from the top-left of the sheet</param>
public readonly record struct AccessibilityBounds(int PageIndex, Rect Bounds);

/// <summary>
/// One thing an assistive technology can land on: an element of the document with the role it has, the name it is called by, and what state it
/// is in, or a piece of its text. Nodes form a tree that follows the document, without the elements that mean nothing to a reader.
/// </summary>
public sealed class AccessibilityNode
{
    private readonly List<AccessibilityNode> _children = [];
    private readonly List<AccessibilityBounds> _bounds = [];

    internal AccessibilityNode(string id, AccessibilityRole role)
    {
        Id = id;
        Role = role;
    }

    /// <summary>
    /// A name for the node that is the same each time the document is laid out, so a reader that is on a node stays there when the document is
    /// laid out again. For an element it is built from the element's place in the source.
    /// </summary>
    public string Id { get; }

    /// <summary>What the node is.</summary>
    public AccessibilityRole Role { get; internal set; }

    /// <summary>The name the node is called by, from <c>aria-label</c>, <c>aria-labelledby</c>, the element's own label, its content or its <c>title</c>, in that order of preference. Empty if it has none.</summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>More about the node than its name says, from <c>aria-describedby</c>, <c>aria-description</c> or <c>title</c>. Empty if there is none.</summary>
    public string Description { get; internal set; } = string.Empty;

    /// <summary>The text the node holds, white space collapsed: its own text and that of everything below it.</summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>The language of the node's content as a BCP 47 tag, from the nearest <c>lang</c> attribute, or the document's; null if none is known.</summary>
    public string? Language { get; internal set; }

    /// <summary>The level of a heading (1 to 6, or <c>aria-level</c>), otherwise 0.</summary>
    public int Level { get; internal set; }

    /// <summary>Where a <see cref="AccessibilityRole.Link"/> leads: an address, or <c>#name</c> for a place in the document; otherwise null.</summary>
    public string? Uri { get; internal set; }

    /// <summary>The <c>id</c> attribute of the element, or null if it has none (or the node is text).</summary>
    public string? ElementId { get; internal set; }

    /// <summary>The name of the element, in lower case, or null for text and for the document.</summary>
    public string? TagName { get; internal set; }

    /// <summary>The node it is below; null for the document.</summary>
    public AccessibilityNode? Parent { get; internal set; }

    /// <summary>The nodes below it, in reading order.</summary>
    public IReadOnlyList<AccessibilityNode> Children => _children;

    /// <summary>Where it is: one rectangle for each page it is on, in page order. Empty for something with no place of its own.</summary>
    public IReadOnlyList<AccessibilityBounds> Bounds => _bounds;

    /// <summary>The state of a check box, radio button or switch: <c>checked</c> or <c>aria-checked</c>. <see cref="ToggleState.None"/> if it cannot be checked.</summary>
    public ToggleState Checked { get; internal set; }

    /// <summary>The state of a toggle button: <c>aria-pressed</c>. <see cref="ToggleState.None"/> if it is not one.</summary>
    public ToggleState Pressed { get; internal set; }

    /// <summary>Whether what the node controls is open, for something that can be expanded (<c>aria-expanded</c>, a <c>details</c> element); null if it cannot be.</summary>
    public bool? Expanded { get; internal set; }

    /// <summary>Whether the node is selected (<c>aria-selected</c>, an <c>option</c>'s <c>selected</c>); null if it cannot be.</summary>
    public bool? Selected { get; internal set; }

    /// <summary>Whether the node is disabled: by <c>disabled</c>, by a disabled <c>fieldset</c> around it, or by <c>aria-disabled</c>.</summary>
    public bool Disabled { get; internal set; }

    /// <summary>Whether a value is required (<c>required</c>, <c>aria-required</c>).</summary>
    public bool Required { get; internal set; }

    /// <summary>Whether the value can be read but not changed (<c>readonly</c>, <c>aria-readonly</c>).</summary>
    public bool ReadOnly { get; internal set; }

    /// <summary>Whether the value is not valid (<c>aria-invalid</c>).</summary>
    public bool Invalid { get; internal set; }

    /// <summary>Whether the node is being updated (<c>aria-busy</c>).</summary>
    public bool Busy { get; internal set; }

    /// <summary>Whether the node is a modal dialog, so that everything outside it is out of reach (<c>aria-modal</c>).</summary>
    public bool Modal { get; internal set; }

    /// <summary>Which item of a set the node is the current one of (<c>aria-current</c>: <c>page</c>, <c>step</c>, <c>location</c>, <c>date</c>, <c>time</c> or <c>true</c>); null if it is not.</summary>
    public string? Current { get; internal set; }

    /// <summary>How changes to the node are announced: <c>aria-live</c>, or what its role implies.</summary>
    public LiveSetting Live { get; internal set; }

    /// <summary>Whether a change is announced as a whole rather than as the part that changed (<c>aria-atomic</c>).</summary>
    public bool Atomic { get; internal set; }

    /// <summary>Whether the node can take the keyboard focus: a native control or link, or an element with a <c>tabindex</c>.</summary>
    public bool IsFocusable { get; internal set; }

    /// <summary>The <c>tabindex</c> attribute, or null if there is none.</summary>
    public int? TabIndex { get; internal set; }

    /// <summary>The <c>accesskey</c> attribute, or null.</summary>
    public string? AccessKey { get; internal set; }

    /// <summary>The <c>aria-keyshortcuts</c> attribute, or null.</summary>
    public string? KeyShortcuts { get; internal set; }

    /// <summary>For a cell or a row of a table or grid, the zero-based index of its row (<c>aria-rowindex</c> if given); otherwise -1.</summary>
    public int RowIndex { get; internal set; } = -1;

    /// <summary>For a cell, the zero-based index of its first column (<c>aria-colindex</c> if given); otherwise -1.</summary>
    public int ColumnIndex { get; internal set; } = -1;

    /// <summary>For a cell, how many rows it covers (<c>rowspan</c>, <c>aria-rowspan</c>); 1 otherwise.</summary>
    public int RowSpan { get; internal set; } = 1;

    /// <summary>For a cell, how many columns it covers (<c>colspan</c>, <c>aria-colspan</c>); 1 otherwise.</summary>
    public int ColumnSpan { get; internal set; } = 1;

    /// <summary>For a table or grid, how many rows it has (<c>aria-rowcount</c> if given); otherwise 0.</summary>
    public int RowCount { get; internal set; }

    /// <summary>For a table or grid, how many columns it has (<c>aria-colcount</c> if given); otherwise 0.</summary>
    public int ColumnCount { get; internal set; }

    /// <summary>For a cell, the cells that head its column (or those its <c>headers</c> attribute names that do).</summary>
    public IReadOnlyList<AccessibilityNode> ColumnHeaders { get; internal set; } = [];

    /// <summary>For a cell, the cells that head its row (or those its <c>headers</c> attribute names that do).</summary>
    public IReadOnlyList<AccessibilityNode> RowHeaders { get; internal set; } = [];

    /// <summary>For an item of a set (a list item, option, tab, menu item, tree item), its place in the set counted from 1 (<c>aria-posinset</c>); otherwise 0.</summary>
    public int PositionInSet { get; internal set; }

    /// <summary>For an item of a set, how many items the set has (<c>aria-setsize</c>); otherwise 0.</summary>
    public int SetSize { get; internal set; }

    /// <summary>The nodes this one controls (<c>aria-controls</c>). Nodes the tree leaves out are not listed.</summary>
    public IReadOnlyList<AccessibilityNode> Controls { get; internal set; } = [];

    /// <summary>The nodes this one owns although they are not below it in the document (<c>aria-owns</c>). The tree is not rearranged to show it.</summary>
    public IReadOnlyList<AccessibilityNode> Owns { get; internal set; } = [];

    /// <summary>The nodes a reader may go to next instead of the one that follows (<c>aria-flowto</c>).</summary>
    public IReadOnlyList<AccessibilityNode> FlowTo { get; internal set; } = [];

    /// <summary>The nodes that give more detail than the description does (<c>aria-details</c>).</summary>
    public IReadOnlyList<AccessibilityNode> Details { get; internal set; } = [];

    /// <summary>The nodes that say what is wrong with an invalid value (<c>aria-errormessage</c>).</summary>
    public IReadOnlyList<AccessibilityNode> ErrorMessages { get; internal set; } = [];

    /// <summary>The nodes that give this one its name (<c>aria-labelledby</c>).</summary>
    public IReadOnlyList<AccessibilityNode> LabelledBy { get; internal set; } = [];

    /// <summary>The nodes that describe this one (<c>aria-describedby</c>).</summary>
    public IReadOnlyList<AccessibilityNode> DescribedBy { get; internal set; } = [];

    /// <summary>For a composite widget, the node inside it that has the focus although the widget keeps it (<c>aria-activedescendant</c>); otherwise null.</summary>
    public AccessibilityNode? ActiveDescendant { get; internal set; }

    // What the builder read from the element and could not use until the whole tree was built.
    internal Dictionary<string, string>? RawReferences { get; set; }

    internal int? ExplicitRowIndex { get; set; }

    internal int? ExplicitColumnIndex { get; set; }

    internal int? ExplicitRowCount { get; set; }

    internal int? ExplicitColumnCount { get; set; }

    internal int? ExplicitPosition { get; set; }

    internal int? ExplicitSetSize { get; set; }

    /// <summary>
    /// What a reader says for the node: its <see cref="Name"/>, or when it has none the text it holds directly (the parts of it that are not
    /// elements of their own), or all of its <see cref="Text"/> if it holds only that. A host that has its own tree gives this as the node's name.
    /// </summary>
    public string ReadingText
    {
        get
        {
            if (Name.Length > 0)
                return Name;

            System.Text.StringBuilder? own = null;
            var holdsElements = false;
            foreach (var child in _children)
            {
                if (child.Role == AccessibilityRole.Text)
                {
                    own ??= new System.Text.StringBuilder();
                    if (own.Length > 0)
                        own.Append(' ');

                    own.Append(child.Name);
                }
                else
                {
                    holdsElements = true;
                }
            }

            return holdsElements ? own?.ToString() ?? string.Empty : Text;
        }
    }

    /// <summary>
    /// The nodes below it that a reader steps to, in reading order. The text of an element is read as part of the element (see
    /// <see cref="ReadingText"/>), so its text nodes are left out; the text that belongs to the document itself is kept.
    /// </summary>
    public IEnumerable<AccessibilityNode> ReadingChildren
    {
        get
        {
            foreach (var child in _children)
            {
                if (child.Role != AccessibilityRole.Text || Role == AccessibilityRole.Document)
                    yield return child;
            }
        }
    }

    internal void AddChild(AccessibilityNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    internal void AddBounds(AccessibilityBounds bounds) => _bounds.Add(bounds);

    /// <summary>The node and everything below it, in reading order.</summary>
    /// <returns>the nodes, this one first</returns>
    public IEnumerable<AccessibilityNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        {
            foreach (var below in child.DescendantsAndSelf())
                yield return below;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{Role} \"{Name}\"";
}
