#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PeachBrackets.Css
{
    /// <summary>
    /// A color as it was written, in the space it was written in, not reduced to 8-bit sRGB: <c>color(display-p3 0.96 0.76 0.79)</c>, <c>lab(29 39 20)</c>, the result of a
    /// <c>color-mix()</c> or of relative color syntax. <see cref="ToCssString"/> serializes it as CSS Color 4 section 15 says, which is what a canvas reads back from
    /// <c>fillStyle</c> for such a color. A color in the legacy sRGB forms (a name, hex, <c>rgb()</c>, <c>hsl()</c>, <c>hwb()</c>) is not one: it serializes as <c>#rrggbb</c> or
    /// <c>rgba()</c> from its 8-bit value.
    /// </summary>
    /// <param name="Space">the space the coordinates are in</param>
    /// <param name="Values">the three coordinates (a hue in degrees); a missing one (<c>none</c>) holds 0 and is flagged in <paramref name="Missing"/></param>
    /// <param name="Missing">bit 0 to 2 for a missing coordinate, <see cref="ColorSpaceMath.AlphaMissing"/> for a missing alpha</param>
    /// <param name="Alpha">the alpha, 0 to 1</param>
    internal readonly record struct AuthoredColor(ColorSpaceMath.Space Space, double[] Values, int Missing, double Alpha)
    {
        /// <summary>The serialization: <c>color(srgb 0.5 0 0.5)</c>, <c>lab(29 39 20 / 0.5)</c>; a missing component is <c>none</c> and an alpha of 1 is left out.</summary>
        public string ToCssString()
        {
            var (name, isFunction) = Space switch
            {
                ColorSpaceMath.Space.Lab => ("lab", false),
                ColorSpaceMath.Space.Lch => ("lch", false),
                ColorSpaceMath.Space.Oklab => ("oklab", false),
                ColorSpaceMath.Space.Oklch => ("oklch", false),
                ColorSpaceMath.Space.Srgb => ("srgb", true),
                ColorSpaceMath.Space.SrgbLinear => ("srgb-linear", true),
                ColorSpaceMath.Space.DisplayP3 => ("display-p3", true),
                ColorSpaceMath.Space.A98Rgb => ("a98-rgb", true),
                ColorSpaceMath.Space.ProphotoRgb => ("prophoto-rgb", true),
                ColorSpaceMath.Space.Rec2020 => ("rec2020", true),
                ColorSpaceMath.Space.XyzD50 => ("xyz-d50", true),
                _ => ("xyz-d65", true),
            };

            var text = new StringBuilder();
            text.Append(isFunction ? "color(" + name : name + "(");
            for (var i = 0; i < 3; i++)
            {
                if (i > 0 || isFunction) text.Append(' ');
                text.Append((Missing & (1 << i)) != 0 ? "none" : Number(Values[i]));
            }

            if ((Missing & ColorSpaceMath.AlphaMissing) != 0)
                text.Append(" / none");
            else if (Alpha < 1)
                text.Append(" / ").Append(Number(Math.Max(Alpha, 0)));

            return text.Append(')').ToString();
        }

        // Six significant digits, the precision a serialization needs: 29% of 100 is 29, not the 28.999999 a float leaves behind.
        private static string Number(double value)
        {
            var rounded = double.Parse(value.ToString("G6", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return rounded == 0 ? "0" : rounded.ToString("0.####################", CultureInfo.InvariantCulture);
        }
    }

    internal static partial class ColorFunctionExtensions
    {
        /// <summary>
        /// Reads a color that is not one of the legacy sRGB forms as it was written: <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c>, <c>color()</c>, a <c>color-mix()</c> in
        /// a space of any of those kinds, and relative color syntax in any of the functions but <c>hsl()</c> and <c>hwb()</c>. False for every other color: the 8-bit sRGB color is the color.
        /// </summary>
        public static bool TryResolveAuthored(this IReadOnlyList<Token> value, out AuthoredColor color)
        {
            color = default;
            if (value.OnlyOrDefault() is not { Type: TokenType.Function } function) return false;

            var name = function.Data;
            var args = function.ArgumentTokens;

            if (IsRelative(args))
            {
                if (!TryEvaluateRelative(name.ToString(), args, out var spec, out var values, out var relativeAlpha)) return false;

                switch (spec.Function)
                {
                    case "rgb":
                        color = new AuthoredColor(ColorSpaceMath.Space.Srgb, [values[0] / 255.0, values[1] / 255.0, values[2] / 255.0], 0, relativeAlpha);
                        return true;
                    case "hsl":
                    case "hwb":
                        return false;
                    default:
                        color = new AuthoredColor(spec.Space, values, 0, relativeAlpha);
                        return true;
                }
            }

            if (name.Equals(FunctionNames.Lab, StringComparison.OrdinalIgnoreCase) || name.Equals(FunctionNames.Oklab, StringComparison.OrdinalIgnoreCase))
            {
                var lab = name.Equals(FunctionNames.Lab, StringComparison.OrdinalIgnoreCase);
                if (!TryRectComponents(args, lab ? 125.0 : 0.4, lab ? 100.0 : 1.0, out var v, out var alpha, out var missing)) return false;
                color = new AuthoredColor(lab ? ColorSpaceMath.Space.Lab : ColorSpaceMath.Space.Oklab, v, missing, alpha);
                return true;
            }

            if (name.Equals(FunctionNames.Lch, StringComparison.OrdinalIgnoreCase) || name.Equals(FunctionNames.Oklch, StringComparison.OrdinalIgnoreCase))
            {
                var lch = name.Equals(FunctionNames.Lch, StringComparison.OrdinalIgnoreCase);
                if (!TryPolarComponents(args, lch ? 150.0 : 0.4, lch ? 100.0 : 1.0, out var v, out var alpha, out var missing)) return false;
                color = new AuthoredColor(lch ? ColorSpaceMath.Space.Lch : ColorSpaceMath.Space.Oklch, v, missing, alpha);
                return true;
            }

            if (name.Equals(FunctionNames.ColorFunction, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryColorFunctionComponents(args, out var space, out var v, out var alpha, out var missing)) return false;
                color = new AuthoredColor(space, v, missing, alpha);
                return true;
            }

            if (name.Equals(FunctionNames.ColorMix, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryColorMixOperands(args, out var c1, out var c2, out var t, out var space, out var hue, out var alphaMultiplier, out var oklab1, out var oklab2, out var written1, out var written2))
                    return false;

                // Mixed in a legacy space (hsl, hwb), or with device-cmyk(): the 8-bit color is the answer.
                if (c1.IsDeviceCmyk || c2.IsDeviceCmyk || space is ColorSpaceMath.Space.Hsl or ColorSpaceMath.Space.Hwb) return false;

                var mixed = Color.MixComponents(c1, c2, t, space, hue, out var mixedAlpha, oklab1, oklab2, written1, written2);
                color = new AuthoredColor(space, mixed, 0, Math.Clamp(mixedAlpha * alphaMultiplier, 0, 1));
                return true;
            }

            return false;
        }
    }
}
