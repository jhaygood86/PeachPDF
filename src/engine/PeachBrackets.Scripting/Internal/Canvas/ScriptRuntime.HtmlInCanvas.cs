using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
using Jint.Runtime;
using PeachBrackets.ScriptHosting;
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
            if (!IsDisplayed(element) || Document.GetLayoutBox(element) is not { } box)
                continue;

            state.Painted[element] = (box.Width, box.Height);
        }
    }

    private void ScheduleCanvasPaint(ScriptNode canvas, HtmlCanvasState state)
    {
        if (state.FrameScheduled)
            return;

        state.FrameScheduled = true;
        AddTimer(Js.Function(Engine, "canvasPaint", 0, (_, _) =>
        {
            RunCanvasPaint(canvas, state);
            return JsValue.Undefined;
        }), 0, [], repeat: false, frame: true);
    }

    private void RunCanvasPaint(ScriptNode canvas, HtmlCanvasState state)
    {
        state.FrameScheduled = false;
        if (!state.Requested && state.Changed.Count == 0)
            return;

        var changed = state.Changed.ToArray();
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
            ScheduleCanvasPaint(canvas, state);
    }

    private ScriptNode ElementOf(JsValue source, out ElementImageObject? image)
    {
        image = source as ElementImageObject;
        if (image is not null)
            return image.Element;

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

        if (!element.HasAttribute("drawable") || !IsInside(canvas, element))
            throw DomError("InvalidStateError", "The element is not a drawable child of the canvas.");

        var state = StateOfCanvas(canvas);
        if (needsPaint && (!canvas.IsConnected || !state.Painted.ContainsKey(element)))
            throw DomError("InvalidStateError", "The element has not been laid out and painted (no paint event has been delivered for it).");

        return (state, element);
    }

    private JsValue MatrixOf((double[] Values, bool Is2D) transform) =>
        Engine.Evaluate("(function (a) { return new DOMMatrix(a); })").Call(JsValue.Undefined,
            [Js.Array(Engine, (transform.Is2D ? [transform.Values[0], transform.Values[1], transform.Values[4], transform.Values[5], transform.Values[12], transform.Values[13]] : transform.Values).Select(Js.Num).ToArray())]);

    private void InstallHtmlInCanvas()
    {
        ElementImagePrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Object.PrototypeObject };
        Tag(ElementImagePrototype, "ElementImage");
        Define(Engine.Global, "ElementImage", Js.Constructor(Engine, "ElementImage", ElementImagePrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));

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
            return new ElementImageObject(Engine, ElementImagePrototype, canvas, element, width, height);
        });
        Js.StrictMethod(Engine, canvasElement, "getElementImageDefaultSize", 1, (self, args) =>
        {
            var canvas = Sn(self);
            var source = Js.Arg(args, 0);
            var (state, element) = RequireDrawableChild(canvas, source, needsPaint: source is not ElementImageObject);
            var (width, height) = source is ElementImageObject image ? (image.Width, image.Height) : state.Painted[element];
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
            var (state, element) = RequireDrawableChild(Sn(self), Js.Arg(args, 0), needsPaint: false);
            if (Js.Arg(args, 1) is ObjectInstance init && init.Get("canvasTransform") is ObjectInstance matrix)
            {
                var values = Engine.Evaluate("(function (m) { return Array.from(m.toFloat64Array()); })").Call(JsValue.Undefined, [matrix]);
                var numbers = ((ObjectInstance)values).GetOwnPropertyKeys(Types.String).Select(k => TypeConverter.ToNumber(((ObjectInstance)values).Get(k))).ToArray();
                if (numbers.Length == 16)
                    state.Transforms[element] = (numbers, TypeConverter.ToBoolean(matrix.Get("is2D")));
            }

            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, canvasElement, "clearElementGeometry", 1, (self, args) =>
        {
            var (state, element) = RequireDrawableChild(Sn(self), Js.Arg(args, 0), needsPaint: false);
            state.Transforms.Remove(element);
            return JsValue.Undefined;
        });

        // drawElementImage(element or ElementImage, dx, dy[, dw, dh]) and (…, sx, sy, sw, sh, dx, dy[, dw, dh]): draws the element as it is laid out, and records where it went as its transform.
        Js.StrictMethod(Engine, ContextPrototype, "drawElementImage", 3, (self, args) =>
        {
            var context = Cx(self);
            var canvas = context.Canvas;
            var (state, element) = RequireDrawableChild(canvas, Js.Arg(args, 0), needsPaint: true);
            var painted = Js.Arg(args, 0) is ElementImageObject captured ? (captured.Width, captured.Height) : state.Painted[element];
            // A trailing dictionary {preserveElementGeometry} says whether the element keeps the transform it has instead of getting the one this draw makes.
            var preserve = false;
            if (args.Length > 3 && args[^1] is ObjectInstance options && args[^1] is not NodeObject)
            {
                preserve = options.Get("preserveElementGeometry") is { } flag && !flag.IsUndefined() && TypeConverter.ToBoolean(flag);
                args = args[..^1];
            }

            if (args.Length is not (3 or 5 or 9))
                throw TypeError("drawElementImage takes 3, 5 or 9 arguments.");

            var numbers = args.Skip(1).Select(TypeConverter.ToNumber).ToArray();
            double sx = 0, sy = 0, sw = painted.Item1, sh = painted.Item2, dx, dy, dw, dh;
            if (numbers.Length == 2)
                (dx, dy, dw, dh) = (numbers[0], numbers[1], painted.Item1, painted.Item2);
            else if (numbers.Length == 4)
                (dx, dy, dw, dh) = (numbers[0], numbers[1], numbers[2], numbers[3]);
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
            if (!preserve)
                state.Transforms[element] = ([scaleX, 0, 0, 0, 0, scaleY, 0, 0, 0, 0, 1, 0, dx - sx * scaleX, dy - sy * scaleY, 0, 1], true);

            // A zero-size element has nothing to show.
            if (!(painted.Item1 > 0) || !(painted.Item2 > 0))
                return JsValue.Undefined;

            var picture = Document.RenderElement(element) ?? throw DomError("InvalidStateError", "The element could not be drawn.");
            context.Bitmap.DrawImage(picture.Image, new Rect(sx, sy, sw, sh), new Rect(dx, dy, dw, dh));
            return JsValue.Undefined;
        });
    }

    private static readonly double[] Identity16 = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
}
