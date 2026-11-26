using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachDrawing.Core
{
    /// <summary>
    /// Reads the text of a CSS filter list (<c>blur(5px) grayscale(50%) drop-shadow(2px 2px 4px red)</c>, CSS Filter Effects Level 1) into the
    /// <see cref="LayerEffect"/>s that <see cref="Canvas.BeginLayer"/> applies to what is drawn in a layer, in the order they were written. It is the grammar of the
    /// functions that act on one picture: a <c>url()</c> reference to an SVG filter needs a document to look in and is not read here.
    /// </summary>
    public static class FilterList
    {
        /// <summary>
        /// Reads a filter list. <c>none</c> is a list of no effects. The text is exactly as valid as CSS says it is: one function that is unknown or whose arguments are not
        /// right makes the whole list not valid, and there are then no effects.
        /// </summary>
        /// <param name="text">the filter list</param>
        /// <param name="parseColor">reads the color of a <c>drop-shadow()</c> (any CSS color), or returns <see langword="null"/> when the text is not a color; with no reader, a shadow takes only <paramref name="currentColor"/></param>
        /// <param name="currentColor">the color of a <c>drop-shadow()</c> that names none</param>
        /// <param name="fontSize">what an <c>em</c> or <c>rem</c> length is, in pixels</param>
        /// <param name="effects">the effects the list stands for, none when it is <c>none</c> or not valid</param>
        /// <returns>whether the text is a valid filter list</returns>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/></exception>
        public static bool TryParse(string text, Func<string, PaintColor?>? parseColor, PaintColor currentColor, double fontSize, out IReadOnlyList<LayerEffect> effects)
        {
            ArgumentNullException.ThrowIfNull(text);
            effects = [];

            var trimmed = text.Trim();
            if (trimmed.Length == 0)
                return false;
            if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
                return true;

            var result = new List<LayerEffect>();
            var i = 0;
            while (true)
            {
                while (i < trimmed.Length && char.IsWhiteSpace(trimmed[i]))
                    i++;
                if (i == trimmed.Length)
                    break;

                var nameStart = i;
                while (i < trimmed.Length && (char.IsAsciiLetter(trimmed[i]) || trimmed[i] == '-'))
                    i++;
                if (i == nameStart || i == trimmed.Length || trimmed[i] != '(')
                    return false;

                var name = trimmed[nameStart..i].ToLowerInvariant();
                var close = MatchingParenthesis(trimmed, i);
                if (close < 0)
                    return false;

                var arguments = SplitArguments(trimmed[(i + 1)..close]);
                i = close + 1;
                if (!AddFunction(result, name, arguments, parseColor, currentColor, fontSize))
                    return false;
            }

            effects = result;
            return true;
        }

        private static int MatchingParenthesis(string text, int open)
        {
            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '(')
                    depth++;
                else if (text[i] == ')' && --depth == 0)
                    return i;
            }

            return -1;
        }

        // The arguments of a function: separated by white space, with anything in parentheses (a color function) kept whole.
        private static List<string> SplitArguments(string text)
        {
            var result = new List<string>();
            var depth = 0;
            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var c = i < text.Length ? text[i] : ' ';
                if (char.IsWhiteSpace(c) && depth == 0)
                {
                    if (start >= 0)
                        result.Add(text[start..i]);
                    start = -1;
                    continue;
                }

                if (start < 0)
                    start = i;
                if (c == '(')
                    depth++;
                else if (c == ')')
                    depth--;
            }

            return result;
        }

        private static bool AddFunction(List<LayerEffect> effects, string name, List<string> arguments, Func<string, PaintColor?>? parseColor, PaintColor currentColor, double fontSize)
        {
            switch (name)
            {
                case "blur":
                {
                    if (arguments.Count > 1)
                        return false;

                    var radius = 0.0;
                    if (arguments.Count == 1 && !TryLength(arguments[0], fontSize, out radius) || radius < 0)
                        return false;

                    if (radius > 0)
                        effects.Add(new BlurEffect(radius));
                    return true;
                }

                case "brightness":
                case "contrast":
                case "saturate":
                case "grayscale":
                case "invert":
                case "sepia":
                case "opacity":
                {
                    if (arguments.Count > 1)
                        return false;

                    var amount = 1.0;
                    if (arguments.Count == 1 && !TryAmount(arguments[0], out amount) || amount < 0)
                        return false;

                    effects.Add(new ColorMatrixEffect(name switch
                    {
                        "brightness" => ColorMatrix.Brightness(amount),
                        "contrast" => ColorMatrix.Contrast(amount),
                        "saturate" => ColorMatrix.Saturate(amount),
                        "grayscale" => ColorMatrix.Grayscale(amount),
                        "invert" => ColorMatrix.Invert(amount),
                        "sepia" => ColorMatrix.Sepia(amount),
                        _ => ColorMatrix.Opacity(amount),
                    }));
                    return true;
                }

                case "hue-rotate":
                {
                    if (arguments.Count > 1)
                        return false;

                    var radians = 0.0;
                    if (arguments.Count == 1 && !TryAngle(arguments[0], out radians))
                        return false;

                    effects.Add(new ColorMatrixEffect(ColorMatrix.HueRotate(radians)));
                    return true;
                }

                case "drop-shadow":
                {
                    // Two or three lengths (offset x, offset y, blur) and a color before or after them.
                    var lengths = new List<double>();
                    PaintColor? color = null;
                    foreach (var argument in arguments)
                    {
                        if (TryLength(argument, fontSize, out var length))
                        {
                            lengths.Add(length);
                        }
                        else if (color is null && parseColor?.Invoke(argument) is { } parsed)
                        {
                            color = parsed;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    if (lengths.Count is < 2 or > 3 || lengths.Count == 3 && lengths[2] < 0)
                        return false;

                    var blur = lengths.Count == 3 ? lengths[2] : 0;
                    effects.Add(new DropShadowEffect(lengths[0], lengths[1], blur, blur, color ?? currentColor));
                    return true;
                }

                default:
                    return false;
            }
        }

        // A number or a percentage (50% is 0.5).
        private static bool TryAmount(string text, out double amount)
        {
            if (text.EndsWith('%'))
            {
                if (TryNumber(text[..^1], out var percent))
                {
                    amount = percent / 100;
                    return true;
                }

                amount = 0;
                return false;
            }

            return TryNumber(text, out amount);
        }

        private static bool TryNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && !text.StartsWith(' ');

        // A length in pixels: a number with a unit, or a bare 0.
        private static bool TryLength(string text, double fontSize, out double pixels)
        {
            (string Unit, double Scale)[] units =
            [
                ("px", 1), ("pt", 4.0 / 3), ("pc", 16), ("in", 96), ("cm", 96 / 2.54), ("mm", 96 / 25.4), ("q", 96 / 101.6), ("rem", fontSize), ("em", fontSize),
            ];

            foreach (var (unit, scale) in units)
            {
                if (text.EndsWith(unit, StringComparison.OrdinalIgnoreCase) && TryNumber(text[..^unit.Length], out var number))
                {
                    pixels = number * scale;
                    return true;
                }
            }

            pixels = 0;
            return TryNumber(text, out var zero) && zero == 0;
        }

        // An angle in radians: a number with a unit, or a bare 0.
        private static bool TryAngle(string text, out double radians)
        {
            (string Unit, double Scale)[] units = [("deg", Math.PI / 180), ("grad", Math.PI / 200), ("rad", 1), ("turn", 2 * Math.PI)];
            foreach (var (unit, scale) in units)
            {
                if (text.EndsWith(unit, StringComparison.OrdinalIgnoreCase) && TryNumber(text[..^unit.Length], out var number))
                {
                    radians = number * scale;
                    return true;
                }
            }

            radians = 0;
            return TryNumber(text, out var zero) && zero == 0;
        }
    }
}
