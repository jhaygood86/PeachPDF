using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using PeachBrackets.Scripting.Internal.Canvas;
using PeachBrackets.ScriptHosting;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using DrawingCanvas = PeachDrawing.Core.Canvas;

namespace PeachBrackets.Scripting.Internal;

/// <summary>A <c>CanvasRenderingContext2D</c>: the drawing surface of one canvas element.</summary>
internal sealed class ContextObject : ObjectInstance
{
    public ContextObject(Engine engine, ObjectInstance prototype, ScriptNode canvas, CanvasBitmap bitmap) : base(engine)
    {
        Prototype = prototype;
        Canvas = canvas;
        Bitmap = bitmap;
    }

    public ScriptNode Canvas { get; }

    public CanvasBitmap Bitmap { get; set; }

    public ContextSettings Settings { get; set; } = new();
}

/// <summary>The creation attributes of a 2D context (<c>getContext("2d", settings)</c>).</summary>
internal sealed record ContextSettings(bool Alpha = true, bool Desynchronized = false, bool WillReadFrequently = false, string ColorSpace = "srgb", string ColorType = "unorm8", string ToneMapping = "standard");

/// <summary>A <c>Path2D</c>: a path that is not tied to a canvas.</summary>
internal sealed class Path2DObject : ObjectInstance
{
    public Path2DObject(Engine engine, ObjectInstance prototype) : base(engine)
    {
        Prototype = prototype;
    }

    public PathData Path { get; set; } = new();
}

/// <summary>A <c>CanvasGradient</c>.</summary>
internal sealed class GradientObject : ObjectInstance
{
    public GradientObject(Engine engine, ObjectInstance prototype, CanvasGradient model) : base(engine)
    {
        Prototype = prototype;
        Model = model;
    }

    public CanvasGradient Model { get; }
}

/// <summary>A <c>CanvasPattern</c>.</summary>
internal sealed class PatternObject : ObjectInstance
{
    public PatternObject(Engine engine, ObjectInstance prototype, CanvasPattern model) : base(engine)
    {
        Prototype = prototype;
        Model = model;
    }

    public CanvasPattern Model { get; }
}

// The canvas element and its 2D context, over the recording in Canvas/CanvasModel.cs.
internal sealed partial class ScriptRuntime
{
    private ObjectInstance ContextPrototype = null!;
    private ObjectInstance Path2DPrototype = null!;
    private ObjectInstance GradientPrototype = null!;
    private ObjectInstance PatternPrototype = null!;
    private readonly Dictionary<ScriptNode, ContextObject> _contexts = [];

    // The rectangle with a positive width and height covering the same area: the origin moves to the corner a negative size reaches back from.
    private static Rect Normalized(Rect r) => new(r.Width < 0 ? r.X + r.Width : r.X, r.Height < 0 ? r.Y + r.Height : r.Y, Math.Abs(r.Width), Math.Abs(r.Height));

    private static double Num(JsValue[] args, int index) => TypeConverter.ToNumber(Js.Arg(args, index));

    private static bool Finite(JsValue[] args, int count)
    {
        // Web IDL converts every argument before the method looks at any of them, so a later one's valueOf runs even when an earlier one is not finite.
        var finite = true;
        for (var i = 0; i < count; i++)
            finite &= double.IsFinite(TypeConverter.ToNumber(Js.Arg(args, i)));

        return finite;
    }

    private ContextObject Cx(JsValue self) => self as ContextObject ?? throw TypeError("Illegal invocation");

    // Elements with an id are properties of the window (named access), unless the window has a property of that name.
    private void NameElement(ScriptNode element)
    {
        if (element.GetAttribute("id") is not { Length: > 0 } id)
            return;

        var global = Engine.Global;
        if (global.GetOwnProperty(id) is { } existing && existing != PropertyDescriptor.Undefined && !_namedAccess.Contains(id))
            return;

        _namedAccess.Add(id);
        global.FastSetProperty(id, new GetSetPropertyDescriptor(
            Js.Function(Engine, "get " + id, 0, (_, _) => WrapOrNull(Document.GetElementById(id))),
            Js.Function(Engine, "set " + id, 1, (_, args) =>
            {
                _namedAccess.Remove(id);
                global.FastSetProperty(id, new PropertyDescriptor(Js.Arg(args, 0), PropertyFlag.ConfigurableEnumerableWritable));
                return JsValue.Undefined;
            }),
            enumerable: false, configurable: true));
    }

    private readonly HashSet<string> _namedAccess = [];

    private static (double Width, double Height) CanvasSize(ScriptNode element)
    {
        double Read(string name, double fallback) =>
            ScriptDocument.TryParseNonNegativeInteger(element.GetAttribute(name), out var value) && value <= int.MaxValue ? value : fallback;

        return (Read("width", 300), Read("height", 150));
    }

    private void InstallCanvas()
    {
        var objectPrototype = Engine.Intrinsics.Object.PrototypeObject;
        ContextPrototype = new JsObject(Engine) { Prototype = objectPrototype };
        Path2DPrototype = new JsObject(Engine) { Prototype = objectPrototype };
        GradientPrototype = new JsObject(Engine) { Prototype = objectPrototype };
        PatternPrototype = new JsObject(Engine) { Prototype = objectPrototype };
        Tag(ContextPrototype, "CanvasRenderingContext2D");
        Tag(Path2DPrototype, "Path2D");
        Tag(GradientPrototype, "CanvasGradient");
        Tag(PatternPrototype, "CanvasPattern");

        InstallCanvasFilter();
        InstallCanvasElement();
        InstallContextState();
        // The path a context builds is kept in the space its transform makes, so what is added to it is not moved by the transform.
        InstallPathMethods(ContextPrototype, self => (Cx(self).Bitmap.Path, Matrix3x2.Identity));
        InstallPathMethods(Path2DPrototype, self => (((Path2DObject)self).Path, Matrix3x2.Identity));
        InstallContextDrawing();

        Define(Engine.Global, "CanvasRenderingContext2D", Js.Constructor(Engine, "CanvasRenderingContext2D", ContextPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));

        // Web IDL: an interface object's prototype property is neither writable nor configurable (its methods stay both).
        ((ObjectInstance)Engine.Global.Get("CanvasRenderingContext2D")).DefineOwnProperty("prototype", new PropertyDescriptor(ContextPrototype, PropertyFlag.AllForbidden));
        Define(Engine.Global, "CanvasGradient", Js.Constructor(Engine, "CanvasGradient", GradientPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        Define(Engine.Global, "CanvasPattern", Js.Constructor(Engine, "CanvasPattern", PatternPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        Define(Engine.Global, "Path2D", Js.Constructor(Engine, "Path2D", Path2DPrototype, 0, (_, args, _) =>
        {
            var path = new Path2DObject(Engine, Path2DPrototype);
            if (Js.Arg(args, 0) is Path2DObject other)
                path.Path = other.Path.Clone();
            return path;
        }));
        Js.StrictMethod(Engine, Path2DPrototype, "addPath", 1, (self, args) =>
        {
            if (Js.Arg(args, 0) is Path2DObject other)
            {
                var matrix = args.Length > 1 && args[1] is ObjectInstance init ? MatrixFrom(init) : Affine2D.Identity;
                ((Path2DObject)self).Path.Append(other.Path.Transformed(matrix));
            }

            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, GradientPrototype, "addColorStop", 2, (self, args) =>
        {
            var gradient = ((GradientObject)self).Model;
            var offset = Num(args, 0);
            if (!double.IsFinite(offset))
                throw TypeError("The offset is not finite.");
            if (!(offset >= 0 && offset <= 1))
                throw DomError("IndexSizeError", "The offset must be between 0 and 1.");
            if (!Document.TryParseColor(Js.AsText(Js.Arg(args, 1)), gradient.Owner, out var color, out var serialized))
                throw DomError("SyntaxError", "The color cannot be parsed.");

            gradient.NonLegacy |= serialized is not null;
            gradient.Stops.Add((offset, color));
            return JsValue.Undefined;
        });
    }

    private static Affine2D MatrixFrom(ObjectInstance init)
    {
        double Get(string name, double fallback) => init.HasProperty(name) && !init.Get(name).IsUndefined() ? TypeConverter.ToNumber(init.Get(name)) : fallback;
        return new Affine2D(Get("a", 1), Get("b", 0), Get("c", 0), Get("d", 1), Get("e", 0), Get("f", 0));
    }

    // ---- the element

    private void InstallCanvasElement()
    {
        var element = ElementPrototypeFor("canvas");
        foreach (var (name, fallback) in new[] { ("width", 300), ("height", 150) })
        {
            var (attribute, defaultValue) = (name, fallback);
            Js.Accessor(Engine, element, attribute,
                self => Js.Num(CanvasSize(Sn(self)) is var (w, h) && attribute == "width" ? w : CanvasSize(Sn(self)).Height),
                (self, value) =>
                {
                    // An unsigned long: the value modulo 2^32 (200 - 2^32 is 200, "400x" is 0), and the default when it is more than the attribute can hold.
                    var number = TypeConverter.ToUint32(value);
                    var whole = number <= int.MaxValue ? number : (uint)defaultValue;
                    Sn(self).SetAttribute(attribute, whole.ToString(CultureInfo.InvariantCulture));
                    ResetCanvas(Sn(self));
                });
        }

        Js.StrictMethod(Engine, element, "getContext", 1, (self, args) =>
        {
            if (Js.AsText(Js.Arg(args, 0)) != "2d")
                return JsValue.Null;

            var node = Sn(self);
            if (_contexts.TryGetValue(node, out var existing))
                return existing;

            var settings = ReadContextSettings(Js.Arg(args, 1));
            var (width, height) = CanvasSize(node);
            var bitmap = new CanvasBitmap(node.OwnerDocument, width, height);
            bitmap.UseSurface();
            var context = new ContextObject(Engine, ContextPrototype, node, bitmap) { Settings = settings };
            _contexts[node] = context;
            node.OwnerDocument.SetCanvasPainting(node, context.Bitmap.Painting);
            return context;
        });
        Js.Accessor(Engine, ContextPrototype, "canvas", self => Wrap(Cx(self).Canvas));
    }

    private static readonly string[] ColorSpaces = ["srgb", "display-p3", "srgb-linear", "display-p3-linear"];

    private ContextSettings ReadContextSettings(JsValue options)
    {
        if (options is not ObjectInstance dictionary)
            return new ContextSettings();

        string Choice(string name, string[] allowed, string fallback)
        {
            var value = dictionary.Get(name);
            if (value.IsUndefined())
                return fallback;

            var text = Js.AsText(value);
            return Array.IndexOf(allowed, text) >= 0 ? text : throw TypeError($"The provided value '{text}' is not a valid enum value of type {name}.");
        }

        bool Flag(string name, bool fallback) => dictionary.Get(name) is { } value && !value.IsUndefined() ? TypeConverter.ToBoolean(value) : fallback;

        var tone = "standard";
        if (dictionary.Get("toneMapping") is ObjectInstance mapping && mapping.Get("mode") is { } mode && !mode.IsUndefined())
        {
            tone = Js.AsText(mode);
            if (tone is not ("standard" or "extended"))
                throw TypeError($"The provided value '{tone}' is not a valid enum value of type CanvasToneMappingMode.");
        }

        return new ContextSettings(Flag("alpha", true), Flag("desynchronized", false), Flag("willReadFrequently", false),
            Choice("colorSpace", ColorSpaces, "srgb"), Choice("colorType", ["unorm8", "float16"], "unorm8"), tone);
    }

    // Setting width or height gives the canvas a new, empty bitmap.
    private void ResetCanvas(ScriptNode node)
    {
        if (!_contexts.TryGetValue(node, out var context))
            return;

        var (width, height) = CanvasSize(node);
        context.Bitmap.Dispose();
        context.Bitmap = new CanvasBitmap(node.OwnerDocument, width, height);
        context.Bitmap.UseSurface();
        node.OwnerDocument.SetCanvasPainting(node, context.Bitmap.Painting);
    }

    // ---- state: transform, styles, line and text settings

    private PaintSource StyleFrom(JsValue value, PaintSource current, ScriptNode element)
    {
        switch (value)
        {
            case GradientObject gradient:
                return new PaintSource.GradientSource(gradient.Model);
            case PatternObject pattern:
                return new PaintSource.PatternSource(pattern.Model);
            case ObjectInstance typed when typed.Get("_colorText") is Jint.Native.Function.Function toText:
            {
                // CSSHWB, CSSLab, CSSLCH, CSSOKLab, CSSOKLCH and CSSColor stand for CSS text, which is read as any color is and so reads back serialized.
                var text = Js.AsText(toText.Call(typed, []));
                return Document.TryParseColor(text, element, out var typedColor, out var typedText)
                    ? new PaintSource.ColorSource(typedColor, typedText ?? CanvasBitmap.ColorText(typedColor))
                    : current;
            }
            case ObjectInstance when TryObjectColor(value, out var objectColor):
                return new PaintSource.ColorSource(objectColor, CanvasBitmap.ColorText(objectColor));
        }

        return Document.TryParseColor(Js.AsText(value), element, out var color, out var serialized)
            ? new PaintSource.ColorSource(color, serialized ?? CanvasBitmap.ColorText(color))
            : current;
    }

    private JsValue StyleValue(PaintSource source) => source switch
    {
        PaintSource.ColorSource c => Js.Str(c.Text),
        PaintSource.GradientSource g => _gradients.TryGetValue(g.Gradient, out var obj) ? obj : JsValue.Null,
        PaintSource.PatternSource p => _patterns.TryGetValue(p.Pattern, out var obj) ? obj : JsValue.Null,
        _ => JsValue.Null,
    };

    private readonly Dictionary<CanvasGradient, GradientObject> _gradients = [];
    private readonly Dictionary<CanvasPattern, PatternObject> _patterns = [];

    private JsValue NewGradient(CanvasGradient model)
    {
        var gradient = new GradientObject(Engine, GradientPrototype, model);
        _gradients[model] = gradient;
        return gradient;
    }

    private void InstallContextState()
    {
        var p = ContextPrototype;
        CanvasState S(JsValue self) => Cx(self).Bitmap.State;

        Js.StrictMethod(Engine, p, "save", 0, (self, _) => { Cx(self).Bitmap.Save(); return JsValue.Undefined; });
        Js.StrictMethod(Engine, p, "restore", 0, (self, _) =>
        {
            // A restore cannot reach past a layer to a state saved before the layer opened.
            if (!Cx(self).Bitmap.CanRestore)
                throw DomError("InvalidStateError", "restore() cannot pop a state saved before the open layer.");

            Cx(self).Bitmap.Restore();
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "beginLayer", 0, (self, args) =>
        {
            // The options are a dictionary: nothing, null and any object are one, a primitive is not. Its filter is read (and may throw) before the layer opens.
            var options = Js.Arg(args, 0);
            if (!options.IsUndefined() && !options.IsNull() && options is not ObjectInstance)
                throw TypeError("Failed to execute 'beginLayer': The options are not a dictionary.");

            var effects = options is ObjectInstance dictionary ? LayerFilterEffects(dictionary.Get("filter")) : [];
            Cx(self).Bitmap.BeginLayer(effects);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "endLayer", 0, (self, _) =>
        {
            if (!Cx(self).Bitmap.CanEndLayer)
                throw DomError("InvalidStateError", "endLayer() has no layer to end, or a save() made in it is still open.");

            Cx(self).Bitmap.EndLayer();
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "reset", 0, (self, _) => { Cx(self).Bitmap.Reset(); return JsValue.Undefined; });

        Js.StrictMethod(Engine, p, "scale", 2, (self, args) =>
        {
            if (Finite(args, 2)) Cx(self).Bitmap.Concat(Affine2D.CreateScale(Num(args, 0), Num(args, 1)));
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "rotate", 1, (self, args) =>
        {
            if (Finite(args, 1)) Cx(self).Bitmap.Concat(Affine2D.CreateRotation(Num(args, 0)));
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "translate", 2, (self, args) =>
        {
            if (Finite(args, 2)) Cx(self).Bitmap.Concat(Affine2D.CreateTranslation(Num(args, 0), Num(args, 1)));
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "transform", 6, (self, args) =>
        {
            if (Finite(args, 6))
                Cx(self).Bitmap.Concat(new Affine2D(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), Num(args, 4), Num(args, 5)));
            return JsValue.Undefined;
        });
        // Two overloads: six numbers, or an optional matrix (none, or an object). Anything between is the six-number form with arguments missing.
        Js.Method(Engine, p, "setTransform", 0, (self, args) =>
        {
            if (args.Length is >= 1 and < 6 && !(args.Length == 1 && args[0] is ObjectInstance))
                throw TypeError($"Failed to execute 'setTransform': 6 arguments required, but only {args.Length} present.");

            if (args.Length == 0)
                Cx(self).Bitmap.SetTransform(Affine2D.Identity);
            else if (args[0] is ObjectInstance init)
                Cx(self).Bitmap.SetTransform(MatrixFrom(init));
            else if (Finite(args, 6))
                Cx(self).Bitmap.SetTransform(new Affine2D(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), Num(args, 4), Num(args, 5)));
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "resetTransform", 0, (self, _) => { Cx(self).Bitmap.SetTransform(Affine2D.Identity); return JsValue.Undefined; });
        Js.StrictMethod(Engine, p, "getTransform", 0, (self, _) =>
        {
            var m = S(self).Transform;
            return Engine.Evaluate("(function (a) { return new DOMMatrix(a); })").Call(JsValue.Undefined, [Js.Array(Engine, [Js.Num(m.A), Js.Num(m.B), Js.Num(m.C), Js.Num(m.D), Js.Num(m.E), Js.Num(m.F)])]);
        });

        Js.Accessor(Engine, p, "fillStyle", self => StyleValue(S(self).FillStyle), (self, value) => S(self).FillStyle = StyleFrom(value, S(self).FillStyle, Cx(self).Canvas));
        Js.Accessor(Engine, p, "strokeStyle", self => StyleValue(S(self).StrokeStyle), (self, value) => S(self).StrokeStyle = StyleFrom(value, S(self).StrokeStyle, Cx(self).Canvas));
        Js.Accessor(Engine, p, "lineWidth", self => Js.Num(S(self).LineWidth), (self, value) =>
        {
            var number = TypeConverter.ToNumber(value);
            if (double.IsFinite(number) && number > 0) S(self).LineWidth = number;
        });
        Js.Accessor(Engine, p, "lineCap", self => Js.Str(S(self).LineCap switch { LineCap.Round => "round", LineCap.Square => "square", _ => "butt" }), (self, value) =>
        {
            switch (Js.AsText(value))
            {
                case "butt": S(self).LineCap = LineCap.Butt; break;
                case "round": S(self).LineCap = LineCap.Round; break;
                case "square": S(self).LineCap = LineCap.Square; break;
            }
        });
        Js.Accessor(Engine, p, "lineJoin", self => Js.Str(S(self).LineJoin switch { LineJoin.Round => "round", LineJoin.Bevel => "bevel", _ => "miter" }), (self, value) =>
        {
            switch (Js.AsText(value))
            {
                case "miter": S(self).LineJoin = LineJoin.Miter; break;
                case "round": S(self).LineJoin = LineJoin.Round; break;
                case "bevel": S(self).LineJoin = LineJoin.Bevel; break;
            }
        });
        Js.Accessor(Engine, p, "miterLimit", self => Js.Num(S(self).MiterLimit), (self, value) =>
        {
            var number = TypeConverter.ToNumber(value);
            if (double.IsFinite(number) && number > 0) S(self).MiterLimit = number;
        });
        Js.StrictMethod(Engine, p, "setLineDash", 1, (self, args) =>
        {
            if (Js.Arg(args, 0) is not ObjectInstance list)
                throw TypeError("The argument is not a sequence.");

            var length = (int)TypeConverter.ToNumber(list.Get("length"));
            var values = Enumerable.Range(0, length).Select(i => TypeConverter.ToNumber(list.Get(i.ToString(CultureInfo.InvariantCulture)))).ToList();
            if (values.Any(v => !double.IsFinite(v) || v < 0))
                return JsValue.Undefined;

            if (values.Count % 2 == 1) values.AddRange(values.ToArray());
            S(self).LineDash = values.Sum() == 0 ? [] : [.. values];
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "getLineDash", 0, (self, _) => Js.Array(Engine, [.. S(self).LineDash.Select(Js.Num)]));
        Js.Accessor(Engine, p, "lineDashOffset", self => Js.Num(S(self).LineDashOffset), (self, value) =>
        {
            var number = TypeConverter.ToNumber(value);
            if (double.IsFinite(number)) S(self).LineDashOffset = number;
        });
        Js.Accessor(Engine, p, "globalAlpha", self => Js.Num(S(self).GlobalAlpha), (self, value) =>
        {
            var number = TypeConverter.ToNumber(value);
            if (number >= 0 && number <= 1) S(self).GlobalAlpha = number;
        });
        Js.Accessor(Engine, p, "globalCompositeOperation", self => Js.Str(S(self).Composite), (self, value) =>
        {
            var text = Js.AsText(value);
            if (CanvasBitmap.IsKnownComposite(text)) S(self).Composite = text;
        });
        Js.Accessor(Engine, p, "shadowColor", self => Js.Str(S(self).ShadowColorText), (self, value) =>
        {
            if (Document.TryParseColor(Js.AsText(value), Cx(self).Canvas, out var color, out var serialized))
            {
                S(self).ShadowColor = color;
                S(self).ShadowColorText = serialized ?? CanvasBitmap.ColorText(color);
            }
        });
        foreach (var (name, setter) in new (string, Action<CanvasState, double>)[]
        {
            ("shadowBlur", (s, v) => { if (v >= 0) s.ShadowBlur = v; }), ("shadowOffsetX", (s, v) => s.ShadowOffsetX = v), ("shadowOffsetY", (s, v) => s.ShadowOffsetY = v),
        })
        {
            var (property, set) = (name, setter);
            Js.Accessor(Engine, p, property,
                self => Js.Num(property == "shadowBlur" ? S(self).ShadowBlur : property == "shadowOffsetX" ? S(self).ShadowOffsetX : S(self).ShadowOffsetY),
                (self, value) =>
                {
                    var number = TypeConverter.ToNumber(value);
                    if (double.IsFinite(number)) set(S(self), number);
                });
        }

        Js.Accessor(Engine, p, "font", self => Js.Str(S(self).Font), (self, value) =>
        {
            var text = Js.AsText(value);
            if (ParseFont(text) is not null) S(self).Font = text;
        });
        Js.Accessor(Engine, p, "textAlign", self => Js.Str(S(self).TextAlign), (self, value) =>
        {
            var text = Js.AsText(value);
            if (text is "start" or "end" or "left" or "right" or "center") S(self).TextAlign = text;
        });
        Js.Accessor(Engine, p, "textBaseline", self => Js.Str(S(self).TextBaseline), (self, value) =>
        {
            var text = Js.AsText(value);
            if (text is "top" or "hanging" or "middle" or "alphabetic" or "ideographic" or "bottom") S(self).TextBaseline = text;
        });
        Js.Accessor(Engine, p, "direction", self => Js.Str(S(self).Direction), (self, value) =>
        {
            var text = Js.AsText(value);
            if (text is "ltr" or "rtl" or "inherit") S(self).Direction = text;
        });
        Js.Accessor(Engine, p, "imageSmoothingEnabled", self => Js.Bool(S(self).ImageSmoothing), (self, value) => S(self).ImageSmoothing = TypeConverter.ToBoolean(value));
        Js.Accessor(Engine, p, "imageSmoothingQuality", self => Js.Str(S(self).ImageSmoothingQuality), (self, value) =>
        {
            var text = Js.AsText(value);
            if (text is "low" or "medium" or "high") S(self).ImageSmoothingQuality = text;
        });
        // What was assigned that is not a filter is ignored; a CanvasFilter is kept and given back as it is; text is given back as it was written.
        Js.Accessor(Engine, p, "filter", self => S(self).FilterObject as JsValue ?? Js.Str(S(self).Filter), (self, value) =>
        {
            if (!TryFilterEffects(value, Cx(self).Canvas, out var effects, out var text))
                return;

            var state = S(self);
            state.FilterEffects = effects;
            state.FilterObject = value as CanvasFilterObject;
            state.Filter = value is CanvasFilterObject ? "none" : text;
        });
    }

    // ---- fonts

    private sealed record FontSpec(string[] Families, double SizePixels, bool Italic, double Weight);

    // The CSS font shorthand: style, variant, weight and stretch in any order, then a size, then families.
    private static FontSpec? ParseFont(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var italic = false;
        var weight = 400.0;
        var index = 0;
        for (; index < tokens.Length; index++)
        {
            var token = tokens[index].ToLowerInvariant();
            if (token is "italic" or "oblique") italic = true;
            else if (token is "bold") weight = 700;
            else if (token is "bolder") weight = 700;
            else if (token is "lighter") weight = 300;
            else if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 1000) weight = number;
            else if (token is "normal" or "small-caps" or "ultra-condensed" or "extra-condensed" or "condensed" or "semi-condensed" or "semi-expanded" or "expanded" or "extra-expanded" or "ultra-expanded") continue;
            else break;
        }

        if (index >= tokens.Length - 0 || index >= tokens.Length)
            return null;

        var sizeToken = tokens[index].Split('/')[0].ToLowerInvariant();
        double size;
        if (sizeToken.EndsWith("px", StringComparison.Ordinal) && double.TryParse(sizeToken[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out size)) { }
        else if (sizeToken.EndsWith("pt", StringComparison.Ordinal) && double.TryParse(sizeToken[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out size)) size *= 96.0 / 72.0;
        else if (sizeToken.EndsWith("rem", StringComparison.Ordinal) && double.TryParse(sizeToken[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out size)) size *= 16;
        else if (sizeToken.EndsWith("em", StringComparison.Ordinal) && double.TryParse(sizeToken[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out size)) size *= 10;
        else if (sizeToken.EndsWith('%') && double.TryParse(sizeToken[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out size)) size = size / 100 * 10;
        else return null;

        var families = string.Join(' ', tokens[(index + 1)..]).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.Trim('"', '\'')).ToArray();
        return families.Length == 0 || size < 0 ? null : new FontSpec(families, size, italic, weight);
    }

    private Font? FontFor(ContextObject context)
    {
        if (ParseFont(context.Bitmap.State.Font) is not { } spec)
            return null;

        var style = (spec.Weight >= 600 ? PaintFontStyle.Bold : PaintFontStyle.Regular) | (spec.Italic ? PaintFontStyle.Italic : PaintFontStyle.Regular);
        foreach (var family in spec.Families)
        {
            if (context.Canvas.OwnerDocument.GetFont(family, spec.SizePixels, style, spec.Weight) is { } font)
                return font;
        }

        return context.Canvas.OwnerDocument.GetFont("sans-serif", spec.SizePixels, style, spec.Weight);
    }

    // ---- paths (shared by the context and Path2D)

    private void InstallPathMethods(ObjectInstance p, Func<JsValue, (PathData Path, Matrix3x2 Matrix)> target)
    {
        Js.StrictMethod(Engine, p, "closePath", 0, (self, _) => { target(self).Path.Close(); return JsValue.Undefined; });
        Js.StrictMethod(Engine, p, "moveTo", 2, (self, args) =>
        {
            if (!Finite(args, 2)) return JsValue.Undefined;
            var (path, m) = target(self);
            var point = PathData.T(m, Num(args, 0), Num(args, 1));
            path.MoveTo(point.X, point.Y);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "lineTo", 2, (self, args) =>
        {
            if (!Finite(args, 2)) return JsValue.Undefined;
            var (path, m) = target(self);
            var point = PathData.T(m, Num(args, 0), Num(args, 1));
            path.LineTo(point.X, point.Y);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "quadraticCurveTo", 4, (self, args) =>
        {
            if (!Finite(args, 4)) return JsValue.Undefined;
            var (path, m) = target(self);
            if (!path.HasPoint)
            {
                var start = PathData.T(m, Num(args, 0), Num(args, 1));
                path.MoveTo(start.X, start.Y);
            }

            var (c, e) = (PathData.T(m, Num(args, 0), Num(args, 1)), PathData.T(m, Num(args, 2), Num(args, 3)));
            var (x0, y0) = (path.CurrentX, path.CurrentY);
            path.CurveTo(x0 + 2.0 / 3 * (c.X - x0), y0 + 2.0 / 3 * (c.Y - y0), e.X + 2.0 / 3 * (c.X - e.X), e.Y + 2.0 / 3 * (c.Y - e.Y), e.X, e.Y);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "bezierCurveTo", 6, (self, args) =>
        {
            if (!Finite(args, 6)) return JsValue.Undefined;
            var (path, m) = target(self);
            var (a, b, c) = (PathData.T(m, Num(args, 0), Num(args, 1)), PathData.T(m, Num(args, 2), Num(args, 3)), PathData.T(m, Num(args, 4), Num(args, 5)));
            if (!path.HasPoint) path.MoveTo(a.X, a.Y);
            path.CurveTo(a.X, a.Y, b.X, b.Y, c.X, c.Y);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "arc", 5, (self, args) =>
        {
            if (!Finite(args, 5)) return JsValue.Undefined;
            if (Num(args, 2) < 0) throw DomError("IndexSizeError", "The radius is negative.");
            var (path, m) = target(self);
            path.AddArc(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 2), 0, Num(args, 3), Num(args, 4), TypeConverter.ToBoolean(Js.Arg(args, 5)), m);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "ellipse", 7, (self, args) =>
        {
            if (!Finite(args, 7)) return JsValue.Undefined;
            if (Num(args, 2) < 0 || Num(args, 3) < 0) throw DomError("IndexSizeError", "A radius is negative.");
            var (path, m) = target(self);
            path.AddArc(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), Num(args, 4), Num(args, 5), Num(args, 6), TypeConverter.ToBoolean(Js.Arg(args, 7)), m);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "arcTo", 5, (self, args) =>
        {
            if (!Finite(args, 5)) return JsValue.Undefined;
            if (Num(args, 4) < 0) throw DomError("IndexSizeError", "The radius is negative.");
            var (path, m) = target(self);
            ArcTo(path, m, Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), Num(args, 4));
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "rect", 4, (self, args) =>
        {
            if (!Finite(args, 4)) return JsValue.Undefined;
            var (path, m) = target(self);
            path.AddRect(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), m);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "roundRect", 4, (self, args) =>
        {
            // Web IDL first: every argument is converted (a BigInt is a TypeError) before the method does anything.
            var numbers = new double[4];
            for (var i = 0; i < 4; i++)
                numbers[i] = TypeConverter.ToNumber(Js.Arg(args, i));

            var radii = RoundRectRadii(Js.Arg(args, 4), out var finite);

            // Then the algorithm: a rectangle that is not finite does nothing, and neither do radii that are not; a negative radius or a list of the wrong length is a RangeError.
            if (numbers.Any(n => !double.IsFinite(n)))
                return JsValue.Undefined;
            if (radii.Count is 0 or > 4)
                throw RangeError("roundRect takes one to four radii.");
            if (!finite)
                return JsValue.Undefined;
            if (radii.Any(r => r.X < 0 || r.Y < 0))
                throw RangeError("A radius is negative.");

            var (path, m) = target(self);
            RoundRect(path, m, numbers[0], numbers[1], numbers[2], numbers[3], radii);
            return JsValue.Undefined;
        });
    }

    private Exception RangeError(string message) => new JavaScriptException(Engine.Construct(Engine.Evaluate("RangeError"), [Js.Str(message)]));

    // The radii of roundRect's argument as (horizontal, vertical) pairs: a number or a point (DOMPointInit, a DOMPoint among them) is one corner's radius, a list is one to four. A
    // missing argument is a radius of 0. `finite` says whether every number in them is.
    private static List<(double X, double Y)> RoundRectRadii(JsValue value, out bool finite)
    {
        finite = true;
        var items = new List<(double X, double Y)>();

        (double X, double Y) Item(JsValue v)
        {
            // A number is a circle's radius; null, undefined and any object are read as a point dictionary whose x and y default to 0.
            if (v is ObjectInstance point)
                return (Component(point, "x"), Component(point, "y"));
            if (v.IsUndefined() || v.IsNull())
                return (0, 0);

            var radius = TypeConverter.ToNumber(v);
            return (radius, radius);
        }

        double Component(ObjectInstance point, string name)
        {
            var component = point.Get(name);
            return component.IsUndefined() ? 0 : TypeConverter.ToNumber(component);
        }

        if (value.IsUndefined())
        {
            items.Add((0, 0));
        }
        else if (value is ObjectInstance list && list.IsArray())
        {
            var count = (int)TypeConverter.ToNumber(list.Get("length"));
            for (var i = 0; i < count; i++)
                items.Add(Item(list.Get(i.ToString(CultureInfo.InvariantCulture))));
        }
        else
        {
            items.Add(Item(value));
        }

        finite = items.All(r => double.IsFinite(r.X) && double.IsFinite(r.Y));
        return items;
    }

    // The path of a rounded rectangle, each corner with its own horizontal and vertical radius. The corners are those of the rectangle as the caller gave it: a negative width or
    // height starts the path at (x, y) and goes the other way, so the first radius is the one at (x, y) wherever that is, and the outline winds the other way as a rectangle does.
    private static void RoundRect(PathData path, Matrix3x2 m, double x, double y, double w, double h, List<(double X, double Y)> radii)
    {
        // One value for all four corners; two are the diagonals (top left and bottom right, then top right and bottom left); three have the last two of the diagonal pairs sharing;
        // four are the corners clockwise from the top left.
        var (ul, ur, lr, ll) = radii.Count switch
        {
            1 => (radii[0], radii[0], radii[0], radii[0]),
            2 => (radii[0], radii[1], radii[0], radii[1]),
            3 => (radii[0], radii[1], radii[2], radii[1]),
            _ => (radii[0], radii[1], radii[2], radii[3]),
        };

        var (width, height) = (Math.Abs(w), Math.Abs(h));

        // Radii that would overlap along an edge are all scaled down together until the worst edge just fits.
        var scale = 1.0;
        void Fit(double length, double sum)
        {
            if (sum > 0 && length / sum < scale)
                scale = length / sum;
        }

        Fit(width, ul.X + ur.X);
        Fit(height, ur.Y + lr.Y);
        Fit(width, lr.X + ll.X);
        Fit(height, ul.Y + ll.Y);
        if (scale < 1)
        {
            ul = (ul.X * scale, ul.Y * scale);
            ur = (ur.X * scale, ur.Y * scale);
            lr = (lr.X * scale, lr.Y * scale);
            ll = (ll.X * scale, ll.Y * scale);
        }

        // The outline is built for a positive size at the origin and carried to (x, y) by a mirror: the same code gives every sign of width and height.
        var local = Matrix3x2.CreateScale(w < 0 ? -1f : 1f, h < 0 ? -1f : 1f) * Matrix3x2.CreateTranslation((float)x, (float)y) * m;
        Vector2 P(double px, double py) => PathData.T(local, px, py);

        var start = P(ul.X, 0);
        path.MoveTo(start.X, start.Y);
        var a = P(width - ur.X, 0); path.LineTo(a.X, a.Y);
        path.AddArc(width - ur.X, ur.Y, ur.X, ur.Y, 0, -Math.PI / 2, 0, false, local);
        var b = P(width, height - lr.Y); path.LineTo(b.X, b.Y);
        path.AddArc(width - lr.X, height - lr.Y, lr.X, lr.Y, 0, 0, Math.PI / 2, false, local);
        var c = P(ll.X, height); path.LineTo(c.X, c.Y);
        path.AddArc(ll.X, height - ll.Y, ll.X, ll.Y, 0, Math.PI / 2, Math.PI, false, local);
        var d = P(0, ul.Y); path.LineTo(d.X, d.Y);
        path.AddArc(ul.X, ul.Y, ul.X, ul.Y, 0, Math.PI, Math.PI * 1.5, false, local);
        path.Close();

        // Like rect(), roundRect leaves a new subpath started at (x, y).
        var origin = P(0, 0);
        path.MoveTo(origin.X, origin.Y);
    }

    // arcTo in the space the path is built in: the current point is brought back out of the matrix, the arc worked out there, and the result put back through it.
    private static void ArcTo(PathData path, Matrix3x2 m, double x1, double y1, double x2, double y2, double radius)
    {
        if (!path.HasPoint)
        {
            var first = PathData.T(m, x1, y1);
            path.MoveTo(first.X, first.Y);
            return;
        }

        if (!Matrix3x2.Invert(m, out var inverse))
            return;

        var current = Vector2.Transform(new Vector2((float)path.CurrentX, (float)path.CurrentY), inverse);
        var (p0x, p0y) = ((double)current.X, (double)current.Y);
        var (d0x, d0y) = (p0x - x1, p0y - y1);
        var (d2x, d2y) = (x2 - x1, y2 - y1);
        var (l0, l2) = (Math.Sqrt(d0x * d0x + d0y * d0y), Math.Sqrt(d2x * d2x + d2y * d2y));
        var cross = d0x * d2y - d0y * d2x;
        if (l0 == 0 || l2 == 0 || radius == 0 || Math.Abs(cross) < 1e-12 * l0 * l2)
        {
            var line = PathData.T(m, x1, y1);
            path.LineTo(line.X, line.Y);
            return;
        }

        var (u0x, u0y, u2x, u2y) = (d0x / l0, d0y / l0, d2x / l2, d2y / l2);
        var angle = Math.Acos(Math.Clamp(u0x * u2x + u0y * u2y, -1, 1));
        var distance = radius / Math.Tan(angle / 2);
        var (t1x, t1y) = (x1 + u0x * distance, y1 + u0y * distance);
        var (t2x, t2y) = (x1 + u2x * distance, y1 + u2y * distance);
        var (bx, by) = (u0x + u2x, u0y + u2y);
        var bl = Math.Sqrt(bx * bx + by * by);
        var centerDistance = radius / Math.Sin(angle / 2);
        var (cx, cy) = (x1 + bx / bl * centerDistance, y1 + by / bl * centerDistance);
        var (a1, a2) = (Math.Atan2(t1y - cy, t1x - cx), Math.Atan2(t2y - cy, t2x - cx));
        var delta = a2 - a1;
        while (delta > Math.PI) delta -= Math.PI * 2;
        while (delta <= -Math.PI) delta += Math.PI * 2;
        path.AddArc(cx, cy, radius, radius, 0, a1, a2, counterClockwise: delta < 0, m);
    }

    // ---- drawing

    private void InstallContextDrawing()
    {
        var p = ContextPrototype;
        CanvasBitmap B(JsValue self) => Cx(self).Bitmap;

        Js.StrictMethod(Engine, p, "beginPath", 0, (self, _) => { B(self).Path = new PathData(); return JsValue.Undefined; });

        (PathData Path, FillMode Rule) PathArgs(JsValue self, JsValue[] args)
        {
            var path = B(self).Path;
            var index = 0;
            if (Js.Arg(args, 0) is Path2DObject own)
            {
                path = own.Path;
                index = 1;
            }

            var rule = Js.Arg(args, index) is { } text && text.IsString() && text.AsString() == "evenodd" ? FillMode.EvenOdd : FillMode.Nonzero;
            return (path, rule);
        }

        Js.StrictMethod(Engine, p, "fill", 0, (self, args) =>
        {
            var (path, rule) = PathArgs(self, args);
            B(self).FillPath(path, rule);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "stroke", 0, (self, args) =>
        {
            var path = Js.Arg(args, 0) is Path2DObject own ? own.Path : B(self).Path;
            B(self).StrokePath(path);
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "clip", 0, (self, args) =>
        {
            var (path, rule) = PathArgs(self, args);
            B(self).ClipPath(path, rule);
            return JsValue.Undefined;
        });

        Js.StrictMethod(Engine, p, "fillRect", 4, (self, args) =>
        {
            if (Finite(args, 4)) B(self).FillRect(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3));
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "strokeRect", 4, (self, args) =>
        {
            if (Finite(args, 4)) B(self).StrokeRect(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3));
            return JsValue.Undefined;
        });
        Js.StrictMethod(Engine, p, "clearRect", 4, (self, args) =>
        {
            if (Finite(args, 4)) B(self).ClearRect(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3));
            return JsValue.Undefined;
        });

        Js.StrictMethod(Engine, p, "createLinearGradient", 4, (self, args) =>
        {
            if (!Finite(args, 4)) throw TypeError("The gradient coordinates are not finite.");
            return NewGradient(new CanvasGradient("linear", [Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3)]) { Owner = Cx(self).Canvas, InterpolateInOklab = Document.InterpolateGradientStops });
        });
        Js.StrictMethod(Engine, p, "createRadialGradient", 6, (self, args) =>
        {
            if (!Finite(args, 6)) throw TypeError("The gradient coordinates are not finite.");
            if (Num(args, 2) < 0 || Num(args, 5) < 0) throw DomError("IndexSizeError", "A radius is negative.");
            return NewGradient(new CanvasGradient("radial", [Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), Num(args, 4), Num(args, 5)]) { Owner = Cx(self).Canvas, InterpolateInOklab = Document.InterpolateGradientStops });
        });
        Js.StrictMethod(Engine, p, "createConicGradient", 3, (self, args) =>
        {
            if (!Finite(args, 3)) throw TypeError("The gradient coordinates are not finite.");
            return NewGradient(new CanvasGradient("conic", [Num(args, 0), Num(args, 1), Num(args, 2)]) { Owner = Cx(self).Canvas, InterpolateInOklab = Document.InterpolateGradientStops });
        });
        Js.StrictMethod(Engine, p, "createPattern", 2, (self, args) =>
        {
            var image = Js.Arg(args, 0) is NodeObject { Node: { Kind: ScriptNodeKind.Element } canvasNode } && string.Equals(canvasNode.TagName, "canvas", StringComparison.OrdinalIgnoreCase)
                ? SnapshotOf(canvasNode)
                : ImageOf(Js.Arg(args, 0));
            var repetition = Js.IsNullish(Js.Arg(args, 1)) ? "repeat" : Js.AsText(Js.Arg(args, 1));
            if (repetition is not ("repeat" or "repeat-x" or "repeat-y" or "no-repeat"))
                throw DomError("SyntaxError", "The repetition is not valid.");
            if (image is null)
                return JsValue.Null;

            var model = new CanvasPattern(image, repetition);
            var pattern = new PatternObject(Engine, PatternPrototype, model);
            _patterns[model] = pattern;
            return pattern;
        });

        Js.StrictMethod(Engine, p, "drawImage", 3, (self, args) =>
        {
            var source = Js.Arg(args, 0);
            var numbers = args.Skip(1).Select(TypeConverter.ToNumber).ToArray();
            if (numbers.Length is not (2 or 4 or 8) || numbers.Any(n => !double.IsFinite(n)))
            {
                if (numbers.Length is not (2 or 4 or 8)) throw TypeError("drawImage takes 3, 5 or 9 arguments.");
                return JsValue.Undefined;
            }

            if (source is NodeObject { Node: { Kind: ScriptNodeKind.Element } element } && string.Equals(element.TagName, "canvas", StringComparison.OrdinalIgnoreCase))
            {
                // A canvas with no width or height is not a picture to draw. One that has a size but no context holds nothing: transparent, so nothing is drawn.
                var (canvasWidth, canvasHeight) = CanvasSize(element);
                if (canvasWidth == 0 || canvasHeight == 0)
                    throw DomError("InvalidStateError", "The canvas has no width or height.");

                if (!_contexts.TryGetValue(element, out var other))
                    return JsValue.Undefined;

                var (dx, dy, dw, dh) = numbers.Length switch
                {
                    2 => (numbers[0], numbers[1], other.Bitmap.Width, other.Bitmap.Height),
                    4 => (numbers[0], numbers[1], numbers[2], numbers[3]),
                    _ => (numbers[4], numbers[5], numbers[6], numbers[7]),
                };
                RequireNoOpenLayers(other.Bitmap);
                B(self).DrawCanvas(other.Bitmap, Normalized(new Rect(dx, dy, dw, dh)));
                return JsValue.Undefined;
            }

            if (source is BitmapObject { Image: null })
                throw DomError("InvalidStateError", "The image bitmap is closed.");

            if (ImageOf(source) is not { } image)
            {
                if (source is NodeObject { Node: { Kind: ScriptNodeKind.Element } img } && IsImage(img))
                {
                    // Broken is an error; an image that is not loaded yet (or has nothing to load) is not an error but is not drawn either.
                    _images.TryGetValue(img, out var state);
                    if (state?.Status == 3)
                        throw DomError("InvalidStateError", "The image is broken.");

                    if (state?.Status == 2 && img.GetAttribute("src") is { } svgAddress && Document.IsSvgImage(svgAddress))
                    {
                        var (svgSx, svgSy, svgSw, svgSh) = numbers.Length == 8 ? (numbers[0], numbers[1], numbers[2], numbers[3]) : (0, 0, state.Width, state.Height);
                        var svgDestination = numbers.Length switch
                        {
                            2 => new Rect(numbers[0], numbers[1], state.Width, state.Height),
                            4 => new Rect(numbers[0], numbers[1], numbers[2], numbers[3]),
                            _ => new Rect(numbers[4], numbers[5], numbers[6], numbers[7]),
                        };
                        B(self).DrawSvg(svgAddress, state.Width, state.Height, Normalized(new Rect(svgSx, svgSy, svgSw, svgSh)), Normalized(svgDestination));
                    }

                    return JsValue.Undefined;
                }

                throw TypeError("The image argument is not a drawable object.");
            }

            var (sx, sy, sw, sh) = numbers.Length == 8 ? (numbers[0], numbers[1], numbers[2], numbers[3]) : (0, 0, image.Width, image.Height);
            var d = numbers.Length switch
            {
                2 => new Rect(numbers[0], numbers[1], image.Width, image.Height),
                4 => new Rect(numbers[0], numbers[1], numbers[2], numbers[3]),
                _ => new Rect(numbers[4], numbers[5], numbers[6], numbers[7]),
            };

            // A rectangle with a negative width or height is the same rectangle with its origin moved to the other corner: it never flips the picture.
            if (sw != 0 && sh != 0 && d.Width != 0 && d.Height != 0)
                B(self).DrawImage(image, Normalized(new Rect(sx, sy, sw, sh)), Normalized(d));

            return JsValue.Undefined;
        });

        foreach (var stroke in new[] { false, true })
        {
            var isStroke = stroke;
            Js.StrictMethod(Engine, p, isStroke ? "strokeText" : "fillText", 3, (self, args) =>
            {
                if (!double.IsFinite(Num(args, 1)) || !double.IsFinite(Num(args, 2)))
                    return JsValue.Undefined;

                var context = Cx(self);
                if (FontFor(context) is not { } font)
                    return JsValue.Undefined;

                var state = context.Bitmap.State;
                double? max = args.Length > 3 && !args[3].IsUndefined() ? Num(args, 3) : null;
                if (max is { } m && (double.IsNaN(m) || m <= 0))
                    return JsValue.Undefined;

                var align = state.TextAlign is "start" ? (state.Direction == "rtl" ? "right" : "left") : state.TextAlign is "end" ? (state.Direction == "rtl" ? "left" : "right") : state.TextAlign;
                context.Bitmap.DrawText(Js.AsText(Js.Arg(args, 0)), Num(args, 1), Num(args, 2), isStroke, max, font, align, state.TextBaseline,
                    isStroke ? state.StrokeStyle : state.FillStyle, isStroke ? context.Bitmap.MakePen(state) : null);
                return JsValue.Undefined;
            });
        }

        Js.StrictMethod(Engine, p, "measureText", 1, (self, args) =>
        {
            var context = Cx(self);
            var text = Js.AsText(Js.Arg(args, 0));
            var result = Js.NewObject(Engine);
            var font = FontFor(context);
            var width = font is null ? 0 : context.Bitmap.MeasureText(text, font);
            var (ascent, descent) = font is null ? (0.0, 0.0) : (font.Ascent, Math.Max(0, font.Height - font.Ascent));
            foreach (var (name, value) in new[]
            {
                ("width", width), ("actualBoundingBoxLeft", 0.0), ("actualBoundingBoxRight", width), ("fontBoundingBoxAscent", ascent), ("fontBoundingBoxDescent", descent),
                ("actualBoundingBoxAscent", ascent), ("actualBoundingBoxDescent", descent), ("emHeightAscent", ascent), ("emHeightDescent", descent),
                ("hangingBaseline", ascent * 0.8), ("alphabeticBaseline", 0.0), ("ideographicBaseline", -descent),
            })
                Js.Value(result, name, Js.Num(value));

            return result;
        });
    }

    // The decoded picture an image element, a pattern source or drawImage argument stands for, or null while it is not loaded.
    // A canvas as a picture of what it shows now (a pattern keeps the pixels it was made from).
    private Image? SnapshotOf(ScriptNode canvasNode)
    {
        if (!_contexts.TryGetValue(canvasNode, out var context))
        {
            var (w, h) = CanvasSize(canvasNode);
            return w <= 0 || h <= 0 ? null : canvasNode.OwnerDocument.DecodeImage(PngWriter.Encode((int)w, (int)h, new byte[(int)w * (int)h * 4]));
        }

        RequireNoOpenLayers(context.Bitmap);
        var pixels = context.Bitmap.Pixels();
        return pixels.Length == 0 ? null : canvasNode.OwnerDocument.DecodeImage(PngWriter.Encode((int)context.Bitmap.Width, (int)context.Bitmap.Height, pixels));
    }

    private Image? ImageOf(JsValue source)
    {
        if (source is BitmapObject { Image: { } bitmapImage })
            return bitmapImage;

        if (source is NodeObject { Node: { Kind: ScriptNodeKind.Element } element } && IsImage(element)
            && _images.TryGetValue(element, out var state) && state.Status == 2)
            return element.OwnerDocument.GetLoadedImage(element.GetAttribute("src") ?? string.Empty);

        return null;
    }
}
