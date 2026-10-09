using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core.Animation
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

        private readonly record struct Token(TokenKind Kind, string Raw, double Number = 0, string Unit = "", double[]? Rgba = null);

        private static readonly HashSet<string> LengthUnits = new(StringComparer.OrdinalIgnoreCase)
        {
            "px", "pt", "pc", "in", "cm", "mm", "q", "em", "rem", "ex", "ch", "vw", "vh", "vmin", "vmax", "%"
        };

        private static readonly HashSet<string> IntegerProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "z-index", "order", "column-count", "orphans", "widows"
        };

        /// <summary>
        /// Mixes <paramref name="from"/> and <paramref name="to"/> at <paramref name="t"/> (0 = from, 1 = to; may
        /// leave 0 to 1 for an easing that overshoots).
        /// </summary>
        public static string Interpolate(CssValueParser valueParser, string property, string from, string to, double t)
        {
            from = from.Trim();
            to = to.Trim();

            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return from;

            // CSS Transitions 1 §3 (visibility): visible for the whole of the way between visible and anything else.
            if (property.Equals("visibility", StringComparison.OrdinalIgnoreCase) && t > 0 && t < 1)
            {
                if (from.Equals("visible", StringComparison.OrdinalIgnoreCase) || to.Equals("visible", StringComparison.OrdinalIgnoreCase))
                    return "visible";
            }

            var a = Scan(valueParser, from);
            var b = Scan(valueParser, to);

            if (!SameShape(a, b))
            {
                // CSS Transforms 1 §9 / Filter Effects 1 §9: `none` is the empty list, i.e. every function of the
                // other value at its identity argument.
                if (IsNone(a) && TryIdentity(property, b) is { } identityA) a = identityA;
                else if (IsNone(b) && TryIdentity(property, a) is { } identityB) b = identityB;
            }

            if (SameShape(a, b) && TryMix(property, a, b, t, out var mixed)) return mixed;

            return t < 0.5 ? from : to;
        }

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
                        sb.Append(MixColors(x.Rgba!, y.Rgba!, t));
                        break;
                    default:
                        sb.Append(x.Raw);
                        break;
                }
            }

            result = sb.ToString();
            return true;
        }

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

        /// <summary>Premultiplied mix in sRGB, the interpolation space of legacy colour syntaxes (CSS Color 4 §13).</summary>
        private static string MixColors(double[] from, double[] to, double t)
        {
            var alpha = from[3] + (to[3] - from[3]) * t;
            alpha = Math.Clamp(alpha, 0, 1);

            if (alpha <= 0) return "rgba(0, 0, 0, 0)";

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

        private static string Format(double value)
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
                            || name.Equals("rgb", StringComparison.OrdinalIgnoreCase) || name.Equals("rgba", StringComparison.OrdinalIgnoreCase))
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
                return new Token(TokenKind.Color, text, Rgba: [color.R, color.G, color.B, color.A / 255d]);

            return new Token(TokenKind.Word, text);
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
