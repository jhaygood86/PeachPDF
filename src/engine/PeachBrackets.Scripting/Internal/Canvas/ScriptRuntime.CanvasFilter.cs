using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using PeachBrackets.Scripting.Internal.Canvas;
using PeachBrackets.ScriptHosting;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Scripting.Internal;

/// <summary>A <c>CanvasFilter</c>: the filter primitives a script described, as the effects a layer applies.</summary>
internal sealed class CanvasFilterObject : ObjectInstance
{
    public CanvasFilterObject(Engine engine, ObjectInstance prototype, LayerEffect[] effects) : base(engine)
    {
        Prototype = prototype;
        Effects = effects;
    }

    public LayerEffect[] Effects { get; }
}

// <c>filter</c> of the 2D context and of beginLayer(): CSS filter lists, and the CanvasFilter objects.
internal sealed partial class ScriptRuntime
{
    private ObjectInstance CanvasFilterPrototype = null!;

    // Drawing that reads a canvas back (pixels, an image, a pattern) cannot be made while one of its layers is open: the layer's content is not on the canvas yet.
    private void RequireNoOpenLayers(CanvasBitmap bitmap)
    {
        if (bitmap.HasOpenLayers)
            throw DomError("InvalidStateError", "The canvas has an open layer.");
    }

    private void InstallCanvasFilter()
    {
        CanvasFilterPrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Object.PrototypeObject };
        Tag(CanvasFilterPrototype, "CanvasFilter");
        Define(Engine.Global, "CanvasFilter", Js.Constructor(Engine, "CanvasFilter", CanvasFilterPrototype, 0, (_, args, _) =>
            new CanvasFilterObject(Engine, CanvasFilterPrototype, FilterInitEffects(Js.Arg(args, 0), null))));
    }

    /// <summary>What a script assigned to <c>filter</c> stands for: a CanvasFilter is its primitives; anything else is read as text, and text that is not a filter list is not a filter.</summary>
    private bool TryFilterEffects(JsValue value, ScriptNode? element, out FilterStep[] effects, out string text)
    {
        if (value is CanvasFilterObject filter)
        {
            effects = filter.Effects.Length == 0 ? [] : [new FilterStep(filter.Effects)];
            text = "";
            return true;
        }

        text = TypeConverter.ToString(value);
        return TryFilterSteps(text, element, out effects);
    }

    // A CSS filter list, whose url() references are filters of the document: the functions between them are steps of their own, so the order written is the order run.
    private bool TryFilterSteps(string text, ScriptNode? element, out FilterStep[] steps)
    {
        steps = [];
        var trimmed = text.Trim();
        var result = new List<FilterStep>();
        var pending = new System.Text.StringBuilder();

        bool Flush()
        {
            if (pending.ToString().Trim().Length == 0)
                return true;

            if (!FilterList.TryParse(pending.ToString(), color => Document.TryParseColor(color, element, out var parsedColor) ? parsedColor : null, PaintColor.Black, 16, out var list))
                return false;

            if (list.Count > 0)
                result.Add(new FilterStep([.. list]));

            pending.Clear();
            return true;
        }

        if (trimmed.Length == 0)
            return false;

        var i = 0;
        while (i < trimmed.Length)
        {
            if (char.IsWhiteSpace(trimmed[i]))
            {
                pending.Append(trimmed[i++]);
                continue;
            }

            var start = i;
            while (i < trimmed.Length && trimmed[i] != '(' && !char.IsWhiteSpace(trimmed[i]))
                i++;

            if (i == trimmed.Length || trimmed[i] != '(')
            {
                // A word with no arguments (none, or nonsense): the list parser decides.
                pending.Append(trimmed, start, i - start);
                continue;
            }

            var depth = 0;
            var end = i;
            for (; end < trimmed.Length; end++)
            {
                if (trimmed[end] == '(')
                    depth++;
                else if (trimmed[end] == ')' && --depth == 0)
                    break;
            }

            if (end == trimmed.Length)
                return false;

            var function = trimmed[start..(end + 1)];
            i = end + 1;
            if (!function.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                pending.Append(function);
                continue;
            }

            // url(#id), with or without quotes: a filter of the document.
            var target = function[4..^1].Trim().Trim('"', '\'');
            if (!Flush() || !target.StartsWith('#') || Document.GetSvgFilter(target[1..]) is not { } svg)
                return false;

            result.Add(new FilterStep([], svg));
        }

        if (!Flush())
            return false;

        steps = [.. result];
        return true;
    }

    /// <summary>The effects of the filter of a layer's options: nothing and null are none; a CanvasFilter, a description or a list of them are primitives; anything else is read as CSS, and is none when it is not valid.</summary>
    private FilterStep[] LayerFilterEffects(JsValue value)
    {
        if (value.IsUndefined() || value.IsNull())
            return [];
        if (value is CanvasFilterObject filter)
            return filter.Effects.Length == 0 ? [] : [new FilterStep(filter.Effects)];
        if (value is ObjectInstance description && (description.IsArray() || description.HasProperty("name")))
        {
            var primitives = FilterInitEffects(value, null);
            return primitives.Length == 0 ? [] : [new FilterStep(primitives)];
        }

        return TryFilterEffects(value, null, out var effects, out _) ? effects : [];
    }

    // The init of a CanvasFilter: one primitive's description, or a list of them.
    private LayerEffect[] FilterInitEffects(JsValue init, ScriptNode? element)
    {
        if (init.IsUndefined())
            return [];

        if (init is not ObjectInstance description)
            throw TypeError("Failed to construct 'CanvasFilter': The filter is not an object or a list of objects.");

        var effects = new List<LayerEffect>();
        if (description.IsArray())
        {
            var count = (int)TypeConverter.ToNumber(description.Get("length"));
            for (var i = 0; i < count; i++)
            {
                if (description.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)) is not ObjectInstance item)
                    throw TypeError("Failed to construct 'CanvasFilter': A filter in the list is not an object.");

                effects.AddRange(PrimitiveEffects(item, element));
            }
        }
        else
        {
            effects.AddRange(PrimitiveEffects(description, element));
        }

        return [.. effects];
    }

    // One filter primitive. A name that is not one is left out; a member that is there and is not what it should be is a TypeError, a member that is not there is its default.
    private LayerEffect[] PrimitiveEffects(ObjectInstance description, ScriptNode? element)
    {
        var name = description.HasProperty("name") ? TypeConverter.ToString(description.Get("name")) : "";
        switch (name)
        {
            case "gaussianBlur":
            {
                if (!description.HasProperty("stdDeviation"))
                    throw TypeError("The gaussianBlur filter needs a stdDeviation.");

                var (x, y) = FilterPair(description, "stdDeviation", 0, 0);
                return x > 0 || y > 0 ? [new BlurEffect(Math.Max(0, x), Math.Max(0, y))] : [];
            }

            case "colorMatrix":
            {
                var type = description.HasProperty("type") ? TypeConverter.ToString(description.Get("type")) : "matrix";
                if (type is not ("matrix" or "saturate" or "hueRotate" or "luminanceToAlpha"))
                    throw TypeError("The colorMatrix type is not one of matrix, saturate, hueRotate or luminanceToAlpha.");

                if (type == "luminanceToAlpha")
                    return [new ColorMatrixEffect(ColorMatrix.LuminanceToAlpha())];

                if (type == "matrix")
                {
                    if (!description.HasProperty("values") || description.Get("values") is not ObjectInstance list || !list.IsArray())
                        throw TypeError("A colorMatrix of type matrix needs a list of 20 numbers.");

                    var count = (int)TypeConverter.ToNumber(list.Get("length"));
                    var numbers = new double[count];
                    for (var i = 0; i < count; i++)
                    {
                        numbers[i] = TypeConverter.ToNumber(list.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        if (!double.IsFinite(numbers[i]))
                            throw TypeError("A value of the color matrix is not finite.");
                    }

                    if (count != 20)
                        throw TypeError("A colorMatrix of type matrix needs 20 numbers.");

                    return [new ColorMatrixEffect(ColorMatrix.FromTable(numbers))];
                }

                if (!description.HasProperty("values"))
                    throw TypeError("The colorMatrix needs its values.");

                var amount = FilterNumber(description, "values", 0);
                return [new ColorMatrixEffect(type == "saturate" ? ColorMatrix.Saturate(amount) : ColorMatrix.HueRotate(amount * Math.PI / 180))];
            }

            case "dropShadow":
            {
                var dx = FilterNumber(description, "dx", 2);
                var dy = FilterNumber(description, "dy", 2);
                var (sx, sy) = FilterPair(description, "stdDeviation", 2, 2);
                var opacity = Math.Clamp(FilterNumber(description, "floodOpacity", 1), 0, 1);
                var color = PaintColor.Black;
                if (description.HasProperty("floodColor"))
                {
                    if (description.Get("floodColor") is not JsString text || !Document.TryParseColor(text.ToString(), element, out color))
                        throw TypeError("The floodColor is not a color.");
                }

                color = PaintColor.FromArgb((int)Math.Round(color.A * opacity), color.R, color.G, color.B);
                return [new DropShadowEffect(dx, dy, Math.Max(0, sx), Math.Max(0, sy), color)];
            }

            case "componentTransfer":
                return [ComponentTransfer(description)];

            case "convolveMatrix":
                return ConvolveMatrix(description);

            case "turbulence":
                return [Turbulence(description)];

            default:
                return [];
        }
    }

    // A number member: its default when it is not there; a TypeError when it is there and is not a finite number once converted (undefined included).
    private double FilterNumber(ObjectInstance description, string key, double fallback)
    {
        if (!description.HasProperty(key))
            return fallback;

        var value = description.Get(key);
        if (value.IsUndefined())
            throw TypeError($"The {key} of the filter is undefined.");

        var number = TypeConverter.ToNumber(value);
        if (!double.IsFinite(number))
            throw TypeError($"The {key} of the filter is not a finite number.");

        return number;
    }

    // A number or a pair of numbers (x and y).
    private (double X, double Y) FilterPair(ObjectInstance description, string key, double fallbackX, double fallbackY)
    {
        if (!description.HasProperty(key))
            return (fallbackX, fallbackY);

        var value = description.Get(key);
        if (value.IsUndefined())
            throw TypeError($"The {key} of the filter is undefined.");

        if (value is ObjectInstance list && list.IsArray() && (int)TypeConverter.ToNumber(list.Get("length")) is var length && length != 0 && length != 1)
        {
            if (length != 2)
                throw TypeError($"The {key} of the filter has more than two numbers.");

            var x = TypeConverter.ToNumber(list.Get("0"));
            var y = TypeConverter.ToNumber(list.Get("1"));
            if (!double.IsFinite(x) || !double.IsFinite(y))
                throw TypeError($"The {key} of the filter is not finite.");

            return (x, y);
        }

        var number = TypeConverter.ToNumber(value);
        if (!double.IsFinite(number))
            throw TypeError($"The {key} of the filter is not a finite number.");

        return (number, number);
    }

    // feComponentTransfer: a function for each channel that has one.
    private LayerEffect ComponentTransfer(ObjectInstance description)
    {
        byte[]? Channel(string key)
        {
            if (!description.HasProperty(key))
                return null;

            if (description.Get(key) is not ObjectInstance function)
                throw TypeError($"The {key} of the componentTransfer filter is not an object.");

            var type = function.HasProperty("type") ? TypeConverter.ToString(function.Get("type")) : "identity";
            var kind = type switch
            {
                "identity" => TransferFunctionKind.Identity,
                "table" => TransferFunctionKind.Table,
                "discrete" => TransferFunctionKind.Discrete,
                "linear" => TransferFunctionKind.Linear,
                "gamma" => TransferFunctionKind.Gamma,
                _ => throw TypeError($"The {key} type is not one of identity, table, discrete, linear or gamma."),
            };

            var table = new List<double>();
            if (function.HasProperty("tableValues"))
            {
                // A sequence: an array. Anything else, a number or an object that only yields one when asked, is not.
                if (function.Get("tableValues") is not ObjectInstance list || !list.IsArray())
                    throw TypeError("The tableValues are not a list of numbers.");

                var count = (int)TypeConverter.ToNumber(list.Get("length"));
                for (var i = 0; i < count; i++)
                {
                    var number = TypeConverter.ToNumber(list.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    if (!double.IsFinite(number))
                        throw TypeError("A value of the tableValues is not finite.");

                    table.Add(number);
                }
            }

            return ComponentTransferEffect.BuildTable(kind, table, FilterNumber(function, "slope", 1), FilterNumber(function, "intercept", 0),
                FilterNumber(function, "amplitude", 1), FilterNumber(function, "exponent", 1), FilterNumber(function, "offset", 0));
        }

        return new ComponentTransferEffect(Channel("funcR"), Channel("funcG"), Channel("funcB"), Channel("funcA"));
    }

    // A convolution kernel is a list of rows of numbers, all as long as each other; one with no columns convolves nothing.
    private LayerEffect[] ConvolveMatrix(ObjectInstance description)
    {
        if (!description.HasProperty("kernelMatrix") || description.Get("kernelMatrix") is not ObjectInstance rows || !rows.IsArray())
            throw TypeError("The convolveMatrix filter needs a kernelMatrix.");

        var rowCount = (int)TypeConverter.ToNumber(rows.Get("length"));
        if (rowCount == 0)
            throw TypeError("The kernelMatrix has no rows.");

        var kernel = new List<double>();
        var width = -1;
        for (var r = 0; r < rowCount; r++)
        {
            if (rows.Get(r.ToString(System.Globalization.CultureInfo.InvariantCulture)) is not ObjectInstance row || !row.IsArray())
                throw TypeError("A row of the kernelMatrix is not a list.");

            var count = (int)TypeConverter.ToNumber(row.Get("length"));
            if (width >= 0 && count != width)
                throw TypeError("The rows of the kernelMatrix are not all as long.");

            width = count;
            for (var c = 0; c < count; c++)
            {
                var number = TypeConverter.ToNumber(row.Get(c.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (!double.IsFinite(number))
                    throw TypeError("A value of the kernelMatrix is not finite.");

                kernel.Add(number);
            }
        }

        if (width == 0 && rowCount > 1)
            throw TypeError("The rows of the kernelMatrix are empty.");

        var sum = kernel.Sum();
        var divisor = FilterNumber(description, "divisor", sum == 0 ? 1 : sum);
        var bias = FilterNumber(description, "bias", 0);
        var targetX = (int)FilterNumber(description, "targetX", Math.Floor(width / 2.0));
        var targetY = (int)FilterNumber(description, "targetY", Math.Floor(rowCount / 2.0));
        var edge = description.HasProperty("edgeMode") ? TypeConverter.ToString(description.Get("edgeMode")) : "duplicate";
        var mode = edge switch
        {
            "duplicate" => FilterEdgeMode.Duplicate,
            "wrap" => FilterEdgeMode.Wrap,
            "none" => FilterEdgeMode.None,
            _ => throw TypeError("The edgeMode is not one of duplicate, wrap or none."),
        };
        var preserveAlpha = description.HasProperty("preserveAlpha") && TypeConverter.ToBoolean(description.Get("preserveAlpha"));

        // Nothing to convolve with, or a divisor or a target that cannot work: the filter does nothing.
        if (width == 0 || divisor == 0 || targetX < 0 || targetX >= width || targetY < 0 || targetY >= rowCount)
            return [];

        return [new ConvolveMatrixEffect(width, rowCount, kernel, divisor, bias, targetX, targetY, mode, preserveAlpha)];
    }

    private LayerEffect Turbulence(ObjectInstance description)
    {
        var (fx, fy) = FilterPair(description, "baseFrequency", 0, 0);
        if (fx < 0 || fy < 0)
            throw TypeError("The baseFrequency is negative.");

        var octaves = FilterNumber(description, "numOctaves", 1);
        if (octaves < 0)
            throw TypeError("The numOctaves is negative.");

        var seed = FilterNumber(description, "seed", 0);

        var stitch = false;
        if (description.HasProperty("stitchTiles"))
        {
            if (description.Get("stitchTiles") is not JsString stitchText || stitchText.ToString() is not ("stitch" or "noStitch"))
                throw TypeError("The stitchTiles is not stitch or noStitch.");

            stitch = stitchText.ToString() == "stitch";
        }

        var fractal = false;
        if (description.HasProperty("type"))
        {
            if (description.Get("type") is not JsString typeText || typeText.ToString() is not ("fractalNoise" or "turbulence"))
                throw TypeError("The turbulence type is not fractalNoise or turbulence.");

            fractal = typeText.ToString() == "fractalNoise";
        }

        return new TurbulenceEffect(fx, fy, (int)Math.Min(octaves, 24), (long)Math.Truncate(Math.Clamp(seed, -2147483648.0, 2147483647.0)), fractal, stitch);
    }
}
