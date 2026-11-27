using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// Dedicated workers. A worker is another engine (a runtime over a document with no tree) that the page's runtime drives: its messages, its timers and its
/// network are played from the page's loop on the same virtual clock, so a run is deterministic and needs no threads. What crosses is text (the message
/// codec of <see cref="WorkerScripts"/>), never an object.
/// </summary>
internal sealed partial class ScriptRuntime
{
    private sealed class WorkerHandle
    {
        public int Id { get; init; }

        public string Url { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? Source { get; init; }

        public ScriptRuntime? Child { get; set; }

        public bool Started { get; set; }

        public bool Terminated { get; set; }

        public bool Closed { get; set; }

        public Queue<string> Inbox { get; } = [];

        public List<(string Kind, string Text, string File, int Line, int Column)> Outbox { get; } = [];
    }

    private readonly Dictionary<int, WorkerHandle> _workers = [];
    private int _nextWorkerId = 1;
    private WorkerHandle? _workerSelf;
    private ObjectInstance? _workerCodec;
    private System.Threading.CancellationToken _cancellation;

    /// <summary>Whether this runtime is a worker's.</summary>
    private bool IsWorker => _workerSelf is not null;

    private void InstallWorkerNet(ObjectInstance net)
    {
        Js.Method(Engine, net, "registerWorkerCodec", 1, (_, args) =>
        {
            _workerCodec = Js.Arg(args, 0) as ObjectInstance;
            return JsValue.Undefined;
        });
        Js.Method(Engine, net, "workerCreate", 4, (_, args) =>
        {
            var handle = new WorkerHandle
            {
                Id = _nextWorkerId++,
                Url = Js.AsText(Js.Arg(args, 0)),
                Name = Js.AsText(Js.Arg(args, 1)),
                Source = Js.Arg(args, 3).IsString() ? Js.AsText(Js.Arg(args, 3)) : null,
            };
            _workers[handle.Id] = handle;
            return Js.Num(handle.Id);
        });
        Js.Method(Engine, net, "workerPost", 2, (_, args) =>
        {
            if (_workers.TryGetValue((int)TypeConverter.ToNumber(Js.Arg(args, 0)), out var handle) && !handle.Terminated)
                handle.Inbox.Enqueue(Js.AsText(Js.Arg(args, 1)));

            return JsValue.Undefined;
        });
        Js.Method(Engine, net, "workerTerminate", 1, (_, args) =>
        {
            if (_workers.TryGetValue((int)TypeConverter.ToNumber(Js.Arg(args, 0)), out var handle))
            {
                handle.Terminated = true;
                handle.Inbox.Clear();
            }

            return JsValue.Undefined;
        });

        // The worker's side.
        Js.Method(Engine, net, "workerOut", 1, (_, args) =>
        {
            _workerSelf?.Outbox.Add(("message", Js.AsText(Js.Arg(args, 0)), string.Empty, 0, 0));
            return JsValue.Undefined;
        });
        Js.Method(Engine, net, "workerClose", 0, (_, _) =>
        {
            if (_workerSelf is { } self)
            {
                self.Closed = true;
                self.Inbox.Clear();
                _timers.Clear();
            }

            return JsValue.Undefined;
        });
        Js.Method(Engine, net, "workerImport", 1, (_, args) =>
        {
            var address = Js.AsText(Js.Arg(args, 0));
            var code = Document.LoadTextAsync(address).AsTask().GetAwaiter().GetResult();
            if (code is null)
                throw DomError("NetworkError", $"Failed to execute 'importScripts' on 'WorkerGlobalScope': The script at '{address}' failed to load.");

            Engine.Execute(code, address);
            return JsValue.Undefined;
        });

        // What a message may carry that is not data: a picture (shared, as it cannot change) or the size of an OffscreenCanvas.
        Js.Method(Engine, net, "isHandleable", 1, (_, args) => Js.Bool(Js.Arg(args, 0) is BitmapObject or ElementImageObject || IsOffscreenCanvas(Js.Arg(args, 0))));
        Js.Method(Engine, net, "putHandle", 1, (_, args) =>
        {
            object payload = Js.Arg(args, 0) switch
            {
                BitmapObject { Image: { } image } bitmap => new BitmapHandle(image, bitmap.OriginClean),
                BitmapObject => throw DomError("DataCloneError", "An ImageBitmap is closed and could not be cloned."),
                ElementImageObject { Closed: false } image => new ElementImageHandle(image),
                ElementImageObject => throw DomError("DataCloneError", "An ElementImage is closed and could not be cloned."),
                NodeObject { Node: { } node } => CanvasSize(node) is var (width, height) ? new OffscreenHandle(width, height, _offscreenPlaceholders.TryGetValue(node, out var link) ? link : null) : throw DomError("DataCloneError", "An object could not be cloned."),
                _ => throw DomError("DataCloneError", "An object could not be cloned."),
            };

            var token = System.Threading.Interlocked.Increment(ref s_handleCounter);
            s_handles[token] = payload;
            return Js.Num(token);
        });
        Js.Method(Engine, net, "takeHandle", 1, (_, args) =>
        {
            if (!s_handles.TryRemove((long)TypeConverter.ToNumber(Js.Arg(args, 0)), out var payload))
                throw DomError("DataCloneError", "An object could not be deserialized.");

            if (payload is BitmapHandle bitmap)
                return new BitmapObject(Engine, BitmapPrototype, bitmap.Image) { OriginClean = bitmap.OriginClean };

            if (payload is ElementImageHandle shared)
            {
                var source = shared.Image;
                return new ElementImageObject(Engine, ElementImagePrototype, source.Canvas, source.Element, source.Width, source.Height) { Owner = source.Owner };
            }

            var size = (OffscreenHandle)payload;
            var node = Document.CreateElement("canvas");
            node.SetAttribute("width", size.Width.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            node.SetAttribute("height", size.Height.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            var made = OffscreenOf(node);
            if (size.Link is { } link)
                _offscreenPlaceholders[node] = link;

            return made;
        });

        // Transferring (moves what the object held to a new object of the same kind, and detaches the old one): an ImageBitmap is closed, an OffscreenCanvas is emptied of its size.
        Js.Method(Engine, net, "checkTransfer", 1, (_, args) =>
        {
            switch (Js.Arg(args, 0))
            {
                case ElementImageObject { Closed: true }:
                    throw DomError("DataCloneError", "An ElementImage is closed and could not be transferred.");
                case BitmapObject { Image: null }:
                    throw DomError("DataCloneError", "An ImageBitmap is closed and could not be transferred.");
                case NodeObject { Node: { } node } when IsOffscreenCanvas(Js.Arg(args, 0)):
                    if (_transferred.Contains(node))
                        throw DomError("DataCloneError", "The OffscreenCanvas is detached and could not be transferred.");

                    if (_contexts.ContainsKey(node))
                        throw DomError("InvalidStateError", "An OffscreenCanvas that has a context could not be transferred.");

                    break;
            }

            return JsValue.Undefined;
        });
        Js.Method(Engine, net, "transferObject", 1, (_, args) =>
        {
            if (Js.Arg(args, 0) is ElementImageObject { Closed: false } element)
            {
                var carried = new ElementImageObject(Engine, ElementImagePrototype, element.Canvas, element.Element, element.Width, element.Height) { Owner = element.Owner };
                element.Closed = true;
                return carried;
            }

            if (Js.Arg(args, 0) is BitmapObject { Image: { } image } bitmap)
            {
                var moved = new BitmapObject(Engine, BitmapPrototype, image) { OriginClean = bitmap.OriginClean };
                bitmap.Image = null;
                return moved;
            }

            var node = ((NodeObject)Js.Arg(args, 0)).Node;
            var (width, height) = CanvasSize(node);
            var fresh = Document.CreateElement("canvas");
            fresh.SetAttribute("width", width.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            fresh.SetAttribute("height", height.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            if (_offscreenPlaceholders.TryGetValue(node, out var link))
                _offscreenPlaceholders[fresh] = link;

            _transferred.Add(node);
            node.SetAttribute("width", "0");
            node.SetAttribute("height", "0");
            return OffscreenOf(fresh);
        });
    }

    private sealed record BitmapHandle(PeachDrawing.Core.Image Image, bool OriginClean);

    private sealed record OffscreenHandle(double Width, double Height, (ScriptRuntime Owner, ScriptNode Placeholder)? Link);

    private sealed record ElementImageHandle(ElementImageObject Image);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, object> s_handles = new();
    private static long s_handleCounter;

    private bool IsOffscreenCanvas(JsValue value) => value is NodeObject { Node: { Kind: ScriptNodeKind.Element } node } wrapper && IsCanvasElement(node) && ReferenceEquals(wrapper.Prototype, OffscreenPrototype);

    // The global of a worker has no document: the window-only names are removed, the location is the script's, and the interfaces of a worker global are put in.
    private void BecomeWorker(WorkerHandle handle)
    {
        _workerSelf = handle;
        LoadWebBundle();
        Engine.Evaluate("""
            (function (global) {
                var drop = /^(window|top|parent|frames|document|opener|closed|length|status|frameElement|HTML\w*|SVG\w*|MathML\w*|Node|Element|Document\w*|XMLDocument|CharacterData|Text|Comment|CDATASection|ProcessingInstruction|Attr|NamedNodeMap|NodeList|HTMLCollection|DOMTokenList|Range|Selection|ShadowRoot|ElementImage|CanvasPaintEvent|ElementGeometryUpdateEvent|CanvasRenderingContext2D|getComputedStyle|requestAnimationFrame|cancelAnimationFrame|alert|confirm|prompt|open|print|stop|focus|blur|moveTo|moveBy|resizeTo|resizeBy|scroll|scrollTo|scrollBy|innerWidth|innerHeight|outerWidth|outerHeight|screenX|screenY|scrollX|scrollY|pageXOffset|pageYOffset|devicePixelRatio|screen|history|localStorage|sessionStorage|matchMedia|MutationObserver|IntersectionObserver|ResizeObserver|NodeFilter|TreeWalker|NodeIterator|XPath\w*|customElements|CustomElementRegistry|CSSStyleDeclaration)$/;
                var fonts = global.document && global.document.fonts;
                Object.getOwnPropertyNames(global).forEach(function (name) {
                    if (drop.test(name)) { try { delete global[name]; } catch (e) { } }
                });
                if (fonts) Object.defineProperty(global, "fonts", { value: fonts, writable: true, configurable: true });
            })
            """).Call(JsValue.Undefined, [Engine.Global]);

        Engine.Evaluate("""
            (function (global, address) {
                var u = new URL(address);
                var location = { href: u.href, origin: u.origin, protocol: u.protocol, host: u.host, hostname: u.hostname, port: u.port, pathname: u.pathname, search: u.search, hash: u.hash };
                Object.defineProperty(location, "toString", { value: function () { return u.href; } });
                Object.defineProperty(global, "location", { value: location, writable: true, configurable: true });
            })
            """).Call(JsValue.Undefined, [Engine.Global, Js.Str(handle.Url.StartsWith("blob:", StringComparison.Ordinal) || handle.Url.StartsWith("data:", StringComparison.Ordinal) ? "about:blank" : handle.Url)]);

        _workerCodec?.Get("setup").Call(_workerCodec, [Js.Str(handle.Name)]);
    }

    private async ValueTask StartWorkerAsync(WorkerHandle handle)
    {
        handle.Started = true;
        var code = handle.Source ?? await Document.LoadTextAsync(handle.Url);
        if (code is null)
        {
            handle.Outbox.Add(("error", $"The worker script '{handle.Url}' could not be loaded.", handle.Url, 0, 0));
            handle.Terminated = true;
            return;
        }

        var child = new ScriptRuntime(Document.CreateDocument(true, false), _options, _cancellation) { NowMs = NowMs };
        handle.Child = child;
        await child.Document.LoadFontFacesAsync();
        child.BecomeWorker(handle);
        child.Execute(code, handle.Url);
    }

    private void DeliverToWorkerGlobal(string text)
    {
        if (_workerCodec is null)
            return;

        Call(_workerCodec.Get("toWorkerGlobal"), _workerCodec, Js.Str(text));
    }

    // Runs the timers of this runtime that are due by a time, in order (the step of a worker).
    private bool RunDueTimers(double until)
    {
        var ran = false;
        while (_callbacks < _options.MaxCallbacks)
        {
            var next = _timers.OrderBy(t => t.Due).ThenBy(t => t.Order).FirstOrDefault();
            if (next is null || next.Due > until)
                break;

            NowMs = Math.Max(NowMs, next.Due);
            _timers.Remove(next);
            _callbacks++;
            if (next.Interval is { } interval)
                _timers.Add(next with { Due = NowMs + interval, Order = _order++ });

            Call(next.Callback, Engine.Global, next.IsFrame ? [Js.Num(NowMs)] : next.Args);
            ran = true;
        }

        return ran;
    }

    // Plays what the workers of this runtime have to do by a time: starts them, delivers their messages, runs their timers, and queues what they sent back.
    private async ValueTask<bool> PumpWorkersAsync(double until)
    {
        if (_workers.Count == 0)
            return false;

        var progressed = false;
        foreach (var handle in _workers.Values.ToList())
        {
            if (handle.Terminated && handle.Outbox.Count == 0)
                continue;

            if (!handle.Started && !handle.Terminated)
            {
                await StartWorkerAsync(handle);
                progressed = true;
            }

            if (handle.Child is { } child && !handle.Terminated)
            {
                for (var round = 0; round < 10_000 && !handle.Closed && !handle.Terminated; round++)
                {
                    var step = false;
                    await child.DeliverRequestsAsync();
                    step |= await child.PumpWorkersAsync(until);
                    step |= child.RunDueTimers(until);
                    if (handle.Inbox.Count > 0)
                    {
                        child.NowMs = Math.Max(child.NowMs, until);
                        child.DeliverToWorkerGlobal(handle.Inbox.Dequeue());
                        step = true;
                    }

                    if (!step)
                        break;

                    progressed = true;
                }

                child.NowMs = Math.Max(child.NowMs, until);
            }

            if (handle.Outbox.Count > 0 && _workerCodec is not null)
            {
                foreach (var (kind, text, file, line, column) in handle.Outbox.ToList())
                {
                    var id = handle.Id;
                    AddTimer(Js.Function(Engine, "workerEvent", 0, (_, _) =>
                    {
                        Call(_workerCodec.Get("toWorkerObject"), _workerCodec, Js.Num(id), Js.Str(kind), Js.Str(text), Js.Str(file), Js.Num(line), Js.Num(column));
                        return JsValue.Undefined;
                    }), 0, [], repeat: false, frame: false);
                }

                handle.Outbox.Clear();
                progressed = true;
            }
        }

        return progressed;
    }

    // When a worker next has something to do (its next timer), for a page that has nothing else to wait for.
    private double? NextWorkerWake()
    {
        double? wake = null;
        foreach (var handle in _workers.Values)
        {
            if (handle.Terminated || handle.Closed || handle.Child is not { } child)
                continue;

            if (child._timers.Count > 0)
            {
                var due = child._timers.Min(t => t.Due);
                wake = wake is null ? due : Math.Min(wake.Value, due);
            }

            if (child.NextWorkerWake() is { } nested)
                wake = wake is null ? nested : Math.Min(wake.Value, nested);
        }

        return wake;
    }

    // An error a worker's script raised goes to the Worker object in the page as an error event.
    private void ForwardErrorToParent(string message, JsValue error)
    {
        if (_workerSelf is { } handle)
            handle.Outbox.Add(("error", message, handle.Url, 0, 0));
    }
}
