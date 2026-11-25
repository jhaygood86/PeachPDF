using Jint;
using Jint.Native;
using Jint.Native.Object;
using PeachBrackets.ScriptHosting;
using System.Collections.Generic;

namespace PeachBrackets.Scripting.Internal;

/// <summary>The name an element has in a namespace.</summary>
internal sealed record ElementNamespace(string? Namespace, string? Prefix, string LocalName);

/// <summary>One registered listener.</summary>
internal sealed record Listener(JsValue Callback, bool Capture, bool Once);

/// <summary>An object events can be sent to: a node, the document or the window.</summary>
internal class TargetObject : ObjectInstance
{
    public TargetObject(Engine engine, ObjectInstance prototype) : base(engine)
    {
        Prototype = prototype;
    }

    /// <summary>The listeners added with <c>addEventListener</c>, by event type.</summary>
    public Dictionary<string, List<Listener>> Listeners { get; } = [];

    /// <summary>The <c>on&lt;type&gt;</c> handlers a script assigned, by event type.</summary>
    public Dictionary<string, JsValue> Handlers { get; } = [];

    /// <summary>The next target an event goes to on its way out, or null.</summary>
    public virtual TargetObject? ParentTarget => null;
}

/// <summary>A node of the document, as a script sees it.</summary>
internal sealed class NodeObject : TargetObject
{
    private readonly ScriptRuntime _runtime;

    public NodeObject(ScriptRuntime runtime, ScriptNode node, ObjectInstance prototype) : base(runtime.Engine, prototype)
    {
        _runtime = runtime;
        Node = node;
    }

    public ScriptNode Node { get; }

    /// <summary>The element's <c>classList</c> and <c>style</c> objects, made when first read so the same object comes back each time.</summary>
    public ObjectInstance? ClassList { get; set; }

    public Dictionary<string, ObjectInstance>? TokenLists { get; set; }

    public ObjectInstance? Style { get; set; }

    /// <summary>The namespace, prefix and local name of an element made with <c>createElementNS</c>; null for one the parser made.</summary>
    public ElementNamespace? NamespaceInfo { get; set; }

    /// <summary>The namespace, prefix and local name of each attribute set with a namespace, by qualified name; an attribute not listed has none.</summary>
    public Dictionary<string, ElementNamespace> AttributeNames { get; } = [];

    /// <summary>The <c>Attr</c> objects made for the element's attributes, by qualified name, so the same attribute is the same object.</summary>
    public Dictionary<string, AttrObject> AttrNodes { get; } = [];

    /// <summary>The node's <c>childNodes</c> and <c>children</c> lists, made when first read: each is one object for the life of the node.</summary>
    public JsValue? ChildNodesList { get; set; }

    public JsValue? ChildrenList { get; set; }

    /// <summary>The element's <c>attributes</c> object.</summary>
    public ObjectInstance? AttributeMap { get; set; }

    /// <summary>The functions the element's <c>on&lt;type&gt;</c> attributes compiled to, by event type and the code they came from.</summary>
    public Dictionary<string, (string Code, JsValue Function)> AttributeHandlers { get; } = [];

    public override TargetObject? ParentTarget =>
        Node.Kind == ScriptNodeKind.Document ? _runtime.Window : Node.Parent is { } parent ? _runtime.Wrap(parent) : null;
}

/// <summary>The window: the global object's face and the last stop of an event's way out.</summary>
internal sealed class WindowObject(Engine engine, ObjectInstance prototype) : TargetObject(engine, prototype);

/// <summary>An event, as a script sees it.</summary>
internal sealed class EventObject : ObjectInstance
{
    public EventObject(Engine engine, ObjectInstance prototype, string type, bool bubbles, bool cancelable) : base(engine)
    {
        Prototype = prototype;
        EventType = type;
        Bubbles = bubbles;
        Cancelable = cancelable;
    }

    public string EventType { get; set; }

    public bool Bubbles { get; set; }

    public bool Cancelable { get; set; }

    /// <summary>False for an event <c>document.createEvent</c> made until <c>initEvent</c> is called; such an event cannot be dispatched.</summary>
    public bool Initialized { get; set; } = true;

    /// <summary>Whether the event is being dispatched now.</summary>
    public bool Dispatching { get; set; }

    public TargetObject? Target { get; set; }

    public TargetObject? CurrentTarget { get; set; }

    public int Phase { get; set; }

    public bool DefaultPrevented { get; set; }

    public bool PropagationStopped { get; set; }

    public bool ImmediatePropagationStopped { get; set; }

    public bool IsTrusted { get; set; }

    public double TimeStamp { get; set; }
}
