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
    private bool TryFilterEffects(JsValue value, ScriptNode? element, out LayerEffect[] effects, out string text)
    {
        if (value is CanvasFilterObject filter)
        {
            effects = filter.Effects;
            text = "";
            return true;
        }

        text = TypeConverter.ToString(value);
        var parsed = FilterList.TryParse(text, color => Document.TryParseColor(color, element, out var parsedColor) ? parsedColor : null, PaintColor.Black, 16, out var list);
        effects = [.. list];
        return parsed;
    }

    /// <summary>The effects of the filter of a layer's options: nothing and null are none; a CanvasFilter, a description or a list of them are primitives; anything else is read as CSS, and is none when it is not valid.</summary>
    private LayerEffect[] LayerFilterEffects(JsValue value)
    {
        if (value.IsUndefined() || value.IsNull())
            return [];
        if (value is CanvasFilterObject filter)
            return filter.Effects;
        if (value is ObjectInstance description && (description.IsArray() || description.HasProperty("name")))
            return FilterInitEffects(value, null);

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

            case "convolveMatrix":
                ValidateKernel(description);
                return [];

            case "turbulence":
                ValidateTurbulence(description);
                return [];

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

    // A convolution kernel is a list of rows of numbers, all as long as each other.
    private void ValidateKernel(ObjectInstance description)
    {
        if (!description.HasProperty("kernelMatrix") || description.Get("kernelMatrix") is not ObjectInstance rows || !rows.IsArray())
            throw TypeError("The convolveMatrix filter needs a kernelMatrix.");

        var rowCount = (int)TypeConverter.ToNumber(rows.Get("length"));
        if (rowCount == 0)
            throw TypeError("The kernelMatrix has no rows.");

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
                if (!double.IsFinite(TypeConverter.ToNumber(row.Get(c.ToString(System.Globalization.CultureInfo.InvariantCulture)))))
                    throw TypeError("A value of the kernelMatrix is not finite.");
            }
        }

        if (width == 0 && rowCount > 1)
            throw TypeError("The rows of the kernelMatrix are empty.");

        foreach (var key in new[] { "divisor", "bias", "targetX", "targetY" })
            FilterNumber(description, key, 0);
    }

    private void ValidateTurbulence(ObjectInstance description)
    {
        var (fx, fy) = FilterPair(description, "baseFrequency", 0, 0);
        if (fx < 0 || fy < 0)
            throw TypeError("The baseFrequency is negative.");

        if (FilterNumber(description, "numOctaves", 1) < 0)
            throw TypeError("The numOctaves is negative.");

        FilterNumber(description, "seed", 0);

        if (description.HasProperty("stitchTiles") && (description.Get("stitchTiles") is not JsString stitch || stitch.ToString() is not ("stitch" or "noStitch")))
            throw TypeError("The stitchTiles is not stitch or noStitch.");

        if (description.HasProperty("type") && (description.Get("type") is not JsString type || type.ToString() is not ("fractalNoise" or "turbulence")))
            throw TypeError("The turbulence type is not fractalNoise or turbulence.");
    }
}
