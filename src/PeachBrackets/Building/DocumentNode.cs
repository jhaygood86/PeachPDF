using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Building;

/// <summary>
/// One box of a document built in code: an element, a run of text, an image or a list marker, with the style set on it.
/// </summary>
/// <remarks>
/// The tree is what <see cref="HtmlDocument.CreateAsync"/> lays out, so a node behaves like the box a parsed element would become:
/// <see cref="SetStyle"/> takes a CSS property by name and a CSS value, the way a style attribute would. A node belongs to the
/// <see cref="DocumentBuildContext"/> that made it. A node is a thin handle: two handles to the same box are equal.
/// </remarks>
public sealed class DocumentNode : IEquatable<DocumentNode>
{
    internal DocumentNode(CssBox box, DocumentBuildContext context)
    {
        Box = box;
        Context = context;
    }

    internal CssBox Box { get; }

    internal DocumentBuildContext Context { get; }

    /// <summary>The render context the node's styles are resolved against.</summary>
    public RenderContext RenderContext => Context.RenderContext;

    /// <summary>The node this one is a child of, or null for a root.</summary>
    public DocumentNode? Parent => Box.ParentBox is { } parent ? new DocumentNode(parent, Context) : null;

    /// <summary>The node's children, in order.</summary>
    public IReadOnlyList<DocumentNode> Children => Box.Boxes.Select(child => new DocumentNode(child, Context)).ToArray();

    /// <summary>The name of the element the node stands for (<c>div</c>, <c>td</c>, <c>a</c>), or null for a node that is not an element.</summary>
    public string? TagName => Box.HtmlTag?.Name;

    /// <summary>The text the node holds, or null. A node holds text or children, never both.</summary>
    public string? Text
    {
        get => Box.Text;
        set => Box.Text = value;
    }

    /// <summary>Whether the node is laid out as a table cell.</summary>
    public bool IsTableCell => Box.Display.Value == DisplayMode.TableCell;

    /// <summary>Whether the node has children of its own.</summary>
    public bool HasChildren => Box.Boxes.Count > 0;

    /// <summary>
    /// The section a page-number counter inside this node counts within, set by <c>BeginPageNumberOfSection</c>-style builders: this node is where
    /// the section starts. Null for none.
    /// </summary>
    public string? SectionBeginId
    {
        get => Box.SectionBeginId;
        set => Box.SectionBeginId = value;
    }

    /// <summary>The section that ends at this node, or null for none; the counterpart of <see cref="SectionBeginId"/>.</summary>
    public string? SectionEndId
    {
        get => Box.SectionEndId;
        set => Box.SectionEndId = value;
    }

    /// <summary>
    /// Makes the node a page counter within a section: its text is the page number inside the section (or the section's total page count, if
    /// <paramref name="isTotal"/> is set), counted between the nodes marked with <see cref="SectionBeginId"/> and <see cref="SectionEndId"/>.
    /// </summary>
    /// <param name="sectionId">the section to count within</param>
    /// <param name="isTotal">whether the counter shows the section's page count instead of the current page's number in it</param>
    public void SetSectionPageCounter(string sectionId, bool isTotal)
    {
        ArgumentNullException.ThrowIfNull(sectionId);

        Box.SectionPageCounterSectionId = sectionId;
        Box.SectionPageCounterIsTotal = isTotal;
    }

    /// <summary>
    /// Whether the node's text direction is still to be worked out from its content once the content is complete (<c>dir=auto</c> semantics).
    /// <see cref="HtmlDocument.CreateAsync"/> resolves every node that is waiting.
    /// </summary>
    public bool PendingAutoDirection
    {
        get => Box.PendingAutoDirection;
        set => Box.PendingAutoDirection = value;
    }

    /// <summary>
    /// Sets a CSS property on the node, as a style declaration would.
    /// </summary>
    /// <param name="property">the property name, such as <c>background-color</c> or <c>padding-left</c></param>
    /// <param name="value">the property's value in CSS syntax; one the property does not accept is ignored, as in a style sheet</param>
    public void SetStyle(string property, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(property);
        ArgumentNullException.ThrowIfNull(value);

        Context.Properties.Set(Box, property, value);
    }

    /// <summary>
    /// Sets how the node's text is aligned, both on its lines and on its last line.
    /// </summary>
    /// <param name="keyword">a <c>text-align</c> keyword: <c>left</c>, <c>center</c>, <c>right</c>, <c>justify</c>, <c>start</c> or <c>end</c></param>
    public void SetTextAlign(string keyword)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyword);

        Context.Properties.SetTextAlign(Box, keyword);
    }

    /// <summary>
    /// Gives the node an outline entry (a bookmark) at the given level, labelled with <paramref name="title"/>.
    /// </summary>
    /// <param name="title">the entry's label</param>
    /// <param name="level">the entry's nesting level, 1 or more</param>
    public void SetBookmark(string title, int level)
    {
        ArgumentNullException.ThrowIfNull(title);

        Box.BookmarkLevel = level.ToString(CultureInfo.InvariantCulture);
        Box.BookmarkLabel = "\"" + title.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>Returns the value of an attribute of the element, or null if it has none by that name.</summary>
    /// <param name="name">the attribute name</param>
    /// <returns>the attribute's value, or null</returns>
    public string? GetAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Box.HtmlTag?.TryGetAttribute(name);
    }

    /// <summary>Sets an attribute of the element, making the node an element (a <c>div</c>) if it was not one.</summary>
    /// <param name="name">the attribute name</param>
    /// <param name="value">the attribute value</param>
    public void SetAttribute(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        Box.EnsureHtmlTag();
        Box.HtmlTag!.SetAttribute(name, value);
    }

    /// <summary>Adds a class to the element's <c>class</c> attribute, making the node an element if it was not one.</summary>
    /// <param name="className">the class to add</param>
    public void AddClass(string className)
    {
        ArgumentNullException.ThrowIfNull(className);

        var existing = GetAttribute("class");
        SetAttribute("class", string.IsNullOrEmpty(existing) ? className : existing + " " + className);
    }

    /// <summary>Renames the element, making the node an element if it was not one. Selectors and the default styles see the new name.</summary>
    /// <param name="tagName">the new element name</param>
    public void SetTagName(string tagName)
    {
        ArgumentException.ThrowIfNullOrEmpty(tagName);

        Box.EnsureHtmlTag();
        Box.HtmlTag!.Name = tagName;
    }

    /// <summary>Adds a plain block-level child, the building block of most structure. It inherits the node's text styles.</summary>
    /// <returns>the new child</returns>
    public DocumentNode AppendBlock() => new(CssPropertyFactory.CreateAnonymousBox(Box), Context);

    /// <summary>Adds a child that is a text node with the given text.</summary>
    /// <param name="text">the text</param>
    /// <returns>the new child</returns>
    public DocumentNode AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // A genuine new child, not Text on this node: the node may already hold a child of its own for a reason the caller never sees - a list
        // item's synthesized marker, most notably - and a box must never hold both its own words and child boxes at once. Real HTML never puts
        // text directly on an element box with other children either: a text node always becomes its own anonymous child box.
        var child = AppendBlock();
        child.Text = text;
        return child;
    }

    /// <summary>Adds a child element.</summary>
    /// <param name="tagName">the element name, such as <c>a</c> or <c>td</c></param>
    /// <param name="attributes">the element's attributes, or null for none</param>
    /// <returns>the new child</returns>
    public DocumentNode AppendElement(string tagName, IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(tagName);

        var tag = new HtmlTag(tagName, false, attributes is null ? null : new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase));
        return new DocumentNode(CssBox.CreateBox(Box, tag), Context);
    }

    /// <summary>Adds a list item child, with its marker already in front: the child lays out as <c>display: list-item</c>.</summary>
    /// <returns>the new list item</returns>
    public DocumentNode AppendListItem()
    {
        var item = AppendBlock();
        item.SetStyle("display", "list-item");

        var marker = new CssBoxMarker(item.Box);
        item.Box.Boxes.Remove(marker);
        item.Box.Boxes.Insert(0, marker);
        marker.ResolveDefaultContent();

        return item;
    }

    /// <summary>Adds an <c>img</c> child, which inherits the node's text styles. Give it a picture with one of the <c>Set...Content</c> methods or a <c>src</c> attribute.</summary>
    /// <param name="attributes">the image element's attributes (such as <c>src</c>), or null for none</param>
    /// <returns>the new image node</returns>
    public DocumentNode AppendImage(IReadOnlyDictionary<string, string>? attributes = null)
    {
        var tag = new HtmlTag("img", true, attributes is null ? null : new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase));
        var image = (CssBoxImage)CssBox.CreateBox(tag, Box);
        image.InheritStyle();
        return new DocumentNode(image, Context);
    }

    /// <summary>Gives an image node a decoded raster picture.</summary>
    /// <param name="image">the picture</param>
    /// <exception cref="InvalidOperationException">the node is not an image</exception>
    public void SetImageContent(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        ImageBox().SetDecodedContent(image, null);
    }

    /// <summary>Gives an image node a vector picture.</summary>
    /// <param name="svg">the picture</param>
    /// <exception cref="InvalidOperationException">the node is not an image</exception>
    public void SetImageContent(SvgImage svg)
    {
        ArgumentNullException.ThrowIfNull(svg);

        ImageBox().SetDecodedContent(null, svg.Document);
    }

    /// <summary>
    /// Gives an image node a raster picture made when its size is known: <paramref name="generator"/> is called with the size, in points, the
    /// node ends up at, and returns the encoded image.
    /// </summary>
    /// <param name="generator">makes the encoded image for a size in points</param>
    /// <exception cref="InvalidOperationException">the node is not an image</exception>
    public void SetImageContent(Func<Size, byte[]> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);

        ImageBox().SetDynamicRasterContent(generator);
    }

    /// <summary>
    /// Gives an image node a vector picture made when its size is known: <paramref name="generator"/> is called with the size, in points, the
    /// node ends up at, and returns SVG markup.
    /// </summary>
    /// <param name="generator">makes the SVG markup for a size in points</param>
    /// <exception cref="InvalidOperationException">the node is not an image</exception>
    public void SetSvgContent(Func<Size, string> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);

        ImageBox().SetDynamicSvgContent(generator);
    }

    private CssBoxImage ImageBox() =>
        Box as CssBoxImage ?? throw new InvalidOperationException("The node is not an image; make it with AppendImage.");

    /// <summary>
    /// Parses an HTML fragment and puts what it makes inside a new block child, styled by <paramref name="styleSheet"/> and the fragment's own
    /// <c>&lt;style&gt;</c> elements. A <c>&lt;slot&gt;</c> element in the fragment is a hole the caller fills.
    /// </summary>
    /// <param name="html">the fragment's HTML</param>
    /// <param name="styleSheet">the rules the fragment is styled with, or null for the built-in defaults; it is not changed</param>
    /// <param name="fillSlot">
    /// called for each <c>&lt;slot&gt;</c>, with its name and attributes and a node to build its replacement into; returns whether anything was
    /// built there (if not, the slot's own fallback content stays). Null leaves every slot as it is.
    /// </param>
    /// <returns>the block that holds the fragment</returns>
    public DocumentNode AppendHtmlFragment(string html, StyleSheet? styleSheet = null, Func<HtmlFragmentSlot, DocumentNode, bool>? fillSlot = null)
    {
        ArgumentNullException.ThrowIfNull(html);

        var wrapper = AppendBlock();
        wrapper.SetStyle("display", "block");

        var (fragmentRoot, shells) = BuildFragmentAsync(html, styleSheet).GetAwaiter().GetResult();
        Context.Properties.DisplayContentsShells.AddRange(shells);

        // Must run before slot processing: a slot's replacement content is built afterwards, and those boxes must NOT be flagged - only the
        // fragment's own real-cascade-styled boxes should be.
        MarkFragmentStyled(fragmentRoot);

        ProcessSlots(fragmentRoot, fillSlot);

        wrapper.Box.SetAllBoxes(fragmentRoot);

        // A shell that was a top-level node of the fragment still names the discarded root as its parent.
        foreach (var shell in shells)
        {
            if (ReferenceEquals(shell.ParentBox, fragmentRoot))
                shell.RetargetShellParent(wrapper.Box);
        }

        return wrapper;
    }

    private async Task<(CssBox Root, List<CssBox> DisplayContentsShells)> BuildFragmentAsync(string html, StyleSheet? styleSheet)
    {
        var renderContext = Context.RenderContext;

        // Cloned so this fragment's own <style> tag collection never mutates a caller-shared style sheet, or the cached built-in defaults.
        var cssData = (styleSheet?.Data ?? await DefaultCssDataCache.GetAsync(renderContext)).Clone();

        var cssParser = new CssParser(renderContext, htmlContainer: null);
        var domParser = new DomParser(cssParser);
        return await domParser.GenerateFragmentCssTree(html, renderContext, cssData);
    }

    private static void MarkFragmentStyled(CssBox box)
    {
        box.IsFragmentStyled = true;
        foreach (var child in box.Boxes)
            MarkFragmentStyled(child);
    }

    private void ProcessSlots(CssBox fragmentRoot, Func<HtmlFragmentSlot, DocumentNode, bool>? fillSlot)
    {
        var slotBoxes = new List<CssBox>();
        CollectSlotBoxes(fragmentRoot, slotBoxes);

        if (slotBoxes.Count == 0 || fillSlot is null)
            return;

        foreach (var slotBox in slotBoxes)
        {
            // A <slot> nested inside an earlier sibling slot's own fallback content is still in this list (collection is a single up-front pass,
            // before any slot is filled) even after that ancestor slot's own fill already detached the whole fallback subtree it lived in -
            // invoking its callback at that point would fire real side effects for content that can never reach the final tree, so it is skipped.
            if (!IsAttached(slotBox, fragmentRoot))
                continue;

            var attributes = slotBox.HtmlTag?.Attributes is { } tagAttributes
                ? new Dictionary<string, string>(tagAttributes, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var name = slotBox.HtmlTag?.TryGetAttribute("name", "") ?? "";

            var parent = slotBox.ParentBox!;
            var replacementWrapper = CssBox.CreateBox(parent, tag: null, before: slotBox);

            var filled = fillSlot(new HtmlFragmentSlot(name, attributes), new DocumentNode(replacementWrapper, Context));

            if (filled)
                slotBox.ParentBox = null;
            else
                replacementWrapper.ParentBox = null;
        }
    }

    private static bool IsAttached(CssBox box, CssBox root)
    {
        for (var current = box; current is not null; current = current.ParentBox)
        {
            if (ReferenceEquals(current, root))
                return true;
        }

        return false;
    }

    private static void CollectSlotBoxes(CssBox box, List<CssBox> result)
    {
        if (box.HtmlTag?.Name.Equals("slot", StringComparison.OrdinalIgnoreCase) == true)
            result.Add(box);

        foreach (var child in box.Boxes.ToArray())
            CollectSlotBoxes(child, result);
    }

    /// <inheritdoc />
    public bool Equals(DocumentNode? other) => other is not null && ReferenceEquals(Box, other.Box);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DocumentNode);

    /// <inheritdoc />
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Box);
}

/// <summary>A <c>&lt;slot&gt;</c> element of an HTML fragment spliced into a document, which the caller may fill.</summary>
/// <param name="Name">the slot's <c>name</c> attribute, or the empty string</param>
/// <param name="Attributes">all of the slot's attributes</param>
public sealed record HtmlFragmentSlot(string Name, IReadOnlyDictionary<string, string> Attributes);
