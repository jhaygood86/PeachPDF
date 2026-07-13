// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters.Entities;
using System.Collections.Generic;
using System.Numerics;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Parses the SVG <c>transform</c>/<c>gradientTransform</c> attribute grammar: a whitespace
    /// separated list of <c>matrix()</c>/<c>translate()</c>/<c>scale()</c> functions (the only ones
    /// needed for v1 - <c>rotate()</c>/<c>skewX()</c>/<c>skewY()</c> are recognized syntactically
    /// so parsing doesn't break, but contribute no transform, since none of them can currently be
    /// expressed as-is by <see cref="RMatrix"/> consumers that assume axis-aligned gradients).
    /// </summary>
    internal static class SvgTransformParser
    {
        public static RMatrix? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var pos = 0;
            var combined = Matrix4x4.Identity;
            var hasAny = false;

            while (TryReadFunction(value, ref pos, out var name, out var args))
            {
                var m = BuildMatrix(name, args);

                if (m is not { } matrix)
                    continue;

                // Mirrors the composition convention used for the CSS `transform` property
                // (CssValueParser.ParseTransform): last-written function ends up innermost.
                combined = matrix * combined;
                hasAny = true;
            }

            if (!hasAny)
                return null;

            return new RMatrix(combined.M11, combined.M12, combined.M21, combined.M22, combined.M41, combined.M42);
        }

        private static bool TryReadFunction(string s, ref int pos, out string name, out List<double> args)
        {
            SvgNumberScanner.SkipSeparators(s, ref pos);
            name = "";
            args = [];

            var nameStart = pos;

            while (pos < s.Length && (char.IsAsciiLetter(s[pos]) || s[pos] == '-'))
                pos++;

            if (pos == nameStart)
                return false;

            name = s[nameStart..pos];

            SvgNumberScanner.SkipSeparators(s, ref pos);

            if (pos >= s.Length || s[pos] != '(')
                return false;

            pos++;

            while (SvgNumberScanner.TryReadNumber(s, ref pos, out var num))
                args.Add(num);

            SvgNumberScanner.SkipSeparators(s, ref pos);

            if (pos < s.Length && s[pos] == ')')
                pos++;

            return true;
        }

        private static Matrix4x4? BuildMatrix(string name, List<double> args)
        {
            float Arg(int i) => i < args.Count ? (float)args[i] : 0f;

            switch (name.ToLowerInvariant())
            {
                case "matrix" when args.Count >= 6:
                {
                    float a = Arg(0), b = Arg(1), c = Arg(2), d = Arg(3), e = Arg(4), f = Arg(5);
                    return new Matrix4x4(
                        a, b, 0, 0,
                        c, d, 0, 0,
                        0, 0, 1, 0,
                        e, f, 0, 1);
                }

                case "translate" when args.Count >= 1:
                {
                    var tx = Arg(0);
                    var ty = args.Count >= 2 ? Arg(1) : 0f;
                    return new Matrix4x4(
                        1, 0, 0, 0,
                        0, 1, 0, 0,
                        0, 0, 1, 0,
                        tx, ty, 0, 1);
                }

                case "scale" when args.Count >= 1:
                {
                    var sx = Arg(0);
                    var sy = args.Count >= 2 ? Arg(1) : sx;
                    return new Matrix4x4(
                        sx, 0, 0, 0,
                        0, sy, 0, 0,
                        0, 0, 1, 0,
                        0, 0, 0, 1);
                }

                default:
                    // rotate()/skewX()/skewY()/unknown - recognized only so we can skip past the
                    // parentheses; contributes no transform (documented v1 limitation).
                    return null;
            }
        }
    }
}
