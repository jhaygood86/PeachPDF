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

using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Parses the SVG <c>transform</c>/<c>gradientTransform</c> attribute grammar: a whitespace
    /// separated list of <c>matrix()</c>/<c>translate()</c>/<c>scale()</c>/<c>rotate()</c>/
    /// <c>skewX()</c>/<c>skewY()</c> functions, and the CSS <c>transform</c> syntax (CSS Transforms 1 section 13) a stylesheet or a keyframe uses:
    /// angles with a unit (<c>90deg</c>, <c>0.25turn</c>, <c>1.5rad</c>, <c>100grad</c>), lengths in <c>px</c>, <c>pt</c>, <c>pc</c>, <c>in</c>,
    /// <c>cm</c> and <c>mm</c>, the single-axis <c>translateX()</c>/<c>translateY()</c>/<c>scaleX()</c>/<c>scaleY()</c>, <c>skew()</c> and
    /// <c>rotateZ()</c>. It is one parser for the <c>transform</c> attribute, <c>gradientTransform</c> and <c>patternTransform</c> too, so those
    /// accept the CSS forms as well (a superset of what a browser takes there). A function that has an argument this parser cannot resolve - a unit it
    /// does not know (<c>em</c>, a percentage of a length), a <c>calc()</c> or another function - or that is a 3D function, contributes nothing, and
    /// the functions after it still apply.
    /// </summary>
    internal static class SvgTransformParser
    {
        public static Matrix3x2? Parse(string? value)
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

            return new Matrix3x2(combined.M11, combined.M12, combined.M21, combined.M22, combined.M41, combined.M42);
        }

        private static bool TryReadFunction(string s, ref int pos, out string name, out List<(double Value, string Unit)> args)
        {
            SvgNumberScanner.SkipSeparators(s, ref pos);
            name = "";
            args = [];

            var nameStart = pos;

            while (pos < s.Length && (char.IsAsciiLetter(s[pos]) || s[pos] == '-' || (pos > nameStart && char.IsAsciiDigit(s[pos]))))
                pos++;

            if (pos == nameStart)
                return false;

            name = s[nameStart..pos];

            SvgNumberScanner.SkipSeparators(s, ref pos);

            if (pos >= s.Length || s[pos] != '(')
                return false;

            pos++;

            while (SvgNumberScanner.TryReadNumber(s, ref pos, out var num))
            {
                // A CSS dimension: the unit (letters, or a percent sign) follows the number directly.
                var unitStart = pos;
                while (pos < s.Length && (char.IsAsciiLetter(s[pos]) || s[pos] == '%'))
                    pos++;

                args.Add((num, s[unitStart..pos].ToLowerInvariant()));
            }

            SvgNumberScanner.SkipSeparators(s, ref pos);

            if (pos < s.Length && s[pos] != ')')
            {
                // Something that is not a number (calc(), var(), a nested function): the arguments are not all known here. Skip to the closing
                // parenthesis, nested ones included, and let the function do nothing, so the functions after it still read.
                var depth = 0;
                while (pos < s.Length && !(s[pos] == ')' && depth == 0))
                {
                    if (s[pos] == '(') depth++;
                    else if (s[pos] == ')') depth--;
                    pos++;
                }

                args.Add((double.NaN, "?"));
            }

            if (pos < s.Length && s[pos] == ')')
                pos++;

            return true;
        }

        private static Matrix4x4? BuildMatrix(string name, List<(double Value, string Unit)> args)
        {
            // Set when an argument has a unit this parser cannot resolve here: the function then does nothing.
            var resolvable = true;

            // A plain <number>: matrix() entries and the centre of rotate(a cx cy) take no unit.
            float Arg(int i)
            {
                if (i >= args.Count) return 0f;

                var (value, unit) = args[i];
                if (unit.Length != 0) resolvable = false;
                return (float)value;
            }

            // Degrees. A bare number is degrees too: that is the attribute grammar (`rotate(10)`).
            float Angle(int i)
            {
                if (i >= args.Count) return 0f;

                var (value, unit) = args[i];
                switch (unit)
                {
                    case "" or "deg": return (float)value;
                    case "grad": return (float)(value * 0.9);
                    case "rad": return (float)(value * 180 / Math.PI);
                    case "turn": return (float)(value * 360);
                    default: resolvable = false; return 0f;
                }
            }

            // User units (CSS px). A bare number is one too (the attribute grammar).
            float Length(int i)
            {
                if (i >= args.Count) return 0f;

                var (value, unit) = args[i];
                switch (unit)
                {
                    case "" or "px": return (float)value;
                    case "in": return (float)(value * 96);
                    case "cm": return (float)(value * 96 / 2.54);
                    case "mm": return (float)(value * 96 / 25.4);
                    case "pt": return (float)(value * 4 / 3);
                    case "pc": return (float)(value * 16);
                    default: resolvable = false; return 0f;
                }
            }


            var lowerName = name.ToLowerInvariant();
            Matrix4x4? matrix;

            switch (lowerName)
            {
                case "matrix" when args.Count >= 6:
                {
                    float a = Arg(0), b = Arg(1), c = Arg(2), d = Arg(3), e = Arg(4), f = Arg(5);
                    matrix = new Matrix4x4(
                        a, b, 0, 0,
                        c, d, 0, 0,
                        0, 0, 1, 0,
                        e, f, 0, 1);
                    break;
                }

                case "translate" when args.Count >= 1:
                    matrix = Translation(Length(0), args.Count >= 2 ? Length(1) : 0f);
                    break;

                case "translatex" when args.Count >= 1:
                    matrix = Translation(Length(0), 0f);
                    break;

                case "translatey" when args.Count >= 1:
                    matrix = Translation(0f, Length(0));
                    break;

                case "scale" when args.Count >= 1:
                {
                    var sx = Arg(0);
                    matrix = Scaling(sx, args.Count >= 2 ? Arg(1) : sx);
                    break;
                }

                case "scalex" when args.Count >= 1:
                    matrix = Scaling(Arg(0), 1f);
                    break;

                case "scaley" when args.Count >= 1:
                    matrix = Scaling(1f, Arg(0));
                    break;

                case "rotate" or "rotatez" when args.Count >= 1:
                {
                    var (sin, cos) = SinCos(Angle(0));

                    // With an optional center (cx, cy): equivalent to translate(cx,cy) rotate(angle)
                    // translate(-cx,-cy), expressed here as the single closed-form combined matrix
                    // rather than composing three separate matrices.
                    var (e, f) = args.Count >= 3
                        ? (Arg(1) * (1 - cos) + Arg(2) * sin, Arg(2) * (1 - cos) - Arg(1) * sin)
                        : (0f, 0f);

                    matrix = new Matrix4x4(
                        cos, sin, 0, 0,
                        -sin, cos, 0, 0,
                        0, 0, 1, 0,
                        e, f, 0, 1);
                    break;
                }

                case "skewx" when args.Count >= 1:
                    matrix = Skew(MathF.Tan(Angle(0) * (MathF.PI / 180f)), 0f);
                    break;

                case "skewy" when args.Count >= 1:
                    matrix = Skew(0f, MathF.Tan(Angle(0) * (MathF.PI / 180f)));
                    break;

                case "skew" when args.Count >= 1:
                    matrix = Skew(MathF.Tan(Angle(0) * (MathF.PI / 180f)), args.Count >= 2 ? MathF.Tan(Angle(1) * (MathF.PI / 180f)) : 0f);
                    break;

                default:
                    // Unrecognized function name - recognized only so we can skip past the
                    // parentheses; contributes no transform.
                    return null;
            }

            return resolvable ? matrix : null;
        }

        // Sine and cosine of an angle in degrees, exact at the quarter turns a rotation most often is (rotate(0.25turn) is a clean 0 and 1, not 1.7e-7).
        private static (float Sin, float Cos) SinCos(float degrees)
        {
            var turns = degrees % 360f;
            if (turns < 0) turns += 360f;

            return turns switch
            {
                0f => (0f, 1f),
                90f => (1f, 0f),
                180f => (0f, -1f),
                270f => (-1f, 0f),
                _ => (MathF.Sin(degrees * (MathF.PI / 180f)), MathF.Cos(degrees * (MathF.PI / 180f))),
            };
        }

        private static Matrix4x4 Translation(float tx, float ty) => new(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            tx, ty, 0, 1);

        private static Matrix4x4 Scaling(float sx, float sy) => new(
            sx, 0, 0, 0,
            0, sy, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);

        private static Matrix4x4 Skew(float tanX, float tanY) => new(
            1, tanY, 0, 0,
            tanX, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
    }
}
