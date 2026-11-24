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

// The parts of the DOM standard the rest of the runtime builds on: DOMException, the event interface's legacy methods, the list types,
// character data, node ordering, and the interfaces that need no tree support beyond the node properties (AbortSignal, traversal).
internal sealed partial class ScriptRuntime
{
    private ObjectInstance _domException = null!;
    private ObjectInstance NodeListPrototype = null!;
    private ObjectInstance HtmlCollectionPrototype = null!;

    /// <summary>An exception a page can catch, as the DOM standard names it (<c>HierarchyRequestError</c>, <c>IndexSizeError</c>...).</summary>
    public JavaScriptException DomError(string name, string message) =>
        new(Engine.Construct(_domException, [Js.Str(message), Js.Str(name)]));

    private void InstallExtras()
    {
        var global = Engine.Global;

        // DOMException, list types and the other interfaces that are plain script over the node properties.
        _domException = Engine.Evaluate(ExtrasScripts.DomException).AsObject();
        Define(global, "DOMException", _domException);

        NodeListPrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Array.PrototypeObject };
        HtmlCollectionPrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Array.PrototypeObject };
        Js.Method(Engine, NodeListPrototype, "item", 1, (self, args) => ListItem(self, args));
        Js.Method(Engine, HtmlCollectionPrototype, "item", 1, (self, args) => ListItem(self, args));
        Js.Method(Engine, HtmlCollectionPrototype, "namedItem", 1, (self, args) =>
        {
            var name = Js.AsText(Js.Arg(args, 0));
            if (name.Length == 0 || self is not ObjectInstance list)
                return JsValue.Null;

            var length = (int)TypeConverter.ToNumber(list.Get("length"));
            for (var i = 0; i < length; i++)
            {
                if (list.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)) is NodeObject n && (n.Node.GetAttribute("id") == name || n.Node.GetAttribute("name") == name))
                    return n;
            }

            return JsValue.Null;
        });

        foreach (var (name, prototype) in new[] { ("NodeList", NodeListPrototype), ("HTMLCollection", HtmlCollectionPrototype) })
        {
            var captured = name;
            Define(global, name, Js.Constructor(Engine, name, prototype, 0, (_, _, _) => throw TypeError($"Illegal constructor: {captured}")));
        }

        // EventTarget, Text and DocumentFragment can be made with new.
        Define(global, "EventTarget", Js.Constructor(Engine, "EventTarget", EventTargetPrototype, 0, (self, _, _) =>
            new TargetObject(Engine, (self as ObjectInstance)?.Prototype ?? EventTargetPrototype)));
        Define(global, "Text", Js.Constructor(Engine, "Text", TextPrototype, 0, (_, args, _) =>
            Wrap(Document.CreateTextNode(args.Length == 0 ? string.Empty : Js.AsText(args[0])))));
        Define(global, "DocumentFragment", Js.Constructor(Engine, "DocumentFragment", FragmentPrototype, 0, (_, _, _) => Wrap(Document.CreateEmptyFragment())));

        InstallLegacyEventMethods();
        InstallCharacterData();
        InstallNodeOrdering();

        // The ChildNode and NonDocumentTypeChildNode operations text has as well as elements.
        foreach (var name in new[] { "remove", "before", "after", "replaceWith", "previousElementSibling", "nextElementSibling" })
        {
            if (ElementPrototype.GetOwnProperty(name) is { } descriptor && descriptor != PropertyDescriptor.Undefined)
                TextPrototype.FastSetProperty(name, descriptor);
        }

        var implementation = Js.NewObject(Engine);
        Js.Method(Engine, implementation, "hasFeature", 0, (_, _) => JsBoolean.True);
        foreach (var unsupported in new[] { "createHTMLDocument", "createDocument", "createDocumentType" })
        {
            var captured = unsupported;
            Js.Method(Engine, implementation, captured, 1, (_, _) => throw DomError("NotSupportedError", $"{captured} is not supported: a script cannot make another document."));
        }

        Js.Accessor(Engine, DocumentPrototype, "implementation", _ => implementation);

        // The interfaces written in script: AbortController/AbortSignal, NodeFilter, TreeWalker and NodeIterator.
        Engine.Execute(ExtrasScripts.Interfaces);

        // Object.prototype.toString shows the interface a page's object has.
        foreach (var (name, prototype) in new (string, ObjectInstance)[]
        {
            ("EventTarget", EventTargetPrototype), ("Node", NodePrototype), ("Element", ElementPrototype), ("Text", TextPrototype),
            ("Document", DocumentPrototype), ("DocumentFragment", FragmentPrototype), ("Event", EventPrototype), ("NodeList", NodeListPrototype),
            ("HTMLCollection", HtmlCollectionPrototype), ("CSSStyleDeclaration", StylePrototype), ("DOMTokenList", TokenListPrototype),
        })
            Tag(prototype, name);

        foreach (var (name, prototype) in _eventPrototypes)
            Tag(prototype, name);
        foreach (var (name, prototype) in _interfacePrototypes)
            Tag(prototype, name);
    }

    private static void Tag(ObjectInstance prototype, string name) =>
        prototype.FastSetProperty(GlobalSymbolRegistry.ToStringTag, new PropertyDescriptor(Js.Str(name), PropertyFlag.Configurable));

    private static void Define(ObjectInstance target, string name, JsValue value) =>
        target.FastSetProperty(name, new PropertyDescriptor(value, PropertyFlag.Writable | PropertyFlag.Configurable));

    private JsValue ListItem(JsValue self, JsValue[] args)
    {
        if (self is not ObjectInstance list)
            return JsValue.Null;

        var index = TypeConverter.ToNumber(Js.Arg(args, 0));
        var length = TypeConverter.ToNumber(list.Get("length"));
        return index >= 0 && index < length ? list.Get(((int)index).ToString(System.Globalization.CultureInfo.InvariantCulture)) : JsValue.Null;
    }

    // The event types document.createEvent knows, by their lower-cased interface name; each makes an event of that interface.
    private static readonly Dictionary<string, string> LegacyEventInterfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["event"] = "Event", ["events"] = "Event", ["htmlevents"] = "Event", ["customevent"] = "CustomEvent", ["uievent"] = "UIEvent", ["uievents"] = "UIEvent",
        ["mouseevent"] = "MouseEvent", ["mouseevents"] = "MouseEvent", ["keyboardevent"] = "KeyboardEvent", ["focusevent"] = "FocusEvent",
        ["hashchangeevent"] = "HashChangeEvent", ["messageevent"] = "MessageEvent", ["touchevent"] = "TouchEvent", ["wheelevent"] = "WheelEvent",
    };

    private JsValue CreateLegacyEvent(string interfaceName)
    {
        if (!LegacyEventInterfaces.TryGetValue(interfaceName, out var name) || !_eventPrototypes.TryGetValue(name, out var prototype))
            throw DomError("NotSupportedError", $"The event interface '{interfaceName}' is not supported.");

        var ev = new EventObject(Engine, prototype, string.Empty, false, false) { TimeStamp = NowMs, Initialized = false };
        if (name == "CustomEvent")
            ev.FastSetProperty("detail", new PropertyDescriptor(JsValue.Null, PropertyFlag.ConfigurableEnumerableWritable));
        return ev;
    }

    private void InstallLegacyEventMethods()
    {
        var p = EventPrototype;
        Js.Method(Engine, p, "initEvent", 1, (self, args) =>
        {
            Init(Ev(self), args);
            return JsValue.Undefined;
        });

        if (_eventPrototypes.TryGetValue("CustomEvent", out var custom))
        {
            Js.Method(Engine, custom, "initCustomEvent", 1, (self, args) =>
            {
                var ev = Ev(self);
                Init(ev, args);
                if (!ev.Dispatching)
                    ev.FastSetProperty("detail", new PropertyDescriptor(args.Length > 3 ? args[3] : JsValue.Null, PropertyFlag.ConfigurableEnumerableWritable));
                return JsValue.Undefined;
            });
        }

        Js.Accessor(Engine, p, "cancelBubble", self => Js.Bool(Ev(self).PropagationStopped), (self, value) =>
        {
            if (TypeConverter.ToBoolean(value)) Ev(self).PropagationStopped = true;
        });
        Js.Accessor(Engine, p, "returnValue", self => Js.Bool(!Ev(self).DefaultPrevented), (self, value) =>
        {
            if (!TypeConverter.ToBoolean(value) && Ev(self) is { Cancelable: true } ev) ev.DefaultPrevented = true;
        });
        Js.Accessor(Engine, p, "composed", _ => JsBoolean.False);

        foreach (var (name, value) in new[] { ("NONE", 0), ("CAPTURING_PHASE", 1), ("AT_TARGET", 2), ("BUBBLING_PHASE", 3) })
        {
            p.FastSetProperty(name, new PropertyDescriptor(Js.Num(value), PropertyFlag.AllForbidden));
            if (Engine.Global.Get("Event") is ObjectInstance constructor)
                constructor.FastSetProperty(name, new PropertyDescriptor(Js.Num(value), PropertyFlag.AllForbidden));
        }

        void Init(EventObject ev, JsValue[] args)
        {
            if (args.Length == 0)
                throw TypeError("Failed to execute 'initEvent' on 'Event': 1 argument required, but only 0 present.");
            if (ev.Dispatching)
                return;

            ev.EventType = Js.AsText(args[0]);
            ev.Bubbles = TypeConverter.ToBoolean(Js.Arg(args, 1));
            ev.Cancelable = TypeConverter.ToBoolean(Js.Arg(args, 2));
            ev.PropagationStopped = false;
            ev.ImmediatePropagationStopped = false;
            ev.DefaultPrevented = false;
            ev.IsTrusted = false;
            ev.Target = null;
            ev.Initialized = true;
        }
    }

    // CharacterData: the text a node holds, read and changed by offset (in UTF-16 code units, as script sees strings).
    private void InstallCharacterData()
    {
        var p = TextPrototype;
        string DataOf(JsValue self) => Sn(self).Data ?? string.Empty;

        int Offset(JsValue value, string data)
        {
            var offset = TypeConverter.ToUint32(value);
            if (offset > data.Length)
                throw DomError("IndexSizeError", "The offset is greater than the length of the data.");
            return (int)offset;
        }

        void Replace(JsValue self, JsValue[] args, string replacement)
        {
            var data = DataOf(self);
            var offset = Offset(Js.Arg(args, 0), data);
            var count = (int)Math.Min(TypeConverter.ToUint32(Js.Arg(args, 1)), (uint)(data.Length - offset));
            Sn(self).Data = string.Concat(data.AsSpan(0, offset), replacement, data.AsSpan(offset + count));
        }

        Js.Method(Engine, p, "substringData", 2, (self, args) =>
        {
            var data = DataOf(self);
            var offset = Offset(Js.Arg(args, 0), data);
            var count = (int)Math.Min(TypeConverter.ToUint32(Js.Arg(args, 1)), (uint)(data.Length - offset));
            return Js.Str(data.Substring(offset, count));
        });
        Js.Method(Engine, p, "appendData", 1, (self, args) =>
        {
            Sn(self).Data = DataOf(self) + Js.AsText(Js.Arg(args, 0));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "insertData", 2, (self, args) =>
        {
            Replace(self, [Js.Arg(args, 0), Js.Num(0)], Js.AsText(Js.Arg(args, 1)));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "deleteData", 2, (self, args) =>
        {
            Replace(self, args, string.Empty);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "replaceData", 3, (self, args) =>
        {
            Replace(self, args, Js.AsText(Js.Arg(args, 2)));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "splitText", 1, (self, args) =>
        {
            var node = Sn(self);
            var data = DataOf(self);
            var offset = Offset(Js.Arg(args, 0), data);
            var tail = Document.CreateTextNode(data[offset..]);
            var next = node.NextSibling;
            node.Data = data[..offset];
            node.Parent?.InsertBefore(tail, next);
            return Wrap(tail);
        });
    }

    private const int Disconnected = 1, Preceding = 2, Following = 4, Contains = 8, ContainedBy = 16, ImplementationSpecific = 32;

    private void InstallNodeOrdering()
    {
        var p = NodePrototype;
        foreach (var (name, value) in new[]
        {
            ("DOCUMENT_POSITION_DISCONNECTED", Disconnected), ("DOCUMENT_POSITION_PRECEDING", Preceding), ("DOCUMENT_POSITION_FOLLOWING", Following),
            ("DOCUMENT_POSITION_CONTAINS", Contains), ("DOCUMENT_POSITION_CONTAINED_BY", ContainedBy), ("DOCUMENT_POSITION_IMPLEMENTATION_SPECIFIC", ImplementationSpecific),
        })
        {
            p.FastSetProperty(name, new PropertyDescriptor(Js.Num(value), PropertyFlag.AllForbidden));
        }

        Js.Method(Engine, p, "compareDocumentPosition", 1, (self, args) =>
        {
            var other = NodeArg(args, 0);
            return Js.Num(ComparePosition(Sn(self), other));
        });
    }

    // The position of other relative to node, as compareDocumentPosition reports it.
    private static int ComparePosition(ScriptNode node, ScriptNode other)
    {
        if (ReferenceEquals(node, other))
            return 0;

        var mine = Ancestry(node);
        var theirs = Ancestry(other);
        if (!ReferenceEquals(mine[0], theirs[0]))
        {
            // Different trees: an order that stays the same from one call to the next.
            var order = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(mine[0]) < System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(theirs[0]) ? Following : Preceding;
            return Disconnected | ImplementationSpecific | order;
        }

        var shared = 0;
        while (shared < mine.Count && shared < theirs.Count && ReferenceEquals(mine[shared], theirs[shared]))
            shared++;

        if (shared == mine.Count)
            return ContainedBy | Following;
        if (shared == theirs.Count)
            return Contains | Preceding;

        var parent = mine[shared - 1];
        var siblings = parent.Children;
        return IndexOf(siblings, theirs[shared]) < IndexOf(siblings, mine[shared]) ? Preceding : Following;

        static int IndexOf(IReadOnlyList<ScriptNode> list, ScriptNode item)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item))
                    return i;
            }

            return -1;
        }
    }

    // The node and its ancestors, root first.
    private static List<ScriptNode> Ancestry(ScriptNode node)
    {
        var path = new List<ScriptNode>();
        for (var current = node; current is not null; current = current.Parent)
            path.Insert(0, current);
        return path;
    }
}
