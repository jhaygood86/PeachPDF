using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using PeachDrawing.Core;
using System;

namespace PeachBrackets.Scripting.Internal;

// A color given as an object rather than a string: a dictionary {r, g, b, a} of numbers from 0 to 1, a CSSRGB, or a CSSHSL (see ColorScripts). Read when the style is set,
// so changing the object afterwards does not change the style.
internal sealed partial class ScriptRuntime
{
    private static bool TryObjectColor(JsValue value, out PaintColor color)
    {
        color = default;
        if (value is not ObjectInstance obj)
            return false;

        // The alpha is optional (a or alpha, one if both), but one that is given has to be a number.
        var alpha = 1.0;
        var alphaName = !HasNo(obj, "alpha") ? "alpha" : !HasNo(obj, "a") ? "a" : null;
        if (alphaName is not null)
        {
            if (Number(obj, alphaName) is not { } given || double.IsNaN(given))
                return false;

            alpha = Math.Clamp(given, 0, 1);
        }

        double r, g, b;
        if (Number(obj, "r") is { } red && Number(obj, "g") is { } green && Number(obj, "b") is { } blue)
        {
            (r, g, b) = (red, green, blue);
        }
        else if (Degrees(obj.Get("h")) is { } hue && Number(obj, "s") is { } saturation && Number(obj, "l") is { } lightness)
        {
            (r, g, b) = HslToRgb(hue, Math.Clamp(saturation, 0, 1), Math.Clamp(lightness, 0, 1));
        }
        else
        {
            return false;
        }

        if (double.IsNaN(r) || double.IsNaN(g) || double.IsNaN(b))
            return false;

        color = PaintColor.FromArgb(Byte(alpha), Byte(r), Byte(g), Byte(b));
        return true;
    }

    private static int Byte(double unit) => (int)Math.Round(Math.Clamp(unit, 0, 1) * 255, MidpointRounding.AwayFromZero);

    // A property that is absent or undefined does not make the object not a color (a is optional); one that is present but not a number does.
    private static bool HasNo(ObjectInstance obj, string name) => obj.Get(name).IsUndefined();

    // A number, or a CSSUnitValue of a number or a percentage (as a fraction): the CSSNumberish a color component is. Null when it is neither.
    private static double? Number(ObjectInstance obj, string name)
    {
        var value = obj.Get(name);
        if (value.IsNumber())
            return value.AsNumber();

        if (value is ObjectInstance unitValue && unitValue.Get("unit") is { } unit && unit.IsString() && unitValue.Get("value").IsNumber())
        {
            var number = unitValue.Get("value").AsNumber();
            return unit.AsString() switch
            {
                "percent" => number / 100,
                "number" => number,
                _ => null,
            };
        }

        return null;
    }

    // A hue as degrees: a number is degrees already, a CSSUnitValue in deg, rad, grad or turn is converted.
    private static double? Degrees(JsValue value)
    {
        if (value.IsNumber())
            return value.AsNumber();

        if (value is ObjectInstance unitValue && unitValue.Get("unit") is { } unit && unit.IsString() && unitValue.Get("value").IsNumber())
        {
            var number = unitValue.Get("value").AsNumber();
            return unit.AsString() switch
            {
                "deg" or "number" => number,
                "rad" => number * 180 / Math.PI,
                "grad" => number * 0.9,
                "turn" => number * 360,
                _ => null,
            };
        }

        return null;
    }

    private static (double R, double G, double B) HslToRgb(double hueDegrees, double saturation, double lightness)
    {
        var hue = hueDegrees % 360;
        if (hue < 0)
            hue += 360;

        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - chroma / 2;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };

        return (r + m, g + m, b + m);
    }
}
