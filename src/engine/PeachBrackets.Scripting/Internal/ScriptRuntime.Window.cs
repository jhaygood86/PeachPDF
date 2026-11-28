using Microsoft.Extensions.Logging;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Internal;

internal sealed partial class ScriptRuntime
{
    // One frame of the virtual clock, as a browser paints at 60 Hz.
    private const double FrameMs = 1000d / 60d;

    private sealed record Timer(int Id, double Due, long Order, JsValue Callback, JsValue[] Args, double? Interval, bool IsFrame);

    private readonly List<Timer> _timers = [];
    private int _nextTimerId = 1;
    private long _order;
    private int _callbacks;

    /// <summary>The virtual clock, in milliseconds since the document started.</summary>
    public double NowMs { get; private set; }

    private int AddTimer(JsValue callback, double delay, JsValue[] args, bool repeat, bool frame)
    {
        if (!Js.IsCallable(callback))
            return 0;

        var id = _nextTimerId++;
        var wait = double.IsFinite(delay) ? Math.Max(delay, 0) : 0;
        _timers.Add(new Timer(id, frame ? NextFrameAfter(NowMs) : NowMs + Math.Max(wait, repeat ? 1 : 0), _order++, callback, args, repeat ? Math.Max(wait, 1) : null, frame));
        return id;
    }

    private static double NextFrameAfter(double now) => (Math.Floor(now / FrameMs) + 1) * FrameMs;

    private void ClearTimer(JsValue id)
    {
        if (id.IsNumber())
            _timers.RemoveAll(t => t.Id == (int)id.AsNumber());
    }

    /// <summary>Plays the timers and animation frames in order of their time, until none is left or a limit is reached.</summary>
    private async ValueTask PlayTimersAsync()
    {
        var limit = _options.MaxVirtualTime.TotalMilliseconds;
        while (true)
        {
            // Requests the scripts have sent are answered before the clock moves on: they take no virtual time, and are delivered as a zero-delay timer.
            await DeliverRequestsAsync();
            var workersMoved = DeliverPortMessages() | await PumpWorkersAsync(NowMs);
            if (workersMoved)
                continue;

            // A worker's timer that is due before the page's next one comes first, the clock moving to it.
            var nextPage = _timers.Count > 0 && _callbacks < _options.MaxCallbacks ? _timers.Min(t => t.Due) : (double?)null;
            if (NextWorkerWake() is { } wake && wake <= limit && wake > NowMs && (nextPage is null || wake < nextPage))
            {
                NowMs = wake;
                if (await PumpWorkersAsync(NowMs))
                    continue;
            }

            if (nextPage is null)
                break;

            // The earliest timer, found by one scan: a page can have thousands waiting, and sorting them for each callback is quadratic.
            var next = _timers[0];
            foreach (var candidate in _timers)
            {
                if (candidate.Due < next.Due || (candidate.Due == next.Due && candidate.Order < next.Order))
                    next = candidate;
            }

            if (next.Due > limit)
                break;

            NowMs = Math.Max(NowMs, next.Due);
            if (next.IsFrame)
            {
                // Every callback asked for before this frame starts runs in it, with the same timestamp.
                var frame = _timers.Where(t => t.IsFrame && t.Due <= NowMs).OrderBy(t => t.Order).ToList();
                foreach (var callback in frame)
                {
                    _timers.Remove(callback);
                    _callbacks++;
                    Call(callback.Callback, Engine.Global, Js.Num(NowMs));
                }
            }
            else
            {
                _timers.Remove(next);
                _callbacks++;
                if (next.Interval is { } interval)
                    _timers.Add(next with { Due = NowMs + interval, Order = _order++ });

                Call(next.Callback, Engine.Global, next.Args);
            }
        }
    }

    /// <summary>Says what the page was left waiting for when the scripts stopped, or null when nothing was; see <see cref="ScriptLogEvents.PendingWork"/>.</summary>
    private void ReportPendingWork()
    {
        var parts = new List<string>();
        var limit = _options.MaxVirtualTime.TotalMilliseconds;
        if (_timers.Count > 0)
        {
            var next = _timers.OrderBy(t => t.Due).ThenBy(t => t.Order).First();
            var names = _timers.OrderBy(t => t.Due).Take(3).Select(t => t.IsFrame ? "animation frame" : (t.Callback.IsObject() && t.Callback.AsObject().Get("name") is { } n && n.IsString() && n.AsString().Length > 0 ? n.AsString() : "anonymous")).ToList();
            var why = next.Due > limit ? $", past the {limit:0} ms virtual limit" : _callbacks >= _options.MaxCallbacks ? ", callback limit reached" : string.Empty;
            parts.Add($"{_timers.Count} timers waiting (next at {next.Due - NowMs:0} ms{why}: {string.Join(", ", names)})");
        }

        if (_pendingRequests.Count > 0)
            parts.Add($"{_pendingRequests.Count} requests unanswered ({string.Join(", ", _pendingRequests.Take(3).Select(r => r.Url.AbsoluteUri))})");
        if (_pendingImages.Count > 0)
            parts.Add($"{_pendingImages.Count} images not loaded");
        if (_pendingMedia.Count > 0)
            parts.Add($"{_pendingMedia.Count} media not opened");

        var recent = _recentEvents.Count == 0 ? "no events" : string.Join(", ", _recentEvents);
        var text = (parts.Count == 0 ? "nothing scheduled" : string.Join("; ", parts)) + "; last events: " + recent;
        WriteLog(parts.Count == 0 ? LogLevel.Debug : LogLevel.Information, ScriptLogEvents.PendingWork, text);
    }

    private WindowObject InstallWindow()
    {
        var global = Engine.Global;
        global.Prototype = EventTargetPrototype;
        var window = new WindowObject(Engine, EventTargetPrototype);

        void Define(string name, JsValue value) => global.FastSetProperty(name, new PropertyDescriptor(value, PropertyFlag.Writable | PropertyFlag.Configurable));

        Define("window", global);
        Define("self", global);
        Define("top", global);
        Define("parent", global);
        Define("frames", global);
        Define("document", Wrap(Document.Root));

        // Constructors a page checks instanceof against, and the Node constants.
        var constructors = new (string Name, ObjectInstance Prototype)[]
        {
            ("EventTarget", EventTargetPrototype), ("Node", NodePrototype), ("Element", ElementPrototype), ("HTMLElement", ElementPrototype),
            ("Text", TextPrototype), ("CDATASection", CDataPrototype), ("CharacterData", CharacterDataPrototype), ("Comment", CommentPrototype),
            ("ProcessingInstruction", ProcessingInstructionPrototype), ("DocumentType", DocumentTypePrototype), ("Document", DocumentPrototype), ("XMLDocument", XmlDocumentPrototype), ("HTMLDocument", DocumentPrototype),
            ("DocumentFragment", FragmentPrototype), ("CSSStyleDeclaration", StylePrototype), ("DOMTokenList", TokenListPrototype),
        };

        foreach (var (name, prototype) in constructors)
        {
            var captured = name;
            var constructor = Js.Constructor(Engine, name, prototype, 0, (_, _, _) => throw TypeError($"Illegal constructor: {captured}"));
            if (name == "Node")
            {
                // The node-type constants are on the interface as well as on every node.
                foreach (var constant in new[] { "ELEMENT_NODE", "ATTRIBUTE_NODE", "TEXT_NODE", "CDATA_SECTION_NODE", "ENTITY_REFERENCE_NODE", "ENTITY_NODE", "PROCESSING_INSTRUCTION_NODE", "COMMENT_NODE", "DOCUMENT_NODE", "DOCUMENT_TYPE_NODE", "DOCUMENT_FRAGMENT_NODE", "NOTATION_NODE" })
                    constructor.FastSetProperty(constant, prototype.GetOwnProperty(constant));
            }

            Define(name, constructor);
        }

        // One interface per kind of element, so an instanceof check tells an iframe from a div as it does in a browser (React asks).
        foreach (var (tag, name) in ElementInterfaces)
        {
            var prototype = ElementPrototypeFor(tag);
            if (ReferenceEquals(prototype, ElementPrototype)) continue;
            var captured = name;
            Define(name, Js.Constructor(Engine, name, prototype, 0, (_, _, _) => throw TypeError($"Illegal constructor: {captured}")));
        }

        InstallExtras();
        InstallLazyWebGlobals();

        Define("location", LocationObject());
        Define("navigator", NavigatorObject());
        Engine.Execute(ExtrasScripts.NavigatorHandlers);
        InstallPopover();
        Define("console", ConsoleObject());
        if (_options.PrinceCompatibility)
        {
            InstallApi(BuildLogApi());
            Define("Prince", PrinceObject());
        }
        Define("performance", PerformanceObject());
        Define("history", HistoryObject());

        Js.Method(Engine, global, "setTimeout", 2, (_, args) =>
            Js.Num(AddTimer(Js.Arg(args, 0), TypeConverter.ToNumber(Js.Arg(args, 1)), [.. args.Skip(2)], repeat: false, frame: false)));
        Js.Method(Engine, global, "setInterval", 2, (_, args) =>
            Js.Num(AddTimer(Js.Arg(args, 0), TypeConverter.ToNumber(Js.Arg(args, 1)), [.. args.Skip(2)], repeat: true, frame: false)));
        Js.Method(Engine, global, "clearTimeout", 1, (_, args) => { ClearTimer(Js.Arg(args, 0)); return JsValue.Undefined; });
        Js.Method(Engine, global, "clearInterval", 1, (_, args) => { ClearTimer(Js.Arg(args, 0)); return JsValue.Undefined; });
        Js.Method(Engine, global, "requestAnimationFrame", 1, (_, args) => Js.Num(AddTimer(Js.Arg(args, 0), 0, [], repeat: false, frame: true)));
        Js.Method(Engine, global, "cancelAnimationFrame", 1, (_, args) => { ClearTimer(Js.Arg(args, 0)); return JsValue.Undefined; });
        Js.Method(Engine, global, "requestIdleCallback", 1, (_, args) => Js.Num(AddTimer(Js.Arg(args, 0), 1, [], repeat: false, frame: false)));
        Js.Method(Engine, global, "cancelIdleCallback", 1, (_, args) => { ClearTimer(Js.Arg(args, 0)); return JsValue.Undefined; });
        Js.Method(Engine, global, "queueMicrotask", 1, (_, args) =>
        {
            Engine.Evaluate("(function (f) { Promise.resolve().then(f); })").Call(JsValue.Undefined, [Js.Arg(args, 0)]);
            return JsValue.Undefined;
        });
        Js.Method(Engine, global, "getComputedStyle", 1, (_, args) =>
        {
            if (Js.Arg(args, 0) is not NodeObject element || element.Node.Kind != ScriptNodeKind.Element)
                throw TypeError("The argument is not an Element.");

            return new StyleObject(Engine, StylePrototype, name => ComputedValue(element.Node, name));
        });
        Js.Method(Engine, global, "matchMedia", 1, (_, args) =>
        {
            var query = Js.AsText(Js.Arg(args, 0));
            var list = Js.NewObject(Engine);
            Js.Value(list, "media", Js.Str(query));
            Js.Value(list, "matches", Js.Bool(query.Contains("print", StringComparison.OrdinalIgnoreCase) || query.Contains("scripting", StringComparison.OrdinalIgnoreCase)));
            Js.Method(Engine, list, "addEventListener", 2, (_, _) => JsValue.Undefined);
            Js.Method(Engine, list, "removeEventListener", 2, (_, _) => JsValue.Undefined);
            Js.Method(Engine, list, "addListener", 1, (_, _) => JsValue.Undefined);
            Js.Method(Engine, list, "removeListener", 1, (_, _) => JsValue.Undefined);
            return list;
        });
        Js.Method(Engine, global, "atob", 1, (_, args) =>
        {
            if (args.Length == 0)
                throw TypeError("Failed to execute 'atob' on 'Window': 1 argument required, but only 0 present.");

            // The forgiving-base64 decode of the Infra standard.
            var input = new StringBuilder();
            foreach (var c in Js.AsText(args[0]))
            {
                if (c is not (' ' or '\t' or '\n' or '\f' or '\r'))
                    input.Append(c);
            }

            var text = input.ToString();
            if (text.Length % 4 == 0 && text.EndsWith("==", StringComparison.Ordinal))
                text = text[..^2];
            else if (text.Length % 4 == 0 && text.EndsWith('='))
                text = text[..^1];

            if (text.Length % 4 == 1 || text.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/')))
                throw DomError("InvalidCharacterError", "The string to be decoded is not correctly encoded.");

            try
            {
                return Js.Str(Encoding.Latin1.GetString(Convert.FromBase64String(text.PadRight(text.Length + (4 - text.Length % 4) % 4, '='))));
            }
            catch (FormatException)
            {
                throw DomError("InvalidCharacterError", "The string to be decoded is not correctly encoded.");
            }
        });
        Js.Method(Engine, global, "btoa", 1, (_, args) =>
        {
            if (args.Length == 0)
                throw TypeError("Failed to execute 'btoa' on 'Window': 1 argument required, but only 0 present.");

            var text = Js.AsText(args[0]);
            if (text.Any(c => c > 255))
                throw DomError("InvalidCharacterError", "The string to be encoded contains characters outside of the Latin1 range.");

            return Js.Str(Convert.ToBase64String(Encoding.Latin1.GetBytes(text)));
        });
        foreach (var no in new[] { "alert", "focus", "blur", "scrollTo", "scrollBy", "scroll", "print", "close", "stop" })
            Js.Method(Engine, global, no, 0, (_, _) => JsValue.Undefined);

        // window.open: an address that is not a URL is a SyntaxError; otherwise a window the page cannot see (there are no other windows) that can be closed.
        Js.Method(Engine, global, "open", 0, (_, args) =>
        {
            if (args.Length > 0 && !args[0].IsUndefined())
                RequireValidUrl(Js.AsText(args[0]));

            var opened = Js.NewObject(Engine);
            Js.Value(opened, "closed", JsBoolean.False);
            Js.Method(Engine, opened, "close", 0, (_, _) => JsValue.Undefined);
            return opened;
        });

        Js.Method(Engine, global, "confirm", 1, (_, _) => JsBoolean.False);
        Js.Method(Engine, global, "prompt", 1, (_, _) => JsValue.Null);

        Define("innerWidth", Js.Num(800));
        Define("innerHeight", Js.Num(600));
        Define("outerWidth", Js.Num(800));
        Define("outerHeight", Js.Num(600));
        Define("devicePixelRatio", Js.Num(1));
        Define("scrollX", Js.Num(0));
        Define("scrollY", Js.Num(0));
        Define("pageXOffset", Js.Num(0));
        Define("pageYOffset", Js.Num(0));
        Define("screenX", Js.Num(0));
        Define("screenY", Js.Num(0));
        Define("isSecureContext", JsBoolean.False);

        var css = Js.NewObject(Engine);
        Js.Method(Engine, css, "supports", 2, (_, args) => Js.Bool(args.Length > 0 && Js.AsText(args[0]).Length > 0));
        Js.Method(Engine, css, "escape", 1, (_, args) => Js.Str(Js.AsText(Js.Arg(args, 0))));
        Define("CSS", css);

        foreach (var name in new[] { "ResizeObserver", "IntersectionObserver", "PerformanceObserver" })
            Define(name, ObserverConstructor(name));

        return window;
    }

    private ObjectInstance ObserverConstructor(string name)
    {
        var prototype = Js.NewObject(Engine);
        foreach (var method in new[] { "observe", "unobserve", "disconnect" })
            Js.Method(Engine, prototype, method, 1, (_, _) => JsValue.Undefined);

        Js.Method(Engine, prototype, "takeRecords", 0, (_, _) => Js.Array(Engine, []));
        return Js.Constructor(Engine, name, prototype, 1, (_, _, _) =>
        {
            var observer = Js.NewObject(Engine);
            observer.Prototype = prototype;
            return observer;
        });
    }

    private ObjectInstance LocationObject()
    {
        var uri = Document.BaseUri;
        var location = Js.NewObject(Engine);
        Js.Value(location, "href", Js.Str(uri?.AbsoluteUri ?? "about:blank"));
        Js.Value(location, "origin", Js.Str(uri is { IsAbsoluteUri: true } && uri.Scheme is "http" or "https" ? uri.GetLeftPart(UriPartial.Authority) : "null"));
        Js.Value(location, "protocol", Js.Str(uri is null ? "about:" : uri.Scheme + ":"));
        Js.Value(location, "host", Js.Str(uri?.Authority ?? string.Empty));
        Js.Value(location, "hostname", Js.Str(uri?.Host ?? string.Empty));
        Js.Value(location, "port", Js.Str(uri is { IsDefaultPort: false } ? uri.Port.ToString() : string.Empty));
        Js.Value(location, "pathname", Js.Str(uri?.AbsolutePath ?? "blank"));
        Js.Value(location, "search", Js.Str(uri?.Query ?? string.Empty));
        Js.Value(location, "hash", Js.Str(uri?.Fragment ?? string.Empty));
        Js.Method(Engine, location, "toString", 0, (_, _) => Js.Str(uri?.AbsoluteUri ?? "about:blank"));
        foreach (var no in new[] { "assign", "replace", "reload" })
            Js.Method(Engine, location, no, 1, (_, _) => JsValue.Undefined);

        return location;
    }

    private ObjectInstance NavigatorObject()
    {
        var navigator = Js.NewObject(Engine);
        Js.Value(navigator, "userAgent", Js.Str("Mozilla/5.0 (compatible; PeachBrackets)"));
        Js.Value(navigator, "appName", Js.Str("Netscape"));
        Js.Value(navigator, "platform", Js.Str("PeachBrackets"));
        Js.Value(navigator, "language", Js.Str("en-US"));
        Js.Value(navigator, "languages", Js.Array(Engine, [Js.Str("en-US")]));
        Js.Value(navigator, "onLine", JsBoolean.True);
        Js.Value(navigator, "cookieEnabled", JsBoolean.False);
        Js.Value(navigator, "hardwareConcurrency", Js.Num(1));
        return navigator;
    }

    private ObjectInstance HistoryObject()
    {
        var history = Js.NewObject(Engine);
        Js.Value(history, "length", Js.Num(1));
        foreach (var no in new[] { "pushState", "replaceState", "back", "forward", "go" })
            Js.Method(Engine, history, no, 1, (_, _) => JsValue.Undefined);

        return history;
    }

    private ObjectInstance PerformanceObject()
    {
        var performance = Js.NewObject(Engine);
        Js.Method(Engine, performance, "now", 0, (_, _) => Js.Num(NowMs));
        Js.Method(Engine, performance, "mark", 1, (_, _) => JsValue.Undefined);
        Js.Method(Engine, performance, "measure", 1, (_, _) => JsValue.Undefined);
        Js.Method(Engine, performance, "getEntriesByType", 1, (_, _) => Js.Array(Engine, []));
        Js.Method(Engine, performance, "getEntriesByName", 1, (_, _) => Js.Array(Engine, []));
        Js.Value(performance, "timeOrigin", Js.Num(0));
        return performance;
    }

    private ObjectInstance ConsoleObject()
    {
        var console = Js.NewObject(Engine);
        foreach (var level in new[] { "log", "info", "warn", "error", "debug", "trace" })
        {
            var captured = level;
            Js.Method(Engine, console, level, 0, (_, args) =>
            {
                WriteLog(captured switch
                {
                    "warn" => LogLevel.Warning,
                    "error" => LogLevel.Error,
                    "debug" or "trace" => LogLevel.Debug,
                    _ => LogLevel.Information,
                }, ScriptLogEvents.Message, string.Join(' ', args.Select(Js.AsText)));
                return JsValue.Undefined;
            });
        }

        Js.Method(Engine, console, "assert", 1, (_, args) =>
        {
            if (!TypeConverter.ToBoolean(Js.Arg(args, 0)))
                WriteLog(LogLevel.Error, ScriptLogEvents.Message, "Assertion failed: " + string.Join(' ', args.Skip(1).Select(Js.AsText)));

            return JsValue.Undefined;
        });
        foreach (var no in new[] { "clear", "group", "groupEnd", "groupCollapsed", "time", "timeEnd", "table", "dir", "count" })
            Js.Method(Engine, console, no, 0, (_, _) => JsValue.Undefined);

        return console;
    }

    // What the cascade says a property of an element is. Without a layout it is what the element's own style attribute says.
    private string? ComputedValue(ScriptNode element, string name) =>
        Document.GetComputedValue(element, name) ?? InlineStyle.Parse(element.GetAttribute("style")).Get(name);
}
