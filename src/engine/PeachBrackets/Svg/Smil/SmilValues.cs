using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PeachBrackets.Html.Animation;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;

namespace PeachBrackets.Svg.Smil
{
    /// <summary>
    /// The values of a SMIL animation as text: mixed, added and scaled. An attribute value is a list of numbers with whatever sits between them (a length, a
    /// point list, a viewBox, path data), so two values with the same text between their numbers are mixed number by number; a colour or a keyword goes through the
    /// CSS interpolator; anything else switches at the half way point (SMIL Animation §3.2.3, discrete values).
    /// </summary>
    internal static class SmilValues
    {
        private static readonly Regex NumberPattern = new(@"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>A value cut into its numbers and the text around them: <c>Parts.Length == Numbers.Length + 1</c>.</summary>
        internal sealed record Template(string[] Parts, double[] Numbers)
        {
            public bool SameShape(Template other)
            {
                if (Numbers.Length != other.Numbers.Length) return false;
                for (var i = 0; i < Parts.Length; i++)
                {
                    if (!string.Equals(Parts[i], other.Parts[i], StringComparison.Ordinal)) return false;
                }

                return true;
            }

            public string Format(double[] numbers)
            {
                var text = new StringBuilder(Parts[0]);
                for (var i = 0; i < numbers.Length; i++)
                {
                    text.Append(CssValueInterpolator.Format(numbers[i])).Append(Parts[i + 1]);
                }

                return text.ToString();
            }
        }

        private static readonly string[] ColorMarkers = ["#", "rgb", "hsl", "hwb", "lab", "lch", "oklab", "oklch", "color("];

        internal static Template? TryTemplate(string? text)
        {
            if (text is null) return null;

            var trimmed = text.Trim();
            foreach (var marker in ColorMarkers)
            {
                if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) return null;
            }

            var matches = NumberPattern.Matches(trimmed);
            var parts = new string[matches.Count + 1];
            var numbers = new double[matches.Count];
            var last = 0;
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                parts[i] = trimmed[last..match.Index];
                if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i])) return null;
                last = match.Index + match.Length;
            }

            parts[matches.Count] = trimmed[last..];
            return new Template(parts, numbers);
        }

        /// <summary>The value <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
        internal static string Mix(CssValueParser? parser, string attribute, string from, string to, double t, CssBox? box)
        {
            if (TryTemplate(from) is { } a && TryTemplate(to) is { } b && a.Numbers.Length > 0 && a.SameShape(b))
            {
                var mixed = new double[a.Numbers.Length];
                for (var i = 0; i < mixed.Length; i++) mixed[i] = a.Numbers[i] + ((b.Numbers[i] - a.Numbers[i]) * t);
                return a.Format(mixed);
            }

            if (parser is not null && LooksInterpolable(from) && LooksInterpolable(to))
            {
                try
                {
                    return CssValueInterpolator.Interpolate(parser, attribute, from, to, t, box);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
                {
                    // Fall through to the discrete answer.
                }
            }

            return t < 0.5 ? from : to;
        }

        // A colour, or a keyword the CSS interpolator knows what to do with (a number list that did not line up is not one).
        private static bool LooksInterpolable(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0) return false;
            if (trimmed[0] == '#') return true;
            foreach (var marker in ColorMarkers)
            {
                if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return char.IsLetter(trimmed[0]);
        }

        /// <summary>The sum of two values of the same shape (a length and a length, a point list and a point list), or null when they cannot be added.</summary>
        internal static string? Add(string? a, string? b)
        {
            if (TryTemplate(a) is not { } x || TryTemplate(b) is not { } y || x.Numbers.Length == 0 || !x.SameShape(y)) return null;

            var sum = new double[x.Numbers.Length];
            for (var i = 0; i < sum.Length; i++) sum[i] = x.Numbers[i] + y.Numbers[i];
            return x.Format(sum);
        }

        /// <summary>A value with every number multiplied by <paramref name="factor"/>, or null when it has none.</summary>
        internal static string? Scale(string? value, double factor)
        {
            if (TryTemplate(value) is not { } template || template.Numbers.Length == 0) return null;

            var scaled = new double[template.Numbers.Length];
            for (var i = 0; i < scaled.Length; i++) scaled[i] = template.Numbers[i] * factor;
            return template.Format(scaled);
        }

        /// <summary>The distance between two values of the same shape, for <c>calcMode="paced"</c>; 0 when they are not numbers of one shape.</summary>
        internal static double Distance(string a, string b)
        {
            if (TryTemplate(a) is not { } x || TryTemplate(b) is not { } y || x.Numbers.Length == 0 || !x.SameShape(y)) return 0;

            double sum = 0;
            for (var i = 0; i < x.Numbers.Length; i++)
            {
                var d = y.Numbers[i] - x.Numbers[i];
                sum += d * d;
            }

            return Math.Sqrt(sum);
        }

        /// <summary>Splits a <c>values</c>, <c>keyTimes</c> or <c>keySplines</c> list at its semicolons.</summary>
        internal static List<string> SplitList(string? text)
        {
            var list = new List<string>();
            if (text is null) return list;

            foreach (var part in text.Split(';'))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) list.Add(trimmed);
            }

            return list;
        }

        internal static List<double>? ParseNumbers(string? text)
        {
            if (text is null) return null;

            var numbers = new List<double>();
            foreach (var part in SplitList(text))
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
                numbers.Add(value);
            }

            return numbers.Count == 0 ? null : numbers;
        }

        /// <summary>The cubic Bézier <c>keySplines</c> entry (x1 y1 x2 y2) as an easing: the progress <paramref name="x"/> of the segment becomes the eased one.</summary>
        internal static double Spline(string spline, double x)
        {
            var parts = spline.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return x;

            var c = new double[4];
            for (var i = 0; i < 4; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out c[i])) return x;
            }

            // Solve the curve's x for the parameter, then read its y (the control points run (0,0), (x1,y1), (x2,y2), (1,1)).
            double lo = 0, hi = 1, s = x;
            for (var i = 0; i < 40; i++)
            {
                var value = Bezier(c[0], c[2], s);
                if (Math.Abs(value - x) < 1e-7) break;
                if (value < x) lo = s; else hi = s;
                s = (lo + hi) / 2;
            }

            return Bezier(c[1], c[3], s);
        }

        private static double Bezier(double p1, double p2, double s)
        {
            var u = 1 - s;
            return (3 * u * u * s * p1) + (3 * u * s * s * p2) + (s * s * s);
        }
    }
}
