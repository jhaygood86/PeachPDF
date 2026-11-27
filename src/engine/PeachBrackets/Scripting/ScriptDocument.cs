using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Utils;
using PeachPDF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.ScriptHosting;

/// <summary>A rectangle of the laid-out page, in CSS pixels.</summary>
/// <param name="X">the distance from the left</param>
/// <param name="Y">the distance from the top</param>
/// <param name="Width">the width</param>
/// <param name="Height">the height</param>
public readonly record struct ScriptBox(double X, double Y, double Width, double Height);

/// <summary>
/// A document whose scripts are about to run: the tree the HTML parser made, which a script changes. Handed to <see cref="IScriptHost.RunAsync"/>.
/// What it holds when the host returns is what the document is styled and laid out from.
/// </summary>
public sealed class ScriptDocument
{
    private readonly HtmlContainerInt _container;
    private readonly Dictionary<CssBox, ScriptNode> _nodes = new(ReferenceEqualityComparer.Instance as IEqualityComparer<CssBox>);

    internal ScriptDocument(HtmlContainerInt container, CssBox root, DocumentMode mode, bool html = true, bool declareDoctype = true, string? contentType = null)
    {
        _container = container;
        RootBox = root;
        Mode = mode;
        IsHtml = html;
        ContentType = contentType ?? (html ? "text/html" : "application/xml");
        Root = Wrap(root);

        // The document type the page declared is a node of the document, from which the mode is written back.
        if (declareDoctype && mode != DocumentMode.Quirks)
        {
            var info = mode == DocumentMode.NoQuirks
                ? new SpecialNode(ScriptNodeKind.DocumentType, "html", string.Empty)
                : new SpecialNode(ScriptNodeKind.DocumentType, "html", string.Empty, "-//W3C//DTD XHTML 1.0 Transitional//EN", "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd");
            var doctype = SpecialNode.Create(info);
            doctype.ParentBox = root;
            if (root.Boxes.FirstOrDefault(b => !ReferenceEquals(b, doctype)) is { } first)
                doctype.SetBeforeBox(first);
        }
    }

    internal CssBox RootBox { get; }

    /// <summary>Whether the document is an HTML document: element names are matched without regard to case and made in lower case.</summary>
    public bool IsHtml { get; }

    /// <summary>The document's MIME type: <c>text/html</c> for an HTML document, <c>application/xml</c> for one made empty.</summary>
    public string ContentType { get; }

    /// <summary>Makes another, empty document that shares this one's resource loader: what <c>document.implementation</c> and <c>DOMParser</c> hand a script.</summary>
    /// <param name="html">whether it is an HTML document (otherwise an XML one)</param>
    /// <param name="withDocumentType">whether it starts with an HTML document type</param>
    /// <param name="contentType">its MIME type, or null for the default of its kind</param>
    /// <returns>the document</returns>
    public ScriptDocument CreateDocument(bool html, bool withDocumentType, string? contentType = null) =>
        new(_container, new CssBox(null, null), withDocumentType ? DocumentMode.NoQuirks : DocumentMode.Quirks, html, withDocumentType, contentType);

    /// <summary>Parses HTML into another document, as a browser parses a page.</summary>
    /// <param name="html">the markup</param>
    /// <returns>the document</returns>
    public ScriptDocument ParseDocument(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var root = Html.Parse.HtmlParser.ParseDocument(html, out var mode, null, document: true, scripting: false);
        return new ScriptDocument(_container, root, mode, html: true, declareDoctype: true);
    }

    // A node that came from another document, and its descendants, now belong to this one.
    internal void Adopt(ScriptNode node)
    {
        var from = node.OwnerDocument;
        if (ReferenceEquals(from, this))
            return;

        if (node.Box.ParentBox is not null)
        {
            node.Box.ParentBox = null;
            from.NoteChanged();
        }

        void Move(CssBox box)
        {
            if (from._nodes.Remove(box, out var wrapper))
            {
                wrapper.OwnerDocument = this;
                _nodes[box] = wrapper;
            }

            foreach (var child in box.Boxes.ToArray())
                Move(child);
        }

        Move(node.Box);
        if (!_nodes.ContainsKey(node.Box))
            Wrap(node.Box);
    }

    /// <summary>Takes a node and its descendants out of the document they are in (detaching them) and makes them this document's.</summary>
    /// <param name="node">the node</param>
    /// <returns>the node</returns>
    public ScriptNode AdoptNode(ScriptNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Adopt(node);
        return node;
    }

    internal Func<string, ValueTask<HtmlDocument>>? Probe { get; set; }

    internal Func<string, ValueTask<HtmlDocument>>? RenderProbeFactory { get; set; }

    private HtmlDocument? _renderProbe;
    private int _renderProbeVersion = -1;
    private readonly List<(string Markup, HtmlDocument Document)> _renderProbes = [];

    private HtmlDocument? _probe;
    private int _probeVersion = -1;

    // The laid-out documents of the markups the tree has had, newest last: a script that adds an element, reads where it is and takes it away again (and does so for many
    // elements that are the same) lays the same document out each time, which is only done once.
    private readonly List<(string Markup, HtmlDocument Document)> _probes = [];
    private const int MaxProbes = 8;

    // The document as it is now, laid out: loaded again from the tree when the tree has changed since.
    private HtmlDocument? CurrentProbe()
    {
        if (Probe is null)
            return null;

        if (_probe is null || _probeVersion != Version)
        {
            var markup = Serialize();
            var cached = _probes.FindIndex(p => string.Equals(p.Markup, markup, StringComparison.Ordinal));
            if (cached >= 0)
            {
                _probe = _probes[cached].Document;
                var entry = _probes[cached];
                _probes.RemoveAt(cached);
                _probes.Add(entry);
            }
            else
            {
                _probe = null;
                try
                {
                    // The scripts run inside a load that is already asynchronous; the probe's own load is of an in-memory string.
                    _probe = Probe(markup).AsTask().GetAwaiter().GetResult();
                    _probes.Add((markup, _probe));
                    while (_probes.Count > MaxProbes)
                    {
                        _probes[0].Document.Dispose();
                        _probes.RemoveAt(0);
                    }
                }
                catch (HtmlRenderException)
                {
                    _probe = null;
                }
            }

            _probeVersion = Version;
        }

        return _probe;
    }

    // The document as it is now, laid out for drawing from: what a picture of an element may show is less than what a script may measure (it leaves out images from other origins).
    private HtmlDocument? RenderProbe()
    {
        if (RenderProbeFactory is null)
            return null;

        if (_renderProbe is null || _renderProbeVersion != Version)
        {
            var markup = Serialize();
            var cached = _renderProbes.FindIndex(p => string.Equals(p.Markup, markup, StringComparison.Ordinal));
            if (cached >= 0)
            {
                _renderProbe = _renderProbes[cached].Document;
                var entry = _renderProbes[cached];
                _renderProbes.RemoveAt(cached);
                _renderProbes.Add(entry);
            }
            else
            {
                _renderProbe = null;
                try
                {
                    // The scripts run inside a load that is already asynchronous; the probe's own load is of an in-memory string.
                    _renderProbe = RenderProbeFactory(markup).AsTask().GetAwaiter().GetResult();
                    _renderProbes.Add((markup, _renderProbe));
                    while (_renderProbes.Count > MaxProbes)
                    {
                        _renderProbes[0].Document.Dispose();
                        _renderProbes.RemoveAt(0);
                    }
                }
                catch (HtmlRenderException)
                {
                    _renderProbe = null;
                }
            }

            _renderProbeVersion = Version;
        }

        return _renderProbe;
    }

    /// <summary>Whether the host gave a way to lay the document out, so <see cref="GetLayoutBox"/> and <see cref="GetComputedValue"/> can answer.</summary>
    public bool CanMeasure => Probe is not null;

    /// <summary>The place and size of an element in the document as it is now, in CSS pixels from the top left of the page's content area. Lays the document out again if it changed since the last read.</summary>
    /// <param name="element">the element</param>
    /// <returns>the box, or null when there is no layout to read (see <see cref="CanMeasure"/>) or the element is not displayed</returns>
    public ScriptBox? GetLayoutBox(ScriptNode element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var ordinal = OrdinalOf(element);
        if (ordinal < 0 || CurrentProbe() is not { } probe || probe.ProbeBounds(ordinal) is not { } bounds)
            return null;

        return new ScriptBox(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    /// <summary>The whole box of an element (the union of the parts a broken one takes) and its bounding box with its own CSS transform applied, in CSS pixels from the top left of the page.</summary>
    /// <param name="element">the element</param>
    /// <returns>the two boxes, or null when there is no layout to read or the element is not displayed</returns>
    public (ScriptBox Box, ScriptBox Transformed)? GetBoundingBoxes(ScriptNode element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var ordinal = OrdinalOf(element);
        if (ordinal < 0 || CurrentProbe() is not { } probe || probe.ProbeBoxes(ordinal) is not { } boxes)
            return null;

        return (new ScriptBox(boxes.Box.X, boxes.Box.Y, boxes.Box.Width, boxes.Box.Height), new ScriptBox(boxes.Transformed.X, boxes.Transformed.Y, boxes.Transformed.Width, boxes.Transformed.Height));
    }

    private readonly Dictionary<int, ScriptElementPicture?> _pictures = [];
    private int _picturesVersion = -1;

    /// <summary>
    /// A picture of an element as it is laid out in the document now (drawn from the probe, which lays out the children of a canvas that makes them drawable), at one pixel to a CSS pixel.
    /// Kept until the document changes; the document owns it.
    /// </summary>
    /// <param name="element">the element</param>
    /// <returns>the picture, or null when there is no layout to draw from (see <see cref="CanMeasure"/>) or the element has no area</returns>
    public ScriptElementPicture? RenderElement(ScriptNode element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var ordinal = OrdinalOf(element);
        if (ordinal < 0 || RenderProbe() is not { } probe || !_container.CanvasSurfaces && _container.MeasuringCanvasFactory is null)
            return null;

        if (_picturesVersion != Version)
        {
            // Not disposed: a canvas that drew one keeps it in what it recorded, to draw again.
            _pictures.Clear();
            _picturesVersion = Version;
        }

        if (_pictures.TryGetValue(ordinal, out var cached))
            return cached;

        using var measuring = CreateMeasuringCanvas();
        ScriptElementPicture? made = null;
        if (measuring is not null)
        {
            var rendered = probe.ProbeRenderAsync(ordinal, (w, h) => measuring.CreateOffscreenSurface(w, h), CanvasPaintingsByOrdinal()).AsTask().GetAwaiter().GetResult();
            if (rendered is { } picture)
                made = new ScriptElementPicture(picture.Image, picture.Width, picture.Height);
        }

        _pictures[ordinal] = made;
        return made;
    }

    // What the canvases of the document show now, by the position the layout knows each by: a picture of something that holds a canvas draws it.
    private Dictionary<int, ScriptCanvasPainting> CanvasPaintingsByOrdinal()
    {
        var result = new Dictionary<int, ScriptCanvasPainting>();
        PublishInto(result);
        return result;
    }

    /// <summary>The rectangles the characters from <paramref name="from"/> to <paramref name="to"/> of an element's text take in the document as it is now, one for each stretch of a line, in CSS pixels.</summary>
    /// <param name="element">the element that holds the text</param>
    /// <param name="from">the index of the first character</param>
    /// <param name="to">the index one past the last</param>
    /// <returns>the boxes; none when there is no layout to read</returns>
    public IReadOnlyList<ScriptBox> GetTextRects(ScriptNode element, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(element);
        var ordinal = OrdinalOf(element);
        if (ordinal < 0 || CurrentProbe() is not { } probe)
            return [];

        return [.. probe.ProbeTextRects(ordinal, from, to).Select(r => new ScriptBox(r.X, r.Y, r.Width, r.Height))];
    }

    /// <summary>The place between two characters nearest a point of the page (in CSS pixels): the element whose text it is in and how many characters of the text come before it.</summary>
    /// <param name="x">the distance from the left of the page</param>
    /// <param name="y">the distance from the top</param>
    /// <returns>the element and the offset, or null when no text is there to find</returns>
    public (ScriptNode Element, int Offset)? GetCaretAt(double x, double y)
    {
        if (CurrentProbe() is not { } probe || probe.ProbeCaretAt(x, y) is not { } caret)
            return null;

        var elements = ElementsInOrder();
        return caret.Ordinal >= 0 && caret.Ordinal < elements.Count ? (elements[caret.Ordinal], caret.Offset) : null;
    }

    /// <summary>The computed value of a CSS property of an element in the document as it is now, or null when there is no layout to read.</summary>
    /// <param name="element">the element</param>
    /// <param name="property">the property name, such as <c>display</c> or <c>background-color</c></param>
    /// <returns>the value, or null</returns>
    public string? GetComputedValue(ScriptNode element, string property)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(property);
        var ordinal = OrdinalOf(element);
        return ordinal < 0 || CurrentProbe() is not { } probe ? null : probe.ProbeComputedValue(ordinal, property);
    }

    internal void ReleaseProbe()
    {
        foreach (var (_, document) in _renderProbes)
            document.Dispose();

        _renderProbes.Clear();
        _renderProbe = null;
        foreach (var (_, document) in _probes)
            document.Dispose();

        _probes.Clear();
        _probe = null;
    }

    internal DocumentMode Mode { get; }

    /// <summary>The document node: the parent of the <c>html</c> element.</summary>
    public ScriptNode Root { get; }

    /// <summary>The global objects the host of the document asked for (<see cref="HtmlDocumentOptions.ScriptApis"/>); a script host installs them before the scripts run.</summary>
    public IReadOnlyList<ScriptApi> Apis { get; internal set; } = [];

    /// <summary>
    /// Set by the host from <see cref="IScriptHost.RunAsync"/> to keep the document live after the scripts have run, so it is told when the document has
    /// been laid out (<see cref="IScriptHost.LayoutCompletedAsync"/>) and can change it again. A host that does not need that leaves it false and the
    /// tree is released as soon as the scripts are done.
    /// </summary>
    public bool KeepAlive { get; set; }

    /// <summary>Whatever the host wants to keep for this document between its calls (the host's script engine, say). The engine never reads it.</summary>
    public object? HostState { get; set; }

    /// <summary>How many changes have been made to the tree so far. A host compares it to find out whether anything changed.</summary>
    public int Version { get; private set; }

    internal void NoteChanged() => Version++;

    /// <summary>Raised for each change a script makes to the tree: the node and what changed. Nothing is built unless something listens.</summary>
    public event Action<ScriptMutation>? Mutated;

    internal int SuppressMutations { get; set; }

    internal void RaiseMutation(ScriptMutation mutation)
    {
        if (Mutated is { } handler && SuppressMutations == 0)
            handler(mutation);
    }

    internal ScriptNode Wrap(CssBox box)
    {
        if (!_nodes.TryGetValue(box, out var node))
            _nodes[box] = node = new ScriptNode(this, box);

        return node;
    }

    /// <summary>The <c>html</c> element, or null for a document that has none.</summary>
    public ScriptNode? DocumentElement => Root.ChildElements.FirstOrDefault();

    /// <summary>The <c>head</c> element, or null.</summary>
    public ScriptNode? Head => DocumentElement is { } html && IsNamed(html, "html") ? html.ChildElements.FirstOrDefault(e => IsNamed(e, "head")) : null;

    /// <summary>The <c>body</c> element, or null.</summary>
    public ScriptNode? Body => DocumentElement is { } html && IsNamed(html, "html") ? html.ChildElements.FirstOrDefault(e => IsNamed(e, "body") || IsNamed(e, "frameset")) : null;

    private static bool IsNamed(ScriptNode node, string name) => string.Equals(node.TagName, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the document is in quirks mode, limited-quirks mode or neither.</summary>
    public string CompatMode => Mode == DocumentMode.Quirks ? "BackCompat" : "CSS1Compat";

    /// <summary>The address the document's relative references resolve against, or null when it has none.</summary>
    public Uri? BaseUri => _container.DocumentBaseUri;

    /// <summary>The text of the document's <c>title</c> element, with white space collapsed.</summary>
    public string Title
    {
        get
        {
            var title = Descendants(Root).FirstOrDefault(e => IsNamed(e, "title"));
            return title is null ? string.Empty : string.Join(' ', title.TextContent.Split([' ', '\t', '\r', '\n', '\f'], StringSplitOptions.RemoveEmptyEntries));
        }
    }

    /// <summary>The element with the given <c>id</c>, or null. When several have it, the first in document order.</summary>
    /// <param name="id">the id</param>
    /// <returns>the element, or null</returns>
    public ScriptNode? GetElementById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.Length == 0 ? null : Descendants(Root).FirstOrDefault(e => e.GetAttribute("id") == id);
    }

    /// <summary>
    /// The SVG <c>filter</c> element with the given <c>id</c>, built to be applied to pixels, or null when there is none (or the element of that id is not a filter in an inline
    /// <c>svg</c>). The filter is read from the markup of the outermost <c>svg</c> it is in, with that element's own style rules.
    /// </summary>
    /// <param name="id">the id</param>
    /// <returns>the filter, or null</returns>
    public ScriptSvgFilter? GetSvgFilter(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (GetElementById(id) is not { } element || !string.Equals(element.TagName, "filter", StringComparison.OrdinalIgnoreCase))
            return null;

        ScriptNode? svg = null;
        for (var node = element; node is not null; node = node.Parent)
        {
            if (string.Equals(node.TagName, "svg", StringComparison.OrdinalIgnoreCase))
                svg = node;
        }

        if (svg is null)
            return null;

        try
        {
            var root = SvgElementOf(svg);
            // The page's own style rules apply to the filter's elements too (a class on a primitive), as well as the ones inside the svg.
            var pageStyles = string.Join('\n', Descendants(Root).Where(e => string.Equals(e.TagName, "style", StringComparison.OrdinalIgnoreCase)).Select(e => e.TextContent));
            var cssData = Svg.SvgCssStyling.BuildStyleData(pageStyles + "\n" + Svg.SvgCssStyling.CollectStyleText(root), _container.RenderContext);
            var document = Svg.SvgTreeBuilder.Build(new Svg.XElementSvgSourceNode(root, root, cssData, "print", null), _container.RenderContext);
            return document.Filters.TryGetValue(id, out var filter) ? new ScriptSvgFilter(filter) : null;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // An SVG element of the page's tree as a standalone XML element (the page's HTML serialization does not close an empty foreign element, so it cannot be parsed back as XML).
    private static System.Xml.Linq.XElement SvgElementOf(ScriptNode node)
    {
        System.Xml.Linq.XNamespace svg = "http://www.w3.org/2000/svg";
        var element = new System.Xml.Linq.XElement(svg + node.TagName!);
        foreach (var name in node.AttributeNames)
        {
            // Namespace declarations and prefixed attributes (xlink:href) are not carried: a filter does not need them.
            if (name.Contains(':', StringComparison.Ordinal) || name.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase))
                continue;

            element.SetAttributeValue(name, node.GetAttribute(name) ?? "");
        }

        foreach (var child in node.Children)
        {
            if (child.Kind == ScriptNodeKind.Element && child.TagName is not null)
                element.Add(SvgElementOf(child));
            else if (child.Kind == ScriptNodeKind.Text && child.Data is { } text)
                element.Add(new System.Xml.Linq.XText(text));
        }

        return element;
    }

    private bool _fontFacesLoaded;

    /// <summary>Whether <see cref="LoadFontFacesAsync"/> has finished.</summary>
    public bool FontFacesLoaded => _fontFacesLoaded;

    /// <summary>
    /// Registers the <c>@font-face</c> fonts of the page's style sheets (its <c>style</c> elements and linked sheets) with the render context, so a script (a canvas's text, <c>document.fonts</c>)
    /// can use them before the page is laid out, which is when they are otherwise registered. Done once per document.
    /// </summary>
    public async ValueTask LoadFontFacesAsync()
    {
        if (_fontFacesLoaded)
            return;

        _fontFacesLoaded = true;
        try
        {
            var renderContext = _container.RenderContext;
            var css = new Html.CssData(renderContext);
            var parser = new Html.Parse.CssParser(renderContext, _container);
            foreach (var element in Descendants(Root).ToList())
            {
                if (string.Equals(element.TagName, "style", StringComparison.OrdinalIgnoreCase))
                {
                    await parser.ParseStyleSheet(css, element.TextContent, BaseUri);
                }
                else if (string.Equals(element.TagName, "link", StringComparison.OrdinalIgnoreCase) && element.GetAttribute("href") is { Length: > 0 } href
                    && (element.GetAttribute("rel") ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(token => token.Equals("stylesheet", StringComparison.OrdinalIgnoreCase)))
                {
                    if (await LoadTextAsync(href) is { } sheet)
                        await parser.ParseStyleSheet(css, sheet, CommonUtils.ResolveAgainstDocumentBase(_container, href));
                }
            }

            await Html.Parse.DomParser.CascadeApplyStyleFonts(css, renderContext, _container.SynchronousOnly);
        }
        catch (Exception ex) when (ex is not SynchronousOnlyViolationException and not OutOfMemoryException)
        {
            // A font that cannot be loaded is the page's own business; the page lays out with what it has.
        }
    }

    /// <summary>Whether text is a CSS <c>src</c> list a font face can be loaded from (<c>url(...)</c> or <c>local(...)</c>, with their formats).</summary>
    /// <param name="source">the text</param>
    public bool IsFontSource(string source) => Html.Parse.CssValueParser.GetFontFacePropertyValue(source).Any(c => c.Url is not null || c.Local is not null);

    /// <summary>Loads a font a script made (<c>new FontFace(...)</c>) and registers it, so the page's text and canvases can use it.</summary>
    /// <param name="family">the family name the font is known by</param>
    /// <param name="source">the CSS <c>src</c> (<c>url(...)</c>, <c>local(...)</c>), or null when <paramref name="data"/> is the font</param>
    /// <param name="data">the font file's bytes, or null</param>
    /// <param name="weight">the <c>font-weight</c> descriptor, or null</param>
    /// <param name="style">the <c>font-style</c> descriptor, or null</param>
    /// <param name="stretch">the <c>font-stretch</c> descriptor, or null</param>
    /// <param name="unicodeRange">the <c>unicode-range</c> descriptor, or null</param>
    /// <returns>whether a font was loaded</returns>
    public async ValueTask<bool> LoadFontFaceAsync(string family, string? source, byte[]? data, string? weight, string? style, string? stretch, string? unicodeRange)
    {
        try
        {
            var context = _container.RenderContext;
            if (data is not null)
            {
                // The bytes go in as a data address, which the context reads like any other source.
                return await context.AddFontFamilyFromUrl(family, "data:font/ttf;base64," + Convert.ToBase64String(data), null, BaseUri,
                    Html.Utils.FontFaceDescriptorResolver.Resolve(weight, style, stretch), Html.Utils.UnicodeRangeParser.Parse(unicodeRange));
            }

            var descriptors = Html.Utils.FontFaceDescriptorResolver.Resolve(weight, style, stretch);
            var ranges = Html.Utils.UnicodeRangeParser.Parse(unicodeRange);
            foreach (var candidate in Html.Parse.CssValueParser.GetFontFacePropertyValue(source ?? ""))
            {
                var loaded = false;
                if (candidate.Local is not null)
                    loaded = await context.AddLocalFontFamily(family, candidate.Local, descriptors, ranges);

                if (!loaded && candidate.Url is not null)
                    loaded = await context.AddFontFamilyFromUrl(family, candidate.Url, candidate.Format, BaseUri, descriptors, ranges);

                if (loaded)
                    return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is not SynchronousOnlyViolationException and not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>The elements with the given name, in document order. <c>*</c> names every element.</summary>
    /// <param name="tagName">the element name, matched without regard to case</param>
    /// <returns>the elements</returns>
    public IReadOnlyList<ScriptNode> GetElementsByTagName(string tagName)
    {
        ArgumentNullException.ThrowIfNull(tagName);
        return [.. Descendants(Root).Where(e => tagName == "*" || IsNamed(e, tagName))];
    }

    /// <summary>The elements that have all of the given classes (separated by white space), in document order.</summary>
    /// <param name="classNames">the classes</param>
    /// <returns>the elements</returns>
    public IReadOnlyList<ScriptNode> GetElementsByClassName(string classNames)
    {
        ArgumentNullException.ThrowIfNull(classNames);
        var wanted = classNames.Split([' ', '\t', '\r', '\n', '\f'], StringSplitOptions.RemoveEmptyEntries);
        return wanted.Length == 0 ? [] : [.. Descendants(Root).Where(e => wanted.All(w => e.ClassNames.Contains(w)))];
    }

    /// <summary>The first element in the document that the CSS selector matches, or null.</summary>
    /// <param name="selector">a selector list</param>
    /// <returns>the element, or null</returns>
    public ScriptNode? QuerySelector(string selector) => Root.QuerySelector(selector);

    /// <summary>The elements in the document that the CSS selector matches, in document order.</summary>
    /// <param name="selector">a selector list</param>
    /// <returns>the elements</returns>
    public IReadOnlyList<ScriptNode> QuerySelectorAll(string selector) => Root.QuerySelectorAll(selector);

    /// <summary>The <c>script</c> elements in the tree, in document order.</summary>
    public IReadOnlyList<ScriptNode> Scripts => GetElementsByTagName("script");

    /// <summary>Makes an element that is not in the tree.</summary>
    /// <param name="tagName">the element name</param>
    /// <returns>the element</returns>
    public ScriptNode CreateElement(string tagName)
    {
        ArgumentException.ThrowIfNullOrEmpty(tagName);

        var name = IsHtml ? tagName.ToLowerInvariant() : tagName;
        return Wrap(CssBox.CreateBox(new HtmlTag(name, HtmlUtils.IsSingleTag(name.ToLowerInvariant()))));
    }

    /// <summary>The document type node among the document's children, or null.</summary>
    public ScriptNode? DocumentType => Root.Children.FirstOrDefault(c => c.Kind == ScriptNodeKind.DocumentType);

    /// <summary>Makes a comment that is not in the tree.</summary>
    /// <param name="data">the comment's text</param>
    /// <returns>the node</returns>
    public ScriptNode CreateComment(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Wrap(SpecialNode.Create(new SpecialNode(ScriptNodeKind.Comment, "#comment", data)));
    }

    /// <summary>Makes a processing instruction that is not in the tree.</summary>
    /// <param name="target">the target</param>
    /// <param name="data">the data</param>
    /// <returns>the node</returns>
    public ScriptNode CreateProcessingInstruction(string target, string data)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(data);
        return Wrap(SpecialNode.Create(new SpecialNode(ScriptNodeKind.ProcessingInstruction, target, data)));
    }

    /// <summary>Makes a document type that is not in the tree.</summary>
    /// <param name="name">the name</param>
    /// <param name="publicId">the public identifier</param>
    /// <param name="systemId">the system identifier</param>
    /// <returns>the node</returns>
    public ScriptNode CreateDocumentType(string name, string publicId, string systemId)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Wrap(SpecialNode.Create(new SpecialNode(ScriptNodeKind.DocumentType, name, string.Empty, publicId ?? string.Empty, systemId ?? string.Empty)));
    }

    /// <summary>Makes a CDATA section (a text node of an XML document) that is not in the tree.</summary>
    /// <param name="data">the text</param>
    /// <returns>the node</returns>
    public ScriptNode CreateCDataSection(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var box = new CssBox(null, null) { Text = data };
        SpecialNode.MarkCData(box);
        return Wrap(box);
    }

    /// <summary>Makes a text node that is not in the tree.</summary>
    /// <param name="text">the text</param>
    /// <returns>the node</returns>
    public ScriptNode CreateTextNode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Wrap(new CssBox(null, null) { Text = text });
    }

    /// <summary>
    /// Makes a node for each child of the HTML <paramref name="html"/> and puts them in a holder that is not in the tree (what a document
    /// fragment is for). The holder's children are the nodes.
    /// </summary>
    /// <param name="html">the markup</param>
    /// <returns>the holder</returns>
    public ScriptNode CreateFragment(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var holder = Html.Parse.HtmlParser.ParseDocument(html, out _, null, document: false, scripting: true);
        return Wrap(holder);
    }

    /// <summary>Makes an empty fragment: a holder that is not in the tree, whose children replace it when it is inserted.</summary>
    /// <returns>the fragment</returns>
    public ScriptNode CreateEmptyFragment() => Wrap(new CssBox(null, null));

    /// <summary>The address a script's relative request addresses resolve against: the document's base.</summary>
    public Uri? BaseAddress => BaseUri;

    /// <summary>Sends a request a script made (<c>XMLHttpRequest</c>, <c>fetch</c>) through the document's <see cref="HtmlDocumentOptions.ScriptNetwork"/>, or its resource loader.</summary>
    /// <param name="request">the request</param>
    /// <param name="cancellationToken">cancels the work</param>
    /// <returns>the answer, or null for a network error</returns>
    public ValueTask<ScriptNetworkResponse?> SendAsync(ScriptNetworkRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IScriptNetwork network = _container.ScriptNetwork ?? new LoaderScriptNetwork(_container);
        return network.SendAsync(request, cancellationToken);
    }

    private readonly Dictionary<ScriptNode, ScriptCanvasPainting> _canvases = new(ReferenceEqualityComparer.Instance as IEqualityComparer<ScriptNode>);
    private readonly Dictionary<string, PeachDrawing.Core.Image> _loadedImages = [];
    private readonly Dictionary<string, Svg.SvgDocument> _loadedSvgs = [];

    /// <summary>Sets what a <c>canvas</c> element shows: a drawing a script made, replacing any earlier one. Null clears it.</summary>
    /// <param name="canvas">the <c>canvas</c> element</param>
    /// <param name="painting">the drawing, or null</param>
    public void SetCanvasPainting(ScriptNode canvas, ScriptCanvasPainting? painting)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (painting is null)
            _canvases.Remove(canvas);
        else
            _canvases[canvas] = painting;

        // A picture that holds this canvas shows what it showed.
        _picturesVersion = -1;
    }

    // The drawings of the canvas elements still in the tree, by the position the layout knows each element by.
    internal void PublishCanvasPaintings(Dictionary<int, ScriptCanvasPainting> target)
    {
        // What an earlier run of the scripts drew is replaced; release what it held unless it is still in use.
        var earlier = target.Values.ToList();
        target.Clear();
        try
        {
            PublishInto(target);
        }
        finally
        {
            foreach (var painting in earlier)
            {
                if (painting.Owner is { } owner && !target.Values.Any(p => ReferenceEquals(p.Owner, owner)))
                    owner.Dispose();
            }
        }
    }

    private void PublishInto(Dictionary<int, ScriptCanvasPainting> target)
    {
        if (_canvases.Count == 0)
            return;

        var index = 0;
        foreach (var element in Descendants(Root))
        {
            if (_canvases.TryGetValue(element, out var painting))
                target[index] = painting;

            index++;
        }
    }

    /// <summary>Reads a CSS color (<c>#f00</c>, <c>rgb(...)</c>, a name, <c>transparent</c>...) as a script's style strings spell it.</summary>
    /// <param name="css">the text</param>
    /// <param name="color">the color</param>
    /// <returns>whether the text is a color</returns>
    public bool TryParseColor(string css, out PeachDrawing.Core.PaintColor color) => TryParseColor(css, null, out color);

    /// <summary>
    /// Reads a CSS color as the HTML standard says a canvas does: <c>currentcolor</c> is the computed <c>color</c> of <paramref name="element"/> (the canvas) at this moment,
    /// and opaque black when there is no element, it is not in the document, or the host gave no layout to read it from.
    /// </summary>
    /// <param name="css">the text</param>
    /// <param name="element">the element whose <c>color</c> is <c>currentcolor</c>, or null for black</param>
    /// <param name="color">the color</param>
    /// <returns>whether the text is a color</returns>
    public bool TryParseColor(string css, ScriptNode? element, out PeachDrawing.Core.PaintColor color) => TryParseColor(css, element, out color, out _);

    /// <summary>
    /// <see cref="TryParseColor(string, ScriptNode?, out PeachDrawing.Core.PaintColor)"/> that also gives the color's serialization when it is not one of the legacy sRGB forms:
    /// <c>color(display-p3 0.96 0.76 0.79)</c>, <c>lab(29 39 20)</c>, the result of <c>color-mix()</c> or relative color syntax, as the HTML standard has a canvas read them back.
    /// </summary>
    /// <param name="css">the text</param>
    /// <param name="element">the element whose <c>color</c> is <c>currentcolor</c>, or null for black</param>
    /// <param name="color">the color, reduced to 8-bit sRGB (what is painted)</param>
    /// <param name="serialized">the serialization, or null when <paramref name="color"/> is the color (a name, hex, <c>rgb()</c>, <c>hsl()</c>, <c>hwb()</c>)</param>
    /// <returns>whether the text is a color</returns>
    public bool TryParseColor(string css, ScriptNode? element, out PeachDrawing.Core.PaintColor color, out string? serialized)
    {
        serialized = null;
        ArgumentNullException.ThrowIfNull(css);
        var parser = new Html.Parse.CssValueParser(_container.RenderContext);
        var text = css.Trim();

        if (text.Contains("currentcolor", StringComparison.OrdinalIgnoreCase))
        {
            var current = PeachDrawing.Core.PaintColor.Black;
            if (element is not null && GetComputedValue(element, "color") is { } computed && parser.IsColorValid(computed))
                current = parser.GetActualColor(computed);

            text = Html.Parse.CssValueParser.SubstituteCurrentColor(text, current);
        }

        if (!parser.IsColorValid(text))
        {
            color = default;
            return false;
        }

        color = parser.GetActualColor(text);
        if (parser.TryGetAuthoredColor(text, out var authored))
            serialized = authored.ToCssString();

        return true;
    }

    /// <summary>
    /// The stops of a gradient with colors between them that make the gradient interpolate in Oklab, which is how a gradient with a color that is not legacy sRGB
    /// (<c>color(display-p3 ...)</c>, <c>lab()</c>, a <c>color-mix()</c>) goes from one stop to the next (CSS Color 4 section 12). Each segment gains fifteen colors; a hard
    /// stop (two stops at one offset) stays hard.
    /// </summary>
    /// <param name="stops">the stops in ascending offset, each an offset from 0 to 1 and a color</param>
    /// <returns>the stops and the colors between them</returns>
    public List<(double Offset, PeachDrawing.Core.PaintColor Color)> InterpolateGradientStops(IReadOnlyList<(double Offset, PeachDrawing.Core.PaintColor Color)> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var result = new List<(double Offset, PeachDrawing.Core.PaintColor Color)>(stops.Count * 16);
        for (var i = 0; i < stops.Count; i++)
        {
            if (i > 0 && stops[i].Offset > stops[i - 1].Offset)
            {
                var (from, to) = (stops[i - 1], stops[i]);
                for (var k = 1; k <= 15; k++)
                {
                    var t = k / 16.0;
                    result.Add((from.Offset + t * (to.Offset - from.Offset),
                        Html.Utils.ColorSpaceConverter.Interpolate(from.Color, to.Color, t, Html.Entities.GradientColorSpace.Oklab, Html.Entities.HueInterpolationMethod.Shorter)));
                }
            }

            result.Add(stops[i]);
        }

        return result;
    }

    /// <summary>
    /// Reads a number out of an attribute by the HTML rules for parsing non-negative integers: white space is skipped, a plus sign is allowed, the leading digits are the number and
    /// anything after them is ignored (<c>"100.999"</c> and <c>"100em"</c> are 100, <c>"0x100"</c> is 0).
    /// </summary>
    /// <param name="text">the attribute's value, or null for none</param>
    /// <param name="value">the number</param>
    /// <returns>false when there is no number to read</returns>
    public static bool TryParseNonNegativeInteger(string? text, out uint value) => Html.Utils.HtmlIntegerParser.TryParseNonNegative(text, out value);

    /// <summary>Finds a font by family name, as the document's render context knows it.</summary>
    /// <param name="family">one family name or a generic one</param>
    /// <param name="sizePixels">the size in CSS pixels</param>
    /// <param name="style">normal, italic or oblique</param>
    /// <param name="weight">the weight, 400 for normal and 700 for bold</param>
    /// <returns>the font, or null when there is none</returns>
    public PeachDrawing.Core.Font? GetFont(string family, double sizePixels, PeachDrawing.Core.PaintFontStyle style, double weight)
    {
        ArgumentNullException.ThrowIfNull(family);
        return _container.RenderContext.GetFont(family, sizePixels, style, weight);
    }

    /// <summary>The font to draw a character with that the font a script asked for lacks: one of the system's that has it.</summary>
    /// <param name="rune">the character</param>
    /// <param name="sizePixels">the size</param>
    /// <param name="style">the style of the font asked for</param>
    /// <param name="weight">the weight of the font asked for</param>
    /// <returns>the font, or null when none has the character</returns>
    public PeachDrawing.Core.Font? GetFallbackFont(System.Text.Rune rune, double sizePixels, PeachDrawing.Core.PaintFontStyle style, double weight) =>
        _container.RenderContext.GetSystemFallbackFontForCodepoint(sizePixels, style, rune, weight);

    /// <summary>Makes a small canvas text can be measured on (what the host gave as <see cref="HtmlDocumentOptions.MeasuringCanvasFactory"/>), or null when it gave none.</summary>
    /// <returns>the canvas, which the caller disposes</returns>
    public PeachDrawing.Core.Canvas? CreateMeasuringCanvas() => _container.MeasuringCanvasFactory?.Invoke();

    /// <summary>
    /// A bitmap of the host's backend for a canvas element to draw into (see <see cref="HtmlDocumentOptions.CanvasSurfaces"/>), or null when the host did not ask for
    /// them or its canvases have none to give. The caller owns it.
    /// </summary>
    /// <param name="widthPixels">the width, in pixels</param>
    /// <param name="heightPixels">the height, in pixels</param>
    public PeachDrawing.Core.OffscreenSurface? CreateCanvasSurface(int widthPixels, int heightPixels) => CreateCanvasSurface(widthPixels, heightPixels, wide: false);

    /// <summary>
    /// <see cref="CreateCanvasSurface(int, int)"/> for a canvas that keeps floating-point pixels (<paramref name="wide"/>). Such a canvas needs its surface to keep what it draws exactly, so
    /// it is made whether or not the host asked for surfaces; it is null when the host's canvases cannot make one, and the canvas then plays its recording on a floating-point raster canvas to read it.
    /// </summary>
    /// <param name="widthPixels">the width, in pixels</param>
    /// <param name="heightPixels">the height, in pixels</param>
    /// <param name="wide">whether the pixels are floating point</param>
    public PeachDrawing.Core.OffscreenSurface? CreateCanvasSurface(int widthPixels, int heightPixels, bool wide)
    {
        if (!wide && !_container.CanvasSurfaces)
            return null;

        using var measuring = CreateMeasuringCanvas();
        return measuring?.CreateOffscreenSurface(widthPixels, heightPixels, wide);
    }

    /// <summary>An image of floating-point pixels (straight RGBA floats, four per pixel), which a floating-point canvas draws as they are; null when the host's canvases cannot make one.</summary>
    /// <param name="widthPixels">the width, in pixels</param>
    /// <param name="heightPixels">the height, in pixels</param>
    /// <param name="straightRgba">the pixels</param>
    public PeachDrawing.Core.Image? CreateWideImage(int widthPixels, int heightPixels, System.ReadOnlySpan<float> straightRgba)
    {
        using var measuring = CreateMeasuringCanvas();
        return measuring?.CreateWideImage(widthPixels, heightPixels, straightRgba);
    }

    /// <summary>Decodes an encoded image (PNG, JPEG...) with the document's render context.</summary>
    /// <param name="bytes">the file's bytes</param>
    /// <returns>the image, or null when it cannot be decoded</returns>
    public PeachDrawing.Core.Image? DecodeImage(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return ImageColorSources.Decode(_container.RenderContext, bytes);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The colors an image means in a canvas's color space. A file that declares a space other than sRGB (a Display-P3 PNG) is decoded to its pixels as written; this reads the file again and converts from
    /// the space it declares.
    /// </summary>
    /// <param name="image">an image this document decoded</param>
    /// <param name="colorSpace">the canvas's space: <c>srgb</c>, <c>display-p3</c>, <c>srgb-linear</c> or <c>display-p3-linear</c></param>
    /// <param name="wide">whether to give straight floats (a float canvas) rather than eight-bit straight RGBA</param>
    /// <returns>the converted pixels, or null when the image's file declares no space other than sRGB</returns>
    public (int Width, int Height, byte[]? Bytes, float[]? Floats)? ConvertImageToColorSpace(PeachDrawing.Core.Image image, string colorSpace, bool wide)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(colorSpace);
        return ImageColorSources.Convert(image, colorSpace, wide);
    }

    /// <summary>
    /// Encodes a canvas's pixels the way <c>toBlob</c>, <c>toDataURL</c> and <c>convertToBlob</c> do: as the image type asked for when the document can write it, else as PNG. PNG keeps the canvas's color space
    /// (the file says which) and the precision a float canvas has; JPEG and WebP are written in the canvas's gamut with the sRGB transfer function, on opaque black.
    /// </summary>
    /// <param name="width">the width in pixels</param>
    /// <param name="height">the height in pixels</param>
    /// <param name="straightBytes">eight-bit straight RGBA, or null when <paramref name="straightFloats"/> is given</param>
    /// <param name="straightFloats">straight RGBA as floats (a float canvas), or null</param>
    /// <param name="colorSpace">the canvas's space: <c>srgb</c>, <c>display-p3</c>, <c>srgb-linear</c> or <c>display-p3-linear</c></param>
    /// <param name="mimeType">the image type asked for</param>
    /// <param name="quality">the quality from 0 to 1 for a type that has one, or null</param>
    /// <param name="writtenType">the image type that was written</param>
    /// <returns>the file's bytes, or null when the pixels cannot be encoded</returns>
    public byte[]? EncodeImage(int width, int height, byte[]? straightBytes, float[]? straightFloats, string colorSpace, string mimeType, double? quality, out string writtenType)
    {
        ArgumentNullException.ThrowIfNull(colorSpace);
        ArgumentNullException.ThrowIfNull(mimeType);
        writtenType = "image/png";
        return ImageColorSources.Encode(width, height, straightBytes, straightFloats, colorSpace, mimeType, quality, out writtenType);
    }

    /// <summary>The image turned upright by the orientation its file records (EXIF), or the image itself when the file records none.</summary>
    /// <param name="image">an image this document decoded</param>
    public PeachDrawing.Core.Image UprightImage(PeachDrawing.Core.Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return ImageColorSources.Upright(_container.RenderContext, image);
    }

    /// <summary>The image as its file stores it, before the orientation the file records is applied (or the image itself when none was).</summary>
    /// <param name="image">an image this document decoded</param>
    public PeachDrawing.Core.Image UnorientedImage(PeachDrawing.Core.Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return ImageColorSources.Unoriented(image);
    }

    /// <summary>Whether the document has a media provider, so that its <c>audio</c> and <c>video</c> elements can play.</summary>
    public bool HasMediaProvider => _container.MediaProvider is not null;

    /// <summary>What the document's media provider says of a media type, in the words of <c>HTMLMediaElement.canPlayType</c>; an empty string with no provider.</summary>
    /// <param name="mimeType">a media type, with <c>codecs</c> if the caller names them</param>
    public string CanPlayMediaType(string mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        try
        {
            return _container.MediaProvider?.CanPlayType(mimeType) ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A provider is the host's code: one that cannot answer plays nothing.
            return string.Empty;
        }
    }

    /// <summary>Opens a media resource with the document's media provider.</summary>
    /// <param name="address">the address as written, absolute or relative to the document</param>
    /// <param name="kind">whether an <c>audio</c> or a <c>video</c> element asks</param>
    /// <param name="contentType">the <c>type</c> of the <c>source</c> element that named the address, or null</param>
    /// <param name="crossOrigin">the element's <c>crossorigin</c> attribute, or null</param>
    /// <param name="muted">whether the element is muted</param>
    /// <param name="loop">whether the element loops</param>
    /// <param name="preload">the element's <c>preload</c></param>
    /// <returns>the session, or null when there is no provider or the resource cannot be played</returns>
    public async ValueTask<PeachBrackets.Media.IMediaSession?> OpenMediaAsync(string address, PeachBrackets.Media.MediaKind kind, string? contentType, string? crossOrigin, bool muted, bool loop, PeachBrackets.Media.MediaPreload preload)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (_container.MediaProvider is not { } provider)
            return null;

        var uri = CommonUtils.ResolveAgainstDocumentBase(_container, address.Trim());
        if (uri is not { IsAbsoluteUri: true })
            return null;

        var request = new PeachBrackets.Media.MediaRequest(uri, kind, _container.RenderContext, _ => _container.RenderContext.GetResourceStream(uri))
        {
            ContentType = contentType,
            CrossOrigin = crossOrigin,
            Muted = muted,
            Loop = loop,
            Preload = preload,
        };

        try
        {
            return await provider.OpenAsync(request);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Whether the image was decoded from a file that declares a color space other than sRGB, so that <see cref="ConvertImageToColorSpace"/> has something to convert from.</summary>
    /// <param name="image">an image this document decoded</param>
    public bool DeclaresColorSpace(PeachDrawing.Core.Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return ImageColorSources.Has(image);
    }

    /// <summary>The decoded image an earlier <see cref="LoadImageAsync"/> of this address made, or null.</summary>
    /// <param name="address">the address as it was given to <see cref="LoadImageAsync"/></param>
    /// <returns>the image, or null</returns>
    public PeachDrawing.Core.Image? GetLoadedImage(string address) => _loadedImages.GetValueOrDefault(address);

    /// <summary>Whether the image loaded from <paramref name="address"/> is an SVG document, which is drawn as vector content with <see cref="DrawSvgImage"/> rather than as an <see cref="PeachDrawing.Core.Image"/>.</summary>
    /// <param name="address">the address as it was given to <see cref="LoadImageAsync"/></param>
    public bool IsSvgImage(string address) => _loadedSvgs.ContainsKey(address);

    /// <summary>Draws the SVG image loaded from <paramref name="address"/> onto <paramref name="canvas"/>, its whole viewport mapped onto <paramref name="viewport"/>.</summary>
    /// <param name="canvas">where to draw</param>
    /// <param name="address">the address as it was given to <see cref="LoadImageAsync"/></param>
    /// <param name="viewport">where the image's whole width and height land, in the canvas's units</param>
    /// <returns>false when no SVG image was loaded from that address</returns>
    public bool DrawSvgImage(PeachDrawing.Core.Canvas canvas, string address, PeachDrawing.Core.Rect viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!_loadedSvgs.TryGetValue(address, out var svg))
            return false;

        Svg.SvgRenderer.RenderInto(canvas, svg, viewport);
        return true;
    }

    /// <summary>Loads an image a script asked for (<c>new Image()</c>, an <c>img</c> element's <c>src</c>) to find out whether it can be decoded and how large it is.</summary>
    /// <param name="address">the address as written, absolute or relative</param>
    /// <returns>the image's size and address, or null when it could not be loaded or decoded</returns>
    public async ValueTask<ScriptImageInfo?> LoadImageAsync(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        try
        {
            // A data: address carries its bytes; everything else goes through the document's resource loader.
            if (!address.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase) && DataUriUtils.TryDecodeDataUri(address, out _, out var bytes))
            {
                var decoded = ImageColorSources.Upright(_container.RenderContext, ImageColorSources.Decode(_container.RenderContext, bytes));
                _loadedImages[address] = decoded;
                return new ScriptImageInfo(decoded.Width, decoded.Height, address);
            }

            using var handler = new ImageLoadHandler(_container);
            await handler.LoadImage(address);
            var resolved = CommonUtils.ResolveAgainstDocumentBase(_container, address);
            var url = resolved?.IsAbsoluteUri == true ? resolved.AbsoluteUri : address;
            if (handler.Image is { } loaded)
            {
                var image = ImageColorSources.Upright(_container.RenderContext, loaded);
                _loadedImages[address] = image;
                return new ScriptImageInfo(image.Width, image.Height, url);
            }

            if (handler.SvgDocument is { } svg)
            {
                _loadedSvgs[address] = svg;
                var width = svg.Width ?? svg.ViewBox?.Width ?? 300;
                var height = svg.Height ?? svg.ViewBox?.Height ?? 150;
                return new ScriptImageInfo(width, height, url);
            }

            return null;
        }
        catch (Exception ex) when (ex is not SynchronousOnlyViolationException and not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Reads the text of a resource: an external script. It resolves against <see cref="BaseUri"/>.</summary>
    /// <param name="address">the address as written, absolute or relative</param>
    /// <returns>the text, or null when it could not be read</returns>
    public async ValueTask<string?> LoadTextAsync(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        try
        {
            var uri = CommonUtils.ResolveAgainstDocumentBase(_container, address);
            if (uri is not { IsAbsoluteUri: true })
                return null;

            var response = await AsyncGuard.Require(_container.RenderContext.GetResourceStream(uri), _container.SynchronousOnly, "load the script", uri.AbsoluteUri);
            if (response?.ResourceStream is not { } stream)
                return null;

            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex) when (ex is not SynchronousOnlyViolationException)
        {
            return null;
        }
    }

    /// <summary>The position of an element among the document's elements in document order, which is the number the layout knows it by; -1 when it is not in the tree.</summary>
    /// <param name="element">the element</param>
    /// <returns>the position</returns>
    public int OrdinalOf(ScriptNode element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var index = 0;
        foreach (var candidate in Descendants(Root))
        {
            if (ReferenceEquals(candidate, element))
                return index;

            index++;
        }

        return -1;
    }

    /// <summary>The elements of the document in source order, the position of each being its <see cref="OrdinalOf"/>.</summary>
    internal List<ScriptNode> ElementsInOrder() => [.. Descendants(Root)];

    private IEnumerable<ScriptNode> Descendants(ScriptNode node)
    {
        foreach (var child in node.Box.Boxes.ToArray())
        {
            if (child.HtmlTag is null)
                continue;

            var wrapped = Wrap(child);
            yield return wrapped;

            foreach (var inner in Descendants(wrapped))
                yield return inner;
        }
    }

    // The document as markup: the DOCTYPE that gives its mode, and the tree.
    internal string Serialize() => ScriptSerializer.SerializeChildren(RootBox);
}
