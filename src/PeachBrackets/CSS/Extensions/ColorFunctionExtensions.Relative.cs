#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    // Relative colour syntax (CSS Color 5 §4): rgb(from red r g b), hsl(from ...), hwb(from ...), lab(from ...), lch(from ...), oklab(from ...),
    // oklch(from ...) and color(from ... <space> ...). The origin colour is converted to the space of the function it is written in, its components
    // become channel keywords (r g b, h s l, l a b, ... and alpha) that stand for numbers, and each component of the result is then a number, a percentage,
    // none, a keyword, or a calc() over keywords.
    internal static partial class ColorFunctionExtensions
    {
        private static bool IsRelative(IEnumerable<Token> args)
        {
            foreach (var token in args)
            {
                if (token.Type == TokenType.Whitespace) continue;
                return token.Type == TokenType.Ident && token.Data.Equals("from", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static Color? ParseRelative(string name, IEnumerable<Token> args, bool wantOklab, out double[] oklab)
        {
            oklab = null;

            var tokens = args.Where(t => t.Type != TokenType.Whitespace).ToList();
            // from <color> <components> [/ alpha]: no commas, and the origin is a single token (a nested function arrives as one).
            if (tokens.Count < 5 || tokens.Any(t => t.Type == TokenType.Comma)) return null;

            if (!Relative.TryGetSpec(name.ToLowerInvariant(), tokens, out var spec)) return null;

            // Exactly the components, then optionally a slash and one alpha.
            var rest = tokens.Skip(2).ToList();
            var slash = rest.FindIndex(t => t.Type == TokenType.Delim && t.Data.Is("/"));
            if (slash < 0 ? rest.Count != spec.ComponentCount : slash != spec.ComponentCount || rest.Count != spec.ComponentCount + 2) return null;

            if (!Extract(rest, spec.ComponentCount, out var comps, out var alphaTok, allowImplicitAlpha: false)) return null;
            if (spec.SpaceToken && !Relative.TryReadSpace(comps[0], out spec)) return null;

            // color-mix() operands and relative origins nest without bound in the grammar; they share one depth limit.
            if (_colorMixDepth >= MaxColorMixDepth) return null;
            Color? origin;
            double[] originOklab;
            _colorMixDepth++;
            try
            {
                origin = ResolveColor([tokens[1]], wantOklab: true, out originOklab);
            }
            finally
            {
                _colorMixDepth--;
            }

            if (origin is not { } originColor || originColor.IsDeviceCmyk) return null;

            var coords = Relative.OriginCoordinates(originColor, originOklab, spec.Space);
            var variables = new Dictionary<string, double>(4);
            for (var i = 0; i < 3; i++) variables[spec.Keywords[i]] = coords[i] * spec.Scale;
            variables["alpha"] = originColor.A / 255.0;

            var offset = spec.SpaceToken ? 1 : 0;
            var values = new double[3];
            for (var i = 0; i < 3; i++)
            {
                var token = comps[offset + i];
                var value = spec.IsHue(i) ? Relative.EvaluateHue(token, variables) : Relative.Evaluate(token, spec.References[i], variables);
                if (value is null) return null;
                values[i] = value.Value;
            }

            var alpha = 1f;
            if (alphaTok is { } alphaToken)
            {
                var a = Relative.Evaluate(alphaToken, 1.0, variables);
                if (a is null) return null;
                alpha = (float)Math.Clamp(a.Value, 0, 1);
            }
            else
            {
                alpha = originColor.A / 255f;
            }

            return Relative.Build(spec, values, alpha, wantOklab, out oklab);
        }

        // What each function says about its components: the space its result is in, the channel keywords, what 100% of each component is, which one is a hue.
        private readonly record struct RelativeSpec(ColorSpaceMath.Space Space, string[] Keywords, double[] References, int HueSlot, double Scale, bool SpaceToken, int ComponentCount, string Function)
        {
            public bool IsHue(int slot) => slot == HueSlot;
        }

        private static class Relative
        {
            public static bool TryGetSpec(string name, List<Token> tokens, out RelativeSpec spec)
            {
                spec = default;
                switch (name)
                {
                    case "rgb":
                    case "rgba":
                        spec = new RelativeSpec(ColorSpaceMath.Space.Srgb, ["r", "g", "b"], [255, 255, 255], -1, 255, false, 3, "rgb");
                        return true;
                    case "hsl":
                    case "hsla":
                        spec = new RelativeSpec(ColorSpaceMath.Space.Hsl, ["h", "s", "l"], [360, 100, 100], 0, 1, false, 3, "hsl");
                        return true;
                    case "hwb":
                        spec = new RelativeSpec(ColorSpaceMath.Space.Hwb, ["h", "w", "b"], [360, 100, 100], 0, 1, false, 3, "hwb");
                        return true;
                    case "lab":
                        spec = new RelativeSpec(ColorSpaceMath.Space.Lab, ["l", "a", "b"], [100, 125, 125], -1, 1, false, 3, "lab");
                        return true;
                    case "oklab":
                        spec = new RelativeSpec(ColorSpaceMath.Space.Oklab, ["l", "a", "b"], [1, 0.4, 0.4], -1, 1, false, 3, "oklab");
                        return true;
                    case "lch":
                        spec = new RelativeSpec(ColorSpaceMath.Space.Lch, ["l", "c", "h"], [100, 150, 360], 2, 1, false, 3, "lch");
                        return true;
                    case "oklch":
                        spec = new RelativeSpec(ColorSpaceMath.Space.Oklch, ["l", "c", "h"], [1, 0.4, 360], 2, 1, false, 3, "oklch");
                        return true;
                    case "color":
                        // The space is the first component; its keywords follow from it (TryReadSpace).
                        spec = new RelativeSpec(ColorSpaceMath.Space.Srgb, ["r", "g", "b"], [1, 1, 1], -1, 1, true, 4, "color");
                        return true;
                    default:
                        return false;
                }
            }

            public static bool TryReadSpace(Token token, out RelativeSpec spec)
            {
                spec = default;
                if (token.Type != TokenType.Ident || !PredefinedSpaces.TryGetValue(token.Data.ToString(), out var space)) return false;

                var isXyz = space is ColorSpaceMath.Space.XyzD65 or ColorSpaceMath.Space.XyzD50;
                spec = new RelativeSpec(space, isXyz ? ["x", "y", "z"] : ["r", "g", "b"], [1, 1, 1], -1, 1, true, 4, "color");
                return true;
            }

            // The origin's components in the space of the function it is relative to. A plain sRGB colour converts from its own 8-bit value (so rgb(from red r g b) is red
            // exactly); one that can lie outside the gamut from the coordinates it was written with, unclipped.
            public static double[] OriginCoordinates(Color origin, double[] oklab, ColorSpaceMath.Space space)
            {
                if (oklab is not null && space is not (ColorSpaceMath.Space.Hsl or ColorSpaceMath.Space.Hwb))
                    return ColorSpaceMath.ToSpaceFromOklab(oklab, space);

                return ColorSpaceMath.ToSpace(origin.R / 255.0, origin.G / 255.0, origin.B / 255.0, space);
            }

            // A component: a number (or a percentage of the reference), none (0), a channel keyword, or a calc() over the keywords.
            public static double? Evaluate(Token token, double reference, Dictionary<string, double> variables)
            {
                if (IsNone(token)) return 0.0;

                if (token.Type == TokenType.Ident)
                    return variables.TryGetValue(token.Data.ToString().ToLowerInvariant(), out var keyword) ? keyword : null;

                if (token.Type == TokenType.Function && CalcParser.IsCalcFamily(token.Data))
                {
                    var node = CalcParser.Parse(token, variables);
                    if (node is null || Calc(node) is not { } result) return null;
                    return result.Category switch
                    {
                        CalcCategory.Number => result.Value,
                        CalcCategory.Percentage => result.Value / 100.0 * reference,
                        _ => null
                    };
                }

                return Component(token, reference);
            }

            public static double? EvaluateHue(Token token, Dictionary<string, double> variables)
            {
                if (IsNone(token)) return 0.0;

                if (token.Type == TokenType.Ident)
                    return variables.TryGetValue(token.Data.ToString().ToLowerInvariant(), out var keyword) ? keyword : null;

                if (token.Type == TokenType.Function && CalcParser.IsCalcFamily(token.Data))
                {
                    var node = CalcParser.Parse(token, variables);
                    if (node is null || Calc(node) is not { } result) return null;
                    return result.Category is CalcCategory.Number or CalcCategory.Angle ? result.Value : null;
                }

                return HueDegrees(token);
            }

            // A calc() tree to a number, with the category it type-checks as: numbers, percentages and angles (in degrees), the only kinds a colour component takes.
            private static (double Value, CalcCategory Category)? Calc(CalcNode node)
            {
                switch (node)
                {
                    case NumberCalcNode number:
                        return (number.Value, CalcCategory.Number);
                    case PercentageCalcNode percentage:
                        return (percentage.Value, CalcCategory.Percentage);
                    case AngleCalcNode angle:
                        return (angle.Unit switch
                        {
                            Angle.Unit.Grad => angle.Value * 0.9,
                            Angle.Unit.Turn => angle.Value * 360.0,
                            Angle.Unit.Rad => angle.Value * 180.0 / Math.PI,
                            _ => angle.Value
                        }, CalcCategory.Angle);
                    case UnaryCalcNode unary:
                        return Calc(unary.Operand) is { } operand ? (unary.Negative ? -operand.Value : operand.Value, operand.Category) : null;
                    case BinaryCalcNode binary:
                    {
                        if (Calc(binary.Left) is not { } left || Calc(binary.Right) is not { } right) return null;
                        switch (binary.Operator)
                        {
                            case '+' when left.Category == right.Category:
                                return (left.Value + right.Value, left.Category);
                            case '-' when left.Category == right.Category:
                                return (left.Value - right.Value, left.Category);
                            case '*' when left.Category == CalcCategory.Number:
                                return (left.Value * right.Value, right.Category);
                            case '*' when right.Category == CalcCategory.Number:
                                return (left.Value * right.Value, left.Category);
                            case '/' when right.Category == CalcCategory.Number && right.Value != 0:
                                return (left.Value / right.Value, left.Category);
                            default:
                                return null;
                        }
                    }
                    case CallCalcNode call:
                    {
                        var values = new List<(double Value, CalcCategory Category)>(call.Arguments.Count);
                        foreach (var argument in call.Arguments)
                        {
                            if (Calc(argument) is not { } value) return null;
                            if (values.Count > 0 && values[0].Category != value.Category) return null;
                            values.Add(value);
                        }

                        if (values.Count == 0) return null;
                        var category = values[0].Category;
                        if (call.Name.Equals(FunctionNames.Min, StringComparison.OrdinalIgnoreCase)) return (values.Min(v => v.Value), category);
                        if (call.Name.Equals(FunctionNames.Max, StringComparison.OrdinalIgnoreCase)) return (values.Max(v => v.Value), category);
                        if (call.Name.Equals(FunctionNames.Clamp, StringComparison.OrdinalIgnoreCase) && values.Count == 3)
                            return (values[0].Value > values[2].Value ? values[2].Value : Math.Clamp(values[1].Value, values[0].Value, values[2].Value), category);
                        return null;
                    }
                    default:
                        return null;
                }
            }

            public static Color? Build(RelativeSpec spec, double[] v, float alpha, bool wantOklab, out double[] oklab)
            {
                oklab = null;
                switch (spec.Function)
                {
                    case "rgb":
                        return new Color((byte)Math.Round(Math.Clamp(v[0], 0, 255)), (byte)Math.Round(Math.Clamp(v[1], 0, 255)), (byte)Math.Round(Math.Clamp(v[2], 0, 255)), Clamp01Byte(alpha));
                    case "hsl":
                        return Color.FromHsla((float)(v[0] / 360.0), (float)(v[1] / 100.0), (float)(v[2] / 100.0), alpha);
                    case "hwb":
                        return Color.FromHwba((float)(v[0] / 360.0), (float)(v[1] / 100.0), (float)(v[2] / 100.0), alpha);
                    case "lab":
                    case "oklab":
                    {
                        var reference = spec.References[0];
                        double[] coords = [Math.Clamp(v[0], 0, reference), v[1], v[2]];
                        if (wantOklab) oklab = ColorSpaceMath.ToOklab(coords, spec.Space);
                        return Color.FromSpaceComponents(coords, spec.Space, alpha);
                    }
                    case "lch":
                    case "oklch":
                    {
                        var reference = spec.References[0];
                        double[] coords = [Math.Clamp(v[0], 0, reference), Math.Max(v[1], 0), v[2]];
                        if (wantOklab) oklab = ColorSpaceMath.ToOklab(coords, spec.Space);
                        return Color.FromSpaceComponents(coords, spec.Space, alpha);
                    }
                    default:
                        if (wantOklab) oklab = ColorSpaceMath.ToOklab(v, spec.Space);
                        return Color.FromSpaceComponents(v, spec.Space, alpha);
                }
            }
        }
    }
}
