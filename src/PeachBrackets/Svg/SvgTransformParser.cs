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
using System.Linq;
using System.Numerics;

namespace PeachBrackets.Svg
{
    /// <summary>
    /// Parses the SVG <c>transform</c>/<c>gradientTransform</c> attribute grammar: a whitespace
    /// separated list of <c>matrix()</c>/<c>translate()</c>/<c>scale()</c>/<c>rotate()</c>/
    /// <c>skewX()</c>/<c>skewY()</c> functions, and the CSS <c>transform</c> syntax (CSS Transforms 1 section 13) a stylesheet or a keyframe uses:
    /// angles with a unit (<c>90deg</c>, <c>0.25turn</c>, <c>1.5rad</c>, <c>100grad</c>), lengths in <c>px</c>, <c>pt</c>, <c>pc</c>, <c>in</c>,
    /// <c>cm</c> and <c>mm</c>, the single-axis <c>translateX()</c>/<c>translateY()</c>/<c>scaleX()</c>/<c>scaleY()</c>, <c>skew()</c> and
    /// <c>rotateZ()</c>. It is one parser for the <c>transform</c> attribute, <c>gradientTransform</c> and <c>patternTransform</c> too, so those
    /// accept the CSS forms as well (a superset of what a browser takes there). A function that has an argument this parser cannot resolve - a unit it
    /// does not know (<c>em</c>, a percentage of a length), a <c>calc()</c> or another function - contributes nothing, and
    /// the functions after it still apply. The 3D functions (CSS Transforms 2) are read into a 4x4 matrix (<see cref="ParseFull"/>); <see cref="Parse"/>, for the places that
    /// hold only a 2D matrix, ignores a list that holds one.
    /// </summary>
    internal static class SvgTransformParser
    {
        /// <summary>
        /// The matrix a transform list stands for. <paramref name="context"/> resolves the font-relative (<c>em</c>, <c>rem</c>) and percentage lengths of
        /// <c>translate()</c>; without it such a function does nothing. <paramref name="allowUnits"/> is false for <c>gradientTransform</c> and
        /// <c>patternTransform</c>, whose attribute grammar takes bare numbers only: a function with any unit does nothing there.
        /// </summary>
        public static Matrix3x2? Parse(string? value, SvgTransformContext? context = null, bool allowUnits = true, bool allowCalc = false)
        {
            // The 2D readers (gradientTransform, patternTransform, a clipPath's transform) cannot hold a 3D function: CSS Transforms 2 section 11 ignores the whole list there.
            var parsed = ParseFull(value, context, allowUnits, allowCalc);
            return parsed is { Full: null } flat ? flat.Affine : null;
        }

        /// <summary>
        /// The transform a list stands for, with its full 4x4 matrix when the list holds a 3D function (<c>translate3d()</c>, <c>translateZ()</c>, <c>scale3d()</c>,
        /// <c>scaleZ()</c>, <c>rotate3d()</c>, <c>rotateX()</c>, <c>rotateY()</c>, <c>matrix3d()</c> or <c>perspective()</c>). <see cref="SvgParsedTransform.Affine"/> is
        /// that matrix's orthographic projection onto the plane (what a renderer that cannot do 3D would draw). Null when no function of the list reads.
        /// </summary>
        public static SvgParsedTransform? ParseFull(string? value, SvgTransformContext? context = null, bool allowUnits = true, bool allowCalc = false)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var pos = 0;
            var combined = Matrix4x4.Identity;
            var hasAny = false;
            var has3D = false;

            while (TryReadFunction(value, ref pos, out var name, out var args))
            {
                var m = BuildMatrix(name, args, context, allowUnits, allowCalc, out var is3D);

                // A 3D function counts whether or not it resolved: a list that holds one is a 3D list (and is ignored whole where 3D cannot be).
                has3D |= is3D;

                if (m is not { } matrix)
                    continue;

                // Mirrors the composition convention used for the CSS `transform` property
                // (CssValueParser.ParseTransform): last-written function ends up innermost.
                combined = matrix * combined;
                hasAny = true;
            }

            if (!hasAny)
                return null;

            return new SvgParsedTransform(
                new Matrix3x2(combined.M11, combined.M12, combined.M21, combined.M22, combined.M41, combined.M42),
                has3D ? combined : null);
        }

        /// <summary>Marks an argument that is a <c>calc()</c> expression; the text after it is the expression as written.</summary>
        private const string CalcPrefix = "calc:";

        private static bool IsCalc(string unit) => unit.StartsWith(CalcPrefix, StringComparison.Ordinal);

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

            while (true)
            {
                SvgNumberScanner.SkipSeparators(s, ref pos);

                if (pos >= s.Length || s[pos] == ')')
                    break;

                if (SvgNumberScanner.TryReadNumber(s, ref pos, out var num))
                {
                    // A CSS dimension: the unit (letters, or a percent sign) follows the number directly.
                    var unitStart = pos;
                    while (pos < s.Length && (char.IsAsciiLetter(s[pos]) || s[pos] == '%'))
                        pos++;

                    args.Add((num, pos == unitStart ? string.Empty : s.AsSpan(unitStart, pos - unitStart).ToString().ToLowerInvariant()));
                    continue;
                }

                // Something that is not a number: a calc() is kept as written (CalcPrefix) for a length to evaluate, anything else (var(), another
                // function) is not known here and makes the function do nothing, so the functions after it still read. Nested parentheses are skipped whole.
                var start = pos;
                var depth = 0;
                while (pos < s.Length)
                {
                    var c = s[pos];
                    if (c == '(') depth++;
                    else if (c == ')')
                    {
                        if (depth == 0) break;
                        depth--;
                    }
                    else if (depth == 0 && (c == ',' || char.IsWhiteSpace(c))) break;

                    pos++;
                }

                if (pos == start)
                    break;

                var text = s[start..pos];
                args.Add(text.StartsWith("calc(", StringComparison.OrdinalIgnoreCase) ? (double.NaN, CalcPrefix + text) : (double.NaN, "?"));
            }

            if (pos < s.Length && s[pos] == ')')
                pos++;

            return true;
        }

        /// <summary>
        /// Whether <paramref name="value"/> is a valid CSS <c>transform</c> declaration value (CSS Transforms 1 section 13) in the forms this parser reads: lengths and
        /// angles carry a unit unless they are zero (so <c>rotate(10)</c> is not valid where the attribute <c>rotate(10)</c> is), and each function has its own
        /// argument count (<c>rotate(a, cx, cy)</c> is SVG attribute syntax only). A browser drops an invalid declaration, so the next cascade tier answers.
        /// </summary>
        public static bool IsValidCssTransform(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                return true;

            var pos = 0;
            var any = false;

            while (TryReadFunction(value, ref pos, out var name, out var args))
            {
                any = true;
                var count = args.Count;

                if (args.Exists(a => a.Unit == "?" || IsCalc(a.Unit)))
                    continue; // calc() / var(): judged when it is a number

                bool Plain(int i) => args[i].Unit.Length == 0;
                bool Zero(int i) => args[i].Value == 0;
                bool LengthOk(int i) => !Plain(i) ? args[i].Unit is not ("deg" or "grad" or "rad" or "turn") : Zero(i);
                bool AngleOk(int i) => !Plain(i) ? args[i].Unit is "deg" or "grad" or "rad" or "turn" : Zero(i);

                bool PlainOrPercent(int i) => Plain(i) || args[i].Unit == "%";

                // True when every argument in [0, n) passes the check (kind: 0 plain number, 1 length, 2 angle, 3 number or percentage).
                bool AllOk(int n, int kind)
                {
                    for (var i = 0; i < n; i++)
                    {
                        var ok = kind switch { 0 => Plain(i), 1 => LengthOk(i), 2 => AngleOk(i), _ => PlainOrPercent(i) };
                        if (!ok) return false;
                    }

                    return true;
                }

                var valid = name.ToLowerInvariant() switch
                {
                    "matrix" => count == 6 && AllOk(6, 0),
                    "translate" => count is 1 or 2 && AllOk(count, 1),
                    "translatex" or "translatey" or "translatez" => count == 1 && LengthOk(0),
                    "scale" => count is 1 or 2 && AllOk(count, 3),
                    "scalex" or "scaley" or "scalez" => count == 1 && (Plain(0) || args[0].Unit == "%"),
                    "rotate" or "rotatex" or "rotatey" or "rotatez" or "skewx" or "skewy" => count == 1 && AngleOk(0),
                    "skew" => count is 1 or 2 && AllOk(count, 2),
                    "translate3d" => count == 3 && AllOk(3, 1),
                    "scale3d" => count == 3 && AllOk(3, 3),
                    "rotate3d" => count == 4 && AllOk(3, 0) && AngleOk(3),
                    "matrix3d" => count == 16 && AllOk(16, 0),
                    "perspective" => count == 1 && LengthOk(0) && args[0].Value >= 0,
                    _ => true, // anything else: not judged here
                };

                if (!valid)
                    return false;
            }

            return any;
        }

        private static Matrix4x4? BuildMatrix(string name, List<(double Value, string Unit)> args, SvgTransformContext? context, bool allowUnits, bool allowCalc, out bool is3D)
        {
            var lowerName = name.ToLowerInvariant();
            is3D = lowerName is "translatez" or "translate3d" or "scalez" or "scale3d" or "rotatex" or "rotatey" or "rotate3d" or "matrix3d" or "perspective";

            // Set when an argument has a unit this parser cannot resolve here: the function then does nothing. An argument that was not a
            // number at all (calc(), a nested function) counts, wherever it stands in the list, even where the function reads fewer arguments.
            var calcArgs = args.Count(arg => IsCalc(arg.Unit));
            var calcRead = 0;
            var resolvable = args.TrueForAll(arg => arg.Unit != "?") && (allowUnits || args.TrueForAll(arg => arg.Unit.Length == 0));

            // A plain <number>: matrix() entries and the centre of rotate(a cx cy) take no unit.
            float Arg(int i)
            {
                if (i >= args.Count) return 0f;

                var (value, unit) = args[i];
                if (unit.Length != 0) resolvable = false;
                return (float)value;
            }

            // A scale factor: a <number>, or a <percentage> that is the same number (CSS Transforms 2: scale(50%) is scale(0.5)).
            float Factor(int i)
            {
                if (i >= args.Count) return 0f;

                var (value, unit) = args[i];
                if (unit == "%") return (float)(value / 100);
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

            // User units (CSS px). A bare number is one too (the attribute grammar). axis: 0 horizontal, 1 vertical (what a percentage is of), 2 depth (no percentage).
            float Length(int i, int axis)
            {
                if (i >= args.Count) return 0f;

                var (value, unit) = args[i];
                switch (unit)
                {
                    case "" or "px": return (float)value;
                    case "em" when context is not null: return (float)(value * context.EmPx);
                    case "rem" when context is not null: return (float)(value * context.RootEmPx);
                    case "%" when axis != 2 && context is not null && !double.IsNaN(axis == 0 ? context.ReferenceWidth : context.ReferenceHeight):
                        return (float)(value / 100 * (axis == 0 ? context.ReferenceWidth : context.ReferenceHeight));
                    case "in": return (float)(value * 96);
                    case "cm": return (float)(value * 96 / 2.54);
                    case "mm": return (float)(value * 96 / 25.4);
                    case "pt": return (float)(value * 4 / 3);
                    case "pc": return (float)(value * 16);
                    case "q": return (float)(value * 96 / 101.6);
                    case var calc when IsCalc(calc) && allowCalc && context is not null:
                    {
                        var reference = axis == 0 ? context.ReferenceWidth : context.ReferenceHeight;
                        var result = SvgValueParsers.ParseLength(calc[CalcPrefix.Length..], double.IsNaN(reference) ? null : reference, context.Basis);
                        if (result is { } px) { calcRead++; return (float)px; }
                        resolvable = false;
                        return 0f;
                    }
                    default: resolvable = false; return 0f;
                }
            }


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
                    matrix = Translation(Length(0, 0), args.Count >= 2 ? Length(1, 1) : 0f);
                    break;

                case "translatex" when args.Count >= 1:
                    matrix = Translation(Length(0, 0), 0f);
                    break;

                case "translatey" when args.Count >= 1:
                    matrix = Translation(0f, Length(0, 1));
                    break;

                case "scale" when args.Count >= 1:
                {
                    var sx = Factor(0);
                    matrix = Scaling(sx, args.Count >= 2 ? Factor(1) : sx);
                    break;
                }

                case "scalex" when args.Count >= 1:
                    matrix = Scaling(Factor(0), 1f);
                    break;

                case "scaley" when args.Count >= 1:
                    matrix = Scaling(1f, Factor(0));
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

                case "translatez" when args.Count >= 1:
                    matrix = Matrix4x4.CreateTranslation(0, 0, Length(0, 2));
                    break;

                case "translate3d" when args.Count >= 3:
                    matrix = Matrix4x4.CreateTranslation(Length(0, 0), Length(1, 1), Length(2, 2));
                    break;

                case "scalez" when args.Count >= 1:
                    matrix = Matrix4x4.CreateScale(1f, 1f, Factor(0));
                    break;

                case "scale3d" when args.Count >= 3:
                    matrix = Matrix4x4.CreateScale(Factor(0), Factor(1), Factor(2));
                    break;

                case "rotatex" when args.Count >= 1:
                    matrix = Matrix4x4.CreateRotationX(Angle(0) * (MathF.PI / 180f));
                    break;

                case "rotatey" when args.Count >= 1:
                    matrix = Matrix4x4.CreateRotationY(Angle(0) * (MathF.PI / 180f));
                    break;

                case "rotate3d" when args.Count >= 4:
                {
                    var axis = new Vector3(Arg(0), Arg(1), Arg(2));
                    var degrees = Angle(3);

                    // A zero direction vector turns nothing (CSS Transforms 2 section 14).
                    matrix = axis.LengthSquared() < 1e-12f
                        ? Matrix4x4.Identity
                        : Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), degrees * (MathF.PI / 180f));
                    break;
                }

                case "matrix3d" when args.Count >= 16:
                {

                    // CSS lists the sixteen numbers column-major, and System.Numerics is row-major with the translation in the fourth row: the two are
                    // transposes of each other, so the numbers read in order into the row-major constructor are the right matrix.
                    var v = new float[16];
                    for (var i = 0; i < 16; i++) v[i] = Arg(i);
                    matrix = new Matrix4x4(
                        v[0], v[1], v[2], v[3],
                        v[4], v[5], v[6], v[7],
                        v[8], v[9], v[10], v[11],
                        v[12], v[13], v[14], v[15]);
                    break;
                }

                case "perspective" when args.Count >= 1:
                {
                    var distance = Length(0, 2);

                    // A negative or zero length does nothing (as does none, which is not a number and did not resolve above). Under one pixel is one pixel
                    // (CSS Transforms 2 section 12.2): a smaller distance is a sliver of the plane the viewer sits in, nothing a document means to draw.
                    if (!(distance > 0))
                        return null;

                    var perspective = Matrix4x4.Identity;
                    perspective.M34 = -1f / MathF.Max(distance, 1f);
                    matrix = perspective;
                    break;
                }

                default:
                    // Unrecognized function name - recognized only so we can skip past the
                    // parentheses; contributes no transform.
                    return null;
            }

            // A calc() the function did not read (an extra argument) is one it cannot resolve, as is any other argument it left unread.
            return resolvable && calcRead == calcArgs ? matrix : null;
        }

        // Sine and cosine of an angle in degrees, exact at the quarter turns a rotation most often is (rotate(0.25turn) is a clean 0 and 1, not 1.7e-7).
        private static (float Sin, float Cos) SinCos(float degrees)
        {
            var normalised = degrees % 360f;
            if (normalised < 0) normalised += 360f;

            return normalised switch
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

namespace PeachBrackets.Svg
{
    /// <summary>A parsed transform list: <paramref name="Affine"/> is its 2D matrix, and <paramref name="Full"/> the 4x4 matrix when the list holds a 3D function (null for a 2D list).</summary>
    internal readonly record struct SvgParsedTransform(Matrix3x2 Affine, Matrix4x4? Full);

    /// <summary>What a CSS transform needs to resolve its relative lengths: the font sizes (<c>em</c>, <c>rem</c>) and the reference box a percentage is of.</summary>
    /// <param name="EmPx">The element's font size in user units.</param>
    /// <param name="RootEmPx">The root element's font size in user units.</param>
    /// <param name="ReferenceWidth">The width of the transform reference box; NaN when it is not known when the transform is read (a <c>fill-box</c>).</param>
    /// <param name="ReferenceHeight">The height of the transform reference box; NaN when it is not known.</param>
    /// <param name="Basis">The font a <c>calc()</c> length resolves its font-relative units against.</param>
    internal sealed record SvgTransformContext(double EmPx, double RootEmPx, double ReferenceWidth, double ReferenceHeight, ISvgLengthBasis? Basis = null);
}
