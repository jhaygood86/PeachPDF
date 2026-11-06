using System;
using System.Collections.Generic;
using System.Text;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;

namespace PeachBrackets.Html.Animation
{
    internal static partial class CssValueInterpolator
    {
        /// <summary>
        /// Combines an animation's keyframe <paramref name="value"/> with the value underneath it (CSS Animations 2 §3.10, <c>animation-composition</c>; Web Animations 1 §5.5):
        /// <c>add</c> puts the two together (a sum, a product for <c>scale</c>, the transform lists one after the other), <c>accumulate</c> sums the parts that are the
        /// same (<c>rotate(10deg)</c> and <c>rotate(20deg)</c> are <c>rotate(30deg)</c>).
        /// </summary>
        /// <returns>the combined value, or null when the two cannot be combined (a keyword, a list of another shape, a colour that is not plain sRGB): the value then replaces</returns>
        internal static string? Composite(CssValueParser valueParser, string property, string operation, string underlying, string value, CssBox? box)
        {
            var accumulate = operation.Equals("accumulate", StringComparison.OrdinalIgnoreCase);
            if (!accumulate && !operation.Equals("add", StringComparison.OrdinalIgnoreCase)) return null;

            underlying = underlying.Trim();
            value = value.Trim();

            if (OpacityProperties.Contains(property))
            {
                underlying = PercentageAsNumber(underlying);
                value = PercentageAsNumber(value);
            }

            if (property.Equals("transform", StringComparison.OrdinalIgnoreCase)) return CompositeTransform(valueParser, accumulate, underlying, value);

            var a = Scan(valueParser, underlying);
            var b = Scan(valueParser, value);

            if (a.Count == 1 && b.Count == 1 && a[0].Kind == TokenKind.Color && b[0].Kind == TokenKind.Color) return AddColors(a[0], b[0]);
            if (!SameShape(a, b)) return null;

            var multiplies = property.Equals("scale", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            for (var i = 0; i < a.Count; i++)
            {
                switch (a[i].Kind)
                {
                    case TokenKind.Number:
                        if (!TryAddNumbers(property, a[i], b[i], accumulate, multiplies, sb, a.Count == 1)) return null;
                        break;
                    case TokenKind.Color:
                        if (AddColors(a[i], b[i]) is not { } color) return null;
                        sb.Append(color);
                        break;
                    default:
                        sb.Append(a[i].Raw);
                        break;
                }
            }

            return sb.ToString();
        }

        // Two numbers of one unit are added; a zero takes the other's unit; lengths of different units are left to calc() at layout; angles are added in degrees.
        // A scale factor adds by multiplying (add) or by summing what each is past 1 (accumulate).
        private static bool TryAddNumbers(string property, Token x, Token y, bool accumulate, bool factor, StringBuilder sb, bool wholeValue)
        {
            var unitX = x.Unit;
            var unitY = y.Unit;
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
                    unitX = "deg";
                }
                else if (LengthUnits.Contains(unitX) && LengthUnits.Contains(unitY))
                {
                    sb.Append("calc(").Append(Format(numberX)).Append(unitX).Append(" + ").Append(Format(numberY)).Append(unitY).Append(')');
                    return true;
                }
                else
                {
                    return false;
                }
            }

            var sum = factor ? (accumulate ? numberX + numberY - 1 : numberX * numberY) : numberX + numberY;

            if (IntegerProperties.Contains(property) && unitX.Length == 0) sum = Math.Round(sum, MidpointRounding.AwayFromZero);
            else if (wholeValue && unitX.Length == 0 && property.Equals("opacity", StringComparison.OrdinalIgnoreCase)) sum = Math.Clamp(sum, 0, 1);
            else if (wholeValue && sum < 0 && NonNegativeProperties.Contains(property)) sum = 0;

            sb.Append(Format(sum)).Append(unitX);
            return true;
        }

        // Premultiplied sum of two sRGB colours (CSS Color 4: the components add, alpha caps at 1); one that is not plain sRGB is left to replace.
        private static string? AddColors(Token x, Token y)
        {
            if (x.InOklab || y.InOklab || x.Rgba is not { } from || y.Rgba is not { } to) return null;

            var alpha = Math.Min(1, from[3] + to[3]);
            if (alpha <= 0) return "rgba(0, 0, 0, 0)";

            var channels = new int[3];
            for (var i = 0; i < 3; i++)
                channels[i] = (int)Math.Round(Math.Clamp(((from[i] * from[3]) + (to[i] * to[3])) / alpha, 0, 255));

            return alpha >= 0.9995
                ? $"rgb({channels[0]}, {channels[1]}, {channels[2]})"
                : $"rgba({channels[0]}, {channels[1]}, {channels[2]}, {Format(alpha)})";
        }

        // add: the two lists one after the other. accumulate: functions of the same kind in the same places have their arguments summed; lists that do not line up are
        // put one after the other too.
        private static string? CompositeTransform(CssValueParser valueParser, bool accumulate, string underlying, string value)
        {
            if (IsNoneValue(underlying)) return value;
            if (IsNoneValue(value)) return underlying;

            var concatenated = underlying + " " + value;
            if (!accumulate) return concatenated;

            var first = SplitFunctions(underlying);
            var second = SplitFunctions(value);
            if (first is null || second is null || first.Count != second.Count) return concatenated;

            var sb = new StringBuilder();
            for (var i = 0; i < first.Count; i++)
            {
                if (!first[i].Name.Equals(second[i].Name, StringComparison.OrdinalIgnoreCase)) return concatenated;
                if (first[i].Name.StartsWith("matrix", StringComparison.OrdinalIgnoreCase) || first[i].Name.Equals("perspective", StringComparison.OrdinalIgnoreCase)) return concatenated;

                var a = Scan(valueParser, first[i].Args);
                var b = Scan(valueParser, second[i].Args);
                if (!SameShape(a, b)) return concatenated;

                var factor = first[i].Name.StartsWith("scale", StringComparison.OrdinalIgnoreCase);
                var args = new StringBuilder();
                for (var j = 0; j < a.Count; j++)
                {
                    if (a[j].Kind == TokenKind.Number)
                    {
                        if (!TryAddNumbers("transform", a[j], b[j], accumulate: true, factor, args, wholeValue: false)) return concatenated;
                    }
                    else
                    {
                        args.Append(a[j].Raw);
                    }
                }

                if (i > 0) sb.Append(' ');
                sb.Append(first[i].Name).Append('(').Append(args).Append(')');
            }

            return sb.ToString();
        }

        private static bool IsIndividualTransform(string property) =>
            property.Equals("translate", StringComparison.OrdinalIgnoreCase) || property.Equals("rotate", StringComparison.OrdinalIgnoreCase)
            || property.Equals("scale", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Puts two values of <c>translate</c>, <c>rotate</c> or <c>scale</c> in one shape so they can be mixed (CSS Transforms 2 §3): <c>none</c> is the identity, a list that
        /// is shorter than three is filled in (a missing translate part is 0, a scale's y is its x and its z is 1), and a rotation is written with its axis, so
        /// <c>rotate: 30deg</c> and <c>rotate: z 30deg</c> are the same. False when two rotations turn about different axes, which have no mix of their own here.
        /// </summary>
        private static bool TryNormalizeIndividual(string property, ref string from, ref string to)
        {
            if (property.Equals("translate", StringComparison.OrdinalIgnoreCase))
            {
                from = NormalizeTranslate(from);
                to = NormalizeTranslate(to);
                return true;
            }

            if (property.Equals("scale", StringComparison.OrdinalIgnoreCase))
            {
                from = NormalizeScale(from);
                to = NormalizeScale(to);
                return true;
            }

            var a = ParseRotation(from);
            var b = ParseRotation(to);
            if (a is null || b is null) return false;

            // A rotation by nothing turns about any axis.
            var (axisA, angleA) = a.Value;
            var (axisB, angleB) = b.Value;
            if (angleA.Length == 0) axisA = axisB;
            if (angleB.Length == 0) axisB = axisA;
            if (axisA != axisB) return false;

            from = $"{axisA} {(angleA.Length == 0 ? "0deg" : angleA)}";
            to = $"{axisB} {(angleB.Length == 0 ? "0deg" : angleB)}";
            return true;
        }

        private static string NormalizeTranslate(string value)
        {
            if (IsNoneValue(value)) return "0px 0px 0px";

            var parts = CssValueParser.SplitTopLevel(value);
            return $"{parts[0]} {(parts.Count > 1 ? parts[1] : "0px")} {(parts.Count > 2 ? parts[2] : "0px")}";
        }

        private static string NormalizeScale(string value)
        {
            if (IsNoneValue(value)) return "1 1 1";

            var parts = CssValueParser.SplitTopLevel(value);
            return $"{parts[0]} {(parts.Count > 1 ? parts[1] : parts[0])} {(parts.Count > 2 ? parts[2] : "1")}";
        }

        // The axis ("x y z" as numbers) and the angle of a rotate value; an empty angle for none.
        private static (string Axis, string Angle)? ParseRotation(string value)
        {
            if (IsNoneValue(value)) return ("0 0 1", string.Empty);

            var parts = CssValueParser.SplitTopLevel(value);
            var angle = parts.Find(CssValueParser.IsAngleText);
            if (angle is null) return null;

            var axis = parts.FindAll(p => !ReferenceEquals(p, angle));
            return axis.Count switch
            {
                0 => ("0 0 1", angle),
                1 when axis[0].Equals("x", StringComparison.OrdinalIgnoreCase) => ("1 0 0", angle),
                1 when axis[0].Equals("y", StringComparison.OrdinalIgnoreCase) => ("0 1 0", angle),
                1 when axis[0].Equals("z", StringComparison.OrdinalIgnoreCase) => ("0 0 1", angle),
                3 => ($"{axis[0]} {axis[1]} {axis[2]}", angle),
                _ => null,
            };
        }

        private static bool IsNoneValue(string value) => value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase);

        // The functions of a transform list as written, or null when it holds anything but functions.
        private static List<(string Name, string Args)>? SplitFunctions(string list)
        {
            var calls = new List<(string, string)>();
            var i = 0;
            while (i < list.Length)
            {
                if (char.IsWhiteSpace(list[i])) { i++; continue; }

                var open = list.IndexOf('(', i);
                if (open < 0) return null;

                var depth = 0;
                var end = open;
                for (; end < list.Length; end++)
                {
                    if (list[end] == '(') depth++;
                    else if (list[end] == ')' && --depth == 0) break;
                }

                if (depth != 0) return null;

                calls.Add((list[i..open].Trim(), list[(open + 1)..end]));
                i = end + 1;
            }

            return calls;
        }
    }
}
