using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Html.Animation
{
    /// <summary>
    /// Mixes two CSS values of the same property at a position between them (CSS Values 4 §10, "Combination of
    /// Values: Interpolation, Addition, and Accumulation"). Values travel through the cascade as text, so this
    /// works on text: both values are scanned into tokens, and when they have the same <em>shape</em> - the same
    /// keywords, functions and separators, differing only in their numbers and colours - each differing pair is
    /// mixed and the result written back as text for the ordinary property setter to parse.
    /// </summary>
    /// <remarks>
    /// That one rule covers the interpolable types a document uses: <c>&lt;number&gt;</c>, <c>&lt;length&gt;</c>,
    /// <c>&lt;percentage&gt;</c>, <c>&lt;angle&gt;</c>, <c>&lt;color&gt;</c>, and any list or function built from
    /// them (<c>transform</c> function lists, <c>filter</c>, <c>box-shadow</c>, <c>background-position</c>).
    /// Anything of a different shape is not interpolable and flips from one value to the other half way, as CSS
    /// Values 4 §10.1 specifies for discrete values. Where the two values have different shapes but the property
    /// defines a way to compare them - <c>none</c> against a <c>transform</c> or <c>filter</c> list, or against
    /// <c>visibility</c> - that is handled first.
    /// </remarks>
    internal static class CssValueInterpolator
    {
        private enum TokenKind { Number, Word, Color, Open, Close, Space, Punct, Text }

        /// <summary>
        /// One lexical piece of a value. A colour token has <c>Rgba</c> (the colour clipped to 8-bit sRGB, alpha 0 to 1; outside 0..255 for a <c>currentcolor</c>
        /// whose <c>color</c> is outside the gamut, see <see cref="CurrentColorToken"/>), <c>InOklab</c> (it takes a mix out of sRGB) and, for a <c>lab()</c>, <c>lch()</c>,
        /// <c>oklab()</c>, <c>oklch()</c> or <c>color-mix()</c>, <c>Oklab</c> (the coordinates it was written with, which can lie outside the sRGB gamut);
        /// <c>Oklab</c> being set does not imply <c>InOklab</c>.
        /// </summary>
        private readonly record struct Token(TokenKind Kind, string Raw, double Number = 0, string Unit = "", double[]? Rgba = null, bool InOklab = false, double[]? Oklab = null, int OklabMissing = 0);

        private static readonly HashSet<string> LengthUnits = new(StringComparer.OrdinalIgnoreCase)
        {
            "px", "pt", "pc", "in", "cm", "mm", "q", "em", "rem", "ex", "ch", "vw", "vh", "vmin", "vmax", "%"
        };

        /// <summary>
        /// Every function that is a colour. Scanned whole and resolved to sRGB, never as a function of plain numbers:
        /// a hue or a lab component mixed like a length gives the wrong colour (red to lime through the wrong hues). A
        /// colour the parser cannot resolve stays text and flips half way.
        /// </summary>
        private static readonly HashSet<string> ColorFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "rgb", "rgba", "hsl", "hsla", "hwb", "lab", "lch", "oklab", "oklch", "color", "color-mix"
        };

        private static readonly HashSet<string> SpacingProperties = new(StringComparer.OrdinalIgnoreCase) { "letter-spacing", "word-spacing" };

        // The opacities: a percentage is the number it stands for (CSS Color 4 section 3.2), which Interpolate puts in before mixing.
        private static readonly HashSet<string> OpacityProperties = new(StringComparer.OrdinalIgnoreCase) { "opacity", "fill-opacity", "stroke-opacity", "stop-opacity" };

        /// <summary>
        /// Properties whose value may not be negative. An easing that overshoots can take the mix below zero, where the
        /// property's range clamps it (CSS Values 4 §10.1: a mixed value is clamped to the range the property allows).
        /// </summary>
        private static readonly HashSet<string> NonNegativeProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "width", "height", "min-width", "min-height", "max-width", "max-height",
            "padding-top", "padding-right", "padding-bottom", "padding-left",
            "padding-block-start", "padding-block-end", "padding-inline-start", "padding-inline-end", "column-rule-width",
            "border-top-width", "border-right-width", "border-bottom-width", "border-left-width", "outline-width",
            "border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius",
            "font-size", "line-height", "column-width", "row-gap", "column-gap", "flex-grow", "flex-shrink", "stroke-width",
            "r", "rx", "ry"
        };

        /// <summary>
        /// Properties that take a number or a length, where a bare <c>0</c> is the number: against a calc() of lengths it is not a zero length, and
        /// the pair flips half way as in a browser (<c>0px</c> is a length, and blends).
        /// </summary>
        internal static bool ZeroIsANumber(string property) => ZeroIsANumberProperties.Contains(property);

        private static readonly HashSet<string> ZeroIsANumberProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "line-height", "tab-size", "border-image-width", "border-image-outset"
        };

        private static readonly HashSet<string> IntegerProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "z-index", "order", "column-count", "orphans", "widows"
        };

        /// <summary>
        /// Mixes <paramref name="from"/> and <paramref name="to"/> at <paramref name="t"/> (0 = from, 1 = to; may
        /// leave 0 to 1 for an easing that overshoots). <paramref name="box"/> is what a length in a <c>transform</c> list
        /// resolves against when two lists have to be mixed as matrices, and where <c>currentcolor</c> takes its colour from;
        /// without one those pairs flip.
        /// </summary>
        public static string Interpolate(CssValueParser valueParser, string property, string from, string to, double t, CssBox? box = null)
        {
            from = from.Trim();
            to = to.Trim();

            // CSS Color 4 section 3.2: a percentage opacity is the number it stands for, and a number and a percentage would not mix as they are.
            if (OpacityProperties.Contains(property))
            {
                from = PercentageAsNumber(from);
                to = PercentageAsNumber(to);
            }

            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return from;

            // CSS Text 3 §8.1 and §8.2: normal is the zero length for the two spacings, and a keyword does not mix with a length as it is.
            if (SpacingProperties.Contains(property))
            {
                if (from.Equals("normal", StringComparison.OrdinalIgnoreCase)) from = "0px";
                if (to.Equals("normal", StringComparison.OrdinalIgnoreCase)) to = "0px";
            }

            // CSS Values 4 §10.1: a calc() against a length is mixed as the calc() of the two terms, resolved at layout.
            if (SupportsCalcMix(property) && TryMixWithCalc(valueParser, property, from, to, t, out var calcMixed)) return calcMixed;

            // CSS Transitions 1 §3 (visibility): visible for the whole of the way between visible and anything else.
            if (property.Equals("visibility", StringComparison.OrdinalIgnoreCase) && t > 0 && t < 1)
            {
                if (from.Equals("visible", StringComparison.OrdinalIgnoreCase) || to.Equals("visible", StringComparison.OrdinalIgnoreCase))
                    return "visible";
            }

            // CSS Display 3 §2.5.1 (display): none against anything else is a discrete step that shows the other value
            // for the whole of the way between, so a box fading out is not removed from the layout half way.
            if (property.Equals("display", StringComparison.OrdinalIgnoreCase) && t > 0 && t < 1)
            {
                if (from.Equals("none", StringComparison.OrdinalIgnoreCase)) return to;
                if (to.Equals("none", StringComparison.OrdinalIgnoreCase)) return from;
            }

            var a = Scan(valueParser, from);
            var b = Scan(valueParser, to);

            var isTransform = property.Equals("transform", StringComparison.OrdinalIgnoreCase);

            // CSS Color 4 (Color Interpolation): interpolating to or from currentcolor uses its used value, the box's own `color`.
            // `color` itself is left alone: there currentcolor means inherit, which the keyframe's global-keyword step settles.
            if (box is not null && (HasCurrentColor(a) || HasCurrentColor(b))
                && !property.Equals("color", StringComparison.OrdinalIgnoreCase) && !SameShape(a, b)
                && CurrentColorToken(valueParser, box) is { } currentColor)
            {
                a = SubstituteCurrentColor(a, currentColor);
                b = SubstituteCurrentColor(b, currentColor);
            }

            // CSS Transforms 1 §11-13: lists are paired function by function, then what is left is mixed as matrices.
            if (isTransform && TryMixTransformLists(valueParser, a, b, t, box, out var transformed)) return transformed;

            // matrix() and matrix3d() are mixed only as matrices (§12); entry by entry would shrink a turning box.
            var mixesEntryByEntry = !(isTransform && (HasMatrixFunction(a) || HasMatrixFunction(b)));

            if (mixesEntryByEntry && !SameShape(a, b))
            {
                // CSS Transforms 1 §11 / Filter Effects 1 §9: `none` is the empty list, i.e. every function of the
                // other value at its identity argument.
                if (IsNone(a) && TryIdentity(property, b) is { } identityA) a = identityA;
                else if (IsNone(b) && TryIdentity(property, a) is { } identityB) b = identityB;
            }

            if (mixesEntryByEntry && SameShape(a, b) && TryMix(property, a, b, t, out var mixed)) return mixed;

            if (box is not null && isTransform && TryMixTransformMatrices(from, to, t, box, out var matrix))
                return matrix;

            return t < 0.5 ? from : to;
        }

        /// <summary>
        /// A calc() against a length (or another calc()) mixed as <c>calc(from * (1 - t) + to * t)</c> (CSS Values 4 §10.1), which layout resolves like
        /// any other calc(): the two cannot be added up before it, as a calc() can hold a percentage or a font-relative unit. False when either value is not
        /// a whole calc() or a single number with a length unit (a unitless number is not one, so a calc() of numbers is left to the generic mix).
        /// </summary>
        private static bool TryMixWithCalc(CssValueParser valueParser, string property, string from, string to, double t, out string result)
        {
            result = string.Empty;

            // Only a calc() of lengths can be added to a length, and a bare 0 is a zero length beside one (CSS Values 4 §5.2), except where the property takes a number too.
            var fromIsCalc = IsWholeCalc(from);
            var toIsCalc = IsWholeCalc(to);
            if (!fromIsCalc && !toIsCalc) return false;

            if ((fromIsCalc && !HoldsALength(valueParser, from)) || (toIsCalc && !HoldsALength(valueParser, to))) return false;

            var zeroIsLength = !ZeroIsANumberProperties.Contains(property);

            if (!TryCalcTerm(valueParser, from, 1 - t, zeroIsLength, out var fromTerm) || !TryCalcTerm(valueParser, to, t, zeroIsLength, out var toTerm)) return false;
            if (!fromIsCalc && !toIsCalc) return false;

            // Two calc() of the same shape are mixed term by term, as they always were.
            if (fromIsCalc && toIsCalc && SameShape(Scan(valueParser, from), Scan(valueParser, to))) return false;

            var mixed = $"calc({fromTerm} + {toTerm})";

            // The properties that take no negative value clamp the mix, as the numbers do, which an easing that overshoots would take below zero.
            result = NonNegativeProperties.Contains(property) ? $"max(0px, {mixed})" : mixed;
            return true;
        }

        private static bool IsWholeCalc(string value) =>
            value.StartsWith("calc(", StringComparison.OrdinalIgnoreCase) && MatchingParen(value, 4) == value.Length;

        // The mix is a calc() of lengths: the opacities are numbers, and a dash list is split on spaces, which a calc() holds, so it keeps flipping.
        // A browser does not mix a calc() for the text stroke's width either (it flips, even against 0px), so that one stays as it was.
        private static bool SupportsCalcMix(string property) =>
            !OpacityProperties.Contains(property)
            && !property.Equals("stroke-dasharray", StringComparison.OrdinalIgnoreCase)
            && !property.Equals("-webkit-text-stroke-width", StringComparison.OrdinalIgnoreCase);

        private static bool HoldsALength(CssValueParser valueParser, string value) =>
            IsWholeCalc(value) && Scan(valueParser, value).Any(token => token.Kind == TokenKind.Number && LengthUnits.Contains(token.Unit));

        private static bool TryCalcTerm(CssValueParser valueParser, string value, double weight, bool zeroIsLength, out string term)
        {
            term = string.Empty;

            if (IsWholeCalc(value))
            {
                term = $"({value[5..^1]}) * {Format(weight)}";
                return true;
            }

            var tokens = Scan(valueParser, value);
            if (tokens is not [{ Kind: TokenKind.Number } number]) return false;

            if (number.Unit.Length == 0 && number.Number == 0 && zeroIsLength) term = "0px";
            else if (LengthUnits.Contains(number.Unit)) term = Format(number.Number * weight) + number.Unit;
            else return false;

            return true;
        }

        /// <summary>
        /// CSS Transforms 1 §13: two lists that cannot be paired function by function are composed to a matrix each and mixed
        /// through their decompositions. False when that is not possible here - a percentage or a container query unit (they resolve
        /// against a size which no layout has given yet), a matrix without a decomposition (see <see cref="TransformMatrixInterpolator"/>) -
        /// and the pair flips. A list with no function this renderer implements composes to the identity, as it paints.
        /// </summary>
        private static bool TryMixTransformMatrices(string from, string to, double t, CssBox box, out string result)
        {
            result = string.Empty;

            // A function argument is the only place a percentage can sit in a transform list; a container query unit
            // (cqw, cqh, cqi, cqb, cqmin, cqmax) waits for a container size just the same.
            if (DependsOnLayout(from) || DependsOnLayout(to)) return false;

            var a = CssValueParser.ComposeTransformFunctions(from, box) ?? Matrix4x4.Identity;
            var b = CssValueParser.ComposeTransformFunctions(to, box) ?? Matrix4x4.Identity;

            return TransformMatrixInterpolator.TryMix(a, b, t, out result);
        }

        private static bool DependsOnLayout(string transform) =>
            transform.Contains('%') || transform.Contains("cq", StringComparison.OrdinalIgnoreCase);

        private readonly record struct TransformCall(string Name, List<string> Arguments);

        /// <summary>
        /// CSS Transforms 1 §11: the shorter list is padded with identity functions, and the lists are walked in step for as long
        /// as the two functions at a position share a primitive (<c>translateX</c> with <c>translate</c>, <c>rotate</c> with
        /// <c>rotateZ</c>, <c>scale(1)</c> with <c>scale(2, .5)</c>); each pair is mixed on its own, which keeps the angle of a
        /// rotation as written (<c>rotate(0deg)</c> to <c>rotate(360deg) skewX(10deg)</c> turns a full time). At the first position
        /// with no common primitive, what is left of each list is composed to one matrix and the two matrices are mixed (§13).
        /// False when the lists do not parse into plain numbers in plain functions, or the matrix mix is not possible; the
        /// generic mix and the whole-list matrix mix are then tried.
        /// </summary>
        private static bool TryMixTransformLists(CssValueParser valueParser, List<Token> a, List<Token> b, double t, CssBox? box, out string result)
        {
            result = string.Empty;
            if (TransformCalls(a) is not { } source || TransformCalls(b) is not { } target) return false;

            List<TransformCall> pairedSource = [], pairedTarget = [];
            var position = 0;

            for (var count = Math.Max(source.Count, target.Count); position < count; position++)
            {
                TransformCall? x = position < source.Count ? source[position] : null;
                TransformCall? y = position < target.Count ? target[position] : null;

                if (!TryPairTransformCalls(x, y, out var pairX, out var pairY)) break;

                pairedSource.Add(pairX);
                pairedTarget.Add(pairY);
            }

            var sb = new StringBuilder();

            if (pairedSource.Count > 0)
            {
                var mixedA = Scan(valueParser, TransformText(pairedSource));
                var mixedB = Scan(valueParser, TransformText(pairedTarget));

                if (!SameShape(mixedA, mixedB) || !TryMix("transform", mixedA, mixedB, t, out var pairs)) return false;

                sb.Append(pairs);
            }

            var remainderSource = source.Skip(position).ToList();
            var remainderTarget = target.Skip(position).ToList();

            if (remainderSource.Count > 0 || remainderTarget.Count > 0)
            {
                if (box is null) return false;

                if (!TryMixTransformMatrices(TransformText(remainderSource), TransformText(remainderTarget), t, box, out var matrix)) return false;

                if (sb.Length > 0) sb.Append(' ');
                sb.Append(matrix);
            }

            result = sb.ToString();
            return true;
        }

        /// <summary>The functions of a transform list, each with its numbers as written; null for anything but plain numbers in plain functions (<c>none</c> is the empty list).</summary>
        private static List<TransformCall>? TransformCalls(List<Token> tokens)
        {
            var calls = new List<TransformCall>();
            if (IsNone(tokens)) return calls;

            List<string>? arguments = null;
            var name = string.Empty;

            foreach (var token in tokens)
            {
                switch (token.Kind)
                {
                    case TokenKind.Open when arguments is null:
                        name = token.Raw[..^1];
                        arguments = [];
                        break;
                    case TokenKind.Number when arguments is not null:
                        arguments.Add(token.Raw);
                        break;
                    case TokenKind.Close when arguments is not null:
                        calls.Add(new TransformCall(name, arguments));
                        arguments = null;
                        break;
                    case TokenKind.Space:
                    case TokenKind.Punct when arguments is not null && token.Raw == ",":
                        break;
                    default:
                        return null;
                }
            }

            return arguments is null && calls.Count > 0 ? calls : null;
        }

        private static string TransformText(List<TransformCall> calls) =>
            calls.Count == 0 ? "none" : string.Join(" ", calls.Select(call => $"{call.Name}({string.Join(", ", call.Arguments)})"));

        /// <summary>
        /// The two functions at one position of the lists, brought to a common shape when they share a primitive. Either may be
        /// missing (a shorter list), standing for the identity of the other. False at the first position with no common
        /// primitive, and for the functions that are always mixed as matrices (CSS Transforms 1 §12). Two <c>perspective()</c>
        /// calls pair and mix their lengths, which is not the spec's reciprocal mix but is what a spinning card's constant
        /// <c>perspective(600px)</c> needs.
        /// </summary>
        private static bool TryPairTransformCalls(TransformCall? x, TransformCall? y, out TransformCall pairedX, out TransformCall pairedY)
        {
            pairedX = pairedY = default;

            if (x is not { } first || y is not { } second)
            {
                if ((x ?? y) is not { } present || !TryIdentityCall(present, out var identity)) return false;

                (pairedX, pairedY) = x is null ? (identity, present) : (present, identity);
                return true;
            }

            if (IsMatrixFunction(first.Name) || IsMatrixFunction(second.Name)) return false;

            if (!first.Name.Equals(second.Name, StringComparison.OrdinalIgnoreCase) || first.Arguments.Count != second.Arguments.Count)
            {
                if (CommonPrimitive(first) is not { } primitiveX || CommonPrimitive(second) is not { } primitiveY
                    || !TryUnifyPrimitives(ref primitiveX, ref primitiveY)) return false;

                (first, second) = (primitiveX, primitiveY);
            }

            // Two axes in different directions are not an angle apart: matrices (§12).
            if (first.Name.Equals("rotate3d", StringComparison.OrdinalIgnoreCase) && (first.Arguments.Count != 4 || !SameAxis(first, second))) return false;

            (pairedX, pairedY) = (first, second);
            return true;
        }

        private static bool HasMatrixFunction(List<Token> tokens) =>
            tokens.Any(token => token.Kind == TokenKind.Open && IsMatrixFunction(token.Raw[..^1]));

        private static bool IsMatrixFunction(string name) =>
            name.Equals("matrix", StringComparison.OrdinalIgnoreCase) || name.Equals("matrix3d", StringComparison.OrdinalIgnoreCase);

        /// <summary>The identity of <paramref name="call"/>: the same function with every argument at its neutral value (a rotation keeps its axis).</summary>
        private static bool TryIdentityCall(TransformCall call, out TransformCall identity)
        {
            identity = default;
            var name = call.Name.ToLowerInvariant();

            if (name == "rotate3d")
            {
                if (call.Arguments.Count != 4) return false;

                identity = new TransformCall(call.Name, [call.Arguments[0], call.Arguments[1], call.Arguments[2], "0"]);
                return true;
            }

            if (IdentityArgument(isTransform: true, name) is not { } neutral) return false;

            identity = new TransformCall(call.Name, [.. call.Arguments.Select(_ => Format(neutral))]);
            return true;
        }

        /// <summary>
        /// The primitive a function is a form of (CSS Transforms 1 §12): <c>translate</c>, <c>translate3d</c>, <c>scale</c>,
        /// <c>scale3d</c>, <c>rotate</c>, <c>rotate3d</c> or <c>skew</c>, written with all of its arguments; null for a function
        /// without one or one written with the wrong number of arguments.
        /// </summary>
        private static TransformCall? CommonPrimitive(TransformCall call)
        {
            var args = call.Arguments;

            return (call.Name.ToLowerInvariant(), args.Count) switch
            {
                ("translate", 1) or ("translatex", 1) => new TransformCall("translate", [args[0], "0"]),
                ("translate", 2) => new TransformCall("translate", args),
                ("translatey", 1) => new TransformCall("translate", ["0", args[0]]),
                ("translatez", 1) => new TransformCall("translate3d", ["0", "0", args[0]]),
                ("translate3d", 3) => new TransformCall("translate3d", args),
                ("scale", 1) => new TransformCall("scale", [args[0], args[0]]),
                ("scale", 2) => new TransformCall("scale", args),
                ("scalex", 1) => new TransformCall("scale", [args[0], "1"]),
                ("scaley", 1) => new TransformCall("scale", ["1", args[0]]),
                ("scalez", 1) => new TransformCall("scale3d", ["1", "1", args[0]]),
                ("scale3d", 3) => new TransformCall("scale3d", args),
                ("rotate", 1) or ("rotatez", 1) => new TransformCall("rotate", args),
                ("rotatex", 1) => new TransformCall("rotate3d", ["1", "0", "0", args[0]]),
                ("rotatey", 1) => new TransformCall("rotate3d", ["0", "1", "0", args[0]]),
                ("rotate3d", 4) => new TransformCall("rotate3d", args),
                ("skew", 1) or ("skewx", 1) => new TransformCall("skew", [args[0], "0"]),
                ("skew", 2) => new TransformCall("skew", args),
                ("skewy", 1) => new TransformCall("skew", ["0", args[0]]),
                _ => null
            };
        }

        /// <summary>A two-dimensional primitive meeting its three-dimensional form is converted to it; false when the primitives differ.</summary>
        private static bool TryUnifyPrimitives(ref TransformCall x, ref TransformCall y)
        {
            if (x.Name == y.Name) return true;

            static TransformCall? Spatial(TransformCall call) => call.Name switch
            {
                "translate" => new TransformCall("translate3d", [call.Arguments[0], call.Arguments[1], "0"]),
                "scale" => new TransformCall("scale3d", [call.Arguments[0], call.Arguments[1], "1"]),
                "rotate" => new TransformCall("rotate3d", ["0", "0", "1", call.Arguments[0]]),
                _ => null
            };

            if (Spatial(x) is { } spatialX && spatialX.Name == y.Name) x = spatialX;
            else if (Spatial(y) is { } spatialY && spatialY.Name == x.Name) y = spatialY;
            else return false;

            return true;
        }

        private static bool SameAxis(TransformCall x, TransformCall y)
        {
            static bool TryAxis(TransformCall call, out (double X, double Y, double Z) axis)
            {
                axis = default;
                if (!double.TryParse(call.Arguments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var ax)
                    || !double.TryParse(call.Arguments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ay)
                    || !double.TryParse(call.Arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var az)) return false;

                var length = Math.Sqrt(ax * ax + ay * ay + az * az);
                if (!(length > 0) || !double.IsFinite(length)) return false;

                axis = (ax / length, ay / length, az / length);
                return true;
            }

            return TryAxis(x, out var u) && TryAxis(y, out var v)
                && Math.Abs(u.X - v.X) < 1e-6 && Math.Abs(u.Y - v.Y) < 1e-6 && Math.Abs(u.Z - v.Z) < 1e-6;
        }

        private static bool IsCurrentColor(Token token) =>
            token.Kind == TokenKind.Word && token.Raw.Equals("currentcolor", StringComparison.OrdinalIgnoreCase);

        private static bool HasCurrentColor(List<Token> tokens) => tokens.Exists(IsCurrentColor);

        /// <summary>
        /// The box's <c>color</c> as a colour token, or null when it is not one the mixer can read: the colour as the cascade
        /// has it at the animation step, including a <c>color</c> animated in the same keyframes (<see cref="AnimationApplier"/>
        /// samples <c>color</c> first), but not one a later animation or an <c>!important</c> declaration sets.
        /// </summary>
        private static Token? CurrentColorToken(CssValueParser valueParser, CssBox box)
        {
            if (CssUtils.GetPropertyValue(box, "color") is not { } text) return null;

            // CSS Color 4 interpolates the used value of currentcolor, which would make a lab() text colour take the pair into Oklab. Chrome
            // does not (white to currentcolor on a `color: lab(...)` box is an sRGB mix), and this follows Chrome: the keyword counts as a
            // legacy colour whatever it resolves to, and only the other side can take the mix into Oklab. The Oklab coordinates stay on the
            // token: when the other side does take the mix there, Chrome mixes an out-of-gamut `color` from them too (measured: `lab(60 90 60)`
            // text colour against `lab(50 40 20)`, 0 to 1 off).
            var token = ColorOrWord(valueParser, text);
            if (token.Kind != TokenKind.Color) return null;

            // Against a legacy colour the mix is in sRGB, and Chrome makes it from the unclipped sRGB value of a `color` outside the gamut (a
            // channel below 0 or above 255) and clips the result once: `color: lch(50 60 200)` to red is 64 of 255 off from the clipped colour.
            if (token.Oklab is { } wide)
            {
                var srgb = ColorSpaceMath.ToSpaceFromOklab(wide, ColorSpaceMath.Space.Srgb);
                return token with { InOklab = false, Rgba = [srgb[0] * 255, srgb[1] * 255, srgb[2] * 255, token.Rgba![3]] };
            }

            return token with { InOklab = false };
        }

        private static List<Token> SubstituteCurrentColor(List<Token> tokens, Token color) =>
            tokens.ConvertAll(token => IsCurrentColor(token) ? color : token);

        private static bool IsNone(List<Token> tokens) =>
            tokens is [{ Kind: TokenKind.Word } word] && word.Raw.Equals("none", StringComparison.OrdinalIgnoreCase);

        private static bool SameShape(List<Token> a, List<Token> b)
        {
            if (a.Count != b.Count) return false;

            for (var i = 0; i < a.Count; i++)
            {
                var x = a[i];
                var y = b[i];
                if (x.Kind != y.Kind) return false;

                switch (x.Kind)
                {
                    case TokenKind.Word:
                    case TokenKind.Open:
                    case TokenKind.Text:
                    case TokenKind.Punct:
                        if (!string.Equals(x.Raw, y.Raw, StringComparison.OrdinalIgnoreCase)) return false;
                        break;
                }
            }

            return true;
        }

        private static bool TryMix(string property, List<Token> a, List<Token> b, double t, out string result)
        {
            var sb = new StringBuilder();
            result = string.Empty;

            for (var i = 0; i < a.Count; i++)
            {
                var x = a[i];
                var y = b[i];

                switch (x.Kind)
                {
                    case TokenKind.Number:
                        if (!TryMixNumbers(property, x, y, t, sb, a.Count == 1)) return false;
                        break;
                    case TokenKind.Color:
                        sb.Append(MixColors(x, y, t));
                        break;
                    default:
                        sb.Append(x.Raw);
                        break;
                }
            }

            result = sb.ToString();
            return true;
        }

        private static string PercentageAsNumber(string value) =>
            value.Length > 1 && value[^1] == '%'
            && double.TryParse(value.AsSpan(0, value.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent)
                ? Format(percent / 100)
                : value;

        private static bool TryMixNumbers(string property, Token x, Token y, double t, StringBuilder sb, bool wholeValue)
        {
            var unitX = x.Unit;
            var unitY = y.Unit;

            // A bare 0 stands for a zero of any unit (CSS Values 4 §5.2: a unitless zero is a valid <length>).
            if (unitX.Length == 0 && x.Number == 0 && unitY.Length > 0) unitX = unitY;
            else if (unitY.Length == 0 && y.Number == 0 && unitX.Length > 0) unitY = unitX;

            var numberX = x.Number;
            var numberY = y.Number;

            if (!string.Equals(unitX, unitY, StringComparison.OrdinalIgnoreCase))
            {
                if (TryToDegrees(numberX, unitX, out var degreesX) && TryToDegrees(numberY, unitY, out var degreesY))
                {
                    numberX = degreesX;
                    numberY = degreesY;
                    unitX = unitY = "deg";
                }
                else if (LengthUnits.Contains(unitX) && LengthUnits.Contains(unitY))
                {
                    // Lengths of different units cannot be added up until layout, so let calc() do it there.
                    sb.Append("calc(").Append(Format(numberX * (1 - t))).Append(unitX)
                        .Append(" + ").Append(Format(numberY * t)).Append(unitY).Append(')');
                    return true;
                }
                else
                {
                    return false;
                }
            }

            var mixed = numberX + (numberY - numberX) * t;

            if (IntegerProperties.Contains(property) && unitX.Length == 0) mixed = Math.Round(mixed, MidpointRounding.AwayFromZero);
            else if (wholeValue && unitX.Length == 0 && property.Equals("opacity", StringComparison.OrdinalIgnoreCase)) mixed = Math.Clamp(mixed, 0, 1);
            else if (wholeValue && mixed < 0 && NonNegativeProperties.Contains(property)) mixed = 0;

            sb.Append(Format(mixed)).Append(unitX);
            return true;
        }

        private static bool TryToDegrees(double value, string unit, out double degrees)
        {
            switch (unit.ToLowerInvariant())
            {
                case "deg": degrees = value; return true;
                case "grad": degrees = value * 0.9; return true;
                case "rad": degrees = value * 180 / Math.PI; return true;
                case "turn": degrees = value * 360; return true;
                default: degrees = 0; return false;
            }
        }

        /// <summary>
        /// Premultiplied mix of two colour tokens (sRGB channels 0 to 255 and alpha 0 to 1): in gamma-encoded sRGB when both are legacy
        /// sRGB color formats, in Oklab as soon as one is not (CSS Color 4 §13.2, Color Space for Interpolation: Oklab is the default where
        /// the host syntax names none, which an animation does not).
        /// </summary>
        private static string MixColors(Token x, Token y, double t)
        {
            var from = x.Rgba!;
            var to = y.Rgba!;

            // A missing alpha (written as none) takes the other colour's.
            var fromAlpha = from[3];
            var toAlpha = to[3];
            var alphaBit = ColorSpaceMath.AlphaMissing;
            if ((x.OklabMissing & alphaBit) != 0 && (y.OklabMissing & alphaBit) == 0) fromAlpha = toAlpha;
            else if ((y.OklabMissing & alphaBit) != 0 && (x.OklabMissing & alphaBit) == 0) toAlpha = fromAlpha;

            var alpha = fromAlpha + (toAlpha - fromAlpha) * t;
            alpha = Math.Clamp(alpha, 0, 1);

            if (alpha <= 0) return "rgba(0, 0, 0, 0)";

            if (x.InOklab || y.InOklab) return MixInOklab(x, y, t, alpha, fromAlpha, toAlpha);

            from = [from[0], from[1], from[2], fromAlpha];
            to = [to[0], to[1], to[2], toAlpha];

            var channels = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var premultiplied = from[i] * from[3] * (1 - t) + to[i] * to[3] * t;
                channels[i] = (int)Math.Round(Math.Clamp(premultiplied / alpha, 0, 255));
            }

            return alpha >= 0.9995
                ? $"rgb({channels[0]}, {channels[1]}, {channels[2]})"
                : $"rgba({channels[0]}, {channels[1]}, {channels[2]}, {Format(alpha)})";
        }

        /// <summary>
        /// The Oklab mix, premultiplied by alpha like the sRGB one, written as an <c>oklab()</c> colour for the property setter to parse
        /// (and gamut-map, as it does a static <c>oklab()</c>). An endpoint is mixed from the Oklab coordinates it was written with when it is a
        /// <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c> or <c>color-mix()</c> colour (which can lie outside the sRGB gamut), and from its
        /// 8-bit sRGB value otherwise.
        /// </summary>
        private static string MixInOklab(Token x, Token y, double t, double alpha, double fromAlpha, double toAlpha)
        {
            var from = x.Rgba!;
            var to = y.Rgba!;
            var a = x.Oklab ?? ColorSpaceMath.ToSpace(from[0] / 255, from[1] / 255, from[2] / 255, ColorSpaceMath.Space.Oklab);
            var b = y.Oklab ?? ColorSpaceMath.ToSpace(to[0] / 255, to[1] / 255, to[2] / 255, ColorSpaceMath.Space.Oklab);

            // A missing component (written as none) takes the other colour's value.
            a = [.. a];
            b = [.. b];
            for (var i = 0; i < 3; i++)
            {
                var bit = 1 << i;
                if ((x.OklabMissing & bit) != 0 && (y.OklabMissing & bit) == 0) a[i] = b[i];
                else if ((y.OklabMissing & bit) != 0 && (x.OklabMissing & bit) == 0) b[i] = a[i];
            }

            var channels = new double[3];
            for (var i = 0; i < 3; i++)
                channels[i] = (a[i] * fromAlpha * (1 - t) + b[i] * toAlpha * t) / alpha;

            // An easing that overshoots can take the lightness past the ends of its range; a browser holds it there (and keeps a and b), where
            // the colour parser would take the value as it comes and wash out to white or black.
            channels[0] = Math.Clamp(channels[0], 0, 1);

            var text = $"oklab({Format(channels[0])} {Format(channels[1])} {Format(channels[2])}";
            return alpha >= 0.9995 ? text + ")" : $"{text} / {Format(alpha)})";
        }

        internal static string Format(double value)
        {
            var text = value.ToString("0.######", CultureInfo.InvariantCulture);
            return text is "-0" ? "0" : text;
        }

        /// <summary>
        /// The same list as <paramref name="tokens"/> with every function at its identity argument, or null when the
        /// property has no identity list or a function in it has no identity this renderer knows.
        /// </summary>
        private static List<Token>? TryIdentity(string property, List<Token> tokens)
        {
            var isTransform = property.Equals("transform", StringComparison.OrdinalIgnoreCase);
            if (!isTransform && !property.Equals("filter", StringComparison.OrdinalIgnoreCase)) return null;

            var identity = new List<Token>(tokens.Count);
            string? function = null;
            var depth = 0;
            var neutral = 0d;

            foreach (var token in tokens)
            {
                switch (token.Kind)
                {
                    case TokenKind.Open when depth == 0:
                        function = token.Raw[..^1].ToLowerInvariant();
                        neutral = IdentityArgument(isTransform, function) ?? double.NaN;
                        if (double.IsNaN(neutral)) return null;
                        depth = 1;
                        identity.Add(token);
                        break;
                    case TokenKind.Open:
                        return null;
                    case TokenKind.Close:
                        depth--;
                        identity.Add(token);
                        break;
                    case TokenKind.Number when depth == 1:
                        // A percentage argument states the same amount 100 times larger: brightness(1) is brightness(100%).
                        var amount = token.Unit == "%" ? neutral * 100 : neutral;
                        identity.Add(token with { Number = amount, Raw = Format(amount) + token.Unit });
                        break;
                    case TokenKind.Space or TokenKind.Punct when depth == 1:
                        identity.Add(token);
                        break;
                    case TokenKind.Space when depth == 0:
                        identity.Add(token);
                        break;
                    default:
                        return null;
                }
            }

            return depth == 0 && function is not null ? identity : null;
        }

        private static double? IdentityArgument(bool isTransform, string function) => isTransform
            ? function switch
            {
                "translate" or "translatex" or "translatey" or "translatez" or "translate3d"
                    or "rotate" or "rotatex" or "rotatey" or "rotatez" or "skew" or "skewx" or "skewy" => 0,
                "scale" or "scalex" or "scaley" or "scalez" or "scale3d" => 1,
                _ => null
            }
            : function switch
            {
                "blur" or "grayscale" or "sepia" or "invert" or "hue-rotate" => 0,
                "brightness" or "contrast" or "saturate" or "opacity" => 1,
                _ => null
            };

        private static List<Token> Scan(CssValueParser valueParser, string text)
        {
            var tokens = new List<Token>();
            var i = 0;

            while (i < text.Length)
            {
                var c = text[i];

                if (char.IsWhiteSpace(c))
                {
                    while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                    tokens.Add(new Token(TokenKind.Space, " "));
                }
                else if (StartsNumber(text, i))
                {
                    var start = i;
                    i = ScanNumberEnd(text, i);
                    var number = double.Parse(text.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                    var unitStart = i;
                    while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '%')) i++;
                    var unit = text[unitStart..i];
                    tokens.Add(new Token(TokenKind.Number, text[start..i], number, unit));
                }
                else if (c == '#')
                {
                    var start = i++;
                    while (i < text.Length && Uri.IsHexDigit(text[i])) i++;
                    tokens.Add(ColorOrWord(valueParser, text[start..i]));
                }
                else if (char.IsLetter(c) || c == '_' || c == '-' || c > 127)
                {
                    var start = i;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '-' or '_' || text[i] > 127)) i++;
                    var name = text[start..i];

                    if (i < text.Length && text[i] == '(')
                    {
                        if (name.Equals("url", StringComparison.OrdinalIgnoreCase) || name.Equals("var", StringComparison.OrdinalIgnoreCase)
                            || ColorFunctions.Contains(name))
                        {
                            var end = MatchingParen(text, i);
                            var whole = text[start..end];
                            i = end;
                            tokens.Add(name.Equals("url", StringComparison.OrdinalIgnoreCase) || name.Equals("var", StringComparison.OrdinalIgnoreCase)
                                ? new Token(TokenKind.Text, whole)
                                : ColorOrWord(valueParser, whole));
                        }
                        else
                        {
                            i++;
                            tokens.Add(new Token(TokenKind.Open, name + "("));
                        }
                    }
                    else
                    {
                        tokens.Add(ColorOrWord(valueParser, name));
                    }
                }
                else if (c is '"' or '\'')
                {
                    var start = i++;
                    while (i < text.Length && text[i] != c) i += text[i] == '\\' ? 2 : 1;
                    i = Math.Min(i + 1, text.Length);
                    tokens.Add(new Token(TokenKind.Text, text[start..i]));
                }
                else if (c == '(')
                {
                    i++;
                    tokens.Add(new Token(TokenKind.Open, "("));
                }
                else if (c == ')')
                {
                    i++;
                    tokens.Add(new Token(TokenKind.Close, ")"));
                }
                else
                {
                    i++;
                    tokens.Add(new Token(TokenKind.Punct, c.ToString()));
                }
            }

            return tokens;
        }

        private static Token ColorOrWord(CssValueParser valueParser, string text)
        {
            if (valueParser.TryGetColor(text, 0, text.Length, out var color))
            {
                var inOklab = TakesAMixIntoOklab(text);
                double[] oklab = [];
                var missing = 0;
                var read = inOklab && valueParser.TryGetMixOperand(text, out oklab, out missing);
                return new Token(TokenKind.Color, text, Rgba: [color.R, color.G, color.B, color.A / 255d], InOklab: inOklab,
                    Oklab: read ? oklab : null, OklabMissing: read ? missing : 0);
            }

            return new Token(TokenKind.Word, text);
        }

        /// <summary>
        /// Whether a colour takes a mix out of sRGB: anything but the legacy sRGB color formats CSS Color 4 §13.2 lists (hex, named colours,
        /// <c>rgb()</c>, <c>hsl()</c>, <c>hwb()</c>, and <c>transparent</c>, which the spec's own gradient example counts as legacy). That is
        /// <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c>, <c>color()</c>, <c>color-mix()</c> (whatever its space) and relative colour
        /// syntax (<c>rgb(from ...)</c>), as Chrome mixes them, and a legacy function with a component written as <c>none</c>.
        /// </summary>
        private static bool TakesAMixIntoOklab(string text)
        {
            var open = text.IndexOf('(');
            if (open <= 0) return false;

            var name = text.AsSpan(0, open).Trim();
            if (name.Equals("lab", StringComparison.OrdinalIgnoreCase) || name.Equals("lch", StringComparison.OrdinalIgnoreCase)
                || name.Equals("oklab", StringComparison.OrdinalIgnoreCase) || name.Equals("oklch", StringComparison.OrdinalIgnoreCase)
                || name.Equals("color", StringComparison.OrdinalIgnoreCase) || name.Equals("color-mix", StringComparison.OrdinalIgnoreCase))
                return true;

            // A component written as none (a missing one) takes a mix out of sRGB even in a legacy colour function: Chrome mixes it in Oklab.
            if ((name.Equals("rgb", StringComparison.OrdinalIgnoreCase) || name.Equals("rgba", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("hsl", StringComparison.OrdinalIgnoreCase) || name.Equals("hsla", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("hwb", StringComparison.OrdinalIgnoreCase))
                && text.AsSpan(open).Contains("none", StringComparison.OrdinalIgnoreCase))
                return true;

            var inside = text.AsSpan(open + 1).TrimStart();
            return inside.Length > 4 && inside.StartsWith("from", StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(inside[4]);
        }

        private static bool StartsNumber(string text, int i)
        {
            var c = text[i];
            if (char.IsAsciiDigit(c)) return true;
            if (c == '.') return i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]);
            if (c is '+' or '-')
                return i + 1 < text.Length && (char.IsAsciiDigit(text[i + 1]) || (text[i + 1] == '.' && i + 2 < text.Length && char.IsAsciiDigit(text[i + 2])));
            return false;
        }

        private static int ScanNumberEnd(string text, int i)
        {
            if (text[i] is '+' or '-') i++;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            if (i + 1 < text.Length && text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            }

            // An exponent only when a digit follows, so the `e` of `1em` stays a unit.
            if (i + 1 < text.Length && text[i] is 'e' or 'E')
            {
                var j = i + 1;
                if (j < text.Length && text[j] is '+' or '-') j++;
                if (j < text.Length && char.IsAsciiDigit(text[j]))
                {
                    while (j < text.Length && char.IsAsciiDigit(text[j])) j++;
                    i = j;
                }
            }

            return i;
        }

        /// <summary>The index just past the parenthesis closing the one at <paramref name="open"/>.</summary>
        private static int MatchingParen(string text, int open)
        {
            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0) return i + 1;
            }

            return text.Length;
        }
    }
}
