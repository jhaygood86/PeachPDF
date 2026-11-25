using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Native.Symbol;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

/// <summary>An attribute as a node: <c>Attr</c>. It reads and writes the element's attribute while it is on the element, and holds its own value when it is not.</summary>
internal sealed class AttrObject : ObjectInstance
{
    public AttrObject(Engine engine, ObjectInstance prototype, ScriptDocument document, ElementNamespace name) : base(engine)
    {
        Prototype = prototype;
        Document = document;
        Name = name;
    }

    public ScriptDocument Document { get; set; }

    public ElementNamespace Name { get; }

    public NodeObject? Owner { get; set; }

    public string DetachedValue { get; set; } = string.Empty;

    /// <summary>The qualified name: the prefix and the local name.</summary>
    public string QualifiedName => Name.Prefix is null ? Name.LocalName : Name.Prefix + ":" + Name.LocalName;

    public string Value
    {
        get => Owner is { } owner ? owner.Node.GetAttribute(QualifiedName) ?? DetachedValue : DetachedValue;
        set
        {
            if (Owner is { } owner)
                owner.Node.SetAttribute(QualifiedName, value);
            else
                DetachedValue = value;
        }
    }
}

/// <summary>An element's <c>attributes</c>: its attributes as <c>Attr</c> nodes, in order, with indexed access.</summary>
internal sealed class AttributeMapObject : ObjectInstance
{
    private readonly ScriptRuntime _runtime;

    public AttributeMapObject(ScriptRuntime runtime, ObjectInstance prototype, NodeObject owner) : base(runtime.Engine)
    {
        _runtime = runtime;
        Prototype = prototype;
        Owner = owner;
    }

    public NodeObject Owner { get; }

    public override JsValue Get(JsValue property, JsValue receiver)
    {
        if (property.IsString() && uint.TryParse(property.AsString(), out var index))
        {
            var names = Owner.Node.AttributeNames;
            return index < names.Count ? _runtime.AttrFor(Owner, names[(int)index]) : JsValue.Undefined;
        }

        return base.Get(property, receiver);
    }

    public override List<JsValue> GetOwnPropertyKeys(Types types = Types.String | Types.Symbol)
    {
        var keys = new List<JsValue>();
        if ((types & Types.String) != 0)
        {
            for (var i = 0; i < Owner.Node.AttributeNames.Count; i++)
                keys.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        keys.AddRange(base.GetOwnPropertyKeys(types));
        return keys;
    }

    public override PropertyDescriptor GetOwnProperty(JsValue property)
    {
        if (property.IsString() && uint.TryParse(property.AsString(), out var index) && index < Owner.Node.AttributeNames.Count)
            return new PropertyDescriptor(_runtime.AttrFor(Owner, Owner.Node.AttributeNames[(int)index]), PropertyFlag.Enumerable | PropertyFlag.Configurable);

        return base.GetOwnProperty(property);
    }
}

internal sealed partial class ScriptRuntime
{
    private ObjectInstance AttrPrototype = null!;
    private ObjectInstance AttributeMapPrototype = null!;

    // The namespace, prefix and local name of a qualified name once it is checked: what createElementNS and setAttributeNS share.
    private (string? Namespace, string? Prefix, string Local) ValidateAndExtract(JsValue namespaceArg, string qualifiedName)
    {
        var ns = Js.IsNullish(namespaceArg) ? null : Js.AsText(namespaceArg);
        if (ns is { Length: 0 })
            ns = null;

        if (!XmlNames.IsName(qualifiedName))
            throw DomError("InvalidCharacterError", $"'{qualifiedName}' is not a valid name.");
        if (!XmlNames.IsQName(qualifiedName))
            throw DomError("InvalidCharacterError", $"'{qualifiedName}' is not a qualified name.");

        string? prefix = null;
        var local = qualifiedName;
        if (qualifiedName.IndexOf(':') is var colon and >= 0)
        {
            prefix = qualifiedName[..colon];
            local = qualifiedName[(colon + 1)..];
        }

        if (prefix is not null && ns is null)
            throw DomError("NamespaceError", "A prefix needs a namespace.");
        if (prefix == "xml" && ns != XmlNamespace)
            throw DomError("NamespaceError", "The xml prefix belongs to the XML namespace.");
        if ((qualifiedName == "xmlns" || prefix == "xmlns") && ns != XmlnsNamespace)
            throw DomError("NamespaceError", "The xmlns name belongs to the XMLNS namespace.");
        if (ns == XmlnsNamespace && qualifiedName != "xmlns" && prefix != "xmlns")
            throw DomError("NamespaceError", "The XMLNS namespace is for the xmlns name.");

        return (ns, prefix, local);
    }

    private static bool IsHtmlElement(NodeObject element) =>
        element.Node.OwnerDocument.IsHtml && (element.NamespaceInfo is null || element.NamespaceInfo.Namespace == HtmlNamespace);

    private static ElementNamespace AttributeName(NodeObject element, string name) =>
        element.AttributeNames.TryGetValue(name, out var known) ? known : new ElementNamespace(null, null, name);

    // The name an attribute with this name has on the element, or null: HTML elements in HTML documents match a lower-cased name.
    private static string? FindAttribute(NodeObject element, string qualifiedName)
    {
        var wanted = IsHtmlElement(element) ? qualifiedName.ToLowerInvariant() : qualifiedName;
        foreach (var name in element.Node.AttributeNames)
        {
            if (string.Equals(name, wanted, StringComparison.Ordinal))
                return name;
        }

        return null;
    }

    private static string? FindAttributeNs(NodeObject element, string? ns, string local)
    {
        foreach (var name in element.Node.AttributeNames)
        {
            var info = AttributeName(element, name);
            if (info.Namespace == ns && info.LocalName == local)
                return name;
        }

        return null;
    }

    internal AttrObject AttrFor(NodeObject element, string name)
    {
        if (!element.AttrNodes.TryGetValue(name, out var attr))
        {
            attr = new AttrObject(Engine, AttrPrototype, element.Node.OwnerDocument, AttributeName(element, name)) { Owner = element };
            element.AttrNodes[name] = attr;
        }

        return attr;
    }

    private void RemoveAttributeNamed(NodeObject element, string name)
    {
        if (element.AttrNodes.Remove(name, out var attr))
        {
            attr.DetachedValue = element.Node.GetAttribute(name) ?? string.Empty;
            attr.Owner = null;
        }

        element.AttributeNames.Remove(name);
        element.Node.RemoveAttribute(name);
    }

    private void SetAttributeValue(NodeObject element, ElementNamespace name, string value)
    {
        var qualified = name.Prefix is null ? name.LocalName : name.Prefix + ":" + name.LocalName;
        if (name.Namespace is not null || name.Prefix is not null)
            element.AttributeNames[qualified] = name;

        element.Node.SetAttribute(qualified, value);
    }

    private static NodeObject ElementOf(JsValue self, Func<JsValue, NodeObject> node) => node(self);

    private JsValue AttributeNodeOrNull(NodeObject element, string? name) => name is null ? JsValue.Null : AttrFor(element, name);

    private JsValue SetAttributeNode(NodeObject element, JsValue value)
    {
        if (value is not AttrObject attr)
            throw TypeError("The argument is not an Attr.");
        if (attr.Owner is { } owner && !ReferenceEquals(owner, element))
            throw DomError("InUseAttributeError", "The attribute is in use by another element.");
        if (ReferenceEquals(attr.Owner, element))
            return attr;

        var existing = FindAttributeNs(element, attr.Name.Namespace, attr.Name.LocalName);
        JsValue previous = JsValue.Null;
        if (existing is not null)
        {
            previous = AttrFor(element, existing);
            RemoveAttributeNamed(element, existing);
        }

        if (attr.Name.Namespace is not null || attr.Name.Prefix is not null)
            element.AttributeNames[attr.QualifiedName] = attr.Name;

        element.Node.SetAttribute(attr.QualifiedName, attr.DetachedValue);
        attr.Owner = element;
        attr.Document = element.Node.OwnerDocument;
        element.AttrNodes[attr.QualifiedName] = attr;
        return previous;
    }

    private JsValue RemoveAttributeNode(NodeObject element, JsValue value)
    {
        if (value is not AttrObject attr || !ReferenceEquals(attr.Owner, element))
            throw DomError("NotFoundError", "The attribute is not on this element.");

        RemoveAttributeNamed(element, attr.QualifiedName);
        return attr;
    }

    private void InstallAttributes()
    {
        var objectPrototype = Engine.Intrinsics.Object.PrototypeObject;

        AttrPrototype = new JsObject(Engine) { Prototype = NodePrototype };
        AttributeMapPrototype = new JsObject(Engine) { Prototype = objectPrototype };

        AttrObject At(JsValue self) => self as AttrObject ?? throw TypeError("Illegal invocation");
        var a = AttrPrototype;
        Js.Accessor(Engine, a, "name", self => Js.Str(At(self).QualifiedName));
        Js.Accessor(Engine, a, "localName", self => Js.Str(At(self).Name.LocalName));
        Js.Accessor(Engine, a, "namespaceURI", self => Js.Str(At(self).Name.Namespace));
        Js.Accessor(Engine, a, "prefix", self => Js.Str(At(self).Name.Prefix));
        Js.Accessor(Engine, a, "value", self => Js.Str(At(self).Value), (self, value) => At(self).Value = Js.AsText(value));
        Js.Accessor(Engine, a, "nodeValue", self => Js.Str(At(self).Value), (self, value) => At(self).Value = value.IsNull() ? string.Empty : Js.AsText(value));
        Js.Accessor(Engine, a, "textContent", self => Js.Str(At(self).Value), (self, value) => At(self).Value = value.IsNull() ? string.Empty : Js.AsText(value));
        Js.Accessor(Engine, a, "nodeType", _ => Js.Num(2));
        Js.Accessor(Engine, a, "nodeName", self => Js.Str(At(self).QualifiedName));
        Js.Accessor(Engine, a, "specified", _ => JsBoolean.True);
        Js.Accessor(Engine, a, "ownerElement", self => At(self).Owner is { } owner ? owner : JsValue.Null);
        Js.Accessor(Engine, a, "ownerDocument", self => Wrap(At(self).Document.Root));
        Js.Accessor(Engine, a, "parentNode", _ => JsValue.Null);
        Js.Accessor(Engine, a, "parentElement", _ => JsValue.Null);
        Js.Accessor(Engine, a, "childNodes", _ => WrapList([]));
        Js.Accessor(Engine, a, "firstChild", _ => JsValue.Null);
        Js.Accessor(Engine, a, "lastChild", _ => JsValue.Null);
        Js.Accessor(Engine, a, "previousSibling", _ => JsValue.Null);
        Js.Accessor(Engine, a, "nextSibling", _ => JsValue.Null);
        Js.Accessor(Engine, a, "isConnected", self => Js.Bool(At(self).Owner?.Node.IsConnected ?? false));
        Js.Method(Engine, a, "hasChildNodes", 0, (_, _) => JsBoolean.False);
        Js.Method(Engine, a, "isSameNode", 1, (self, args) => Js.Bool(ReferenceEquals(Js.Arg(args, 0), self)));
        Js.Method(Engine, a, "getRootNode", 0, (self, _) => At(self).Owner is { } owner ? Wrap(owner.Node) : self);
        Js.Method(Engine, a, "cloneNode", 0, (self, _) =>
        {
            var source = At(self);
            return new AttrObject(Engine, AttrPrototype, source.Document, source.Name) { DetachedValue = source.Value };
        });
        Js.Method(Engine, a, "isEqualNode", 1, (self, args) =>
            Js.Bool(Js.Arg(args, 0) is AttrObject other && other.Name == At(self).Name && other.Value == At(self).Value));
        Tag(AttrPrototype, "Attr");

        var m = AttributeMapPrototype;
        AttributeMapObject Map(JsValue self) => self as AttributeMapObject ?? throw TypeError("Illegal invocation");
        Js.Accessor(Engine, m, "length", self => Js.Num(Map(self).Owner.Node.AttributeNames.Count));
        Js.Method(Engine, m, "item", 1, (self, args) =>
        {
            var owner = Map(self).Owner;
            var index = TypeConverter.ToUint32(Js.Arg(args, 0));
            var names = owner.Node.AttributeNames;
            return index < names.Count ? AttrFor(owner, names[(int)index]) : JsValue.Null;
        });
        Js.Method(Engine, m, "getNamedItem", 1, (self, args) => AttributeNodeOrNull(Map(self).Owner, FindAttribute(Map(self).Owner, Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, m, "getNamedItemNS", 2, (self, args) =>
            AttributeNodeOrNull(Map(self).Owner, FindAttributeNs(Map(self).Owner, NullIfEmpty(Js.Arg(args, 0)), Js.AsText(Js.Arg(args, 1)))));
        Js.Method(Engine, m, "setNamedItem", 1, (self, args) => SetAttributeNode(Map(self).Owner, Js.Arg(args, 0)));
        Js.Method(Engine, m, "setNamedItemNS", 1, (self, args) => SetAttributeNode(Map(self).Owner, Js.Arg(args, 0)));
        Js.Method(Engine, m, "removeNamedItem", 1, (self, args) =>
        {
            var owner = Map(self).Owner;
            var name = FindAttribute(owner, Js.AsText(Js.Arg(args, 0))) ?? throw DomError("NotFoundError", "There is no such attribute.");
            var attr = AttrFor(owner, name);
            RemoveAttributeNamed(owner, name);
            return attr;
        });
        Js.Method(Engine, m, "removeNamedItemNS", 2, (self, args) =>
        {
            var owner = Map(self).Owner;
            var name = FindAttributeNs(owner, NullIfEmpty(Js.Arg(args, 0)), Js.AsText(Js.Arg(args, 1))) ?? throw DomError("NotFoundError", "There is no such attribute.");
            var attr = AttrFor(owner, name);
            RemoveAttributeNamed(owner, name);
            return attr;
        });
        m.FastSetProperty(GlobalSymbolRegistry.Iterator, new PropertyDescriptor(
            Js.Function(Engine, "values", 0, (self, _) =>
            {
                var owner = Map(self).Owner;
                var items = owner.Node.AttributeNames.Select(n => (JsValue)AttrFor(owner, n)).ToList();
                var array = (ObjectInstance)Js.Array(Engine, items);
                return array.Get(GlobalSymbolRegistry.Iterator).Call(array, []);
            }), PropertyFlag.Writable | PropertyFlag.Configurable));
        Tag(AttributeMapPrototype, "NamedNodeMap");

        // The element's own attribute methods, now aware of names, namespaces and Attr nodes.
        var p = ElementPrototype;
        NodeObject El(JsValue self) => Node(self);
        Js.Accessor(Engine, p, "attributes", self => El(self).AttributeMap ??= new AttributeMapObject(this, AttributeMapPrototype, El(self)));
        Js.Method(Engine, p, "getAttribute", 1, (self, args) =>
            FindAttribute(El(self), Js.AsText(Js.Arg(args, 0))) is { } found ? Js.Str(El(self).Node.GetAttribute(found)) : JsValue.Null);
        Js.Method(Engine, p, "getAttributeNames", 0, (self, _) => Js.Array(Engine, [.. El(self).Node.AttributeNames.Select(Js.Str)]));
        Js.Method(Engine, p, "hasAttribute", 1, (self, args) => Js.Bool(FindAttribute(El(self), Js.AsText(Js.Arg(args, 0))) is not null));
        Js.Method(Engine, p, "setAttribute", 2, (self, args) =>
        {
            var element = El(self);
            var name = Js.AsText(Js.Arg(args, 0));
            if (!XmlNames.IsName(name))
                throw DomError("InvalidCharacterError", $"'{name}' is not a valid attribute name.");

            var value = Js.AsText(Js.Arg(args, 1));
            var existing = FindAttribute(element, name);
            if (existing is not null)
                element.Node.SetAttribute(existing, value);
            else
                element.Node.SetAttribute(IsHtmlElement(element) ? name.ToLowerInvariant() : name, value);

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "removeAttribute", 1, (self, args) =>
        {
            if (FindAttribute(El(self), Js.AsText(Js.Arg(args, 0))) is { } name)
                RemoveAttributeNamed(El(self), name);

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "toggleAttribute", 1, (self, args) =>
        {
            var element = El(self);
            var name = Js.AsText(Js.Arg(args, 0));
            if (!XmlNames.IsName(name))
                throw DomError("InvalidCharacterError", $"'{name}' is not a valid attribute name.");

            var force = Js.Arg(args, 1);
            var existing = FindAttribute(element, name);
            if (existing is null)
            {
                if (!force.IsUndefined() && !TypeConverter.ToBoolean(force))
                    return JsBoolean.False;

                element.Node.SetAttribute(IsHtmlElement(element) ? name.ToLowerInvariant() : name, string.Empty);
                return JsBoolean.True;
            }

            if (!force.IsUndefined() && TypeConverter.ToBoolean(force))
                return JsBoolean.True;

            RemoveAttributeNamed(element, existing);
            return JsBoolean.False;
        });
        Js.Method(Engine, p, "hasAttributes", 0, (self, _) => Js.Bool(El(self).Node.AttributeNames.Count > 0));

        Js.Method(Engine, p, "getAttributeNS", 2, (self, args) =>
        {
            var element = El(self);
            return FindAttributeNs(element, NullIfEmpty(Js.Arg(args, 0)), Js.AsText(Js.Arg(args, 1))) is { } name ? Js.Str(element.Node.GetAttribute(name)) : JsValue.Null;
        });
        Js.Method(Engine, p, "hasAttributeNS", 2, (self, args) =>
            Js.Bool(FindAttributeNs(El(self), NullIfEmpty(Js.Arg(args, 0)), Js.AsText(Js.Arg(args, 1))) is not null));
        Js.Method(Engine, p, "setAttributeNS", 3, (self, args) =>
        {
            var element = El(self);
            var (ns, prefix, local) = ValidateAndExtract(Js.Arg(args, 0), Js.AsText(Js.Arg(args, 1)));
            var value = Js.AsText(Js.Arg(args, 2));
            if (FindAttributeNs(element, ns, local) is { } existing)
                element.Node.SetAttribute(existing, value);
            else
                SetAttributeValue(element, new ElementNamespace(ns, prefix, local), value);

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "removeAttributeNS", 2, (self, args) =>
        {
            if (FindAttributeNs(El(self), NullIfEmpty(Js.Arg(args, 0)), Js.AsText(Js.Arg(args, 1))) is { } name)
                RemoveAttributeNamed(El(self), name);

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "getAttributeNode", 1, (self, args) => AttributeNodeOrNull(El(self), FindAttribute(El(self), Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "getAttributeNodeNS", 2, (self, args) =>
            AttributeNodeOrNull(El(self), FindAttributeNs(El(self), NullIfEmpty(Js.Arg(args, 0)), Js.AsText(Js.Arg(args, 1)))));
        Js.Method(Engine, p, "setAttributeNode", 1, (self, args) => SetAttributeNode(El(self), Js.Arg(args, 0)));
        Js.Method(Engine, p, "setAttributeNodeNS", 1, (self, args) => SetAttributeNode(El(self), Js.Arg(args, 0)));
        Js.Method(Engine, p, "removeAttributeNode", 1, (self, args) => RemoveAttributeNode(El(self), Js.Arg(args, 0)));

        Js.Method(Engine, p, "getElementsByTagNameNS", 2, (self, args) =>
        {
            var (ns, local) = (Js.Arg(args, 0), Js.AsText(Js.Arg(args, 1)));
            return WrapLive(Sn(self).OwnerDocument, () => ElementsByNamespace(Sn(self), ns, local), true);
        });
        Js.Method(Engine, DocumentPrototype, "getElementsByTagNameNS", 2, (self, args) =>
        {
            var (ns, local) = (Js.Arg(args, 0), Js.AsText(Js.Arg(args, 1)));
            return WrapLive(Sn(self).OwnerDocument, () => ElementsByNamespace(Sn(self), ns, local), true);
        });

        Js.Method(Engine, DocumentPrototype, "createAttribute", 1, (self, args) =>
        {
            var document = D(self);
            var name = Js.AsText(Js.Arg(args, 0));
            if (!XmlNames.IsName(name))
                throw DomError("InvalidCharacterError", $"'{name}' is not a valid attribute name.");

            return new AttrObject(Engine, AttrPrototype, document, new ElementNamespace(null, null, document.IsHtml ? name.ToLowerInvariant() : name));
        });
        Js.Method(Engine, DocumentPrototype, "createAttributeNS", 2, (self, args) =>
        {
            var (ns, prefix, local) = ValidateAndExtract(Js.Arg(args, 0), Js.AsText(Js.Arg(args, 1)));
            return new AttrObject(Engine, AttrPrototype, D(self), new ElementNamespace(ns, prefix, local));
        });

        foreach (var (name, prototype) in new[] { ("Attr", AttrPrototype), ("NamedNodeMap", AttributeMapPrototype) })
        {
            var captured = name;
            Define(Engine.Global, name, Js.Constructor(Engine, name, prototype, 0, (_, _, _) => throw TypeError($"Illegal constructor: {captured}")));
        }

        InstallFrames();

        // Namespace lookup, which a node answers by walking up to an element that declares it.
        Js.Method(Engine, NodePrototype, "lookupNamespaceURI", 1, (self, args) => Js.Str(LocateNamespace(self, NullIfEmpty(Js.Arg(args, 0)))));
        Js.Method(Engine, NodePrototype, "lookupPrefix", 1, (self, args) =>
        {
            var ns = NullIfEmpty(Js.Arg(args, 0));
            return ns is null ? JsValue.Null : Js.Str(LocatePrefix(self, ns));
        });
        Js.Method(Engine, NodePrototype, "isDefaultNamespace", 1, (self, args) => Js.Bool(LocateNamespace(self, null) == NullIfEmpty(Js.Arg(args, 0))));
    }

    private readonly Dictionary<NodeObject, (ObjectInstance Window, ScriptDocument Document)> _frames = [];

    // An iframe's document: loaded from its src the first time a script asks (a blank one without a src, or when it cannot be read).
    private void InstallFrames()
    {
        var iframe = ElementPrototypeFor("iframe");

        (ObjectInstance Window, ScriptDocument Document)? Frame(JsValue self)
        {
            var element = Node(self);
            if (!element.Node.IsConnected)
                return null;

            if (_frames.TryGetValue(element, out var made))
                return made;

            ScriptDocument loaded;
            var src = element.Node.GetAttribute("src");
            ScriptNetworkResponse? response = null;
            if (!string.IsNullOrEmpty(src) && src != "about:blank" && Document.BaseUri is { } baseUri && Uri.TryCreate(baseUri, src, out var address))
            {
                try
                {
                    response = Document.SendAsync(new ScriptNetworkRequest("GET", address, [], null)).AsTask().GetAwaiter().GetResult();
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    response = null;
                }
            }

            if (response is { Status: >= 200 and < 300 })
            {
                var type = response.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value ?? "text/html";
                var text = System.Text.Encoding.UTF8.GetString(response.Body);
                var mime = type.Split(';')[0].Trim().ToLowerInvariant();
                loaded = mime.EndsWith("xml", StringComparison.Ordinal) || mime.EndsWith("+xml", StringComparison.Ordinal)
                    ? ParseXml(text, mime)
                    : Document.ParseDocument(text);
            }
            else
            {
                loaded = Document.ParseDocument(string.Empty);
            }

            var window = new JsObject(Engine);
            Js.Value(window, "document", Wrap(loaded.Root));
            foreach (var name in new[] { "DOMException", "Node", "Element", "Document", "Text", "Comment", "DocumentFragment", "Attr", "Event", "EventTarget", "Object", "Array", "Error", "TypeError" })
            {
                if (Engine.Global.Get(name) is { } value && !value.IsUndefined())
                    Js.Value(window, name, value);
            }

            Js.Value(window, "window", window);
            _windows[loaded] = window;
            return _frames[element] = (window, loaded);
        }

        Js.Accessor(Engine, iframe, "contentDocument", self => Frame(self) is { } frame ? Wrap(frame.Document.Root) : JsValue.Null);
        Js.Accessor(Engine, iframe, "contentWindow", self => Frame(self) is { } frame ? frame.Window : JsValue.Null);
    }

    private static string? NullIfEmpty(JsValue value)
    {
        if (Js.IsNullish(value))
            return null;

        var text = Js.AsText(value);
        return text.Length == 0 ? null : text;
    }

    private string? ElementNamespaceOf(NodeObject element) => element.NamespaceInfo is { } info ? info.Namespace : HtmlNamespace;

    private string? LocateNamespace(JsValue self, string? prefix)
    {
        switch (self)
        {
            case AttrObject attr:
                return attr.Owner is { } owner ? LocateNamespace(owner, prefix) : null;
            case NodeObject node when node.Node.Kind == ScriptNodeKind.Element:
            {
                if (ElementNamespaceOf(node) is { } own && (node.NamespaceInfo?.Prefix) == prefix)
                    return own;

                foreach (var name in node.Node.AttributeNames)
                {
                    var info = AttributeName(node, name);
                    if (info.Namespace != XmlnsNamespace)
                        continue;
                    if ((info.Prefix == "xmlns" && info.LocalName == prefix) || (prefix is null && info.Prefix is null && info.LocalName == "xmlns"))
                    {
                        var value = node.Node.GetAttribute(name);
                        return string.IsNullOrEmpty(value) ? null : value;
                    }
                }

                return node.Node.Parent is { Kind: ScriptNodeKind.Element } parent ? LocateNamespace(Wrap(parent), prefix) : null;
            }

            case NodeObject { Node.Kind: ScriptNodeKind.Document } document:
                return document.Node.OwnerDocument.DocumentElement is { } root ? LocateNamespace(Wrap(root), prefix) : null;
            case NodeObject { Node.Kind: ScriptNodeKind.DocumentType or ScriptNodeKind.Fragment }:
                return null;
            case NodeObject other:
                return other.Node.Parent is { Kind: ScriptNodeKind.Element } owner2 ? LocateNamespace(Wrap(owner2), prefix) : null;
            default:
                return null;
        }
    }

    private string? LocatePrefix(JsValue self, string ns)
    {
        switch (self)
        {
            case AttrObject attr:
                return attr.Owner is { } owner ? LocatePrefix(owner, ns) : null;
            case NodeObject node when node.Node.Kind == ScriptNodeKind.Element:
            {
                if (ElementNamespaceOf(node) == ns && node.NamespaceInfo?.Prefix is { } prefix)
                    return prefix;

                foreach (var name in node.Node.AttributeNames)
                {
                    var info = AttributeName(node, name);
                    if (info.Prefix == "xmlns" && node.Node.GetAttribute(name) == ns)
                        return info.LocalName;
                }

                return node.Node.Parent is { Kind: ScriptNodeKind.Element } parent ? LocatePrefix(Wrap(parent), ns) : null;
            }

            case NodeObject { Node.Kind: ScriptNodeKind.Document } document:
                return document.Node.OwnerDocument.DocumentElement is { } root ? LocatePrefix(Wrap(root), ns) : null;
            case NodeObject { Node.Kind: ScriptNodeKind.DocumentType or ScriptNodeKind.Fragment }:
                return null;
            case NodeObject other:
                return other.Node.Parent is { Kind: ScriptNodeKind.Element } owner2 ? LocatePrefix(Wrap(owner2), ns) : null;
            default:
                return null;
        }
    }

    // The descendant elements with a namespace and a local name; "*" matches any.
    private IEnumerable<ScriptNode> ElementsByNamespace(ScriptNode root, JsValue namespaceArg, string local)
    {
        var any = Js.AsText(Js.IsNullish(namespaceArg) ? Js.Str(string.Empty) : namespaceArg) == "*" && !Js.IsNullish(namespaceArg);
        var ns = NullIfEmpty(namespaceArg);
        return root.QuerySelectorAll("*").Where(element =>
        {
            var wrapper = Wrap(element);
            var elementNs = ElementNamespaceOf(wrapper);
            var elementLocal = wrapper.NamespaceInfo?.LocalName ?? (element.OwnerDocument.IsHtml ? element.TagName!.ToLowerInvariant() : element.TagName!);
            return (any || elementNs == ns) && (local == "*" || elementLocal == local);
        });
    }
}
