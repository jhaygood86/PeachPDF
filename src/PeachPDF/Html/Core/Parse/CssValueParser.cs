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

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace PeachPDF.Html.Core.Parse
{
    /// <summary>
    /// Parse CSS properties values like numbers, Urls, etc.
    /// </summary>
    internal sealed class CssValueParser
    {
        #region Fields and Consts

        /// <summary>
        /// 
        /// </summary>
        private readonly RAdapter _adapter;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        public CssValueParser(RAdapter adapter)
        {
            ArgumentNullException.ThrowIfNull(adapter, "global");

            _adapter = adapter;
        }

        /// <summary>
        /// Check if the given substring is a valid double number.
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>true - valid double number, false - otherwise</returns>
        public static bool IsFloat(string str, int idx, int length)
        {
            if (length < 1)
                return false;

            bool sawDot = false;
            for (int i = 0; i < length; i++)
            {
                if (str[idx + i] == '.')
                {
                    if (sawDot)
                        return false;
                    sawDot = true;
                }
                else if (!char.IsDigit(str[idx + i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Check if the given substring is a valid double number.
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>true - valid int number, false - otherwise</returns>
        public static bool IsInt(string str, int idx, int length)
        {
            if (length < 1)
                return false;

            for (int i = 0; i < length; i++)
            {
                if (!char.IsDigit(str[idx + i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Check if the given string is a valid length value.
        /// </summary>
        /// <param name="value">the string value to check</param>
        /// <returns>true - valid, false - invalid</returns>
        public static bool IsValidLength(string value)
        {
            if (value.Length <= 1) return false;

            if (IsCalcFunction(value)) return true;

            var number = string.Empty;

            if (value.EndsWith('%'))
            {
                number = value[..^1];
            }
            else if (value.Length > 2)
            {
                number = value[..^2];
            }

            return double.TryParse(number, out _);
        }

        /// <summary>
        /// Whether <paramref name="value"/> is (syntactically) a single calc-family function - e.g. so
        /// callers that do their own lightweight text scanning of a length string (like
        /// <see cref="Dom.CssBoxProperties"/>'s FontSize setter, which regex-searches for a bare "Nem"
        /// substring to eagerly convert em to points) know to leave a calc() expression alone rather than
        /// mangling it, deferring to <see cref="ParseLength(string, double, double, double, string, bool, bool)"/>'s
        /// real evaluation instead.
        /// </summary>
        public static bool IsCalcFunction(string value)
        {
            return TryGetCalcFunction(value, out _);
        }

        /// <summary>
        /// Recognizes a length string that is a single calc-family (calc/min/max/clamp) function, e.g. for
        /// the syntactic gate in <see cref="IsValidLength"/> and the evaluation branch in
        /// <see cref="ParseLength(string, double, double, double, string, bool, bool)"/>. Real
        /// grammar/type validation already happened in Layer A's CalcValueConverter for any value that
        /// didn't arrive via the var() substitution bypass; this is a syntactic recognizer only.
        /// </summary>
        private static bool TryGetCalcFunction(string length, out FunctionToken? function)
        {
            var tokens = GetCssTokens(length);

            if (tokens is [FunctionToken fn] && CalcParser.IsCalcFamily(fn.Data))
            {
                function = fn;
                return true;
            }

            function = null;
            return false;
        }

        /// <summary>
        /// Evals a number and returns it. If number is a percentage, it will be multiplied by <see cref="hundredPercent"/>
        /// </summary>
        /// <param name="number">Number to be parsed</param>
        /// <param name="hundredPercent">Number that represents the 100% if parsed number is a percentage</param>
        /// <returns>Parsed number. Zero if error while parsing.</returns>
        public static double ParseNumber(string number, double hundredPercent)
        {
            if (string.IsNullOrEmpty(number))
            {
                return 0f;
            }

            var toParse = number;
            var isPercent = number.EndsWith('%');

            if (isPercent)
                toParse = number[..^1];

            if (!double.TryParse(toParse, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out var result))
            {
                return 0f;
            }

            if (isPercent)
            {
                result = (result / 100f) * hundredPercent;
            }

            return result;
        }

        /// <summary>
        /// Parses a length. Lengths are followed by an unit identifier (e.g. 10px, 3.1em)
        /// </summary>
        /// <param name="length">Specified length</param>
        /// <param name="hundredPercent">Equivalent to 100 percent when length is percentage</param>
        /// <param name="fontAdjust">if the length is in pixels and the length is font related it needs to use 72/96 factor</param>
        /// <param name="box"></param>
        /// <returns>the parsed length value with adjustments</returns>
        public static double ParseLength(string length, double hundredPercent, CssBoxProperties box, bool fontAdjust = false)
        {
            return ParseLength(length, hundredPercent, box.GetEmHeight(), box.GetRemHeight(), null, fontAdjust, false);
        }

        /// <summary>
        /// Parses a length. Lengths are followed by an unit identifier (e.g. 10px, 3.1em)
        /// </summary>
        /// <param name="length">Specified length</param>
        /// <param name="hundredPercent">Equivalent to 100 percent when length is percentage</param>
        /// <param name="emFactor"></param>
        /// <param name="remFactor"></param>
        /// <param name="defaultUnit"></param>
        /// <param name="fontAdjust">if the length is in pixels and the length is font related it needs to use 72/96 factor</param>
        /// <param name="returnPoints">Allows the return double to be in points. If false, result will be pixels</param>
        /// <returns>the parsed length value with adjustments</returns>
        public static double ParseLength(string length, double hundredPercent, double emFactor, double remFactor, string? defaultUnit, bool fontAdjust, bool returnPoints)
        {
            //Return zero if no length specified, zero specified
            if (string.IsNullOrEmpty(length) || length == "0")
                return 0f;

            if (TryGetCalcFunction(length, out var calcFunction))
            {
                // Every calc-family string reaching layout has already been validated by Layer A's
                // CalcValueConverter (directly, or via DomParser's var()-substitution re-parse) - a null
                // result here should be unreachable, but 0 is the same "can't make sense of this" fallback
                // used elsewhere in this method for any other degenerate input.
                var node = CalcParser.Parse(calcFunction);
                var context = new CalcContext(hundredPercent, emFactor, remFactor, fontAdjust, returnPoints);
                var pixels = node is not null ? CalcEvaluator.Evaluate(node, context) : null;

                return pixels ?? 0d;
            }

            //Get units of the length
            var (unit, numberValue) = GetUnit(length, defaultUnit, out var hasUnit);

            //Number of the length
            var number = hasUnit ? numberValue : ParseNumber(length, hundredPercent);

            // A bare point value returns the raw point number directly rather than round-tripping
            // through pixel space, avoiding a redundant px->pt->px floating-point conversion.
            if (returnPoints && unit == CssConstants.Pt)
            {
                return number!.Value;
            }

            var lengthUnit = unit is not null ? Length.GetUnit(unit) : Length.Unit.None;

            return new Length((float)number!.Value, lengthUnit).ToPixels(emFactor, remFactor, hundredPercent, fontAdjust);
        }

        /// <summary>
        /// Get the unit to use for the length, use default if no unit found in length string.
        /// </summary>
        private static (string? unit, double? value) GetUnit(string length, string? defaultUnit, out bool hasUnit)
        {
            var tokens = GetCssTokens(length);

            if (tokens is [UnitToken unitToken])
            {
                hasUnit = true;
                return (unitToken.Unit, unitToken.Value);
            }

            hasUnit = false;
            return (defaultUnit, null);
        }

        /// <summary>
        /// Check if the given color string value is valid.
        /// </summary>
        /// <param name="colorValue">color string value to parse</param>
        /// <returns>true - valid, false - invalid</returns>
        public bool IsColorValid(string colorValue)
        {
            return TryGetColor(colorValue, 0, colorValue.Length, out _);
        }

        /// <summary>
        /// Parses a color value in CSS style; e.g. #ff0000, red, rgb(255,0,0), rgb(100%, 0, 0)
        /// </summary>
        /// <param name="colorValue">color string value to parse</param>
        /// <returns>Color value</returns>
        public RColor GetActualColor(string colorValue)
        {
            TryGetColor(colorValue, 0, colorValue.Length, out var color);
            return color;
        }

        /// <summary>
        /// Parses a color value in CSS style; e.g. #ff0000, RED, RGB(255,0,0), RGB(100%, 0, 0)
        /// </summary>
        /// <param name="str">color substring value to parse</param>
        /// <param name="idx">substring start idx </param>
        /// <param name="length">substring length</param>
        /// <param name="color">return the parsed color</param>
        /// <returns>true - valid color, false - otherwise</returns>
        public bool TryGetColor(string str, int idx, int length, out RColor color)
        {
            try
            {
                if (!string.IsNullOrEmpty(str))
                {
                    return length switch
                    {
                        > 1 when str[idx] == '#' => GetColorByHex(str, idx, length, out color),
                        > 10 when CommonUtils.SubStringEquals(str, idx, 4, "rgb(") && str[length - 1] == ')' =>
                            GetColorByRgb(str, idx, length, out color),
                        > 13 when CommonUtils.SubStringEquals(str, idx, 5, "rgba(") && str[length - 1] == ')' =>
                            GetColorByRgba(str, idx, length, out color),
                        _ => GetColorByName(str, idx, length, out color)
                    };
                }
            }
            catch
            { }
            color = RColor.Black;
            return false;
        }

        /// <summary>
        /// Parses a border value in CSS style; e.g. 1px, 1, thin, thick, medium
        /// </summary>
        /// <param name="borderValue"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static double GetActualBorderWidth(string borderValue, CssBoxProperties b)
        {
            if (string.IsNullOrEmpty(borderValue))
            {
                return GetActualBorderWidth(CssConstants.Medium, b);
            }

            return borderValue switch
            {
                CssConstants.Thin => 1f,
                CssConstants.Medium => 2f,
                CssConstants.Thick => 4f,
                _ => Math.Abs(ParseLength(borderValue, 1, b))
            };
        }

        public string GetFontFamilyByName(string propValue)
        {
            int start = 0;
            while (start < propValue.Length)
            {
                while (char.IsWhiteSpace(propValue[start]) || propValue[start] == ',' || propValue[start] == '\'' || propValue[start] == '"')
                    start++;
                var end = propValue.IndexOf(',', start);
                if (end < 0)
                    end = propValue.Length;
                var adjEnd = end - 1;
                while (char.IsWhiteSpace(propValue[adjEnd]) || propValue[adjEnd] == '\'' || propValue[adjEnd] == '"')
                    adjEnd--;

                var font = propValue.Substring(start, adjEnd - start + 1);

                if (_adapter.IsFontExists(font))
                {
                    return font;
                }

                start = end;
            }

            return CssConstants.Inherit;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="propValue">the value of the property to parse</param>
        /// <returns>parsed value</returns>
        public static CssImage? GetImagePropertyValue(string propValue)
        {
            var tokens = GetCssTokens(propValue);

            var urlToken = tokens.OfType<UrlToken>().SingleOrDefault();

            return urlToken is not null ? new CssImage.Url(urlToken.Data) : null;
        }

        public static CssFontFace GetFontFacePropertyValue(string propValue)
        {
            var tokens = GetCssTokens(propValue);

            var urlToken = tokens.OfType<UrlToken>().SingleOrDefault();
            var formatToken = tokens.OfType<FunctionToken>().SingleOrDefault(x => x.Data == "format");
            var techToken = tokens.OfType<FunctionToken>().SingleOrDefault(x => x.Data == "tech");
            var localToken = tokens.OfType<FunctionToken>().SingleOrDefault(x => x.Data == "local");

            return new CssFontFace(urlToken?.Data, formatToken?.ArgumentTokens?.FirstOrDefault()?.Data, techToken?.ArgumentTokens?.FirstOrDefault()?.Data, localToken?.ArgumentTokens?.FirstOrDefault()?.Data);
        }

        public static List<Token> GetCssTokens(string propValue)
        {
            var lexer = new Lexer(propValue);

            List<Token> tokens = [];

            Token token;

            do
            {
                token = lexer.Get();

                if (token.Type != TokenType.EndOfFile && token.Type != TokenType.Whitespace)
                {
                    tokens.Add(token);
                }

            } while (token.Type != TokenType.EndOfFile);

            return tokens;
        }

        public static string GetFontFaceFamilyName(string propValue)
        {
            var tokens = GetCssTokens(propValue);

            if (tokens is [StringToken stringToken])
            {
                return stringToken.Data;
            }

            return propValue;
        }

        /// <summary>
        /// Parses a CSS <c>transform</c> value (a space-separated list of 2D and/or 3D transform
        /// functions) together with <c>transform-origin</c>, and reduces the result to a single
        /// 2D affine matrix suitable for painting via a PDF content-stream 'cm' operator.
        /// </summary>
        /// <remarks>
        /// 3D functions are composed as a genuine 4x4 matrix (see <see cref="BuildFunctionMatrix"/>) and then
        /// projected onto the box's own z=0 plane - this projection is mathematically exact (the flattened
        /// result is a true 2D affine transform, with no approximation), see <see cref="ProjectTo2D"/>.
        /// <c>perspective()</c> is not supported (see docs/html-css-support.md) and is ignored like any other
        /// unrecognized function name, contributing identity.
        /// </remarks>
        public static RMatrix ParseTransform(string transformValue, string transformOriginValue, CssBoxProperties box)
        {
            var built = BuildFinal4(transformValue, transformOriginValue, box);
            if (built is not { } b)
                return RMatrix.Identity;

            var epsilonX = Math.Max(box.ActualWidth / 2, 1);
            var epsilonY = Math.Max(box.ActualHeight / 2, 1);
            return ProjectTo2D(b.Final4, b.Ox, b.Oy, epsilonX, epsilonY);
        }

        private readonly record struct Final4Result(Matrix4x4 Final4, double Ox, double Oy);

        /// <summary>
        /// Shared core of transform parsing: tokenizes, composes the per-function 4x4 matrices (see
        /// <see cref="BuildFunctionMatrix"/>) in CSS's last-written-applied-first order, and wraps the result
        /// around the box-local transform-origin. Returns null for "none"/empty/unparsable input.
        /// </summary>
        private static Final4Result? BuildFinal4(string transformValue, string transformOriginValue, CssBoxProperties box)
        {
            if (string.IsNullOrWhiteSpace(transformValue) ||
                string.Equals(transformValue.Trim(), CssConstants.None, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            List<Token> tokens;
            try
            {
                tokens = GetCssTokens(transformValue);
            }
            catch
            {
                return null;
            }

            var functionTokens = tokens.OfType<FunctionToken>().ToList();
            if (functionTokens.Count == 0)
                return null;

            var combined = Matrix4x4.Identity;
            var hasAny = false;

            foreach (var funcToken in functionTokens)
            {
                Matrix4x4? functionMatrix;
                try
                {
                    functionMatrix = BuildFunctionMatrix(funcToken, box);
                }
                catch
                {
                    functionMatrix = null;
                }

                if (functionMatrix is not { } m)
                    continue;

                // Last-written function is applied first (innermost); first-written applied last (outermost).
                combined = m * combined;
                hasAny = true;
            }

            if (!hasAny)
                return null;

            // Origin is resolved relative to the box's own top-left corner treated as local (0, 0).
            // This matrix is cached and computed once (see CssBoxProperties.ActualTransformMatrix),
            // so it must stay position-independent - the box's actual page position can vary across
            // repeated paint passes (e.g. pagination) and is re-applied at paint time instead, via
            // RMatrix.RebaseOrigin.
            var (ox, oy, oz) = ParseTransformOrigin(transformOriginValue, box);

            var final4 =
                Matrix4x4.CreateTranslation((float)-ox, (float)-oy, (float)-oz) *
                combined *
                Matrix4x4.CreateTranslation((float)ox, (float)oy, (float)oz);

            return new Final4Result(final4, ox, oy);
        }

        /// <summary>
        /// Builds the 4x4 matrix for a single CSS transform function. Returns null for unrecognized functions
        /// (which contribute identity - i.e. are silently skipped rather than invalidating the whole declaration).
        /// </summary>
        private static Matrix4x4? BuildFunctionMatrix(FunctionToken funcToken, CssBoxProperties box)
        {
            var name = funcToken.Data;
            var args = funcToken.ArgumentTokens.ToList();

            double LengthArg(int index, double hundredPercent) =>
                index < args.Count ? ParseLength(SingleTokenText(args[index]), hundredPercent, box) : 0;

            float NumberArg(int index)
            {
                if (index >= args.Count) return 0f;

                var direct = args[index].ToSingle();
                if (direct.HasValue) return direct.Value;

                // A calc()-family argument is a FunctionToken, not a NumberToken, so ToSingle() above
                // can't see it - fall back to evaluating it directly. hundredPercent is a don't-care here
                // (1) since a Number-category calc() can never contain a percentage leaf.
                if (args[index] is [FunctionToken fn] && CalcParser.IsCalcFamily(fn.Data))
                {
                    var node = CalcParser.Parse(fn);
                    var context = new CalcContext(1, box.GetEmHeight(), box.GetRemHeight(), false);
                    var value = node is not null ? CalcEvaluator.Evaluate(node, context) : null;
                    return (float?)value ?? 0f;
                }

                return 0f;
            }

            float AngleArg(int index) =>
                index < args.Count ? (args[index].ToAngle()?.ToRadian() ?? 0f) : 0f;

            if (Named(name, FunctionNames.Translate))
            {
                var tx = LengthArg(0, box.ActualWidth);
                var ty = args.Count > 1 ? LengthArg(1, box.ActualHeight) : 0;
                return Matrix4x4.CreateTranslation((float)tx, (float)ty, 0);
            }
            if (Named(name, FunctionNames.TranslateX))
                return Matrix4x4.CreateTranslation((float)LengthArg(0, box.ActualWidth), 0, 0);
            if (Named(name, FunctionNames.TranslateY))
                return Matrix4x4.CreateTranslation(0, (float)LengthArg(0, box.ActualHeight), 0);
            if (Named(name, FunctionNames.TranslateZ))
                return Matrix4x4.CreateTranslation(0, 0, (float)LengthArg(0, 0));
            if (Named(name, FunctionNames.Translate3d))
            {
                var tx = LengthArg(0, box.ActualWidth);
                var ty = LengthArg(1, box.ActualHeight);
                var tz = LengthArg(2, 0);
                return Matrix4x4.CreateTranslation((float)tx, (float)ty, (float)tz);
            }

            if (Named(name, FunctionNames.Scale))
            {
                var sx = NumberArg(0);
                var sy = args.Count > 1 ? NumberArg(1) : sx;
                return Matrix4x4.CreateScale(sx, sy, 1);
            }
            if (Named(name, FunctionNames.ScaleX))
                return Matrix4x4.CreateScale(NumberArg(0), 1, 1);
            if (Named(name, FunctionNames.ScaleY))
                return Matrix4x4.CreateScale(1, NumberArg(0), 1);
            if (Named(name, FunctionNames.ScaleZ))
                return Matrix4x4.CreateScale(1, 1, NumberArg(0));
            if (Named(name, FunctionNames.Scale3d))
                return Matrix4x4.CreateScale(NumberArg(0), NumberArg(1), NumberArg(2));

            if (Named(name, FunctionNames.Rotate) || Named(name, FunctionNames.RotateZ))
                return Matrix4x4.CreateRotationZ(AngleArg(0));
            if (Named(name, FunctionNames.RotateX))
                return Matrix4x4.CreateRotationX(AngleArg(0));
            if (Named(name, FunctionNames.RotateY))
                return Matrix4x4.CreateRotationY(AngleArg(0));
            if (Named(name, FunctionNames.Rotate3d))
            {
                var axis = new Vector3(NumberArg(0), NumberArg(1), NumberArg(2));
                if (axis.LengthSquared() < 1e-12f)
                    return Matrix4x4.Identity;
                return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), AngleArg(3));
            }

            if (Named(name, FunctionNames.SkewX))
                return new Matrix4x4(1, 0, 0, 0, MathF.Tan(AngleArg(0)), 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
            if (Named(name, FunctionNames.SkewY))
                return new Matrix4x4(1, MathF.Tan(AngleArg(0)), 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
            if (Named(name, FunctionNames.Skew))
            {
                var ax = AngleArg(0);
                var ay = args.Count > 1 ? AngleArg(1) : 0f;
                return new Matrix4x4(1, MathF.Tan(ay), 0, 0, MathF.Tan(ax), 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
            }

            if (Named(name, FunctionNames.Matrix))
            {
                if (args.Count < 6) return null;
                float a = NumberArg(0), b = NumberArg(1), c = NumberArg(2),
                      d = NumberArg(3), e = NumberArg(4), f = NumberArg(5);
                return new Matrix4x4(
                    a, b, 0, 0,
                    c, d, 0, 0,
                    0, 0, 1, 0,
                    e, f, 0, 1);
            }
            if (Named(name, FunctionNames.Matrix3d))
            {
                if (args.Count < 16) return null;
                var v = new float[16];
                for (var i = 0; i < 16; i++) v[i] = NumberArg(i);
                // CSS lists matrix3d() arguments column-major; System.Numerics.Matrix4x4 is row-major
                // with translation in row 4. These two conventions are transposes of each other, so
                // reading the 16 source arguments straight into the row-major constructor (in order)
                // yields the mathematically correct matrix - verified against translate3d() equivalence.
                return new Matrix4x4(
                    v[0], v[1], v[2], v[3],
                    v[4], v[5], v[6], v[7],
                    v[8], v[9], v[10], v[11],
                    v[12], v[13], v[14], v[15]);
            }

            // Unrecognized / unsupported (e.g. perspective(), future functions) -> identity, contributes nothing.
            return null;
        }

        private static bool Named(string data, string functionName) =>
            string.Equals(data, functionName, StringComparison.OrdinalIgnoreCase);

        private static string SingleTokenText(List<Token> group) =>
            group.Count > 0 ? group[0].ToValue() : "0";

        /// <summary>
        /// Parses transform-origin: 1-3 values (X, Y, optional Z). X/Y accept length/percentage/keywords
        /// (resolved against the box's own border-box size), Z is a plain length (no percentage), default 0.
        /// </summary>
        private static (double X, double Y, double Z) ParseTransformOrigin(string value, CssBoxProperties box)
        {
            if (string.IsNullOrWhiteSpace(value))
                return (box.ActualWidth / 2, box.ActualHeight / 2, 0);

            var parts = value.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);

            double ResolveX(string token) => token switch
            {
                "left" => 0,
                "right" => box.ActualWidth,
                "center" => box.ActualWidth / 2,
                "top" or "bottom" => box.ActualWidth / 2, // keyword belongs to the other axis; fall back to center
                _ => ParseLength(token, box.ActualWidth, box)
            };

            double ResolveY(string token) => token switch
            {
                "top" => 0,
                "bottom" => box.ActualHeight,
                "center" => box.ActualHeight / 2,
                "left" or "right" => box.ActualHeight / 2,
                _ => ParseLength(token, box.ActualHeight, box)
            };

            var ox = box.ActualWidth / 2;
            var oy = box.ActualHeight / 2;
            var oz = 0d;

            if (parts.Length >= 1) ox = ResolveX(parts[0].ToLowerInvariant());
            if (parts.Length >= 2) oy = ResolveY(parts[1].ToLowerInvariant());
            if (parts.Length >= 3) oz = ParseLength(parts[2], 0, box);

            // Handle the "top"/"bottom" (or "left"/"right") appearing as the *first* token, e.g. "top center".
            if (parts.Length >= 1)
            {
                var first = parts[0].ToLowerInvariant();
                if (first is "top" or "bottom")
                {
                    oy = first == "top" ? 0 : box.ActualHeight;
                    ox = box.ActualWidth / 2;
                    if (parts.Length >= 2) ox = ResolveX(parts[1].ToLowerInvariant());
                }
            }

            return (ox, oy, oz);
        }

        /// <summary>
        /// Projects a 4x4 transform (applied to the box's own z=0 plane) down to a 2D affine RMatrix,
        /// via numeric differentiation around the transform-origin point (Ox, Oy). This is exact when
        /// the projection is linear (i.e. no perspective() in the chain - w stays constant across the
        /// plane, so the secant used here equals the true tangent everywhere), and a local approximation
        /// otherwise.
        /// </summary>
        /// <param name="epsilonX">
        /// Probe distance along X used to estimate the derivative there - scaled to the box's own half-width
        /// (rather than a small constant) so that, when perspective() is involved, the fitted line reflects
        /// the foreshortening actually spanned by the box's own extent instead of an infinitesimal
        /// neighbourhood around the origin (which would barely differ from the no-perspective case for
        /// typical box sizes and perspective distances, making perspective() look like a no-op).
        /// </param>
        private static RMatrix ProjectTo2D(Matrix4x4 m, double ox, double oy, double epsilonX, double epsilonY)
        {
            (double X, double Y)? Project(double x, double y)
            {
                var p = Vector4.Transform(new Vector4((float)x, (float)y, 0, 1), m);
                if (MathF.Abs(p.W) < 1e-6f)
                    return null;
                return (p.X / p.W, p.Y / p.W);
            }

            var p0 = Project(ox, oy);
            if (p0 is not { } origin)
                return RMatrix.Identity;

            var px = Project(ox + epsilonX, oy);
            var py = Project(ox, oy + epsilonY);
            if (px is not { } pxv || py is not { } pyv)
                return RMatrix.Identity;

            var m11 = (pxv.X - origin.X) / epsilonX;
            var m12 = (pxv.Y - origin.Y) / epsilonX;
            var m21 = (pyv.X - origin.X) / epsilonY;
            var m22 = (pyv.Y - origin.Y) / epsilonY;

            var offsetX = origin.X - (ox * m11 + oy * m21);
            var offsetY = origin.Y - (ox * m12 + oy * m22);

            return new RMatrix(m11, m12, m21, m22, offsetX, offsetY);
        }

        private ParsedLinearGradient? ParseLinearGradient(string value)
        {
            var tokens = GetCssTokens(value);

            var funcToken = tokens.OfType<FunctionToken>().FirstOrDefault(t =>
                string.Equals(t.Data, FunctionNames.LinearGradient, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Data, FunctionNames.RepeatingLinearGradient, StringComparison.OrdinalIgnoreCase));

            if (funcToken == null)
                return null;

            bool isRepeating = string.Equals(funcToken.Data, FunctionNames.RepeatingLinearGradient, StringComparison.OrdinalIgnoreCase);

            var args = funcToken.ArgumentTokens.ToList();
            if (args.Count == 0)
                return null;

            double angleRad = Math.PI; // default: 180deg = top to bottom
            int stopOffset = 0;

            var firstGroup = args[0];
            var firstIdents = firstGroup.Where(t => t.Type == TokenType.Ident).Select(t => t.Data.ToLowerInvariant()).ToList();

            // Phase D: detect "in <colorspace> [<hue-method>]"
            var (linearColorSpace, linearHueMethod, nextIdentIdx) = TryParseInColorSpace(firstIdents);
            if (nextIdentIdx >= 0)
                stopOffset = 1; // first group is a modifier group

            // Direction keyword or angle (may follow the "in" clause in the same first group)
            var dirIdents = nextIdentIdx >= 0 ? firstIdents.Skip(nextIdentIdx).ToList() : firstIdents;
            if (dirIdents.Count > 0 && dirIdents[0] == "to")
            {
                // keyword direction: "to right", "to bottom left", etc.
                angleRad = SideKeywordsToAngleRad(dirIdents.Skip(1).ToList());
                stopOffset = 1;
            }
            else
            {
                var angle = firstGroup.ToAngle();
                if (angle.HasValue)
                {
                    angleRad = angle.Value.ToRadian();
                    stopOffset = 1;
                }
                // else no angle token, stopOffset stays 0 (unless set by "in" detection above)
            }

            var stopGroups = args.Skip(stopOffset).ToList();
            if (stopGroups.Count < 2)
                return null;

            var stops = new List<(RColor? Color, Length? Position, bool IsHint)>();

            foreach (var group in stopGroups)
            {
                var items = group.ToItems();
                if (items.Count == 0)
                    continue;

                Length? position1 = null;
                Length? position2 = null;
                int colorItemCount = items.Count;

                // Last item may be a length/percent position
                var lastItem = items[items.Count - 1];
                var pv = lastItem.ToDistance();
                if (pv.HasValue)
                {
                    position1 = pv.Value;
                    colorItemCount--;

                    // A2: check second-to-last for two-position shorthand (e.g. "red 0 50%")
                    if (colorItemCount > 0)
                    {
                        var pv2 = items[colorItemCount - 1].ToDistance();
                        if (pv2.HasValue)
                        {
                            position2 = position1;          // last position is the second one
                            position1 = pv2.Value;          // second-to-last is the first one
                            colorItemCount--;
                        }
                    }
                }

                if (colorItemCount == 0)
                {
                    // A3: hint — bare position with no color
                    if (position1.HasValue && !position2.HasValue)
                        stops.Add((null, position1, IsHint: true));
                    continue;
                }

                var colorText = BuildColorText(items.Take(colorItemCount));
                if (string.IsNullOrWhiteSpace(colorText))
                    continue;

                var color = GetActualColor(colorText);
                stops.Add((color, position1, IsHint: false));
                if (position2.HasValue)
                    stops.Add((color, position2, IsHint: false));
            }

            if (stops.Count(s => !s.IsHint) < 2)
                return null;

            return new ParsedLinearGradient
            {
                AngleRad = angleRad,
                Stops = stops.ToArray(),
                IsRepeating = isRepeating,
                ColorSpace = linearColorSpace,
                HueMethod = linearHueMethod,
            };
        }

        private ParsedRadialGradient? ParseRadialGradient(string value)
        {
            var tokens = GetCssTokens(value);

            var funcToken = tokens.OfType<FunctionToken>().FirstOrDefault(t =>
                string.Equals(t.Data, FunctionNames.RadialGradient, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Data, FunctionNames.RepeatingRadialGradient, StringComparison.OrdinalIgnoreCase));

            if (funcToken == null)
                return null;

            bool isRepeating = string.Equals(funcToken.Data, FunctionNames.RepeatingRadialGradient, StringComparison.OrdinalIgnoreCase);

            var args = funcToken.ArgumentTokens.ToList();
            if (args.Count == 0)
                return null;

            bool isCircle = false;
            double centerX = 0.5, centerY = 0.5;
            int stopOffset = 0;
            Length? explicitRadiusX = null, explicitRadiusY = null;

            var firstGroup = args[0];
            var firstGroupItems = firstGroup.ToItems();
            var firstIdents = firstGroupItems.SelectMany(i => i)
                                            .Where(t => t.Type == TokenType.Ident)
                                            .Select(t => t.Data.ToLowerInvariant())
                                            .ToList();

            // Phase D: detect "in <colorspace> [<hue-method>]"
            var (radialColorSpace, radialHueMethod, _) = TryParseInColorSpace(firstIdents);

            // A4: scan items before "at" for explicit length sizes (e.g. "20px" or "20px 30px")
            // Guard: stop collecting once we see an ident that is not a known gradient modifier
            // keyword, because it must be a CSS color name (e.g. "red" in "red 0 8px" followed
            // by stop positions that would otherwise be misread as explicit radii).
            static bool IsRadialModifierIdent(string id) =>
                string.Equals(id, "circle", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "ellipse", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "in", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "closest-side", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "farthest-corner", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "closest-corner", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "farthest-side", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "srgb", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "srgb-linear", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "oklab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "oklch", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "lab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "lch", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "hsl", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "hwb", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "display-p3", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "xyz", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "xyz-d50", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "xyz-d65", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "shorter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "longer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "increasing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "decreasing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "hue", StringComparison.OrdinalIgnoreCase);

            var explicitSizeLengths = new List<Length>();
            bool seenNonModifierIdent = false;
            foreach (var item in firstGroupItems)
            {
                if (item.Count == 1 && item[0].Type == TokenType.Ident)
                {
                    var id = item[0].Data;
                    if (id.Equals("at", StringComparison.OrdinalIgnoreCase)) break;
                    if (!IsRadialModifierIdent(id)) seenNonModifierIdent = true;
                    continue;
                }
                // A CSS color function (rgb(), rgba(), hsl(), etc.) means this group is a color stop.
                if (item.Count == 1 && item[0].Type == TokenType.Function)
                {
                    seenNonModifierIdent = true;
                    continue;
                }
                if (!seenNonModifierIdent)
                {
                    var dist = item.ToDistance();
                    if (dist.HasValue) explicitSizeLengths.Add(dist.Value);
                }
            }

            bool isShapeOrPositionGroup =
                firstIdents.Contains("in") ||
                firstIdents.Contains("circle") || firstIdents.Contains("ellipse") ||
                firstIdents.Contains("at") || firstIdents.Contains("closest-side") ||
                firstIdents.Contains("farthest-corner") || firstIdents.Contains("closest-corner") ||
                firstIdents.Contains("farthest-side") || explicitSizeLengths.Count > 0;

            var sizeKeyword = RadialGradientSize.FarthestCorner;

            if (isShapeOrPositionGroup)
            {
                stopOffset = 1;
                isCircle = firstIdents.Contains("circle");

                if (firstIdents.Contains("closest-corner")) sizeKeyword = RadialGradientSize.ClosestCorner;
                else if (firstIdents.Contains("farthest-side")) sizeKeyword = RadialGradientSize.FarthestSide;
                else if (firstIdents.Contains("closest-side")) sizeKeyword = RadialGradientSize.ClosestSide;

                // A4: store explicit size lengths
                if (explicitSizeLengths.Count >= 1)
                {
                    explicitRadiusX = explicitSizeLengths[0];
                    explicitRadiusY = explicitSizeLengths.Count >= 2 ? explicitSizeLengths[1] : explicitSizeLengths[0];
                }

                if (firstIdents.Contains("at"))
                {
                    bool inPosition = false;
                    int posCount = 0;

                    foreach (var item in firstGroup.ToItems())
                    {
                        if (item.Count == 1 && item[0].Type == TokenType.Ident)
                        {
                            string ident = item[0].Data.ToLowerInvariant();
                            if (!inPosition)
                            {
                                if (ident == "at") inPosition = true;
                                continue;
                            }
                            double pos = ident switch
                            {
                                "left" => 0.0,
                                "right" => 1.0,
                                "top" => 0.0,
                                "bottom" => 1.0,
                                "center" => 0.5,
                                _ => 0.5
                            };
                            if (posCount == 0) centerX = pos;
                            else if (posCount == 1) centerY = pos;
                            posCount++;
                        }
                        else if (inPosition)
                        {
                            var dist = item.ToDistance();
                            if (dist.HasValue && dist.Value.Type == Length.Unit.Percent)
                            {
                                double pos = dist.Value.Value / 100.0;
                                if (posCount == 0) centerX = pos;
                                else if (posCount == 1) centerY = pos;
                                posCount++;
                            }
                        }
                    }
                }
            }

            var stopGroups = args.Skip(stopOffset).ToList();
            if (stopGroups.Count < 2)
                return null;

            var stops = new List<(RColor? Color, Length? Position, bool IsHint)>();

            foreach (var group in stopGroups)
            {
                var items = group.ToItems();
                if (items.Count == 0)
                    continue;

                Length? position1 = null;
                Length? position2 = null;
                int colorItemCount = items.Count;

                var lastItem = items[items.Count - 1];
                var pv = lastItem.ToDistance();
                if (pv.HasValue)
                {
                    position1 = pv.Value;
                    colorItemCount--;

                    // A2: two-position shorthand
                    if (colorItemCount > 0)
                    {
                        var pv2 = items[colorItemCount - 1].ToDistance();
                        if (pv2.HasValue)
                        {
                            position2 = position1;
                            position1 = pv2.Value;
                            colorItemCount--;
                        }
                    }
                }

                if (colorItemCount == 0)
                {
                    // A3: hint
                    if (position1.HasValue && !position2.HasValue)
                        stops.Add((null, position1, IsHint: true));
                    continue;
                }

                var colorText = BuildColorText(items.Take(colorItemCount));
                if (string.IsNullOrWhiteSpace(colorText))
                    continue;

                var color = GetActualColor(colorText);
                stops.Add((color, position1, IsHint: false));
                if (position2.HasValue)
                    stops.Add((color, position2, IsHint: false));
            }

            if (stops.Count(s => !s.IsHint) < 2)
                return null;

            return new ParsedRadialGradient
            {
                IsCircle = isCircle,
                CenterX = centerX,
                CenterY = centerY,
                Size = sizeKeyword,
                ExplicitRadiusX = explicitRadiusX,
                ExplicitRadiusY = explicitRadiusY,
                Stops = stops.ToArray(),
                IsRepeating = isRepeating,
                ColorSpace = radialColorSpace,
                HueMethod = radialHueMethod,
            };
        }

        private ParsedConicGradient? ParseConicGradient(string value)
        {
            var tokens = GetCssTokens(value);

            var funcToken = tokens.OfType<FunctionToken>().FirstOrDefault(t =>
                string.Equals(t.Data, FunctionNames.ConicGradient, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Data, FunctionNames.RepeatingConicGradient, StringComparison.OrdinalIgnoreCase));

            if (funcToken == null)
                return null;

            bool isRepeating = string.Equals(funcToken.Data, FunctionNames.RepeatingConicGradient, StringComparison.OrdinalIgnoreCase);

            var args = funcToken.ArgumentTokens.ToList();
            if (args.Count == 0)
                return null;

            double fromAngleRad = 0.0;
            double centerX = 0.5, centerY = 0.5;
            int stopOffset = 0;

            // First group: optionally "in <colorspace>", "from <angle>", and/or "at <x> <y>"
            var firstGroup = args[0];
            var firstIdents = firstGroup.Where(t => t.Type == TokenType.Ident)
                                        .Select(t => t.Data.ToLowerInvariant()).ToList();

            // Phase D: detect "in <colorspace> [<hue-method>]"
            var (conicColorSpace, conicHueMethod, _) = TryParseInColorSpace(firstIdents);

            bool hasFrom = firstIdents.Contains("from");
            bool hasAt   = firstIdents.Contains("at");
            bool hasIn   = firstIdents.Contains("in");

            if (hasFrom || hasAt || hasIn)
            {
                stopOffset = 1;
                var items = firstGroup.ToItems();
                bool inFrom = false, inAt = false;
                int atPosCount = 0;

                foreach (var item in items)
                {
                    if (item.Count == 1 && item[0].Type == TokenType.Ident)
                    {
                        string kw = item[0].Data.ToLowerInvariant();
                        if (kw == "from") { inFrom = true; inAt = false; continue; }
                        if (kw == "at")   { inAt   = true; inFrom = false; continue; }

                        if (inAt)
                        {
                            double kp = kw switch { "left" => 0.0, "right" => 1.0, "top" => 0.0, "bottom" => 1.0, _ => 0.5 };
                            if (atPosCount == 0) centerX = kp;
                            else if (atPosCount == 1) centerY = kp;
                            atPosCount++;
                        }
                        continue;
                    }

                    if (inFrom)
                    {
                        var ang = item.ToAngle();
                        if (ang.HasValue) fromAngleRad = ang.Value.ToRadian();
                        inFrom = false;
                        continue;
                    }

                    if (inAt)
                    {
                        var dist = item.ToDistance();
                        if (dist.HasValue && dist.Value.Type == Length.Unit.Percent)
                        {
                            double pos = dist.Value.Value / 100.0;
                            if (atPosCount == 0) centerX = pos;
                            else if (atPosCount == 1) centerY = pos;
                            atPosCount++;
                        }
                    }
                }
            }

            var stopGroups = args.Skip(stopOffset).ToList();
            if (stopGroups.Count < 2)
                return null;

            var stops = new List<(RColor? Color, double? PositionRad, bool IsHint)>();

            foreach (var group in stopGroups)
            {
                var items = group.ToItems();
                if (items.Count == 0) continue;

                double? position1 = null, position2 = null;
                int colorItemCount = items.Count;

                // Last item may be an angle/percent position
                var lastItem = items[colorItemCount - 1];
                double? pv = TryParseConicAngle(lastItem);
                if (pv.HasValue)
                {
                    position1 = pv.Value;
                    colorItemCount--;

                    // Two-position shorthand
                    if (colorItemCount > 0)
                    {
                        double? pv2 = TryParseConicAngle(items[colorItemCount - 1]);
                        if (pv2.HasValue)
                        {
                            position2 = position1;
                            position1 = pv2.Value;
                            colorItemCount--;
                        }
                    }
                }

                if (colorItemCount == 0)
                {
                    if (position1.HasValue && !position2.HasValue)
                        stops.Add((null, position1, IsHint: true));
                    continue;
                }

                var colorText = BuildColorText(items.Take(colorItemCount));
                if (string.IsNullOrWhiteSpace(colorText)) continue;

                var color = GetActualColor(colorText);
                stops.Add((color, position1, IsHint: false));
                if (position2.HasValue)
                    stops.Add((color, position2, IsHint: false));
            }

            if (stops.Count(s => !s.IsHint) < 2)
                return null;

            return new ParsedConicGradient
            {
                FromAngleRad = fromAngleRad,
                CenterX = centerX,
                CenterY = centerY,
                Stops = stops.ToArray(),
                IsRepeating = isRepeating,
                ColorSpace = conicColorSpace,
                HueMethod = conicHueMethod,
            };
        }

        public CssImage? ParseImage(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;

            var tokens = GetCssTokens(value);

            var urlToken = tokens.OfType<UrlToken>().FirstOrDefault();
            if (urlToken != null)
                return new CssImage.Url(urlToken.Data);

            var funcToken = tokens.OfType<FunctionToken>().FirstOrDefault();
            if (funcToken == null) return null;

            var name = funcToken.Data;
            if (name.Equals(FunctionNames.LinearGradient, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(FunctionNames.RepeatingLinearGradient, StringComparison.OrdinalIgnoreCase))
            {
                var g = ParseLinearGradient(value);
                return g != null ? new CssImage.LinearGradient(g) : null;
            }

            if (name.Equals(FunctionNames.RadialGradient, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(FunctionNames.RepeatingRadialGradient, StringComparison.OrdinalIgnoreCase))
            {
                var g = ParseRadialGradient(value);
                return g != null ? new CssImage.RadialGradient(g) : null;
            }

            if (name.Equals(FunctionNames.ConicGradient, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(FunctionNames.RepeatingConicGradient, StringComparison.OrdinalIgnoreCase))
            {
                var g = ParseConicGradient(value);
                return g != null ? new CssImage.ConicGradient(g) : null;
            }

            return null;
        }

        public IReadOnlyList<CssImage>? ParseImages(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;

            var result = new List<CssImage>();
            foreach (var segment in SplitTopLevelCommas(value))
            {
                var image = ParseImage(segment.Trim());
                if (image != null) result.Add(image);
            }

            return result.Count > 0 ? result : null;
        }

        internal static IEnumerable<string> SplitTopLevelCommas(string value)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    yield return value.Substring(start, i - start);
                    start = i + 1;
                }
            }
            yield return value.Substring(start);
        }

        /// <summary>
        /// Splits a string on top-level (paren-depth-zero) whitespace, e.g. for the two-value form of
        /// border-*-radius or the gap shorthand. Needed because a calc()/min()/max()/clamp() value can
        /// itself contain spaces (e.g. "calc(10px + 2px) 5px"), which a naive Split(' ') would corrupt.
        /// </summary>
        public static IEnumerable<string> SplitTopLevelWhitespace(string value)
        {
            int depth = 0, start = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (char.IsWhiteSpace(c) && depth == 0)
                {
                    if (i > start) yield return value.Substring(start, i - start);
                    start = i + 1;
                }
            }
            if (start < value.Length) yield return value.Substring(start);
        }

        private static double? TryParseConicAngle(List<Token> item)
        {
            var angle = item.ToAngle();
            if (angle.HasValue) return angle.Value.ToRadian();

            var dist = item.ToDistance();
            if (dist.HasValue && dist.Value.Type == Length.Unit.Percent)
                return dist.Value.Value / 100.0 * (2.0 * Math.PI);

            // In CSS, bare 0 is a valid zero value for any dimension including angles.
            if (item.Count == 1 && item[0].Type == TokenType.Number && ((NumberToken)item[0]).Value == 0f)
                return 0.0;

            return null;
        }

        private static GradientColorSpace ParseColorSpaceName(string name) => name switch
        {
            "srgb" => GradientColorSpace.Srgb,
            "srgb-linear" => GradientColorSpace.SrgbLinear,
            "display-p3" => GradientColorSpace.DisplayP3,
            "lab" => GradientColorSpace.Lab,
            "oklab" => GradientColorSpace.Oklab,
            "xyz" or "xyz-d65" => GradientColorSpace.XyzD65,
            "xyz-d50" => GradientColorSpace.XyzD50,
            "hsl" => GradientColorSpace.Hsl,
            "hwb" => GradientColorSpace.Hwb,
            "lch" => GradientColorSpace.Lch,
            "oklch" => GradientColorSpace.Oklch,
            _ => GradientColorSpace.Srgb,
        };

        private static bool IsPolarColorSpace(GradientColorSpace cs) =>
            cs is GradientColorSpace.Hsl or GradientColorSpace.Hwb
                or GradientColorSpace.Lch or GradientColorSpace.Oklch;

        private static HueInterpolationMethod ParseHueMethod(List<string> idents, int startIdx)
        {
            for (int i = startIdx; i + 1 < idents.Count; i++)
            {
                if (idents[i + 1] == "hue")
                    return idents[i] switch
                    {
                        "longer" => HueInterpolationMethod.Longer,
                        "increasing" => HueInterpolationMethod.Increasing,
                        "decreasing" => HueInterpolationMethod.Decreasing,
                        _ => HueInterpolationMethod.Shorter,
                    };
            }
            return HueInterpolationMethod.Shorter;
        }

        // Detects "in <colorspace> [<hue-method>]" in an ident list; returns (colorSpace, hueMethod)
        // and the index of the first ident after the "in" clause (or -1 if "in" not found).
        private static (GradientColorSpace ColorSpace, HueInterpolationMethod HueMethod, int NextIdx)
            TryParseInColorSpace(List<string> idents)
        {
            int inIdx = idents.IndexOf("in");
            if (inIdx < 0 || inIdx + 1 >= idents.Count)
                return (GradientColorSpace.Srgb, HueInterpolationMethod.Shorter, -1);

            var cs = ParseColorSpaceName(idents[inIdx + 1]);
            var hue = HueInterpolationMethod.Shorter;
            int nextIdx = inIdx + 2;
            if (IsPolarColorSpace(cs))
            {
                hue = ParseHueMethod(idents, nextIdx);
                // consume up to 2 extra idents for hue method ("shorter hue" etc.)
                if (nextIdx + 1 < idents.Count && idents[nextIdx + 1] == "hue")
                    nextIdx += 2;
            }
            return (cs, hue, nextIdx);
        }

        private static double SideKeywordsToAngleRad(List<string> sides)
        {
            bool hasTop = sides.Contains("top");
            bool hasBottom = sides.Contains("bottom");
            bool hasLeft = sides.Contains("left");
            bool hasRight = sides.Contains("right");

            if (hasTop && hasRight) return Math.PI / 4;        // 45deg
            if (hasBottom && hasRight) return 3 * Math.PI / 4; // 135deg
            if (hasBottom && hasLeft) return 5 * Math.PI / 4;  // 225deg
            if (hasTop && hasLeft) return 7 * Math.PI / 4;     // 315deg
            if (hasTop) return 0;                               // 0deg
            if (hasRight) return Math.PI / 2;                   // 90deg
            if (hasBottom) return Math.PI;                      // 180deg
            if (hasLeft) return 3 * Math.PI / 2;               // 270deg

            return Math.PI; // default
        }

        private static string BuildColorText(IEnumerable<IEnumerable<Token>> itemGroups)
        {
            var sb = new StringBuilder();
            foreach (var group in itemGroups)
            {
                sb.Append(group.ToText());
            }
            return sb.ToString().Trim();
        }

        #region Private methods

        /// <summary>
        /// Get color by parsing given hex value color string (#A28B34).
        /// </summary>
        /// <returns>true - valid color, false - otherwise</returns>
        private static bool GetColorByHex(string str, int idx, int length, out RColor color)
        {
            int r = -1;
            int g = -1;
            int b = -1;
            if (length == 7)
            {
                r = ParseHexInt(str, idx + 1, 2);
                g = ParseHexInt(str, idx + 3, 2);
                b = ParseHexInt(str, idx + 5, 2);
            }
            else if (length == 4)
            {
                r = ParseHexInt(str, idx + 1, 1);
                r = r * 16 + r;
                g = ParseHexInt(str, idx + 2, 1);
                g = g * 16 + g;
                b = ParseHexInt(str, idx + 3, 1);
                b = b * 16 + b;
            }
            if (r > -1 && g > -1 && b > -1)
            {
                color = RColor.FromArgb(r, g, b);
                return true;
            }
            color = RColor.Empty;
            return false;
        }

        /// <summary>
        /// Get color by parsing given RGB value color string (RGB(255,180,90))
        /// </summary>
        /// <returns>true - valid color, false - otherwise</returns>
        private static bool GetColorByRgb(string str, int idx, int length, out RColor color)
        {
            int r = -1;
            int g = -1;
            int b = -1;

            if (length > 10)
            {
                int s = idx + 4;
                r = ParseIntAtIndex(str, ref s);
                if (s < idx + length)
                {
                    g = ParseIntAtIndex(str, ref s);
                }
                if (s < idx + length)
                {
                    b = ParseIntAtIndex(str, ref s);
                }
            }

            if (r > -1 && g > -1 && b > -1)
            {
                color = RColor.FromArgb(r, g, b);
                return true;
            }
            color = RColor.Empty;
            return false;
        }

        /// <summary>
        /// Get color by parsing given RGBA value color string (RGBA(255,180,90,180))
        /// </summary>
        /// <returns>true - valid color, false - otherwise</returns>
        private static bool GetColorByRgba(string str, int idx, int length, out RColor color)
        {
            int r = -1;
            int g = -1;
            int b = -1;
            double a = -1d;

            if (length > 13)
            {
                int s = idx + 5;
                r = ParseIntAtIndex(str, ref s);

                if (s < idx + length)
                {
                    g = ParseIntAtIndex(str, ref s);
                }
                if (s < idx + length)
                {
                    b = ParseIntAtIndex(str, ref s);
                }
                if (s < idx + length)
                {
                    a = ParseDoubleAtIndex(str, ref s);
                }
            }

            if (r > -1 && g > -1 && b > -1 && a >= 0d)
            {
                color = RColor.FromArgb((int)Math.Round(a * 255), r, g, b);
                return true;
            }
            color = RColor.Empty;
            return false;
        }

        /// <summary>
        /// Get color by given name, including .NET name.
        /// </summary>
        /// <returns>true - valid color, false - otherwise</returns>
        private bool GetColorByName(string str, int idx, int length, out RColor color)
        {
            color = _adapter.GetColor(str.Substring(idx, length));
            return color.A > 0;
        }

        /// <summary>
        /// Parse the given decimal number string to positive int value.<br/>
        /// Start at given <paramref name="startIdx"/>, ignore whitespaces and take
        /// as many digits as possible to parse to int.
        /// </summary>
        /// <param name="str">the string to parse</param>
        /// <param name="startIdx">the index to start parsing at</param>
        /// <returns>parsed int or 0</returns>
        private static int ParseIntAtIndex(string str, ref int startIdx)
        {
            int len = 0;
            while (char.IsWhiteSpace(str, startIdx))
                startIdx++;
            while (char.IsDigit(str, startIdx + len))
                len++;
            var val = ParseInt(str, startIdx, len);
            startIdx = startIdx + len + 1;
            return val;
        }

        private static double ParseDoubleAtIndex(string str, ref int startIdx)
        {
            int len = 0;
            while (startIdx < str.Length && char.IsWhiteSpace(str, startIdx))
                startIdx++;
            while (startIdx + len < str.Length && (char.IsDigit(str, startIdx + len) || str[startIdx + len] == '.'))
                len++;
            if (len < 1) { startIdx++; return -1d; }
            if (!double.TryParse(str.Substring(startIdx, len), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val))
                val = -1d;
            startIdx = startIdx + len + 1;
            return val;
        }

        /// <summary>
        /// Parse the given decimal number string to positive int value.
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>int value, -1 if not valid</returns>
        private static int ParseInt(string str, int idx, int length)
        {
            if (length < 1)
                return -1;

            int num = 0;
            for (int i = 0; i < length; i++)
            {
                int c = str[idx + i];
                if (!(c >= 48 && c <= 57))
                    return -1;

                num = num * 10 + c - 48;
            }
            return num;
        }

        /// <summary>
        /// Parse the given hex number string to positive int value.
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>int value, -1 if not valid</returns>
        private static int ParseHexInt(string str, int idx, int length)
        {
            if (length < 1)
                return -1;

            int num = 0;
            for (int i = 0; i < length; i++)
            {
                int c = str[idx + i];
                if (!(c >= 48 && c <= 57) && !(c >= 65 && c <= 70) && !(c >= 97 && c <= 102))
                    return -1;

                num = num * 16 + (c <= 57 ? c - 48 : (10 + c - (c <= 70 ? 65 : 97)));
            }
            return num;
        }

        #endregion
    }
}