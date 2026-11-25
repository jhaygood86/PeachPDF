using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

internal sealed partial class ScriptRuntime
{
    private ObjectInstance NodePrototype = null!;
    private ObjectInstance ElementPrototype = null!;
    private ObjectInstance TextPrototype = null!;
    private ObjectInstance CharacterDataPrototype = null!;
    private ObjectInstance CommentPrototype = null!;
    private ObjectInstance CDataPrototype = null!;
    private ObjectInstance ProcessingInstructionPrototype = null!;
    private ObjectInstance DocumentTypePrototype = null!;
    private ObjectInstance DocumentPrototype = null!;
    private ObjectInstance XmlDocumentPrototype = null!;
    private ObjectInstance FragmentPrototype = null!;
    private ObjectInstance StylePrototype = null!;
    private ObjectInstance TokenListPrototype = null!;

    // Attributes a script reads and writes as properties of the element: the property name and the attribute it stands for.
    private static readonly (string Property, string Attribute)[] TextAttributes =
    [
        ("title", "title"), ("lang", "lang"), ("dir", "dir"), ("type", "type"), ("value", "value"), ("href", "href"), ("src", "src"),
        ("alt", "alt"), ("placeholder", "placeholder"), ("rel", "rel"), ("target", "target"), ("role", "role"), ("htmlFor", "for"), ("action", "action"),
        ("method", "method"), ("media", "media"), ("charset", "charset"), ("srcset", "srcset"), ("sizes", "sizes"), ("min", "min"), ("max", "max"),
        ("step", "step"), ("content", "content"), ("accept", "accept"), ("pattern", "pattern"), ("label", "label"), ("scope", "scope"), ("headers", "headers"),
        ("span", "span"), ("start", "start"), ("size", "size"), ("rows", "rows"), ("cols", "cols"), ("maxLength", "maxlength"), ("tabIndex", "tabindex"),
        ("colSpan", "colspan"), ("rowSpan", "rowspan"), ("width", "width"), ("height", "height"), ("accessKey", "accesskey"), ("autocomplete", "autocomplete"),
    ];

    private static readonly (string Property, string Attribute)[] BooleanAttributes =
    [
        ("hidden", "hidden"), ("disabled", "disabled"), ("checked", "checked"), ("selected", "selected"), ("open", "open"), ("readOnly", "readonly"),
        ("required", "required"), ("multiple", "multiple"), ("autofocus", "autofocus"), ("controls", "controls"), ("loop", "loop"), ("muted", "muted"),
        ("defer", "defer"), ("async", "async"), ("draggable", "draggable"), ("noValidate", "novalidate"), ("reversed", "reversed"),
    ];

    // The element interfaces a page checks with instanceof, by element name.
    private static readonly Dictionary<string, string> ElementInterfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = "HTMLAnchorElement", ["area"] = "HTMLAreaElement", ["audio"] = "HTMLAudioElement", ["base"] = "HTMLBaseElement",
        ["body"] = "HTMLBodyElement", ["br"] = "HTMLBRElement", ["button"] = "HTMLButtonElement", ["canvas"] = "HTMLCanvasElement",
        ["data"] = "HTMLDataElement", ["datalist"] = "HTMLDataListElement", ["details"] = "HTMLDetailsElement", ["dialog"] = "HTMLDialogElement",
        ["dir"] = "HTMLDirectoryElement", ["div"] = "HTMLDivElement", ["dl"] = "HTMLDListElement", ["embed"] = "HTMLEmbedElement",
        ["fieldset"] = "HTMLFieldSetElement", ["font"] = "HTMLFontElement", ["form"] = "HTMLFormElement", ["frame"] = "HTMLFrameElement",
        ["frameset"] = "HTMLFrameSetElement", ["head"] = "HTMLHeadElement", ["hr"] = "HTMLHRElement", ["html"] = "HTMLHtmlElement",
        ["iframe"] = "HTMLIFrameElement", ["img"] = "HTMLImageElement", ["input"] = "HTMLInputElement", ["label"] = "HTMLLabelElement",
        ["legend"] = "HTMLLegendElement", ["li"] = "HTMLLIElement", ["link"] = "HTMLLinkElement", ["map"] = "HTMLMapElement",
        ["marquee"] = "HTMLMarqueeElement", ["menu"] = "HTMLMenuElement", ["meta"] = "HTMLMetaElement", ["meter"] = "HTMLMeterElement",
        ["ins"] = "HTMLModElement", ["del"] = "HTMLModElement", ["object"] = "HTMLObjectElement", ["ol"] = "HTMLOListElement",
        ["optgroup"] = "HTMLOptGroupElement", ["option"] = "HTMLOptionElement", ["output"] = "HTMLOutputElement", ["p"] = "HTMLParagraphElement",
        ["param"] = "HTMLParamElement", ["picture"] = "HTMLPictureElement", ["pre"] = "HTMLPreElement", ["listing"] = "HTMLPreElement",
        ["xmp"] = "HTMLPreElement", ["progress"] = "HTMLProgressElement", ["q"] = "HTMLQuoteElement", ["blockquote"] = "HTMLQuoteElement",
        ["script"] = "HTMLScriptElement", ["select"] = "HTMLSelectElement", ["slot"] = "HTMLSlotElement", ["source"] = "HTMLSourceElement",
        ["span"] = "HTMLSpanElement", ["style"] = "HTMLStyleElement", ["caption"] = "HTMLTableCaptionElement", ["td"] = "HTMLTableCellElement",
        ["th"] = "HTMLTableCellElement", ["col"] = "HTMLTableColElement", ["colgroup"] = "HTMLTableColElement", ["table"] = "HTMLTableElement",
        ["tr"] = "HTMLTableRowElement", ["tbody"] = "HTMLTableSectionElement", ["thead"] = "HTMLTableSectionElement", ["tfoot"] = "HTMLTableSectionElement",
        ["template"] = "HTMLTemplateElement", ["textarea"] = "HTMLTextAreaElement", ["time"] = "HTMLTimeElement", ["title"] = "HTMLTitleElement",
        ["track"] = "HTMLTrackElement", ["ul"] = "HTMLUListElement", ["video"] = "HTMLVideoElement", ["h1"] = "HTMLHeadingElement",
        ["h2"] = "HTMLHeadingElement", ["h3"] = "HTMLHeadingElement", ["h4"] = "HTMLHeadingElement", ["h5"] = "HTMLHeadingElement",
        ["h6"] = "HTMLHeadingElement",
    };

    private readonly Dictionary<string, ObjectInstance> _interfacePrototypes = [];

    private ObjectInstance ElementPrototypeFor(string? tag)
    {
        if (tag is null || !ElementInterfaces.TryGetValue(tag, out var name))
            return ElementPrototype;

        if (!_interfacePrototypes.TryGetValue(name, out var prototype))
        {
            prototype = new JsObject(Engine) { Prototype = ElementPrototype };
            _interfacePrototypes[name] = prototype;
            InstallReflected(prototype, ReflectedFor(name));
        }

        return prototype;
    }

    // The window an iframe's document has: its document and the interfaces a page reaches through it.
    private readonly Dictionary<ScriptDocument, ObjectInstance> _windows = [];

    // The address a document's relative references resolve against: its first base element's href resolved against the document's address, else the address.
    private static string BaseOf(ScriptDocument document)
    {
        var address = document.BaseUri?.AbsoluteUri ?? "about:blank";
        var element = document.QuerySelector("base[href]");
        if (element?.GetAttribute("href") is { } href)
        {
            // An address with a scheme of its own stands as it is (about:blank has no authority, which a relative resolution would not keep).
            if (System.Text.RegularExpressions.Regex.IsMatch(href, "^[A-Za-z][A-Za-z0-9+.-]*:"))
                return href.Trim();

            if (Uri.TryCreate(document.BaseUri ?? new Uri("about:blank"), href, out var resolved))
                return resolved.AbsoluteUri;
        }

        return address;
    }

    // The document a document object stands for.
    private ScriptDocument D(JsValue self) => Sn(self).OwnerDocument;

    private JsValue ImportNode(ScriptDocument into, ScriptNode node, bool deep)
    {
        if (node.Kind == ScriptNodeKind.Document)
            throw DomError("NotSupportedError", "A document cannot be imported.");

        var copy = node.CloneNode(deep);
        into.AdoptNode(copy);
        CopyNamespaces(node, copy, deep);
        return Wrap(copy);
    }

    private JsValue AdoptNode(ScriptDocument into, ScriptNode node)
    {
        if (node.Kind == ScriptNodeKind.Document)
            throw DomError("NotSupportedError", "A document cannot be adopted.");

        into.AdoptNode(node);
        return Wrap(node);
    }

    private NodeObject Node(JsValue self) => self as NodeObject ?? throw TypeError("Illegal invocation");

    private ScriptNode Sn(JsValue self) => Node(self).Node;

    private ScriptNode NodeArg(JsValue[] args, int index) =>
        Js.Arg(args, index) is NodeObject node ? node.Node : throw TypeError("The argument is not a Node.");

    private void InstallPrototypes()
    {
        var objectPrototype = Engine.Intrinsics.Object.PrototypeObject;
        InstallEventPrototypes(objectPrototype);

        NodePrototype = new JsObject(Engine);
        NodePrototype.Prototype = EventTargetPrototype;
        InstallNode();

        ElementPrototype = new JsObject(Engine);
        ElementPrototype.Prototype = NodePrototype;
        InstallElement();

        CharacterDataPrototype = new JsObject(Engine);
        CharacterDataPrototype.Prototype = NodePrototype;
        Js.Accessor(Engine, CharacterDataPrototype, "data", self => Js.Str(Sn(self).Data), (self, value) => Sn(self).Data = value.IsNull() ? string.Empty : Js.AsText(value));
        Js.Accessor(Engine, CharacterDataPrototype, "length", self => Js.Num((Sn(self).Data ?? string.Empty).Length));

        TextPrototype = new JsObject(Engine);
        TextPrototype.Prototype = CharacterDataPrototype;
        Js.Accessor(Engine, TextPrototype, "wholeText", self => Js.Str(Sn(self).Data));

        CDataPrototype = new JsObject(Engine);
        CDataPrototype.Prototype = TextPrototype;

        CommentPrototype = new JsObject(Engine);
        CommentPrototype.Prototype = CharacterDataPrototype;

        ProcessingInstructionPrototype = new JsObject(Engine);
        ProcessingInstructionPrototype.Prototype = CharacterDataPrototype;
        Js.Accessor(Engine, ProcessingInstructionPrototype, "target", self => Js.Str(Sn(self).Name));

        DocumentTypePrototype = new JsObject(Engine);
        DocumentTypePrototype.Prototype = NodePrototype;
        Js.Accessor(Engine, DocumentTypePrototype, "name", self => Js.Str(Sn(self).Name));
        Js.Accessor(Engine, DocumentTypePrototype, "publicId", self => Js.Str(Sn(self).PublicId));
        Js.Accessor(Engine, DocumentTypePrototype, "systemId", self => Js.Str(Sn(self).SystemId));

        FragmentPrototype = new JsObject(Engine);
        FragmentPrototype.Prototype = NodePrototype;
        InstallParentOperations(FragmentPrototype);

        DocumentPrototype = new JsObject(Engine);
        DocumentPrototype.Prototype = NodePrototype;
        InstallDocument();
        InstallParentOperations(DocumentPrototype);

        XmlDocumentPrototype = new JsObject(Engine);
        XmlDocumentPrototype.Prototype = DocumentPrototype;

        StylePrototype = new JsObject(Engine);
        StylePrototype.Prototype = objectPrototype;
        InstallStyle();

        TokenListPrototype = new JsObject(Engine);
        TokenListPrototype.Prototype = objectPrototype;
        InstallTokenList();

        InstallEventConstructors();
    }

    private void InstallNode()
    {
        var p = NodePrototype;
        foreach (var (name, value) in new[] { ("ELEMENT_NODE", 1), ("ATTRIBUTE_NODE", 2), ("TEXT_NODE", 3), ("CDATA_SECTION_NODE", 4), ("ENTITY_REFERENCE_NODE", 5), ("ENTITY_NODE", 6), ("PROCESSING_INSTRUCTION_NODE", 7), ("COMMENT_NODE", 8), ("DOCUMENT_NODE", 9), ("DOCUMENT_TYPE_NODE", 10), ("DOCUMENT_FRAGMENT_NODE", 11), ("NOTATION_NODE", 12) })
            p.FastSetProperty(name, new PropertyDescriptor(Js.Num(value), PropertyFlag.AllForbidden));

        Js.Accessor(Engine, p, "nodeType", self => Js.Num(Sn(self).Kind switch
        {
            ScriptNodeKind.Element => 1,
            ScriptNodeKind.Text => Sn(self).IsCDataSection ? 4 : 3,
            ScriptNodeKind.ProcessingInstruction => 7,
            ScriptNodeKind.Comment => 8,
            ScriptNodeKind.Document => 9,
            ScriptNodeKind.DocumentType => 10,
            _ => 11,
        }));
        Js.Accessor(Engine, p, "nodeName", self => Js.Str(Sn(self).Kind switch
        {
            ScriptNodeKind.Element => QualifiedName(Node(self)),
            ScriptNodeKind.Text => Sn(self).IsCDataSection ? "#cdata-section" : "#text",
            ScriptNodeKind.Comment => "#comment",
            ScriptNodeKind.ProcessingInstruction or ScriptNodeKind.DocumentType => Sn(self).Name ?? string.Empty,
            ScriptNodeKind.Document => "#document",
            _ => "#document-fragment",
        }));
        Js.Accessor(Engine, p, "nodeValue", self => Sn(self).Data is { } data ? Js.Str(data) : JsValue.Null,
            (self, value) => { if (Sn(self).Data is not null) Sn(self).Data = Js.IsNullish(value) ? string.Empty : Js.AsText(value); });
        Js.Accessor(Engine, p, "parentNode", self => WrapOrNull(Sn(self).Parent));
        Js.Accessor(Engine, p, "parentElement", self => Sn(self).Parent is { Kind: ScriptNodeKind.Element } parent ? Wrap(parent) : JsValue.Null);
        Js.Accessor(Engine, p, "childNodes", self => Node(self).ChildNodesList ??= WrapLive(Sn(self).OwnerDocument, () => Sn(self).Children, false));
        Js.Accessor(Engine, p, "firstChild", self => WrapOrNull(Sn(self).FirstChild));
        Js.Accessor(Engine, p, "lastChild", self => WrapOrNull(Sn(self).LastChild));
        Js.Accessor(Engine, p, "previousSibling", self => WrapOrNull(Sn(self).PreviousSibling));
        Js.Accessor(Engine, p, "nextSibling", self => WrapOrNull(Sn(self).NextSibling));
        Js.Accessor(Engine, p, "isConnected", self => Js.Bool(Sn(self).IsConnected));
        Js.Accessor(Engine, p, "baseURI", self => Js.Str(BaseOf(Sn(self).OwnerDocument)));
        Js.Accessor(Engine, p, "ownerDocument", self => Sn(self).Kind == ScriptNodeKind.Document ? JsValue.Null : Wrap(Sn(self).OwnerDocument.Root));
        Js.Accessor(Engine, p, "textContent",
            self => Sn(self).Kind is ScriptNodeKind.Document or ScriptNodeKind.DocumentType ? JsValue.Null : Js.Str(Sn(self).TextContent),
            (self, value) =>
            {
                if (Sn(self).Kind is not (ScriptNodeKind.Document or ScriptNodeKind.DocumentType)) Sn(self).TextContent = Js.IsNullish(value) ? string.Empty : Js.AsText(value);
            });

        Js.Method(Engine, p, "hasChildNodes", 0, (self, _) => Js.Bool(Sn(self).Children.Count > 0));
        Js.Method(Engine, p, "appendChild", 1, (self, args) => Wrap(Guard(() => Sn(self).AppendChild(NodeArg(args, 0)))));
        Js.Method(Engine, p, "insertBefore", 2, (self, args) =>
            Wrap(Guard(() => Sn(self).InsertBefore(NodeArg(args, 0), Js.IsNullish(Js.Arg(args, 1)) ? null : NodeArg(args, 1)))));
        Js.Method(Engine, p, "removeChild", 1, (self, args) => Wrap(Guard(() => Sn(self).RemoveChild(NodeArg(args, 0)))));
        Js.Method(Engine, p, "replaceChild", 2, (self, args) => Wrap(Guard(() => Sn(self).ReplaceChild(NodeArg(args, 0), NodeArg(args, 1)))));
        Js.Method(Engine, p, "cloneNode", 0, (self, args) =>
        {
            var copy = Sn(self).CloneNode(TypeConverter.ToBoolean(Js.Arg(args, 0)));
            CopyNamespaces(Sn(self), copy, TypeConverter.ToBoolean(Js.Arg(args, 0)));
            return Wrap(copy);
        });
        Js.Method(Engine, p, "contains", 1, (self, args) =>
        {
            if (Js.Arg(args, 0) is not NodeObject other) return JsBoolean.False;
            for (var node = other.Node; node is not null; node = node.Parent)
            {
                if (ReferenceEquals(node, Sn(self))) return JsBoolean.True;
            }

            return JsBoolean.False;
        });
        Js.Method(Engine, p, "normalize", 0, (_, _) => JsValue.Undefined);
        Js.Method(Engine, p, "isEqualNode", 1, (self, args) =>
            Js.Bool(Js.Arg(args, 0) is NodeObject other && other.Node.OuterHtml == Sn(self).OuterHtml));
        Js.Method(Engine, p, "isSameNode", 1, (self, args) => Js.Bool(ReferenceEquals(Js.Arg(args, 0), self)));
        Js.Method(Engine, p, "getRootNode", 0, (self, _) =>
        {
            var node = Sn(self);
            while (node.Parent is { } parent) node = parent;
            return Wrap(node);
        });
    }

    // Wraps a tree operation so the exceptions it throws are the page's to catch.
    private T Guard<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (InvalidOperationException error)
        {
            throw DomError(error.Message.Contains("not a child", StringComparison.Ordinal) ? "NotFoundError" : "HierarchyRequestError", error.Message);
        }
    }

    // The operations that document, fragments and elements share: children, queries, append and prepend.
    private void InstallParentOperations(ObjectInstance p)
    {
        Js.Accessor(Engine, p, "children", self => Node(self).ChildrenList ??= WrapLive(Sn(self).OwnerDocument, () => Sn(self).ChildElements, true));
        Js.Accessor(Engine, p, "childElementCount", self => Js.Num(Sn(self).ChildElements.Count));
        Js.Accessor(Engine, p, "firstElementChild", self => WrapOrNull(Sn(self).ChildElements.FirstOrDefault()));
        Js.Accessor(Engine, p, "lastElementChild", self => WrapOrNull(Sn(self).ChildElements.LastOrDefault()));
        Js.Method(Engine, p, "querySelector", 1, (self, args) =>
            WrapOrNull(QueryGuard(() => Sn(self).QuerySelector(Js.AsText(Js.Arg(args, 0))))));
        Js.Method(Engine, p, "querySelectorAll", 1, (self, args) =>
            WrapList(QueryGuard(() => Sn(self).QuerySelectorAll(Js.AsText(Js.Arg(args, 0))))!));
        Js.Method(Engine, p, "append", 1, (self, args) =>
        {
            Sn(self).AppendChild(ToNode(Sn(self).OwnerDocument, args));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "prepend", 1, (self, args) =>
        {
            Sn(self).InsertBefore(ToNode(Sn(self).OwnerDocument, args), Sn(self).FirstChild);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "replaceChildren", 0, (self, args) =>
        {
            foreach (var child in Sn(self).Children) Sn(self).RemoveChild(child);
            if (args.Length > 0) Sn(self).AppendChild(ToNode(Sn(self).OwnerDocument, args));
            return JsValue.Undefined;
        });
    }

    private T? QueryGuard<T>(Func<T> query)
    {
        try
        {
            return query();
        }
        catch (ArgumentException error)
        {
            throw DomError("SyntaxError", error.Message);
        }
    }

    // The nodes and strings of an append(...) call as one node: strings become text nodes.
    private ScriptNode ToNode(ScriptDocument document, JsValue[] args)
    {
        var fragment = document.CreateEmptyFragment();
        foreach (var arg in args)
            fragment.AppendChild(arg is NodeObject node ? node.Node : document.CreateTextNode(Js.AsText(arg)));

        return fragment;
    }

    private void InstallElement()
    {
        var p = ElementPrototype;
        InstallParentOperations(p);

        Js.Accessor(Engine, p, "tagName", self => Js.Str(QualifiedName(Node(self))));
        Js.Accessor(Engine, p, "localName", self => Js.Str(Node(self).NamespaceInfo?.LocalName ?? (Sn(self).OwnerDocument.IsHtml ? Sn(self).TagName!.ToLowerInvariant() : Sn(self).TagName)));
        Js.Accessor(Engine, p, "namespaceURI", self => Js.Str(Node(self).NamespaceInfo is { } ns ? ns.Namespace : HtmlNamespace));
        Js.Accessor(Engine, p, "prefix", self => Js.Str(Node(self).NamespaceInfo?.Prefix));
        Js.Accessor(Engine, p, "id", self => Js.Str(Sn(self).Id), (self, value) => Sn(self).SetAttribute("id", Js.AsText(value)));
        Js.Accessor(Engine, p, "className", self => Js.Str(Sn(self).GetAttribute("class") ?? string.Empty),
            (self, value) => Sn(self).SetAttribute("class", Js.AsText(value)));
        Js.Accessor(Engine, p, "classList", self =>
        {
            var node = Node(self);
            return node.ClassList ??= new TokenListObject(this, node.Node);
        }, (self, value) => Sn(self).SetAttribute("class", Js.AsText(value)));
        Js.Accessor(Engine, p, "style", self =>
        {
            var node = Node(self);
            return node.Style ??= new StyleObject(Engine, StylePrototype, node.Node);
        }, (self, value) => new StyleObject(Engine, StylePrototype, Sn(self)).CssText = Js.AsText(value));

        Js.Accessor(Engine, p, "innerHTML", self => Js.Str(Sn(self).InnerHtml), (self, value) => Sn(self).InnerHtml = value.IsNull() ? string.Empty : Js.AsText(value));
        Js.Accessor(Engine, p, "outerHTML", self => Js.Str(Sn(self).OuterHtml), (self, value) =>
        {
            var element = Sn(self);
            var parent = element.Parent;
            if (parent is null)
                return;
            if (parent.Kind == ScriptNodeKind.Document)
                throw DomError("NoModificationAllowedError", "The element's parent is a document.");
            parent.ReplaceChild(element.OwnerDocument.CreateFragment(value.IsNull() ? string.Empty : Js.AsText(value)), element);
        });
        Js.Accessor(Engine, p, "innerText", self => Js.Str(Sn(self).TextContent), (self, value) => Sn(self).TextContent = Js.AsText(value));
        Js.Accessor(Engine, p, "previousElementSibling", self => WrapOrNull(ElementSibling(Sn(self), -1)));
        Js.Accessor(Engine, p, "nextElementSibling", self => WrapOrNull(ElementSibling(Sn(self), 1)));
        Js.Accessor(Engine, p, "attributes", self => Js.Array(Engine, [.. Sn(self).AttributeNames.Select(name => (JsValue)AttributeObject(Sn(self), name))]));

        Js.Method(Engine, p, "getAttribute", 1, (self, args) => Js.Str(Sn(self).GetAttribute(Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "setAttribute", 2, (self, args) =>
        {
            Sn(self).SetAttribute(Js.AsText(Js.Arg(args, 0)), Js.AsText(Js.Arg(args, 1)));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "removeAttribute", 1, (self, args) =>
        {
            Sn(self).RemoveAttribute(Js.AsText(Js.Arg(args, 0)));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "hasAttribute", 1, (self, args) => Js.Bool(Sn(self).HasAttribute(Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "hasAttributes", 0, (self, _) => Js.Bool(Sn(self).AttributeNames.Count > 0));
        Js.Method(Engine, p, "getAttributeNames", 0, (self, _) => Js.Array(Engine, [.. Sn(self).AttributeNames.Select(Js.Str)]));
        Js.Method(Engine, p, "toggleAttribute", 1, (self, args) =>
        {
            var name = Js.AsText(Js.Arg(args, 0));
            var force = Js.Arg(args, 1);
            var has = Sn(self).HasAttribute(name);
            var want = force.IsUndefined() ? !has : TypeConverter.ToBoolean(force);
            if (want && !has) Sn(self).SetAttribute(name, string.Empty);
            else if (!want && has) Sn(self).RemoveAttribute(name);
            return Js.Bool(want);
        });
        Js.Method(Engine, p, "matches", 1, (self, args) => Js.Bool(QueryGuard(() => Sn(self).Matches(Js.AsText(Js.Arg(args, 0))))));
        Js.Method(Engine, p, "webkitMatchesSelector", 1, (self, args) => Js.Bool(QueryGuard(() => Sn(self).Matches(Js.AsText(Js.Arg(args, 0))))));
        Js.Method(Engine, p, "closest", 1, (self, args) => WrapOrNull(QueryGuard(() => Sn(self).Closest(Js.AsText(Js.Arg(args, 0))))));
        Js.Method(Engine, p, "getElementsByTagName", 1, (self, args) =>
        {
            var name = Js.AsText(Js.Arg(args, 0));
            return WrapLive(Sn(self).OwnerDocument, () => ElementsByTagName(Sn(self), name), true);
        });
        Js.Method(Engine, p, "getElementsByClassName", 1, (self, args) =>
        {
            var names = Js.AsText(Js.Arg(args, 0));
            return WrapLive(Sn(self).OwnerDocument, () => ElementsByClassName(Sn(self), names), true);
        });
        Js.Method(Engine, p, "remove", 0, (self, _) =>
        {
            Sn(self).Remove();
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "before", 1, (self, args) =>
        {
            Sn(self).Parent?.InsertBefore(ToNode(Sn(self).OwnerDocument, args), Sn(self));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "after", 1, (self, args) =>
        {
            Sn(self).Parent?.InsertBefore(ToNode(Sn(self).OwnerDocument, args), Sn(self).NextSibling);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "replaceWith", 1, (self, args) =>
        {
            Sn(self).Parent?.ReplaceChild(ToNode(Sn(self).OwnerDocument, args), Sn(self));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "insertAdjacentHTML", 2, (self, args) => InsertAdjacent(Sn(self), Js.AsText(Js.Arg(args, 0)), Sn(self).OwnerDocument.CreateFragment(Js.AsText(Js.Arg(args, 1))), JsValue.Undefined, markup: true));
        Js.Method(Engine, p, "insertAdjacentElement", 2, (self, args) => InsertAdjacent(Sn(self), Js.AsText(Js.Arg(args, 0)), NodeArg(args, 1), Js.Arg(args, 1)));
        Js.Method(Engine, p, "insertAdjacentText", 2, (self, args) => InsertAdjacent(Sn(self), Js.AsText(Js.Arg(args, 0)), Sn(self).OwnerDocument.CreateTextNode(Js.AsText(Js.Arg(args, 1))), JsValue.Undefined));
        // click() sends the click a user would: a bubbling, cancelable mouse event, unless the element is a disabled control.
        Js.Method(Engine, p, "click", 0, (self, _) =>
        {
            var node = Sn(self);
            if (node.HasAttribute("disabled") && node.TagName?.ToLowerInvariant() is "button" or "input" or "select" or "textarea")
                return JsValue.Undefined;

            var click = (EventObject)Engine.Evaluate("(function () { return new MouseEvent('click', { bubbles: true, cancelable: true, composed: true }); })").Call(JsValue.Undefined, []);
            click.IsTrusted = false;
            DispatchEvent(Node(self), click);
            return JsValue.Undefined;
        });
        foreach (var focus in new[] { "focus", "blur", "scrollIntoView", "scrollTo", "scroll", "select" })
            Js.Method(Engine, p, focus, 0, (_, _) => JsValue.Undefined);

        foreach (var (property, attribute) in TextAttributes)
        {
            var (prop, attr) = (property, attribute);
            Js.Accessor(Engine, p, prop, self => Js.Str(Sn(self).GetAttribute(attr) ?? string.Empty), (self, value) => Sn(self).SetAttribute(attr, Js.AsText(value)));
        }

        foreach (var (property, attribute) in BooleanAttributes)
        {
            var (prop, attr) = (property, attribute);
            Js.Accessor(Engine, p, prop, self => Js.Bool(Sn(self).HasAttribute(attr)), (self, value) =>
            {
                if (TypeConverter.ToBoolean(value)) Sn(self).SetAttribute(attr, string.Empty);
                else Sn(self).RemoveAttribute(attr);
            });
        }

        InstallReflected(p, GlobalReflected());

        // A form control's value is the attribute here: there is no user to change it.
        Js.Accessor(Engine, p, "dataset", self =>
        {
            var data = Js.NewObject(Engine);
            foreach (var name in Sn(self).AttributeNames.Where(n => n.StartsWith("data-", StringComparison.OrdinalIgnoreCase)))
                data.FastSetProperty(DataKey(name), new PropertyDescriptor(Js.Str(Sn(self).GetAttribute(name)), PropertyFlag.ConfigurableEnumerableWritable));

            return data;
        });

        Js.Method(Engine, p, "getBoundingClientRect", 0, (self, _) => RectObject(LayoutOf(Sn(self))));
        Js.Method(Engine, p, "getClientRects", 0, (self, _) => Js.Array(Engine, [RectObject(LayoutOf(Sn(self)))]));
        Js.Accessor(Engine, p, "offsetWidth", self => Js.Num(Math.Round(LayoutOf(Sn(self)).Width)));
        Js.Accessor(Engine, p, "offsetHeight", self => Js.Num(Math.Round(LayoutOf(Sn(self)).Height)));
        Js.Accessor(Engine, p, "offsetLeft", self => Js.Num(Math.Round(LayoutOf(Sn(self)).X)));
        Js.Accessor(Engine, p, "offsetTop", self => Js.Num(Math.Round(LayoutOf(Sn(self)).Y)));
        Js.Accessor(Engine, p, "clientWidth", self => Js.Num(Math.Round(LayoutOf(Sn(self)).Width)));
        Js.Accessor(Engine, p, "clientHeight", self => Js.Num(Math.Round(LayoutOf(Sn(self)).Height)));
        Js.Accessor(Engine, p, "scrollWidth", self => Js.Num(Math.Round(LayoutOf(Sn(self)).Width)));
        Js.Accessor(Engine, p, "scrollHeight", self => Js.Num(Math.Round(LayoutOf(Sn(self)).Height)));
        Js.Accessor(Engine, p, "scrollTop", _ => Js.Num(0), (_, _) => { });
        Js.Accessor(Engine, p, "scrollLeft", _ => Js.Num(0), (_, _) => { });
        Js.Accessor(Engine, p, "offsetParent", self => WrapOrNull(Document.Body is { } body && !ReferenceEquals(body, Sn(self)) ? body : null));
    }

    private static string DataKey(string attribute)
    {
        var parts = attribute["data-".Length..].Split('-');
        return parts[0] + string.Concat(parts.Skip(1).Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private ObjectInstance AttributeObject(ScriptNode node, string name)
    {
        var attribute = Js.NewObject(Engine);
        Js.Value(attribute, "name", Js.Str(name));
        Js.Value(attribute, "localName", Js.Str(name));
        Js.Value(attribute, "value", Js.Str(node.GetAttribute(name)));
        Js.Value(attribute, "nodeValue", Js.Str(node.GetAttribute(name)));
        return attribute;
    }

    private static ScriptNode? ElementSibling(ScriptNode node, int step)
    {
        if (node.Parent is not { } parent)
            return null;

        var siblings = parent.ChildElements;
        var index = siblings.ToList().IndexOf(node) + step;
        return index >= 0 && index < siblings.Count ? siblings[index] : null;
    }

    private JsValue InsertAdjacent(ScriptNode element, string where, ScriptNode node, JsValue result, bool markup = false)
    {
        if (markup && where.ToLowerInvariant() is "beforebegin" or "afterend" && (element.Parent is null || element.Parent.Kind == ScriptNodeKind.Document))
            throw DomError("NoModificationAllowedError", "The element has no parent that can take the markup.");

        switch (where.ToLowerInvariant())
        {
            case "beforebegin": element.Parent?.InsertBefore(node, element); break;
            case "afterbegin": element.InsertBefore(node, element.FirstChild); break;
            case "beforeend": element.AppendChild(node); break;
            case "afterend": element.Parent?.InsertBefore(node, element.NextSibling); break;
            default: throw DomError("SyntaxError", "The position '" + where + "' is not valid.");
        }

        return result;
    }

    // The encoding a document declares in a meta element (charset, or a Content-Type http-equiv), by its Encoding Standard name; the UTF-16 labels mean UTF-8 there.
    private static string CharacterSetOf(ScriptDocument document)
    {
        if (!document.IsHtml)
            return "UTF-8";

        string? label = document.QuerySelector("meta[charset]")?.GetAttribute("charset");
        if (label is null)
        {
            foreach (var meta in document.QuerySelectorAll("meta[http-equiv]"))
            {
                if (string.Equals(meta.GetAttribute("http-equiv")?.Trim(), "content-type", StringComparison.OrdinalIgnoreCase)
                    && System.Text.RegularExpressions.Regex.Match(meta.GetAttribute("content") ?? string.Empty, @"charset\s*=\s*[""']?([^\s;""']+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } found)
                {
                    label = found.Groups[1].Value;
                    break;
                }
            }
        }

        var name = label is null ? null : EncodingLabels.NameOf(label);
        return name is null or "replacement" || name.StartsWith("UTF-16", StringComparison.Ordinal) ? "UTF-8" : name;
    }

    private void InstallDocument()
    {
        var p = DocumentPrototype;
        Js.Accessor(Engine, p, "documentElement", self => WrapOrNull(D(self).DocumentElement));
        Js.Accessor(Engine, p, "head", self => WrapOrNull(D(self).Head));
        Js.Accessor(Engine, p, "body", self => WrapOrNull(D(self).Body));
        Js.Accessor(Engine, p, "scrollingElement", self => WrapOrNull(D(self).DocumentElement));
        Js.Accessor(Engine, p, "activeElement", self => WrapOrNull(D(self).Body));
        Js.Accessor(Engine, p, "title", self => Js.Str(D(self).Title), (self, value) =>
        {
            var title = D(self).GetElementsByTagName("title").FirstOrDefault();
            if (title is null && D(self).Head is { } head)
                head.AppendChild(title = D(self).CreateElement("title"));

            if (title is not null) title.TextContent = Js.AsText(value);
        });
        Js.Accessor(Engine, p, "readyState", self => Js.Str(ReadyState));
        Js.Accessor(Engine, p, "compatMode", self => Js.Str(D(self).IsHtml ? D(self).CompatMode : "CSS1Compat"));
        Js.Accessor(Engine, p, "characterSet", self => Js.Str(CharacterSetOf(D(self))));
        Js.Accessor(Engine, p, "charset", self => Js.Str(CharacterSetOf(D(self))));
        Js.Accessor(Engine, p, "inputEncoding", self => Js.Str(CharacterSetOf(D(self))));
        Js.Accessor(Engine, p, "contentType", self => Js.Str(D(self).ContentType));
        Js.Accessor(Engine, p, "URL", self => Js.Str(_blankDocuments.Contains(D(self)) ? "about:blank" : D(self).BaseUri?.AbsoluteUri ?? "about:blank"));
        Js.Accessor(Engine, p, "documentURI", self => Js.Str(_blankDocuments.Contains(D(self)) ? "about:blank" : D(self).BaseUri?.AbsoluteUri ?? "about:blank"));
        Js.Accessor(Engine, p, "baseURI", self => Js.Str(BaseOf(Sn(self).OwnerDocument)));
        Js.Accessor(Engine, p, "location", self => ReferenceEquals(D(self), Document) ? Engine.Global.Get("location") : _windows.TryGetValue(D(self), out var frame) && frame is ObjectInstance frameWindow ? frameWindow.Get("location") : JsValue.Null);
        Js.Accessor(Engine, p, "defaultView", self => ReferenceEquals(D(self), Document) ? Engine.Global : _windows.TryGetValue(D(self), out var window) ? window : JsValue.Null);
        Js.Accessor(Engine, p, "currentScript", self => WrapOrNull(CurrentScript));
        Js.Accessor(Engine, p, "visibilityState", self => Js.Str("visible"));
        Js.Accessor(Engine, p, "hidden", self => JsBoolean.False);
        Js.Accessor(Engine, p, "cookie", self => Js.Str(string.Empty), (self, _) => { });
        Js.Accessor(Engine, p, "referrer", self => Js.Str(string.Empty));
        Js.Accessor(Engine, p, "scripts", self => WrapLive(D(self), () => D(self).Scripts, true));
        Js.Accessor(Engine, p, "forms", self => WrapLive(D(self), () => ElementsByTagName(D(self).Root, "form"), true));
        Js.Accessor(Engine, p, "images", self => WrapLive(D(self), () => ElementsByTagName(D(self).Root, "img"), true));
        Js.Accessor(Engine, p, "links", self => WrapLive(D(self), () => D(self).QuerySelectorAll("a[href], area[href]"), true));
        Js.Accessor(Engine, p, "fonts", self => FontFaceSet());
        Js.Accessor(Engine, p, "styleSheets", self => Js.Array(Engine, []));
        Js.Method(Engine, p, "createElement", 1, (self, args) =>
        {
            var name = Js.AsText(Js.Arg(args, 0));
            if (!XmlNames.IsElementLocalName(name))
                throw DomError("InvalidCharacterError", $"'{name}' is not a valid element name.");
            var created = Wrap(D(self).CreateElement(name));
            // In an XML document that is not XHTML an element made by createElement has no namespace.
            if (!D(self).IsHtml && D(self).ContentType != "application/xhtml+xml")
            {
                created.NamespaceInfo = new ElementNamespace(null, null, name);
                created.Prototype = ElementPrototype;
            }

            return created;
        });
        Js.Method(Engine, p, "createElementNS", 2, (self, args) => CreateElementNs(D(self), Js.Arg(args, 0), Js.AsText(Js.Arg(args, 1))));
        Js.Method(Engine, p, "createTextNode", 1, (self, args) => Wrap(D(self).CreateTextNode(Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "createComment", 1, (self, args) => Wrap(D(self).CreateComment(Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "createProcessingInstruction", 2, (self, args) =>
        {
            var target = Js.AsText(Js.Arg(args, 0));
            var data = Js.AsText(Js.Arg(args, 1));
            if (!XmlNames.IsName(target))
                throw DomError("InvalidCharacterError", $"'{target}' is not a valid processing instruction target.");
            if (data.Contains("?>", StringComparison.Ordinal))
                throw DomError("InvalidCharacterError", "The data contains '?>'.");
            return Wrap(D(self).CreateProcessingInstruction(target, data));
        });
        Js.Accessor(Engine, p, "doctype", self => WrapOrNull(D(self).DocumentType));
        Js.Method(Engine, p, "createCDATASection", 1, (self, args) =>
        {
            if (D(self).IsHtml)
                throw DomError("NotSupportedError", "An HTML document has no CDATA sections.");

            var data = Js.AsText(Js.Arg(args, 0));
            if (data.Contains("]]>", StringComparison.Ordinal))
                throw DomError("InvalidCharacterError", "The data contains ']]>'.");

            return Wrap(D(self).CreateCDataSection(data));
        });
        Js.Method(Engine, p, "createDocumentFragment", 0, (self, _) => Wrap(D(self).CreateEmptyFragment()));
        Js.Method(Engine, p, "getElementById", 1, (self, args) => WrapOrNull(D(self).GetElementById(Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "getElementsByTagName", 1, (self, args) =>
        {
            var name = Js.AsText(Js.Arg(args, 0));
            return WrapLive(D(self), () => ElementsByTagName(D(self).Root, name), true);
        });
        Js.Method(Engine, p, "getElementsByClassName", 1, (self, args) =>
        {
            var names = Js.AsText(Js.Arg(args, 0));
            return WrapLive(D(self), () => ElementsByClassName(D(self).Root, names), true);
        });
        Js.Method(Engine, p, "getElementsByName", 1, (self, args) =>
        {
            var name = Js.AsText(Js.Arg(args, 0));
            return WrapLive(D(self), () => D(self).QuerySelectorAll("*").Where(e => e.GetAttribute("name") == name), false);
        });
        Js.Method(Engine, p, "importNode", 1, (self, args) => ImportNode(D(self), NodeArg(args, 0), TypeConverter.ToBoolean(Js.Arg(args, 1))));
        Js.Method(Engine, p, "adoptNode", 1, (self, args) => AdoptNode(D(self), NodeArg(args, 0)));
        Js.Method(Engine, p, "hasFocus", 0, (self, _) => JsBoolean.True);
        Js.Method(Engine, p, "elementFromPoint", 2, (self, _) => WrapOrNull(D(self).Body));
        Js.Method(Engine, p, "createEvent", 1, (self, args) => CreateLegacyEvent(Js.AsText(Js.Arg(args, 0))));
        Js.Method(Engine, p, "write", 1, (self, args) =>
        {
            // A write after the parser is done would replace the document; during a script it adds to the body, which is the common intent.
            if (D(self).Body is { } body)
                body.AppendChild(D(self).CreateFragment(string.Concat(args.Select(Js.AsText))));

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "writeln", 1, (self, args) =>
        {
            if (D(self).Body is { } body)
                body.AppendChild(D(self).CreateFragment(string.Concat(args.Select(Js.AsText)) + "\n"));

            return JsValue.Undefined;
        });
        foreach (var no in new[] { "open", "close" })
            Js.Method(Engine, p, no, 0, (self, _) => JsValue.Undefined);
    }

    private JsValue FontFaceSet()
    {
        var fonts = Js.NewObject(Engine);
        Js.Value(fonts, "ready", Engine.Evaluate("Promise.resolve()"));
        Js.Value(fonts, "status", Js.Str("loaded"));
        Js.Method(Engine, fonts, "load", 1, (_, _) => Engine.Evaluate("Promise.resolve([])"));
        Js.Method(Engine, fonts, "check", 1, (_, _) => JsBoolean.True);
        Js.Method(Engine, fonts, "forEach", 1, (_, _) => JsValue.Undefined);
        Js.Method(Engine, fonts, "add", 1, (_, _) => JsValue.Undefined);
        return fonts;
    }

    private void InstallStyle()
    {
        var p = StylePrototype;
        Js.Accessor(Engine, p, "cssText", self => Js.Str(Style(self).CssText), (self, value) => Style(self).CssText = Js.AsText(value));
        Js.Accessor(Engine, p, "length", self => Js.Num(Style(self).Length));
        Js.Method(Engine, p, "item", 1, (self, args) => Js.Str(Style(self).NameAt((int)TypeConverter.ToNumber(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "getPropertyValue", 1, (self, args) => Js.Str(Style(self).GetValue(Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "getPropertyPriority", 1, (self, args) => Js.Str(Style(self).GetPriority(Js.AsText(Js.Arg(args, 0)))));
        Js.Method(Engine, p, "setProperty", 2, (self, args) =>
        {
            Style(self).SetValue(Js.AsText(Js.Arg(args, 0)), Js.IsNullish(Js.Arg(args, 1)) ? string.Empty : Js.AsText(Js.Arg(args, 1)),
                Js.IsNullish(Js.Arg(args, 2)) ? string.Empty : Js.AsText(Js.Arg(args, 2)));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "removeProperty", 1, (self, args) => Js.Str(Style(self).RemoveValue(Js.AsText(Js.Arg(args, 0)))));
    }

    private StyleObject Style(JsValue self) => self as StyleObject ?? throw TypeError("Illegal invocation");

    /// <summary>A <c>DOMTokenList</c> over an element's classes, with indexed access.</summary>
    private sealed class TokenListObject : ObjectInstance
    {
        public TokenListObject(ScriptRuntime runtime, ScriptNode node, string attribute = "class") : base(runtime.Engine)
        {
            Prototype = runtime.TokenListPrototype;
            Node = node;
            Attribute = attribute;
        }

        public ScriptNode Node { get; }

        public string Attribute { get; }

        public override JsValue Get(JsValue property, JsValue receiver)
        {
            if (property.IsString() && int.TryParse(property.AsString(), out var index))
            {
                var classes = ParseTokens(Node.GetAttribute(Attribute));
                return index >= 0 && index < classes.Count ? Js.Str(classes[index]) : JsValue.Undefined;
            }

            return base.Get(property, receiver);
        }
    }

    // The place an element has on the page: filled from a layout when the host has one, otherwise empty.
    private (double X, double Y, double Width, double Height) LayoutOf(ScriptNode node) =>
        Document.GetLayoutBox(node) is { } box ? (box.X, box.Y, box.Width, box.Height) : (0, 0, 0, 0);

    private JsValue RectObject((double X, double Y, double Width, double Height) rect) =>
        Engine.Evaluate("(function (x, y, w, h) { return new DOMRect(x, y, w, h); })").Call(JsValue.Undefined, [Js.Num(rect.X), Js.Num(rect.Y), Js.Num(rect.Width), Js.Num(rect.Height)]);
}
