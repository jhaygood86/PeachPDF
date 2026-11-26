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

    private HtmlDocument? _probe;
    private int _probeVersion = -1;

    // The document as it is now, laid out: loaded again from the tree when the tree has changed since.
    private HtmlDocument? CurrentProbe()
    {
        if (Probe is null)
            return null;

        if (_probe is null || _probeVersion != Version)
        {
            _probe?.Dispose();
            _probe = null;
            try
            {
                // The scripts run inside a load that is already asynchronous; the probe's own load is of an in-memory string.
                _probe = Probe(Serialize()).AsTask().GetAwaiter().GetResult();
            }
            catch (HtmlRenderException)
            {
                _probe = null;
            }

            _probeVersion = Version;
        }

        return _probe;
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
        _probe?.Dispose();
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
    }

    // The drawings of the canvas elements still in the tree, by the position the layout knows each element by.
    internal void PublishCanvasPaintings(Dictionary<int, ScriptCanvasPainting> target)
    {
        target.Clear();
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
    public bool TryParseColor(string css, out PeachDrawing.Core.PaintColor color)
    {
        ArgumentNullException.ThrowIfNull(css);
        var parser = new Html.Parse.CssValueParser(_container.RenderContext);
        var text = css.Trim();
        if (!parser.IsColorValid(text))
        {
            color = default;
            return false;
        }

        color = parser.GetActualColor(text);
        return true;
    }

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

    /// <summary>Makes a small canvas text can be measured on (what the host gave as <see cref="HtmlDocumentOptions.MeasuringCanvasFactory"/>), or null when it gave none.</summary>
    /// <returns>the canvas, which the caller disposes</returns>
    public PeachDrawing.Core.Canvas? CreateMeasuringCanvas() => _container.MeasuringCanvasFactory?.Invoke();

    /// <summary>Decodes an encoded image (PNG, JPEG...) with the document's render context.</summary>
    /// <param name="bytes">the file's bytes</param>
    /// <returns>the image, or null when it cannot be decoded</returns>
    public PeachDrawing.Core.Image? DecodeImage(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var stream = new MemoryStream(bytes);
            return _container.RenderContext.ImageFromStream(stream);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The decoded image an earlier <see cref="LoadImageAsync"/> of this address made, or null.</summary>
    /// <param name="address">the address as it was given to <see cref="LoadImageAsync"/></param>
    /// <returns>the image, or null</returns>
    public PeachDrawing.Core.Image? GetLoadedImage(string address) => _loadedImages.GetValueOrDefault(address);

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
                using var stream = new MemoryStream(bytes);
                var decoded = _container.RenderContext.ImageFromStream(stream);
                _loadedImages[address] = decoded;
                return new ScriptImageInfo(decoded.Width, decoded.Height, address);
            }

            using var handler = new ImageLoadHandler(_container);
            await handler.LoadImage(address);
            var resolved = CommonUtils.ResolveAgainstDocumentBase(_container, address);
            var url = resolved?.IsAbsoluteUri == true ? resolved.AbsoluteUri : address;
            if (handler.Image is { } image)
            {
                _loadedImages[address] = image;
                return new ScriptImageInfo(image.Width, image.Height, url);
            }

            if (handler.SvgDocument is { } svg)
            {
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
