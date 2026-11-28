using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
using Jint.Runtime;
using PeachBrackets.ScriptHosting;
using PeachBrackets.Scripting.Internal.Canvas;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

/// <summary>An <c>ElementImage</c>: a drawable child of a canvas as it was when the canvas last painted, which a context of that canvas draws.</summary>
internal sealed class ElementImageObject : ObjectInstance
{
    public ElementImageObject(Engine engine, ObjectInstance prototype, ScriptNode canvas, ScriptNode element, double width, double height) : base(engine)
    {
        Prototype = prototype;
        Canvas = canvas;
        Element = element;
        Width = width;
        Height = height;
    }

    public ScriptNode Canvas { get; }

    public ScriptNode Element { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>The runtime of the page the element is in, which is the one that lays it out and keeps its geometry (an ElementImage a worker holds is still the page's).</summary>
    public ScriptRuntime? Owner { get; init; }

    /// <summary>The element as it looked when the image was captured, so a later change to the element does not change what the image draws; null when the element could not be drawn then.</summary>
    public ScriptElementPicture? Picture { get; init; }

    /// <summary>Whether <c>close()</c> was called: a closed image can no longer be drawn or asked its size.</summary>
    public bool Closed { get; set; }
}

// HTML in canvas: a canvas with content="drawable" lays out its children, the ones marked drawable are drawn onto its 2D context with drawElementImage, and the canvas says when
// they have changed with a paint event (and requestPaint asks for one).
internal sealed partial class ScriptRuntime
{
    private ObjectInstance ElementImagePrototype = null!;
    private ObjectInstance CanvasPaintEventPrototype = null!;

    private sealed class HtmlCanvasState
    {
        /// <summary>A paint is asked for (requestPaint, the first one, or the canvas becoming drawable), whether or not anything changed.</summary>
        public bool Requested { get; set; }

        public bool FrameScheduled { get; set; }

        /// <summary>The drawable children that changed since the last paint, in the order they did.</summary>
        public List<ScriptNode> Changed { get; } = [];

        /// <summary>What was laid out at the last paint: the size of each drawable descendant that was displayed.</summary>
        public Dictionary<ScriptNode, (double Width, double Height)> Painted { get; } = [];

        /// <summary>The transform each element was given (updateElementGeometry or drawElementImage), as the 16 numbers of a DOMMatrix and whether it is 2D.</summary>
        public Dictionary<ScriptNode, (double[] Values, bool Is2D)> Transforms { get; } = [];

        /// <summary>The elements that can be hit, the one drawn or updated last at the end (so the one on top).</summary>
        public List<ScriptNode> HitOrder { get; } = [];

        /// <summary>The part of each element (x, y, width, height in its own space, before its transform) a pointer can find it in.</summary>
        public Dictionary<ScriptNode, (double X, double Y, double Width, double Height)> Clips { get; } = [];
    }

    private readonly Dictionary<ScriptNode, HtmlCanvasState> _htmlCanvases = [];

    private static bool IsCanvasElement(ScriptNode node) =>
        node.Kind == ScriptNodeKind.Element && string.Equals(node.TagName, "canvas", StringComparison.OrdinalIgnoreCase);

    private static bool IsContentDrawable(ScriptNode canvas) =>
        IsCanvasElement(canvas) && string.Equals(canvas.GetAttribute("content"), "drawable", StringComparison.OrdinalIgnoreCase);

    private HtmlCanvasState StateOfCanvas(ScriptNode canvas)
    {
        if (!_htmlCanvases.TryGetValue(canvas, out var state))
            _htmlCanvases[canvas] = state = new HtmlCanvasState();

        return state;
    }

    private static bool IsInside(ScriptNode ancestor, ScriptNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    // The nearest drawable element at or above a node, below the canvas.
    private static ScriptNode? DrawableAt(ScriptNode canvas, ScriptNode node)
    {
        for (var current = node; current is not null && !ReferenceEquals(current, canvas); current = current.Parent)
        {
            if (current.Kind == ScriptNodeKind.Element && current.HasAttribute("drawable"))
                return current;
        }

        return null;
    }

    // Whether a node and everything above it is displayed (none of them is display: none).
    private bool IsDisplayed(ScriptNode node)
    {
        for (var current = node; current is not null && current.Kind == ScriptNodeKind.Element; current = current.Parent)
        {
            if (Document.GetComputedValue(current, "display") == "none")
                return false;
        }

        return true;
    }

    // Lays out what the canvas has now: the size of each drawable descendant that is displayed. What a script reads as the element's size until the next paint.
    private void TakePaintSnapshot(ScriptNode canvas, HtmlCanvasState state)
    {
        state.Painted.Clear();
        if (!IsContentDrawable(canvas) || !canvas.IsConnected || !Document.CanMeasure || !IsDisplayed(canvas))
            return;

        foreach (var element in canvas.QuerySelectorAll("[drawable]"))
        {
            // What a canvas nested in this one holds belongs to that canvas.
            if (!ReferenceEquals(DrawableCanvasOf(element), canvas) || !IsDisplayed(element) || Document.GetLayoutBox(element) is not { } box)
                continue;

            state.Painted[element] = (box.Width, box.Height);
        }
    }

    private void ScheduleCanvasPaint(ScriptNode canvas, HtmlCanvasState state)
    {
        if (state.FrameScheduled)
            return;

        state.FrameScheduled = true;
        _pendingCanvasPaints.Add((canvas, state));
        if (_canvasPaintTimer)
            return;

        // One callback for the frame runs every canvas that is due, in post-tree order: what is inside a canvas paints before the canvas.
        _canvasPaintTimer = true;
        AddTimer(Js.Function(Engine, "canvasPaint", 0, (_, _) =>
        {
            _canvasPaintTimer = false;
            var due = _pendingCanvasPaints.ToList();
            _pendingCanvasPaints.Clear();
            // The last in the document first, so what is inside a canvas paints before the canvas.
            var order = Document.GetElementsByTagName("canvas").ToList();
            foreach (var (due1, state1) in due.OrderByDescending(d => order.IndexOf(d.Canvas)))
                RunCanvasPaint(due1, state1);

            return JsValue.Undefined;
        }), 0, [], repeat: false, frame: true);
    }

    private readonly List<(ScriptNode Canvas, HtmlCanvasState State)> _pendingCanvasPaints = [];
    private bool _canvasPaintTimer;

    private void RunCanvasPaint(ScriptNode canvas, HtmlCanvasState state)
    {
        state.FrameScheduled = false;
        var changed = state.Changed.Where(n => n.IsConnected && IsInside(canvas, n)).ToArray();

        // Painting is for what changed since the last paint: an element that was painted and is gone is a change too, one that came and went between two paints is none.
        var gone = state.Painted.Keys.Any(k => !k.IsConnected || !IsInside(canvas, k));
        if (!state.Requested && changed.Length == 0 && !gone)
        {
            state.Changed.Clear();
            return;
        }

        state.Changed.Clear();
        state.Requested = false;
        TakePaintSnapshot(canvas, state);
        if (!canvas.IsConnected)
            return;

        var ev = CreateEvent("paint", bubbles: false, cancelable: false);
        ev.Prototype = CanvasPaintEventPrototype;
        ev.FastSetProperty("changedElements", new PropertyDescriptor(Js.Array(Engine, changed.Select(n => (JsValue)Wrap(n)).ToArray()), PropertyFlag.Configurable | PropertyFlag.Enumerable));
        DispatchEvent(Wrap(canvas), ev);
    }

    // Paints for the canvases that are drawable when the page has loaded (the first paint), and keeps watching.
    private void StartInitialCanvasPaints()
    {
        foreach (var canvas in Document.GetElementsByTagName("canvas").Where(IsContentDrawable))
        {
            var state = StateOfCanvas(canvas);
            state.Requested = true;
            ScheduleCanvasPaint(canvas, state);
        }
    }

    // The mutations that count as a canvas's children changing, or the canvas becoming drawable.
    private void HandleCanvasSubtreeMutation(ScriptMutation mutation)
    {
        var target = mutation.Target;
        ScriptNode? canvas = null;
        for (var current = target; current is not null; current = current.Parent)
        {
            if (IsCanvasElement(current))
            {
                canvas = current;
                break;
            }
        }

        if (canvas is null)
            return;

        if (ReferenceEquals(canvas, target))
        {
            // Becoming drawable (or going away from it) is a reason to paint; the canvas's own other attributes are not a change of its children.
            if (mutation.Kind == ScriptMutationKind.Attributes && mutation.AttributeName is { } name && name.Equals("content", StringComparison.OrdinalIgnoreCase) && IsContentDrawable(canvas))
            {
                var own = StateOfCanvas(canvas);
                own.Requested = true;
                ScheduleCanvasPaint(canvas, own);
            }

            if (mutation.Kind != ScriptMutationKind.ChildList)
                return;
        }

        if (!IsContentDrawable(canvas))
            return;

        var state = StateOfCanvas(canvas);
        var changed = DrawableAt(canvas, target);
        if (changed is not null && !state.Changed.Contains(changed))
            state.Changed.Add(changed);

        // A child added or removed anywhere in the canvas is a change too, whether or not a drawable element holds it.
        if (changed is not null || mutation.Kind == ScriptMutationKind.ChildList)
        {
            // What was put in is a change of its own (one that is gone again by the paint is no change).
            if (mutation.Kind == ScriptMutationKind.ChildList && mutation.Added is { } added)
            {
                foreach (var node in added.Where(a => a.Kind == ScriptNodeKind.Element))
                {
                    foreach (var element in new[] { node }.Concat(node.QuerySelectorAll("[drawable]")).Where(e => e.HasAttribute("drawable")))
                    {
                        if (ReferenceEquals(DrawableCanvasOf(element), canvas) && !state.Changed.Contains(element))
                            state.Changed.Add(element);
                    }
                }
            }

            ScheduleCanvasPaint(canvas, state);
        }
    }

    private ScriptNode ElementOf(JsValue source, out ElementImageObject? image)
    {
        image = source as ElementImageObject;
        if (image is not null)
            return image.Closed ? throw DomError("InvalidStateError", "The ElementImage is closed.") : image.Element;

        return source is NodeObject { Node: { Kind: ScriptNodeKind.Element } element } ? element : throw TypeError("The argument is not an element or an ElementImage.");
    }

    // The state of a canvas that makes its children drawable, and the element a call is about: an InvalidStateError for one that cannot be.
    private (HtmlCanvasState State, ScriptNode Element) RequireDrawableChild(ScriptNode canvas, JsValue source, bool needsPaint)
    {
        var element = ElementOf(source, out var image);
        if (!IsContentDrawable(canvas))
            throw DomError("InvalidStateError", "The canvas does not make its children drawable (content=drawable).");

        if (image is not null && !ReferenceEquals(image.Canvas, canvas))
            throw DomError("InvalidStateError", "The ElementImage is of another canvas.");

        // A canvas that is not rendered (display: none, here or above) has no layout to draw into.
        if (canvas.IsConnected && Document.GetLayoutBox(canvas) is null)
            throw DomError("InvalidStateError", "The canvas is not laid out.");

        if (!element.HasAttribute("drawable") || !IsInside(canvas, element) || !ReferenceEquals(DrawableCanvasOf(element), canvas))
            throw DomError("InvalidStateError", "The element is not a drawable child of the canvas.");

        var state = StateOfCanvas(canvas);
        if (needsPaint && (!canvas.IsConnected || !state.Painted.ContainsKey(element)))
            throw DomError("InvalidStateError", "The element has not been laid out and painted (no paint event has been delivered for it).");

        return (state, element);
    }

    private JsValue MatrixOf((double[] Values, bool Is2D) transform) =>
        Engine.Evaluate("(function (a) { return new DOMMatrix(a); })").Call(JsValue.Undefined,
            [Js.Array(Engine, (transform.Is2D ? [transform.Values[0], transform.Values[1], transform.Values[4], transform.Values[5], transform.Values[12], transform.Values[13]] : transform.Values).Select(Js.Num).ToArray())]);

    // An element drawn, or given its geometry, is where a pointer can find it, on top of what was there before (unless asked to leave the order).
    private static void Touch(HtmlCanvasState state, ScriptNode element, bool preserveOrder)
    {
        if (preserveOrder && state.HitOrder.Contains(element))
            return;

        state.HitOrder.Remove(element);
        state.HitOrder.Add(element);
    }

    private static (double X, double Y, double Width, double Height) Transformed((double[] Values, bool Is2D) transform, double width, double height) =>
        TransformedRect(transform, 0, 0, width, height);

    private static (double X, double Y, double Width, double Height) TransformedRect((double[] Values, bool Is2D) transform, double rx, double ry, double width, double height)
    {
        var v = transform.Values;
        double A = v[0], B = v[1], C = v[4], D = v[5], E = v[12], F = v[13];
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var (x, y) in new[] { (rx, ry), (rx + width, ry), (rx, ry + height), (rx + width, ry + height) })
        {
            var (tx, ty) = (A * x + C * y + E, B * x + D * y + F);
            left = Math.Min(left, tx); right = Math.Max(right, tx);
            top = Math.Min(top, ty); bottom = Math.Max(bottom, ty);
        }

        return (left, top, right - left, bottom - top);
    }

    private ScriptNode? DrawableCanvasOf(ScriptNode element)
    {
        for (var current = element.Parent; current is not null; current = current.Parent)
        {
            if (IsContentDrawable(current))
                return current;
        }

        return null;
    }

    // An element's rectangle on the page, in CSS pixels. A drawable child of a canvas is where the canvas put it: at the canvas's origin carried by the transform it was given (none is the identity),
    // whatever its place in the layout; what is inside it keeps its place in it, carried by the same transform. Everything else is where it is laid out.
    private (double X, double Y, double Width, double Height) PageRectOf(ScriptNode element)
    {
        var layout = LayoutOf(element);
        if (DrawableCanvasOf(element) is not { } canvas || Document.GetLayoutBox(canvas) is not { } origin)
            return layout;

        var anchor = element.HasAttribute("drawable") ? element : null;
        for (var current = element.Parent; anchor is null && current is not null && !ReferenceEquals(current, canvas); current = current.Parent)
        {
            if (current.Kind == ScriptNodeKind.Element && current.HasAttribute("drawable"))
                anchor = current;
        }

        if (anchor is null || Document.GetBoundingBoxes(anchor) is not { } from)
            return layout;

        // The element's box (with its own CSS transform, which the canvas transform is applied after) as it sits in its drawable ancestor's, which is what the canvas transform carries.
        var own = Document.GetBoundingBoxes(element) is { } mine ? mine.Transformed : new ScriptBox(layout.X, layout.Y, layout.Width, layout.Height);
        var transform = _htmlCanvases.TryGetValue(canvas, out var state) && state.Transforms.TryGetValue(anchor, out var given) ? given : (Identity16, true);
        var (x, y, width, height) = TransformedRect(transform, own.X - from.Box.X, own.Y - from.Box.Y, own.Width, own.Height);
        return (origin.X + x, origin.Y + y, width, height);
    }

    // Where a pointer can find a drawable child: its page rectangle, cut to the clip it was given (which is in its own space, so it moves with its transform).
    private (double X, double Y, double Width, double Height) HitRectOf(HtmlCanvasState state, ScriptNode element)
    {
        var page = PageRectOf(element);
        if (!state.Clips.TryGetValue(element, out var clip))
            return page;

        var layout = LayoutOf(element);
        var left = Math.Max(clip.X, 0);
        var top = Math.Max(clip.Y, 0);
        var right = Math.Min(clip.X + clip.Width, layout.Width);
        var bottom = Math.Min(clip.Y + clip.Height, layout.Height);
        if (right <= left || bottom <= top)
            return (0, 0, 0, 0);

        var canvas = DrawableCanvasOf(element);
        if (canvas is null || Document.GetLayoutBox(canvas) is not { } origin)
            return page;

        var transform = state.Transforms.TryGetValue(element, out var own) ? own : (Identity16, true);
        var (x, y, width, height) = TransformedRect(transform, left, top, right - left, bottom - top);
        return (origin.X + x, origin.Y + y, width, height);
    }

    private bool IsHittable(ScriptNode element)
    {
        if (element.Kind != ScriptNodeKind.Element)
            return false;

        for (var current = element; current is not null && current.Kind == ScriptNodeKind.Element; current = current.Parent)
        {
            if (current.HasAttribute("inert") || Document.GetComputedValue(current, "display") == "none")
                return false;
        }

        return Document.GetComputedValue(element, "pointer-events") != "none" && Document.GetComputedValue(element, "visibility") is not ("hidden" or "collapse");
    }

    /// <summary>The elements under a point (CSS pixels from the top left of the page), the top one first. What a canvas drew or gave geometry to is found by where it put it, in the reverse order it did, above the canvas.</summary>
    private List<ScriptNode> ElementsAtPoint(double x, double y)
    {
        var result = new List<ScriptNode>();
        if (!Document.CanMeasure)
            return result;

        var all = Document.QuerySelectorAll("*").ToList();
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var element = all[i];
            if (DrawableCanvasOf(element) is not null || !IsHittable(element))
                continue;

            var (left, top, width, height) = LayoutOf(element);
            var isRoot = ReferenceEquals(element, Document.DocumentElement);
            if (!isRoot && !(x >= left && x < left + width && y >= top && y < top + height))
                continue;

            if (IsContentDrawable(element) && _htmlCanvases.TryGetValue(element, out var state))
            {
                for (var h = state.HitOrder.Count - 1; h >= 0; h--)
                {
                    var candidate = state.HitOrder[h];
                    if (!IsHittable(candidate) || !state.Transforms.ContainsKey(candidate) && !state.Painted.ContainsKey(candidate))
                        continue;

                    var (cx, cy, cw, ch) = HitRectOf(state, candidate);
                    if (x >= cx && x < cx + cw && y >= cy && y < cy + ch)
                        result.Add(candidate);
                }
            }

            result.Add(element);
        }

        return result;
    }

    // updateElementGeometry(element or ElementImage, {canvasTransform, preserveHitTestOrder}).
    private void ApplyGeometry(ScriptNode canvas, JsValue source, JsValue options, bool queued = false)
    {
        var (state, element) = RequireDrawableChild(canvas, source, needsPaint: false);
        var preserveOrder = options is ObjectInstance order && order.Get("preserveHitTestOrder") is { } keep && !keep.IsUndefined() && TypeConverter.ToBoolean(keep);

        (double[] Values, bool Is2D)? transform = null;
        if (options is ObjectInstance init && init.Get("canvasTransform") is ObjectInstance matrix)
        {
            var values = Engine.Evaluate("(function (m) { return Array.from(m.toFloat64Array()); })").Call(JsValue.Undefined, [matrix]);
            var list = (ObjectInstance)values;
            var numbers = Enumerable.Range(0, (int)TypeConverter.ToNumber(list.Get("length"))).Select(i => TypeConverter.ToNumber(list.Get(i.ToString(CultureInfo.InvariantCulture)))).ToArray();
            if (numbers.Length == 16)
                transform = (numbers, TypeConverter.ToBoolean(matrix.Get("is2D")));
        }

        // canvasClip is a rectangle (negative sides taken as the other corner); left out it keeps the clip the element has.
        (double X, double Y, double Width, double Height)? clipRect = null;
        if (options is ObjectInstance clipInit && clipInit.Get("canvasClip") is ObjectInstance clip)
        {
            double Side(string name) => clip.Get(name) is { } value && !value.IsUndefined() ? TypeConverter.ToNumber(value) : 0;
            var (cx, cy, cw, ch) = (Side("x"), Side("y"), Side("width"), Side("height"));
            (cx, cw) = cw < 0 ? (cx + cw, -cw) : (cx, cw);
            (cy, ch) = ch < 0 ? (cy + ch, -ch) : (cy, ch);
            if (double.IsFinite(cx + cy + cw + ch))
                clipRect = (cx, cy, cw, ch);
        }

        CommitGeometry(canvas, element, queued, () =>
        {
            if (!state.Transforms.ContainsKey(element))
                state.Transforms[element] = (Identity16, true);

            Touch(state, element, preserveOrder);
            if (transform is { } given)
                state.Transforms[element] = given;

            if (clipRect is { } rect)
                state.Clips[element] = rect;
        });
    }

    private void ClearGeometry(ScriptNode canvas, JsValue source, bool queued)
    {
        var (state, element) = RequireDrawableChild(canvas, source, needsPaint: false);
        CommitGeometry(canvas, element, queued, () =>
        {
            state.Transforms.Remove(element);
            state.Clips.Remove(element);
            state.HitOrder.Remove(element);
        });
    }

    // The geometry of an element changes at once when its canvas is asked on the page; an OffscreenCanvas made of the canvas asks from another thread, so its changes are
    // taken in order in a task of their own, which then tells the canvas which elements they were of (elementgeometryupdate).
    private void CommitGeometry(ScriptNode canvas, ScriptNode element, bool queued, Action apply)
    {
        if (!queued)
        {
            apply();
            return;
        }

        if (!_geometryQueue.TryGetValue(canvas, out var pending))
        {
            _geometryQueue[canvas] = pending = [];
            AddTimer(Js.Function(Engine, "elementGeometryUpdate", 0, (_, _) =>
            {
                var batch = _geometryQueue[canvas];
                _geometryQueue.Remove(canvas);
                var elements = new List<ScriptNode>();
                foreach (var (touched, change) in batch)
                {
                    change();
                    if (!elements.Contains(touched))
                        elements.Add(touched);
                }

                var ev = CreateEvent("elementgeometryupdate", bubbles: false, cancelable: false);
                ev.Prototype = ElementGeometryUpdateEventPrototype;
                var array = Js.Array(Engine, elements.Select(n => (JsValue)Wrap(n)).ToArray());
                Engine.Evaluate("Object.freeze").Call(JsValue.Undefined, [array]);
                ev.FastSetProperty("elements", new PropertyDescriptor(array, PropertyFlag.Configurable | PropertyFlag.Enumerable));
                DispatchEvent(Wrap(canvas), ev);
                return JsValue.Undefined;
            }), 0, [], repeat: false, frame: false);
        }

        pending.Add((element, apply));
    }

    private readonly Dictionary<ScriptNode, List<(ScriptNode Element, Action Apply)>> _geometryQueue = [];
    private ObjectInstance ElementGeometryUpdateEventPrototype = null!;

    // The canvas element an OffscreenCanvas was made of by transferControlToOffscreen; InvalidStateError for one that was not.
    private ScriptNode PlaceholderOfOffscreen(ScriptNode offscreen) => LinkOfOffscreen(offscreen).Placeholder;

    // ... and the runtime of the page it is in: the one that keeps the geometry of its children (an OffscreenCanvas in a worker is of a canvas of the page).
    private (ScriptRuntime Owner, ScriptNode Placeholder) LinkOfOffscreen(ScriptNode offscreen) =>
        _offscreenPlaceholders.TryGetValue(offscreen, out var link) ? link : throw DomError("InvalidStateError", "The OffscreenCanvas was not made from a canvas element.");

    private readonly Dictionary<ScriptNode, (ScriptRuntime Owner, ScriptNode Placeholder)> _offscreenPlaceholders = [];

    private void InstallHtmlInCanvas()
    {
        ElementImagePrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Object.PrototypeObject };
        Tag(ElementImagePrototype, "ElementImage");
        Define(Engine.Global, "ElementImage", Js.Constructor(Engine, "ElementImage", ElementImagePrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        // structuredClone of an ElementImage: another one of the same element; null for one that is closed.
        Define(Engine.Global, "__cloneElementImage", Js.Function(Engine, "__cloneElementImage", 1, (_, args) =>
            Js.Arg(args, 0) is ElementImageObject { Closed: false } image ? new ElementImageObject(Engine, ElementImagePrototype, image.Canvas, image.Element, image.Width, image.Height) { Owner = image.Owner, Picture = image.Picture } : JsValue.Null));
        Js.StrictMethod(Engine, ElementImagePrototype, "close", 0, (self, _) =>
        {
            (self as ElementImageObject ?? throw TypeError("Illegal invocation")).Closed = true;
            return JsValue.Undefined;
        });

        ElementGeometryUpdateEventPrototype = new JsObject(Engine) { Prototype = EventPrototype };
        Tag(ElementGeometryUpdateEventPrototype, "ElementGeometryUpdateEvent");
        Define(Engine.Global, "ElementGeometryUpdateEvent", Js.Constructor(Engine, "ElementGeometryUpdateEvent", ElementGeometryUpdateEventPrototype, 1, (_, _, _) => throw TypeError("Illegal constructor")));

        CanvasPaintEventPrototype = new JsObject(Engine) { Prototype = EventPrototype };
        Tag(CanvasPaintEventPrototype, "CanvasPaintEvent");
        Define(Engine.Global, "CanvasPaintEvent", Js.Constructor(Engine, "CanvasPaintEvent", CanvasPaintEventPrototype, 1, (_, _, _) => throw TypeError("Illegal constructor")));

        // Node.moveBefore: insertBefore for a node that is in the same tree already (what it keeps of its state, an iframe's document say, is not modeled).
        Engine.Evaluate("""
            (function (g) {
                function moveBefore(node, child) {
                    if (arguments.length < 2) throw new TypeError("moveBefore needs a node and a child");
                    if (!(node instanceof Node)) throw new TypeError("The first argument is not a Node");
                    if (node.isConnected !== this.isConnected) throw new DOMException("The node and the new parent are not both connected.", "HierarchyRequestError");
                    if (child !== null && child.parentNode !== this) throw new DOMException("The child is not a child of this node.", "NotFoundError");
                    this.insertBefore(node, child);
                }
                ["Element", "Document", "DocumentFragment"].forEach(function (name) {
                    var proto = g[name] && g[name].prototype;
                    if (proto && !("moveBefore" in proto)) Object.defineProperty(proto, "moveBefore", { value: moveBefore, writable: true, configurable: true });
                });
            })
            """).Call(JsValue.Undefined, [Engine.Global]);

        var canvasElement = ElementPrototypeFor("canvas");
        Js.StrictMethod(Engine, canvasElement, "requestPaint", 0, (self, _) =>
        {
            var canvas = Sn(self);
            var state = StateOfCanvas(canvas);
            state.Requested = true;
            ScheduleCanvasPaint(canvas, state);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, canvasElement, "captureElementImage", 1, (self, args) =>
        {
            var canvas = Sn(self);
            var (state, element) = RequireDrawableChild(canvas, Js.Arg(args, 0), needsPaint: true);
            var (width, height) = state.Painted[element];
            return new ElementImageObject(Engine, ElementImagePrototype, canvas, element, width, height) { Owner = this, Picture = Document.RenderElement(element) };
        });
        Js.StrictMethod(Engine, canvasElement, "getElementImageDefaultSize", 1, (self, args) =>
        {
            var canvas = Sn(self);
            var source = Js.Arg(args, 0);
            var (state, element) = RequireDrawableChild(canvas, source, needsPaint: source is not ElementImageObject);
            var (cssWidth, cssHeight) = source is ElementImageObject image ? (image.Width, image.Height) : state.Painted[element];
            var grid = GridScaleOf(canvas);
            var (width, height) = (cssWidth * grid.X, cssHeight * grid.Y);
            var size = Js.NewObject(Engine);
            size.FastSetProperty("width", new PropertyDescriptor(Js.Num(width), PropertyFlag.ConfigurableEnumerableWritable));
            size.FastSetProperty("height", new PropertyDescriptor(Js.Num(height), PropertyFlag.ConfigurableEnumerableWritable));
            return size;
        });
        Js.StrictMethod(Engine, canvasElement, "getElementTransform", 1, (self, args) =>
        {
            var (state, element) = RequireDrawableChild(Sn(self), Js.Arg(args, 0), needsPaint: false);
            return MatrixOf(state.Transforms.TryGetValue(element, out var transform) ? transform : (Identity16, true));
        });
        Js.StrictMethod(Engine, canvasElement, "updateElementGeometry", 1, (self, args) =>
        {
            ApplyGeometry(Sn(self), Js.Arg(args, 0), Js.Arg(args, 1));
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, canvasElement, "clearElementGeometry", 1, (self, args) =>
        {
            ClearGeometry(Sn(self), Js.Arg(args, 0), queued: false);
            return JsValue.Undefined;
        });

        // An OffscreenCanvas works with the ElementImage of a canvas of the page: its size, and the geometry (and so the place in the hit testing order) of the element it came from.
        Js.StrictMethod(Engine, OffscreenPrototype, "getElementImageDefaultSize", 1, (self, args) =>
        {
            var (owner, placeholder) = LinkOfOffscreen(Sn(self));
            var image = Js.Arg(args, 0) as ElementImageObject ?? throw DomError("InvalidStateError", "An OffscreenCanvas answers for an ElementImage only.");
            owner.RequireDrawableChild(placeholder, image, needsPaint: false);
            var offscreenGrid = owner.GridScaleOf(placeholder, Sn(self));
            var size = Js.NewObject(Engine);
            size.FastSetProperty("width", new PropertyDescriptor(Js.Num(image.Width * offscreenGrid.X), PropertyFlag.ConfigurableEnumerableWritable));
            size.FastSetProperty("height", new PropertyDescriptor(Js.Num(image.Height * offscreenGrid.Y), PropertyFlag.ConfigurableEnumerableWritable));
            return size;
        });
        Js.StrictMethod(Engine, OffscreenPrototype, "updateElementGeometry", 1, (self, args) =>
        {
            var (owner, placeholder) = LinkOfOffscreen(Sn(self));
            owner.ApplyGeometry(placeholder, Js.Arg(args, 0), Js.Arg(args, 1), queued: true);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, OffscreenPrototype, "clearElementGeometry", 1, (self, args) =>
        {
            var (owner, placeholder) = LinkOfOffscreen(Sn(self));
            owner.ClearGeometry(placeholder, Js.Arg(args, 0), queued: true);
            return JsValue.Undefined;
        });

        // drawElementImage(element or ElementImage, dx, dy[, dw, dh]) and (…, sx, sy, sw, sh, dx, dy[, dw, dh]): draws the element as it is laid out, and records where it went as its transform.
        Js.StrictMethod(Engine, ContextPrototype, "drawElementImage", 3, (self, args) =>
        {
            var context = Cx(self);
            var canvas = context.Canvas;

            // An OffscreenCanvas made of a canvas draws that canvas's children; the changes to their geometry are taken in a task of their own.
            var queued = ReferenceEquals(context.Prototype, OffscreenContextPrototype);
            var owner = this;
            if (queued)
                (owner, canvas) = LinkOfOffscreen(canvas);

            var (state, element) = owner.RequireDrawableChild(canvas, Js.Arg(args, 0), needsPaint: true);
            var cssPainted = Js.Arg(args, 0) is ElementImageObject captured ? (captured.Width, captured.Height) : state.Painted[element];

            // The source and destination rectangles are in the pixel grid of the canvas; the element is laid out in CSS pixels, a grid scale away.
            var grid = owner.GridScaleOf(canvas, queued ? context.Canvas : null);
            var painted = (cssPainted.Item1 * grid.X, cssPainted.Item2 * grid.Y);
            // A trailing dictionary {preserveElementGeometry} says whether the element keeps the transform it has instead of getting the one this draw makes.
            var preserve = false;
            if (args.Length > 3 && args[^1] is ObjectInstance options && args[^1] is not NodeObject)
            {
                preserve = options.Get("preserveElementGeometry") is { } flag && !flag.IsUndefined() && TypeConverter.ToBoolean(flag);
                args = args[..^1];
            }

            if (args.Length is not (3 or 5 or 7 or 9))
                throw TypeError("drawElementImage takes 3, 5, 7 or 9 arguments.");

            var numbers = args.Skip(1).Select(TypeConverter.ToNumber).ToArray();
            double sx = 0, sy = 0, sw = painted.Item1, sh = painted.Item2, dx, dy, dw, dh;
            if (numbers.Length == 2)
                (dx, dy, dw, dh) = (numbers[0], numbers[1], painted.Item1, painted.Item2);
            else if (numbers.Length == 4)
                (dx, dy, dw, dh) = (numbers[0], numbers[1], numbers[2], numbers[3]);
            else if (numbers.Length == 6)
                (sx, sy, sw, sh, dx, dy, dw, dh) = (numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5], numbers[2], numbers[3]);
            else
                (sx, sy, sw, sh, dx, dy, dw, dh) = (numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5], numbers[6], numbers[7]);

            if (numbers.Any(n => !double.IsFinite(n)) || sw == 0 || sh == 0 || dw == 0 || dh == 0)
                return JsValue.Undefined;

            // A negative width or height is the same rectangle with its origin at the other corner.
            (sx, sw) = sw < 0 ? (sx + sw, -sw) : (sx, sw);
            (sy, sh) = sh < 0 ? (sy + sh, -sh) : (sy, sh);
            (dx, dw) = dw < 0 ? (dx + dw, -dw) : (dx, dw);
            (dy, dh) = dh < 0 ? (dy + dh, -dh) : (dy, dh);

            var scaleX = dw / sw;
            var scaleY = dh / sh;
            if (!queued)
                Touch(state, element, preserveOrder: false);

            if (!preserve)
            {
                // The element's own space to the canvas: placed where it was drawn, then carried by the context's transform.
                var drawn = Affine2D.CreateScale(grid.X, grid.Y) * new Affine2D(scaleX, 0, 0, scaleY, dx - sx * scaleX, dy - sy * scaleY) * context.Bitmap.State.Transform * Affine2D.CreateScale(1 / grid.X, 1 / grid.Y);
                var (source, clipped) = (new Rect(sx, sy, sw, sh), numbers.Length >= 6);
                owner.CommitGeometry(canvas, element, queued, () =>
                {
                    if (queued)
                        Touch(state, element, preserveOrder: false);

                    // What was drawn is the source rectangle of the element, so that is what a pointer can find.
                    if (clipped)
                        state.Clips[element] = (source.X, source.Y, source.Width, source.Height);
                    else
                        state.Clips.Remove(element);

                    state.Transforms[element] = ([drawn.A, drawn.B, 0, 0, drawn.C, drawn.D, 0, 0, 0, 0, 1, 0, drawn.E, drawn.F, 0, 1], true);
                });
            }


            // A zero-size element has nothing to show.
            if (!(painted.Item1 > 0) || !(painted.Item2 > 0))
                return JsValue.Undefined;


            var picture = (Js.Arg(args, 0) as ElementImageObject)?.Picture ?? owner.Document.RenderElement(element) ?? throw DomError("InvalidStateError", "The element could not be drawn.");
            context.Bitmap.DrawImage(picture.Image, new Rect(sx / grid.X, sy / grid.Y, sw / grid.X, sh / grid.Y), new Rect(dx, dy, dw, dh));
            return JsValue.Undefined;
        });
        OffscreenContextPrototype.FastSetProperty("drawElementImage", ContextPrototype.GetOwnProperty("drawElementImage"));
    }

    // How many pixels of the canvas's bitmap (its pixel grid) are in a CSS pixel of the box it has on the page; an element is laid out in CSS pixels and drawn in the grid.
    private (double X, double Y) GridScaleOf(ScriptNode canvas, ScriptNode? bitmapOf = null)
    {
        // An OffscreenCanvas of the canvas has its own size, whatever the placeholder was made.
        var (width, height) = CanvasSize(bitmapOf ?? canvas);
        return Document.GetLayoutBox(canvas) is { Width: > 0, Height: > 0 } box ? (width / box.Width, height / box.Height) : (1, 1);
    }

    private static readonly double[] Identity16 = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
}
