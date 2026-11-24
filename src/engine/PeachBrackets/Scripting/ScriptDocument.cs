using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Utils;
using PeachPDF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    internal ScriptDocument(HtmlContainerInt container, CssBox root, DocumentMode mode)
    {
        _container = container;
        RootBox = root;
        Mode = mode;
        Root = Wrap(root);
    }

    internal CssBox RootBox { get; }

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

    internal ScriptNode Wrap(CssBox box)
    {
        if (!_nodes.TryGetValue(box, out var node))
            _nodes[box] = node = new ScriptNode(this, box);

        return node;
    }

    /// <summary>The <c>html</c> element, or null for a document that has none.</summary>
    public ScriptNode? DocumentElement => Root.ChildElements.FirstOrDefault(e => IsNamed(e, "html"));

    /// <summary>The <c>head</c> element, or null.</summary>
    public ScriptNode? Head => DocumentElement?.ChildElements.FirstOrDefault(e => IsNamed(e, "head"));

    /// <summary>The <c>body</c> element, or null.</summary>
    public ScriptNode? Body => DocumentElement?.ChildElements.FirstOrDefault(e => IsNamed(e, "body") || IsNamed(e, "frameset"));

    private static bool IsNamed(ScriptNode node, string name) => string.Equals(node.TagName, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the document is in quirks mode, limited-quirks mode or neither.</summary>
    public string CompatMode => Mode == DocumentMode.NoQuirks ? "CSS1Compat" : "BackCompat";

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

        var name = tagName.ToLowerInvariant();
        return Wrap(CssBox.CreateBox(new HtmlTag(name, HtmlUtils.IsSingleTag(name))));
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
    internal string Serialize() => ScriptSerializer.DocType(Mode) + ScriptSerializer.SerializeChildren(RootBox);
}
