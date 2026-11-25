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
}

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

    private static double Num(JsValue[] args, int index) => TypeConverter.ToNumber(Js.Arg(args, index));

    private static bool Finite(JsValue[] args, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!double.IsFinite(TypeConverter.ToNumber(Js.Arg(args, i))))
                return false;
        }

        return true;
    }

    private ContextObject Cx(JsValue self) => self as ContextObject ?? throw TypeError("Illegal invocation");

    private static (double Width, double Height) CanvasSize(ScriptNode element)
    {
        double Read(string name, double fallback) =>
            element.GetAttribute(name) is { } text && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;

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

        InstallCanvasElement();
        InstallContextState();
        InstallPathMethods(ContextPrototype, self => (Cx(self).Bitmap.Path, Cx(self).Bitmap.State.Transform));
        InstallPathMethods(Path2DPrototype, self => (((Path2DObject)self).Path, Matrix3x2.Identity));
        InstallContextDrawing();

        Define(Engine.Global, "CanvasRenderingContext2D", Js.Constructor(Engine, "CanvasRenderingContext2D", ContextPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        Define(Engine.Global, "CanvasGradient", Js.Constructor(Engine, "CanvasGradient", GradientPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        Define(Engine.Global, "CanvasPattern", Js.Constructor(Engine, "CanvasPattern", PatternPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        Define(Engine.Global, "Path2D", Js.Constructor(Engine, "Path2D", Path2DPrototype, 0, (_, args, _) =>
        {
            var path = new Path2DObject(Engine, Path2DPrototype);
            if (Js.Arg(args, 0) is Path2DObject other)
                path.Path = other.Path.Clone();
            return path;
        }));
        Js.Method(Engine, Path2DPrototype, "addPath", 1, (self, args) =>
        {
            if (Js.Arg(args, 0) is Path2DObject other)
            {
                var matrix = args.Length > 1 && args[1] is ObjectInstance init ? MatrixFrom(init) : Matrix3x2.Identity;
                ((Path2DObject)self).Path.Append(other.Path.Transformed(matrix));
            }

            return JsValue.Undefined;
        });
        Js.Method(Engine, GradientPrototype, "addColorStop", 2, (self, args) =>
        {
            var gradient = ((GradientObject)self).Model;
            var offset = Num(args, 0);
            if (!(offset >= 0 && offset <= 1))
                throw DomError("IndexSizeError", "The offset must be between 0 and 1.");
            if (!Document.TryParseColor(Js.AsText(Js.Arg(args, 1)), out var color))
                throw DomError("SyntaxError", "The color cannot be parsed.");

            gradient.Stops.Add((offset, color));
            return JsValue.Undefined;
        });
    }

    private static Matrix3x2 MatrixFrom(ObjectInstance init)
    {
        double Get(string name, double fallback) => init.HasProperty(name) && !init.Get(name).IsUndefined() ? TypeConverter.ToNumber(init.Get(name)) : fallback;
        return new Matrix3x2((float)Get("a", 1), (float)Get("b", 0), (float)Get("c", 0), (float)Get("d", 1), (float)Get("e", 0), (float)Get("f", 0));
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
                    var number = TypeConverter.ToNumber(value);
                    var whole = double.IsFinite(number) && number >= 0 && number <= int.MaxValue ? (int)Math.Truncate(number) : defaultValue;
                    Sn(self).SetAttribute(attribute, whole.ToString(CultureInfo.InvariantCulture));
                    ResetCanvas(Sn(self));
                });
        }

        Js.Method(Engine, element, "getContext", 1, (self, args) =>
        {
            if (Js.AsText(Js.Arg(args, 0)) != "2d")
                return JsValue.Null;

            var node = Sn(self);
            if (_contexts.TryGetValue(node, out var existing))
                return existing;

            var (width, height) = CanvasSize(node);
            var context = new ContextObject(Engine, ContextPrototype, node, new CanvasBitmap(node.OwnerDocument, width, height));
            _contexts[node] = context;
            node.OwnerDocument.SetCanvasPainting(node, context.Bitmap.Painting);
            return context;
        });
        Js.Accessor(Engine, ContextPrototype, "canvas", self => Wrap(Cx(self).Canvas));
    }

    // Setting width or height gives the canvas a new, empty bitmap.
    private void ResetCanvas(ScriptNode node)
    {
        if (!_contexts.TryGetValue(node, out var context))
            return;

        var (width, height) = CanvasSize(node);
        context.Bitmap = new CanvasBitmap(node.OwnerDocument, width, height);
        node.OwnerDocument.SetCanvasPainting(node, context.Bitmap.Painting);
    }

    // ---- state: transform, styles, line and text settings

    private PaintSource StyleFrom(JsValue value, PaintSource current)
    {
        switch (value)
        {
            case GradientObject gradient:
                return new PaintSource.GradientSource(gradient.Model);
            case PatternObject pattern:
                return new PaintSource.PatternSource(pattern.Model);
        }

        return Document.TryParseColor(Js.AsText(value), out var color) ? new PaintSource.ColorSource(color, CanvasBitmap.ColorText(color)) : current;
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

        Js.Method(Engine, p, "save", 0, (self, _) => { Cx(self).Bitmap.Save(); return JsValue.Undefined; });
        Js.Method(Engine, p, "restore", 0, (self, _) => { Cx(self).Bitmap.Restore(); return JsValue.Undefined; });
        Js.Method(Engine, p, "reset", 0, (self, _) => { Cx(self).Bitmap.Reset(); return JsValue.Undefined; });

        Js.Method(Engine, p, "scale", 2, (self, args) =>
        {
            if (Finite(args, 2)) Cx(self).Bitmap.Concat(Matrix3x2.CreateScale((float)Num(args, 0), (float)Num(args, 1)));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "rotate", 1, (self, args) =>
        {
            if (Finite(args, 1)) Cx(self).Bitmap.Concat(Matrix3x2.CreateRotation((float)Num(args, 0)));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "translate", 2, (self, args) =>
        {
            if (Finite(args, 2)) Cx(self).Bitmap.Concat(Matrix3x2.CreateTranslation((float)Num(args, 0), (float)Num(args, 1)));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "transform", 6, (self, args) =>
        {
            if (Finite(args, 6))
                Cx(self).Bitmap.Concat(new Matrix3x2((float)Num(args, 0), (float)Num(args, 1), (float)Num(args, 2), (float)Num(args, 3), (float)Num(args, 4), (float)Num(args, 5)));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "setTransform", 6, (self, args) =>
        {
            if (args.Length > 0 && args[0] is ObjectInstance init)
                S(self).Transform = MatrixFrom(init);
            else if (Finite(args, 6))
                S(self).Transform = new Matrix3x2((float)Num(args, 0), (float)Num(args, 1), (float)Num(args, 2), (float)Num(args, 3), (float)Num(args, 4), (float)Num(args, 5));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "resetTransform", 0, (self, _) => { S(self).Transform = Matrix3x2.Identity; return JsValue.Undefined; });
        Js.Method(Engine, p, "getTransform", 0, (self, _) =>
        {
            var m = S(self).Transform;
            var result = Js.NewObject(Engine);
            foreach (var (name, value) in new[] { ("a", m.M11), ("b", m.M12), ("c", m.M21), ("d", m.M22), ("e", m.M31), ("f", m.M32) })
                Js.Value(result, name, Js.Num(value));
            foreach (var (name, value) in new[] { ("m11", m.M11), ("m12", m.M12), ("m21", m.M21), ("m22", m.M22), ("m41", m.M31), ("m42", m.M32) })
                Js.Value(result, name, Js.Num(value));
            return result;
        });

        Js.Accessor(Engine, p, "fillStyle", self => StyleValue(S(self).FillStyle), (self, value) => S(self).FillStyle = StyleFrom(value, S(self).FillStyle));
        Js.Accessor(Engine, p, "strokeStyle", self => StyleValue(S(self).StrokeStyle), (self, value) => S(self).StrokeStyle = StyleFrom(value, S(self).StrokeStyle));
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
        Js.Method(Engine, p, "setLineDash", 1, (self, args) =>
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
        Js.Method(Engine, p, "getLineDash", 0, (self, _) => Js.Array(Engine, [.. S(self).LineDash.Select(Js.Num)]));
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
            if (Document.TryParseColor(Js.AsText(value), out var color))
            {
                S(self).ShadowColor = color;
                S(self).ShadowColorText = CanvasBitmap.ColorText(color);
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
        Js.Accessor(Engine, p, "filter", self => Js.Str(S(self).Filter), (self, value) => S(self).Filter = Js.AsText(value));
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
        Js.Method(Engine, p, "closePath", 0, (self, _) => { target(self).Path.Close(); return JsValue.Undefined; });
        Js.Method(Engine, p, "moveTo", 2, (self, args) =>
        {
            if (!Finite(args, 2)) return JsValue.Undefined;
            var (path, m) = target(self);
            var point = PathData.T(m, Num(args, 0), Num(args, 1));
            path.MoveTo(point.X, point.Y);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "lineTo", 2, (self, args) =>
        {
            if (!Finite(args, 2)) return JsValue.Undefined;
            var (path, m) = target(self);
            var point = PathData.T(m, Num(args, 0), Num(args, 1));
            path.LineTo(point.X, point.Y);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "quadraticCurveTo", 4, (self, args) =>
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
        Js.Method(Engine, p, "bezierCurveTo", 6, (self, args) =>
        {
            if (!Finite(args, 6)) return JsValue.Undefined;
            var (path, m) = target(self);
            var (a, b, c) = (PathData.T(m, Num(args, 0), Num(args, 1)), PathData.T(m, Num(args, 2), Num(args, 3)), PathData.T(m, Num(args, 4), Num(args, 5)));
            if (!path.HasPoint) path.MoveTo(a.X, a.Y);
            path.CurveTo(a.X, a.Y, b.X, b.Y, c.X, c.Y);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "arc", 5, (self, args) =>
        {
            if (!Finite(args, 5)) return JsValue.Undefined;
            if (Num(args, 2) < 0) throw DomError("IndexSizeError", "The radius is negative.");
            var (path, m) = target(self);
            path.AddArc(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 2), 0, Num(args, 3), Num(args, 4), TypeConverter.ToBoolean(Js.Arg(args, 5)), m);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "ellipse", 7, (self, args) =>
        {
            if (!Finite(args, 7)) return JsValue.Undefined;
            if (Num(args, 2) < 0 || Num(args, 3) < 0) throw DomError("IndexSizeError", "A radius is negative.");
            var (path, m) = target(self);
            path.AddArc(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), Num(args, 4), Num(args, 5), Num(args, 6), TypeConverter.ToBoolean(Js.Arg(args, 7)), m);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "arcTo", 5, (self, args) =>
        {
            if (!Finite(args, 5)) return JsValue.Undefined;
            if (Num(args, 4) < 0) throw DomError("IndexSizeError", "The radius is negative.");
            var (path, m) = target(self);
            ArcTo(path, m, Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), Num(args, 4));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "rect", 4, (self, args) =>
        {
            if (!Finite(args, 4)) return JsValue.Undefined;
            var (path, m) = target(self);
            path.AddRect(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), m);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "roundRect", 4, (self, args) =>
        {
            if (!Finite(args, 4)) return JsValue.Undefined;
            var (path, m) = target(self);
            var radii = RoundRectRadii(Js.Arg(args, 4));
            RoundRect(path, m, Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), radii);
            return JsValue.Undefined;
        });
    }

    // The four corner radii (top-left, top-right, bottom-right, bottom-left) of roundRect's argument: a number, or a list of one to four.
    private static double[] RoundRectRadii(JsValue value)
    {
        if (value.IsUndefined()) return [0, 0, 0, 0];
        if (value is not ObjectInstance { } list || !list.IsArray())
        {
            var radius = Math.Max(0, TypeConverter.ToNumber(value));
            return [radius, radius, radius, radius];
        }

        var count = (int)TypeConverter.ToNumber(list.Get("length"));
        var v = Enumerable.Range(0, count).Select(i => Math.Max(0, TypeConverter.ToNumber(list.Get(i.ToString(CultureInfo.InvariantCulture))))).ToArray();
        return v.Length switch
        {
            1 => [v[0], v[0], v[0], v[0]],
            2 => [v[0], v[1], v[0], v[1]],
            3 => [v[0], v[1], v[2], v[1]],
            4 => v,
            _ => [0, 0, 0, 0],
        };
    }

    private static void RoundRect(PathData path, Matrix3x2 m, double x, double y, double w, double h, double[] r)
    {
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        var scale = Math.Min(1, Math.Min(w / Math.Max(1e-9, r[0] + r[1]), Math.Min(w / Math.Max(1e-9, r[3] + r[2]), Math.Min(h / Math.Max(1e-9, r[0] + r[3]), h / Math.Max(1e-9, r[1] + r[2])))));
        r = [.. r.Select(v => v * scale)];
        var sub = new PathData();
        Vector2 P(double px, double py) => PathData.T(m, px, py);
        var start = P(x + r[0], y);
        path.MoveTo(start.X, start.Y);
        var p1 = P(x + w - r[1], y); path.LineTo(p1.X, p1.Y);
        path.AddArc(x + w - r[1], y + r[1], r[1], r[1], 0, -Math.PI / 2, 0, false, m);
        var p2 = P(x + w, y + h - r[2]); path.LineTo(p2.X, p2.Y);
        path.AddArc(x + w - r[2], y + h - r[2], r[2], r[2], 0, 0, Math.PI / 2, false, m);
        var p3 = P(x + r[3], y + h); path.LineTo(p3.X, p3.Y);
        path.AddArc(x + r[3], y + h - r[3], r[3], r[3], 0, Math.PI / 2, Math.PI, false, m);
        var p4 = P(x, y + r[0]); path.LineTo(p4.X, p4.Y);
        path.AddArc(x + r[0], y + r[0], r[0], r[0], 0, Math.PI, Math.PI * 1.5, false, m);
        path.Close();
        _ = sub;
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

        Js.Method(Engine, p, "beginPath", 0, (self, _) => { B(self).Path = new PathData(); return JsValue.Undefined; });

        (PathData Path, FillMode Rule) PathArgs(JsValue self, JsValue[] args)
        {
            var path = B(self).Path;
            var index = 0;
            if (Js.Arg(args, 0) is Path2DObject own)
            {
                path = own.Path.Transformed(B(self).State.Transform);
                index = 1;
            }

            var rule = Js.Arg(args, index) is { } text && text.IsString() && text.AsString() == "evenodd" ? FillMode.EvenOdd : FillMode.Nonzero;
            return (path, rule);
        }

        Js.Method(Engine, p, "fill", 0, (self, args) =>
        {
            var (path, rule) = PathArgs(self, args);
            B(self).FillPath(path, rule);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "stroke", 0, (self, args) =>
        {
            var path = Js.Arg(args, 0) is Path2DObject own ? own.Path.Transformed(B(self).State.Transform) : B(self).Path;
            B(self).StrokePath(path);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "clip", 0, (self, args) =>
        {
            var (path, rule) = PathArgs(self, args);
            B(self).ClipPath(path, rule);
            return JsValue.Undefined;
        });

        Js.Method(Engine, p, "fillRect", 4, (self, args) =>
        {
            if (Finite(args, 4)) B(self).FillRect(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "strokeRect", 4, (self, args) =>
        {
            if (Finite(args, 4)) B(self).StrokeRect(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "clearRect", 4, (self, args) =>
        {
            if (Finite(args, 4)) B(self).ClearRect(Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3));
            return JsValue.Undefined;
        });

        Js.Method(Engine, p, "createLinearGradient", 4, (_, args) =>
        {
            if (!Finite(args, 4)) throw TypeError("The gradient coordinates are not finite.");
            return NewGradient(new CanvasGradient("linear", [Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3)]));
        });
        Js.Method(Engine, p, "createRadialGradient", 6, (_, args) =>
        {
            if (!Finite(args, 6)) throw TypeError("The gradient coordinates are not finite.");
            if (Num(args, 2) < 0 || Num(args, 5) < 0) throw DomError("IndexSizeError", "A radius is negative.");
            return NewGradient(new CanvasGradient("radial", [Num(args, 0), Num(args, 1), Num(args, 2), Num(args, 3), Num(args, 4), Num(args, 5)]));
        });
        Js.Method(Engine, p, "createConicGradient", 3, (_, args) =>
        {
            if (!Finite(args, 3)) throw TypeError("The gradient coordinates are not finite.");
            return NewGradient(new CanvasGradient("conic", [Num(args, 0), Num(args, 1), Num(args, 2)]));
        });
        Js.Method(Engine, p, "createPattern", 2, (self, args) =>
        {
            var image = ImageOf(Js.Arg(args, 0));
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

        Js.Method(Engine, p, "drawImage", 3, (self, args) =>
        {
            var source = Js.Arg(args, 0);
            var numbers = args.Skip(1).Select(TypeConverter.ToNumber).ToArray();
            if (numbers.Length is not (2 or 4 or 8) || numbers.Any(n => !double.IsFinite(n)))
            {
                if (numbers.Length is not (2 or 4 or 8)) throw TypeError("drawImage takes 3, 5 or 9 arguments.");
                return JsValue.Undefined;
            }

            if (source is NodeObject { Node: { Kind: ScriptNodeKind.Element } element } && string.Equals(element.TagName, "canvas", StringComparison.OrdinalIgnoreCase)
                && _contexts.TryGetValue(element, out var other))
            {
                var (dx, dy, dw, dh) = numbers.Length switch
                {
                    2 => (numbers[0], numbers[1], other.Bitmap.Width, other.Bitmap.Height),
                    4 => (numbers[0], numbers[1], numbers[2], numbers[3]),
                    _ => (numbers[4], numbers[5], numbers[6], numbers[7]),
                };
                B(self).DrawCanvas(other.Bitmap, new Rect(dx, dy, dw, dh));
                return JsValue.Undefined;
            }

            if (ImageOf(source) is not { } image)
            {
                if (source is NodeObject { Node: { Kind: ScriptNodeKind.Element } img } && IsImage(img))
                    throw DomError("InvalidStateError", "The image is not loaded.");

                throw TypeError("The image argument is not a drawable object.");
            }

            var (sx, sy, sw, sh) = numbers.Length == 8 ? (numbers[0], numbers[1], numbers[2], numbers[3]) : (0, 0, image.Width, image.Height);
            var d = numbers.Length switch
            {
                2 => new Rect(numbers[0], numbers[1], image.Width, image.Height),
                4 => new Rect(numbers[0], numbers[1], numbers[2], numbers[3]),
                _ => new Rect(numbers[4], numbers[5], numbers[6], numbers[7]),
            };
            if (sw != 0 && sh != 0 && d.Width != 0 && d.Height != 0)
                B(self).DrawImage(image, new Rect(sx, sy, sw, sh), d);

            return JsValue.Undefined;
        });

        foreach (var stroke in new[] { false, true })
        {
            var isStroke = stroke;
            Js.Method(Engine, p, isStroke ? "strokeText" : "fillText", 3, (self, args) =>
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

        Js.Method(Engine, p, "measureText", 1, (self, args) =>
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
    private Image? ImageOf(JsValue source)
    {
        if (source is NodeObject { Node: { Kind: ScriptNodeKind.Element } element } && IsImage(element)
            && _images.TryGetValue(element, out var state) && state.Status == 2)
            return element.OwnerDocument.GetLoadedImage(element.GetAttribute("src") ?? string.Empty);

        return null;
    }
}
