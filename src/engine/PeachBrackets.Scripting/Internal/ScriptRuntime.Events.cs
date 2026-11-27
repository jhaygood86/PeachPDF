using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

internal sealed partial class ScriptRuntime
{
    // The event types that get an on<type> property on every element, the document and the window.
    private static readonly string[] HandlerTypes =
    [
        "click", "dblclick", "mousedown", "mouseup", "mouseover", "mouseout", "mousemove", "mouseenter", "mouseleave", "contextmenu", "wheel",
        "keydown", "keyup", "keypress", "input", "change", "submit", "reset", "focus", "blur", "focusin", "focusout", "select",
        "load", "error", "abort", "scroll", "resize", "pointerdown", "pointerup", "pointermove", "touchstart", "touchend", "touchmove",
        "transitionend", "transitionstart", "transitionrun", "transitioncancel", "animationstart", "animationend", "animationiteration", "animationcancel",
        "readystatechange", "complete", "paint", "elementgeometryupdate", "visibilitychange", "hashchange", "popstate", "pageshow", "pagehide", "unload", "beforeunload", "message", "toggle",
        // The media events GlobalEventHandlers names, which a media element's script listens for with the attribute as often as with addEventListener.
        "loadstart", "progress", "suspend", "emptied", "stalled", "loadedmetadata", "loadeddata", "canplay", "canplaythrough", "playing", "waiting", "seeking", "seeked",
        "ended", "durationchange", "timeupdate", "play", "pause", "ratechange", "volumechange",
    ];

    // Events that go to the window when written as attributes of the body.
    private static readonly HashSet<string> BodyForwarded =
        ["load", "error", "resize", "scroll", "hashchange", "popstate", "pageshow", "pagehide", "unload", "beforeunload", "message", "blur", "focus"];

    private ObjectInstance EventTargetPrototype = null!;
    private ObjectInstance EventPrototype = null!;
    private readonly Dictionary<string, ObjectInstance> _eventPrototypes = [];

    public EventObject CreateEvent(string type, bool bubbles, bool cancelable) => new(Engine, EventPrototype, type, bubbles, cancelable) { TimeStamp = NowMs };

    private void InstallEventPrototypes(ObjectInstance objectPrototype)
    {
        EventTargetPrototype = new JsObject(Engine);
        EventTargetPrototype.Prototype = objectPrototype;

        Js.Method(Engine, EventTargetPrototype, "addEventListener", 2, (self, args) =>
        {
            var target = Target(self);
            var type = Js.AsText(Js.Arg(args, 0));
            var callback = Js.Arg(args, 1);
            if (Js.IsNullish(callback))
                return JsValue.Undefined;

            var capture = false;
            var once = false;
            bool? passive = null;
            JsValue signal = JsValue.Undefined;
            var options = Js.Arg(args, 2);
            if (options.IsObject())
            {
                capture = Jint.Runtime.TypeConverter.ToBoolean(options.AsObject().Get("capture"));
                once = Jint.Runtime.TypeConverter.ToBoolean(options.AsObject().Get("once"));
                if (options.AsObject().Get("passive") is { } passiveValue && !passiveValue.IsUndefined())
                    passive = Jint.Runtime.TypeConverter.ToBoolean(passiveValue);
                signal = options.AsObject().Get("signal");
                if (signal.IsObject() && Jint.Runtime.TypeConverter.ToBoolean(signal.AsObject().Get("aborted")))
                    return JsValue.Undefined;
            }
            else if (!options.IsUndefined())
            {
                capture = Jint.Runtime.TypeConverter.ToBoolean(options);
            }

            if (!target.Listeners.TryGetValue(type, out var list))
                target.Listeners[type] = list = [];

            // touchstart, touchmove, wheel and mousewheel listeners on the window, the document, its element and its body are passive unless said otherwise.
            passive ??= type is "touchstart" or "touchmove" or "wheel" or "mousewheel" && IsDefaultPassiveTarget(target);
            if (!list.Any(l => l.Callback == callback && l.Capture == capture))
            {
                var added = new Listener(callback, capture, once, passive.Value);
                list.Add(added);
                if (signal.IsObject())
                {
                    var removal = Js.Function(Engine, "removeListener", 0, (_, _) =>
                    {
                        list.Remove(added);
                        return JsValue.Undefined;
                    });
                    signal.AsObject().Get("addEventListener").Call(signal, [Js.Str("abort"), removal]);
                }
            }

            return JsValue.Undefined;
        });

        Js.Method(Engine, EventTargetPrototype, "removeEventListener", 2, (self, args) =>
        {
            var target = Target(self);
            var capture = Js.Arg(args, 2) is var options && options.IsObject()
                ? Jint.Runtime.TypeConverter.ToBoolean(options.AsObject().Get("capture"))
                : Jint.Runtime.TypeConverter.ToBoolean(Js.Arg(args, 2));
            if (target.Listeners.TryGetValue(Js.AsText(Js.Arg(args, 0)), out var list))
                list.RemoveAll(l => l.Callback == Js.Arg(args, 1) && l.Capture == capture);

            return JsValue.Undefined;
        });

        Js.Method(Engine, EventTargetPrototype, "dispatchEvent", 1, (self, args) =>
        {
            if (Js.Arg(args, 0) is not EventObject ev)
                throw TypeError("Failed to execute 'dispatchEvent' on 'EventTarget': parameter 1 is not of type 'Event'.");
            if (ev.Dispatching || !ev.Initialized)
                throw DomError("InvalidStateError", "The event is already being dispatched or was never initialized.");

            return Js.Bool(DispatchEvent(Target(self), ev));
        });

        foreach (var type in HandlerTypes)
        {
            var captured = type;
            Js.Accessor(Engine, EventTargetPrototype, "on" + captured,
                self => Target(self).Handlers.TryGetValue(captured, out var handler) ? handler : JsValue.Null,
                (self, value) =>
                {
                    if (Js.IsCallable(value)) Target(self).Handlers[captured] = value;
                    else Target(self).Handlers.Remove(captured);
                });
        }

        EventPrototype = new JsObject(Engine);
        EventPrototype.Prototype = objectPrototype;
        Js.Accessor(Engine, EventPrototype, "type", self => Js.Str(Ev(self).EventType));
        Js.Accessor(Engine, EventPrototype, "target", self => Expose(Ev(self).Target));
        Js.Accessor(Engine, EventPrototype, "srcElement", self => Expose(Ev(self).Target));
        Js.Accessor(Engine, EventPrototype, "currentTarget", self => Expose(Ev(self).CurrentTarget));
        Js.Accessor(Engine, EventPrototype, "eventPhase", self => Js.Num(Ev(self).Phase));
        Js.Accessor(Engine, EventPrototype, "bubbles", self => Js.Bool(Ev(self).Bubbles));
        Js.Accessor(Engine, EventPrototype, "cancelable", self => Js.Bool(Ev(self).Cancelable));
        Js.Accessor(Engine, EventPrototype, "defaultPrevented", self => Js.Bool(Ev(self).DefaultPrevented));
        Js.Accessor(Engine, EventPrototype, "isTrusted", self => Js.Bool(Ev(self).IsTrusted));
        Js.Accessor(Engine, EventPrototype, "timeStamp", self => Js.Num(Ev(self).TimeStamp));
        Js.Method(Engine, EventPrototype, "preventDefault", 0, (self, _) =>
        {
            if (Ev(self) is { Cancelable: true, InPassiveListener: false } ev) ev.DefaultPrevented = true;
            return JsValue.Undefined;
        });
        Js.Method(Engine, EventPrototype, "stopPropagation", 0, (self, _) =>
        {
            Ev(self).PropagationStopped = true;
            return JsValue.Undefined;
        });
        Js.Method(Engine, EventPrototype, "stopImmediatePropagation", 0, (self, _) =>
        {
            Ev(self).PropagationStopped = true;
            Ev(self).ImmediatePropagationStopped = true;
            return JsValue.Undefined;
        });
        Js.Method(Engine, EventPrototype, "composedPath", 0, (self, _) =>
            Js.Array(Engine, [.. PathOf(Ev(self).Target ?? Window).Select(Expose)]));
    }

    private bool IsDefaultPassiveTarget(TargetObject target) =>
        target is WindowObject || target is NodeObject { Node: var node } && (node.Kind == ScriptNodeKind.Document || node.Equals(node.OwnerDocument.DocumentElement) || node.Equals(node.OwnerDocument.Body));

    private TargetObject Target(JsValue self) =>
        self as TargetObject ?? (ReferenceEquals(self, Engine.Global) || self.IsUndefined() || self.IsNull() ? Window : throw TypeError("Illegal invocation"));

    // What a page sees for a target: the window is the global object.
    private JsValue Expose(TargetObject? target) => target is null ? JsValue.Null : target is WindowObject ? Engine.Global : target;

    private EventObject Ev(JsValue self) => self as EventObject ?? throw TypeError("Illegal invocation");

    /// <summary>Makes the <c>Event</c>-like constructors a page can call, one for each name, all sharing the event prototype chain.</summary>
    private void InstallEventConstructors()
    {
        foreach (var (name, parent, members) in EventInterfaces)
        {
            var prototype = name == "Event" ? EventPrototype : new JsObject(Engine);
            if (name != "Event") prototype.Prototype = _eventPrototypes[parent];

            var captured = name;
            _eventPrototypes[name] = prototype;
            var constructor = Js.Constructor(Engine, name, prototype, 1, (self, args, _) =>
            {
                if (args.Length == 0)
                    throw TypeError($"Failed to construct '{captured}': 1 argument required, but only 0 present.");

                var type = Js.AsText(Js.Arg(args, 0));
                var init = Js.Arg(args, 1);
                var bubbles = false;
                var cancelable = false;
                if (init.IsObject())
                {
                    bubbles = Jint.Runtime.TypeConverter.ToBoolean(init.AsObject().Get("bubbles"));
                    cancelable = Jint.Runtime.TypeConverter.ToBoolean(init.AsObject().Get("cancelable"));
                }

                var ev = new EventObject(Engine, (self as ObjectInstance)?.Prototype ?? prototype, type, bubbles, cancelable) { TimeStamp = NowMs };
                var handled = ApplyEventMembers(ev, captured, init.IsObject() ? init.AsObject() : null);
                if (init.IsObject())
                {
                    foreach (var key in init.AsObject().GetOwnPropertyKeys())
                    {
                        if (key.IsString() && key.AsString() is var member && member is not ("bubbles" or "cancelable") && !handled.Contains(member))
                            ev.FastSetProperty(member, new PropertyDescriptor(init.AsObject().Get(key), PropertyFlag.ConfigurableEnumerableWritable));
                    }
                }

                return ev;
            });

            Engine.Global.FastSetProperty(name, new PropertyDescriptor(constructor, PropertyFlag.Writable | PropertyFlag.Configurable));
        }
    }

    // The members each event interface adds to its parent's: name:default, where the default is n (null), 0, e (empty string), f (false) or a (empty array).
    private static readonly (string Name, string Parent, string Members)[] EventInterfaces =
    [
        ("Event", "", ""), ("CustomEvent", "Event", "detail:n"), ("UIEvent", "Event", "view:n detail:0"),
        ("MouseEvent", "UIEvent", "screenX:0 screenY:0 clientX:0 clientY:0 ctrlKey:f shiftKey:f altKey:f metaKey:f button:0 buttons:0 relatedTarget:n"),
        ("KeyboardEvent", "UIEvent", "key:e code:e location:0 repeat:f isComposing:f ctrlKey:f shiftKey:f altKey:f metaKey:f"),
        ("FocusEvent", "UIEvent", "relatedTarget:n"), ("InputEvent", "UIEvent", "data:n isComposing:f inputType:e"),
        ("PointerEvent", "MouseEvent", "pointerId:0 width:1 height:1 pressure:0 tangentialPressure:0 tiltX:0 tiltY:0 twist:0 pointerType:e isPrimary:f"),
        ("WheelEvent", "MouseEvent", "deltaX:0 deltaY:0 deltaZ:0 deltaMode:0"), ("DragEvent", "MouseEvent", "dataTransfer:n"),
        ("TouchEvent", "UIEvent", "ctrlKey:f shiftKey:f altKey:f metaKey:f"), ("CompositionEvent", "UIEvent", "data:e"), ("TextEvent", "UIEvent", "data:e"),
        ("TransitionEvent", "Event", "propertyName:e elapsedTime:0 pseudoElement:e"), ("AnimationEvent", "Event", "animationName:e elapsedTime:0 pseudoElement:e"),
        ("ErrorEvent", "Event", "message:e filename:e lineno:0 colno:0 error:n"), ("MessageEvent", "Event", "data:n origin:e lastEventId:e source:n ports:a"),
        ("ProgressEvent", "Event", "lengthComputable:f loaded:0 total:0"), ("ToggleEvent", "Event", "oldState:e newState:e source:n"), ("HashChangeEvent", "Event", "oldURL:e newURL:e"),
        ("PageTransitionEvent", "Event", "persisted:f"), ("StorageEvent", "Event", "key:n oldValue:n newValue:n url:e storageArea:n"),
        ("BeforeUnloadEvent", "Event", "returnValue:e"), ("DeviceMotionEvent", "Event", "acceleration:n accelerationIncludingGravity:n rotationRate:n interval:0"),
        ("DeviceOrientationEvent", "Event", "alpha:n beta:n gamma:n absolute:f"),
    ];

    // Defines the members of an event interface (and its parents') on the event: the init dictionary's value converted to the member's type, or its default.
    private HashSet<string> ApplyEventMembers(EventObject ev, string interfaceName, ObjectInstance? init)
    {
        var handled = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<string>();
        for (var current = interfaceName; current.Length > 0; current = EventInterfaces.First(e => e.Name == current).Parent)
            chain.Insert(0, current);

        foreach (var interfaceInChain in chain)
        {
            foreach (var spec in EventInterfaces.First(e => e.Name == interfaceInChain).Members.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = spec.Split(':');
                var member = parts[0];
                var given = init is not null && init.Get(member) is { } value && !value.IsUndefined() ? value : (JsValue?)null;
                JsValue result = parts[1] switch
                {
                    "0" => Js.Num(given is { } n ? Jint.Runtime.TypeConverter.ToNumber(n) : 0),
                    "e" => Js.Str(given is { } t ? Js.AsText(t) : string.Empty),
                    "f" => Js.Bool(given is { } b && Jint.Runtime.TypeConverter.ToBoolean(b)),
                    "a" => given ?? Js.Array(Engine, []),
                    _ => given ?? JsValue.Null,
                };
                ev.FastSetProperty(member, new PropertyDescriptor(result, PropertyFlag.ConfigurableEnumerableWritable));
                handled.Add(member);
            }
        }

        return handled;
    }

    /// <summary>The targets an event aimed at <paramref name="target"/> passes through, from the target outwards.</summary>
    private static List<TargetObject> PathOf(TargetObject target)
    {
        var path = new List<TargetObject>();
        for (var current = target; current is not null && path.Count < 10_000; current = current.ParentTarget)
            path.Add(current);

        return path;
    }

    public void FireEvent(TargetObject target, string type, bool bubbles, bool cancelable) => DispatchEvent(target, CreateEvent(type, bubbles, cancelable));

    /// <summary>Sends an event along its path: the capturing listeners from the outside in, the target's, and the bubbling ones from the inside out.</summary>
    /// <returns>false when a listener cancelled it</returns>
    // The last events delivered, newest last, for the diagnostic a run ends with.
    private readonly Queue<string> _recentEvents = new();

    private string DescribeTarget(TargetObject target) => target switch
    {
        WindowObject => "window",
        NodeObject { Node.Kind: ScriptNodeKind.Document } => "document",
        NodeObject { Node: var node } => (node.TagName ?? node.Kind.ToString().ToLowerInvariant()) + (node.Id.Length > 0 ? "#" + node.Id : string.Empty),
        _ => "target",
    };

    public bool DispatchEvent(TargetObject target, EventObject ev)
    {
        _recentEvents.Enqueue(ev.EventType + "@" + DescribeTarget(target));
        while (_recentEvents.Count > 8)
            _recentEvents.Dequeue();

        ev.Target = target;
        ev.Dispatching = true;
        var path = PathOf(target);

        // A click on (or, when it bubbles, inside) a checkbox is its activation: the box is toggled before the listeners run and put back if one cancels it.
        var activation = ev.EventType == "click" && IsMouseEvent(ev) ? ActivationTarget(path, ev.Bubbles) : null;
        var toggled = activation is { Node: var box } && string.Equals(box.TagName, "input", StringComparison.OrdinalIgnoreCase)
            && string.Equals(box.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase);
        var wasChecked = toggled && activation!.Node.HasAttribute("checked");
        if (toggled)
        {
            if (wasChecked) activation!.Node.RemoveAttribute("checked");
            else activation!.Node.SetAttribute("checked", string.Empty);
        }

        ev.Phase = 1;
        for (var i = path.Count - 1; i >= 1 && !ev.PropagationStopped; i--)
            Invoke(path[i], ev, capturing: true);

        if (!ev.PropagationStopped)
        {
            ev.Phase = 2;
            Invoke(path[0], ev, capturing: null);
        }

        if (ev.Bubbles)
        {
            ev.Phase = 3;
            for (var i = 1; i < path.Count && !ev.PropagationStopped; i++)
                Invoke(path[i], ev, capturing: false);
        }

        ev.Phase = 0;
        ev.Dispatching = false;
        ev.CurrentTarget = null;
        if (toggled)
        {
            if (ev.DefaultPrevented)
            {
                if (wasChecked) activation!.Node.SetAttribute("checked", string.Empty);
                else activation!.Node.RemoveAttribute("checked");
            }
            else
            {
                FireEvent(activation!, "input", bubbles: true, cancelable: false);
                FireEvent(activation!, "change", bubbles: true, cancelable: false);
            }
        }

        return !ev.DefaultPrevented;
    }

    private bool IsMouseEvent(EventObject ev)
    {
        for (var prototype = ev.Prototype; prototype is not null; prototype = prototype.Prototype)
        {
            if (ReferenceEquals(prototype, _eventPrototypes["MouseEvent"]))
                return true;
        }

        return false;
    }

    // The first node on the way out from the target with an activation behavior (a link, a button, a form control); only the target when the event does not bubble.
    private static NodeObject? ActivationTarget(List<TargetObject> path, bool bubbles)
    {
        foreach (var candidate in path)
        {
            if (candidate is NodeObject { Node: { Kind: ScriptNodeKind.Element } node } element)
            {
                var tag = node.TagName?.ToLowerInvariant();
                var enabled = !node.HasAttribute("disabled");
                if (enabled && (tag is "button" or "select" or "textarea" or "summary" or "label" || (tag == "input" && !string.Equals(node.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase)) || (tag is "a" or "area" && node.HasAttribute("href"))))
                    return element;
            }

            if (!bubbles)
                break;
        }

        return null;
    }

    // capturing: true runs the capture listeners, false the others, null (at the target) all of them.
    private void Invoke(TargetObject target, EventObject ev, bool? capturing)
    {
        ev.CurrentTarget = target;

        if (target.Listeners.TryGetValue(ev.EventType, out var registered))
        {
            foreach (var listener in registered.ToArray())
            {
                if (capturing is { } wanted && listener.Capture != wanted)
                    continue;

                if (listener.Once)
                    registered.Remove(listener);

                ev.InPassiveListener = listener.Passive;
                try
                {
                    CallListener(listener.Callback, target, ev);
                }
                finally
                {
                    ev.InPassiveListener = false;
                }

                if (ev.ImmediatePropagationStopped)
                    return;
            }
        }

        // The on<type> handler runs where the bubbling listeners do.
        if (capturing != true && HandlerFor(target, ev.EventType) is { } handler)
        {
            var result = Call(handler, Expose(target), ev);
            if (result is { } value && value.IsBoolean() && !value.AsBoolean() && ev.Cancelable && ev.EventType != "error")
                ev.DefaultPrevented = true;
        }
    }

    private void CallListener(JsValue callback, TargetObject target, EventObject ev)
    {
        if (Js.IsCallable(callback))
        {
            Call(callback, Expose(target), ev);
        }
        else if (callback.IsObject() && callback.AsObject().Get("handleEvent") is var handle && Js.IsCallable(handle))
        {
            Call(handle, callback, ev);
        }
    }

    private JsValue? HandlerFor(TargetObject target, string type)
    {
        if (target.Handlers.TryGetValue(type, out var assigned))
            return assigned;

        // A handler written as an attribute, compiled the first time it is needed. The window's own come from the body's attributes.
        var element = target switch
        {
            NodeObject node when node.Node.Kind == ScriptNodeKind.Element => node,
            WindowObject when BodyForwarded.Contains(type) && Document.Body is { } body => Wrap(body),
            _ => null,
        };

        if (element is null || element.Node.GetAttribute("on" + type) is not { Length: > 0 } code)
            return null;

        if (element.AttributeHandlers.TryGetValue(type, out var cached) && cached.Code == code)
            return cached.Function;

        try
        {
            var function = Engine.Evaluate("(function(event){" + code + "\n})");
            element.AttributeHandlers[type] = (code, function);
            return function;
        }
        catch (Exception error) when (IsScriptFailure(error))
        {
            ReportError(error);
            return null;
        }
    }
}
