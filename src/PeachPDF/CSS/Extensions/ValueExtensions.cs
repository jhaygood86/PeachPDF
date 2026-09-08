#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    internal static class ValueExtensions
    {
        private static bool IsWeight(int value)
        {
            // CSS Fonts Level 4 font-weight grammar: <number [1,1000]>.
            return value is >= 1 and <= 1000;
        }

        public static Token? OnlyOrDefault(this IReadOnlyList<Token> value)
        {
            return value.Count == 1 ? value[0] : null;
        }

        // Token?-returning replacements for the LINQ .OfType<X>().FirstOrDefault()/.SingleOrDefault()
        // pattern the former Token class hierarchy allowed - a plain Where(...).FirstOrDefault() against
        // the struct Token would silently return default(Token) (a real, plausible-looking value, not an
        // error) when nothing matches, corrupting the "nothing found" case a null reference used to
        // represent safely.
        public static Token? FirstOfTypeOrNull(this IReadOnlyList<Token> value, TokenType type)
        {
            for (var i = 0; i < value.Count; i++)
            {
                if (value[i].Type == type) return value[i];
            }

            return null;
        }

        public static Token? SingleOfTypeOrNull(this IReadOnlyList<Token> value, TokenType type)
        {
            Token? result = null;

            for (var i = 0; i < value.Count; i++)
            {
                if (value[i].Type != type) continue;
                if (result != null) return null;
                result = value[i];
            }

            return result;
        }

        // Predicate-based siblings of FirstOfTypeOrNull/SingleOfTypeOrNull, for the
        // .OfType<X>().FirstOrDefault(t => ...)/.SingleOrDefault(t => ...) shape (a type check plus an
        // extra condition) - same Token?/default(Token) hazard, same fix.
        public static Token? FirstOrNull(this IReadOnlyList<Token> value, Func<Token, bool> predicate)
        {
            for (var i = 0; i < value.Count; i++)
            {
                if (predicate(value[i])) return value[i];
            }

            return null;
        }

        public static Token? SingleOrNull(this IReadOnlyList<Token> value, Func<Token, bool> predicate)
        {
            Token? result = null;

            for (var i = 0; i < value.Count; i++)
            {
                if (!predicate(value[i])) continue;
                if (result != null) return null;
                result = value[i];
            }

            return result;
        }

        public static bool Is(this IReadOnlyList<Token> value, string expected)
        {
            var identifier = value.ToIdentifier();
            return identifier != null && identifier.Isi(expected);
        }

        public static string ToUri(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is { Type: TokenType.Url } token) return token.Data;

            return null;
        }

        public static Length? ToDistance(this IReadOnlyList<Token> value)
        {
            var percent = value.ToPercent();

            return percent.HasValue
                ? new Length(percent.Value.Value, Length.Unit.Percent)
                : value.ToLength();
        }

        public static Length ToLength(this FontSize fontSize)
        {
            switch (fontSize)
            {
                case FontSize.Big: //1.5em
                    return new Length(1.5f, Length.Unit.Em);
                case FontSize.Huge: //2em
                    return new Length(2f, Length.Unit.Em);
                case FontSize.Large: //1.2em
                    return new Length(1.2f, Length.Unit.Em);
                case FontSize.Larger: //*120%
                    return new Length(120f, Length.Unit.Percent);
                case FontSize.Little: //0.75em
                    return new Length(0.75f, Length.Unit.Em);
                case FontSize.Small: //8/9em
                    return new Length(8f / 9f, Length.Unit.Em);
                case FontSize.Smaller: //*80%
                    return new Length(80f, Length.Unit.Percent);
                case FontSize.Tiny: //0.6em
                    return new Length(0.6f, Length.Unit.Em);
                default: //1em
                    return new Length(1f, Length.Unit.Em);
            }
        }

        public static Percent? ToPercent(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is { Type: TokenType.Percentage } token)
                return new Percent(token.Value);

            return null;
        }

        public static Percent? ToPercentOrFraction(this IReadOnlyList<Token> value)
        {
            var percent = value.ToPercent();

            if (percent is not null)
            {
                return percent;
            }

            var element = value.OnlyOrDefault();
            if (element is not { Type: TokenType.Number } token)
            {
                return null;
            }

            try
            {
                var number = token.Value;
                var percentage = number * 100;
                return new Percent(percentage);
            }
            catch
            {
                return null;
            }
        }

        public static Number? ToPercentOrNumber(this IReadOnlyList<Token> value)
        {
            var percent = value.ToPercent();

            if (percent is not null)
            {
                return new Number(percent.Value.Value, Number.Unit.Percent);
            }

            var element = value.OnlyOrDefault();
            if (element is not { Type: TokenType.Number } token)
            {
                return null;
            }

            try
            {
                return new Number(token.Value, Number.Unit.Float);
            }
            catch
            {
                return null;
            }
        }

        public static string ToCssString(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is { Type: TokenType.String } token) return token.Data;

            return null;
        }

        public static string ToLiterals(this IReadOnlyList<Token> value)
        {
            if (value.Count == 0) return null;

            var elements = new List<string>();

            for (var i = 0; i < value.Count; i++)
            {
                if (value[i].Type != TokenType.Ident) return null;

                elements.Add(value[i].Data);

                i++;
                if (i >= value.Count) break;
                if (value[i].Type != TokenType.Whitespace) return null;
            }

            return string.Join(" ", elements);
        }

        public static string ToIdentifier(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is { Type: TokenType.Ident } token) return token.Data.ToLowerInvariant();

            return null;
        }

        public static string ToIdentifierCaseInsensitive(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is { Type: TokenType.Ident } token) return token.Data;

            return null;
        }

        public static string ToAnimatableIdentifier(this IReadOnlyList<Token> value)
        {
            var identifier = value.ToIdentifier();

            if (identifier != null &&
                (identifier.Isi(Keywords.All) || PropertyFactory.Instance.IsAnimatable(identifier)))
            {
                return identifier;
            }

            return null;
        }

        public static float? ToSingle(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is { Type: TokenType.Number } token) return token.Value;

            return null;
        }

        public static float? ToNaturalSingle(this IReadOnlyList<Token> value)
        {
            var element = value.ToSingle();
            return element >= 0f ? element : null;
        }

        public static float? ToGreaterOrEqualOneSingle(this IReadOnlyList<Token> value)
        {
            var element = value.ToSingle();
            return element >= 1f ? element : null;
        }

        public static int? ToInteger(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is { Type: TokenType.Number } number && number.IsInteger)
            {
                return number.IntegerValue;
            }

            // CSS Values and Units Level 4 §10.9: a math function is accepted anywhere <integer> is
            // expected, provided it type-checks as a plain <number> (no length/percentage/angle leaf -
            // those categories are meaningless for e.g. z-index/order), and its result is rounded to the
            // nearest integer (ties away from zero) rather than kept symbolic. Unlike a length calc(),
            // a Number-category calc() has no em/rem/percent-relative leaf to defer to layout, so it can
            // - and must, to satisfy every existing int.Parse(box.ZIndex)-style consumer - be folded to a
            // concrete integer right here rather than carried as a CalcValue.
            if (element is { Type: TokenType.Function } function && CalcParser.IsCalcFamily(function.Data))
            {
                var node = CalcParser.Parse(function);
                if (node is null || CalcTypeChecker.Check(node) != CalcCategory.Number) return null;

                var result = CalcEvaluator.Evaluate(node, new CalcContext(1, 0, 0));
                return result.HasValue ? (int)Math.Round(result.Value, MidpointRounding.AwayFromZero) : null;
            }

            return null;
        }

        public static int? ToNaturalInteger(this IReadOnlyList<Token> value)
        {
            var element = value.ToInteger();
            return element >= 0 ? element : null;
        }

        public static int? ToPositiveInteger(this IReadOnlyList<Token> value)
        {
            var element = value.ToInteger();
            return element > 0 ? element : null;
        }

        public static int? ToWeightInteger(this IReadOnlyList<Token> value)
        {
            var element = value.ToPositiveInteger();
            return element.HasValue && IsWeight(element.Value) ? element : null;
        }

        public static int? ToBinary(this IReadOnlyList<Token> value)
        {
            var element = value.ToInteger();
            return element.HasValue && (element.Value == 0 || element.Value == 1) ? element : null;
        }

        public static float? ToAlphaValue(this IReadOnlyList<Token> value)
        {
            var element = value.ToNaturalSingle();

            if (element.HasValue) return Math.Min(element.Value, 1f);

            var percent = value.ToPercent();

            return percent?.NormalizedValue;
        }

        public static byte? ToRgbComponent(this IReadOnlyList<Token> value)
        {
            var element = value.ToNaturalInteger();

            if (element.HasValue) return (byte)Math.Min(element.Value, 255);

            var percent = value.ToPercent();

            if (!percent.HasValue) return null;

            return (byte)(255f * percent.Value.NormalizedValue);
        }

        public static Angle? ToAngle(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is not { Type: TokenType.Dimension } token) return null;

            var unit = Angle.GetUnit(token.Unit);

            if (unit != Angle.Unit.None) return new Angle(token.Value, unit);

            return null;
        }

        public static Angle? ToAngleNumber(this IReadOnlyList<Token> value)
        {
            var angle = value.ToAngle();

            if (angle.HasValue) return angle.Value;

            var number = value.ToSingle();

            if (!number.HasValue) return null;

            return new Angle(number.Value, Angle.Unit.Deg);
        }

        public static Length? ToLength(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is { } token)
            {
                switch (token.Type)
                {
                    case TokenType.Dimension:
                        {
                            var unit = Length.GetUnit(token.Unit);

                            if (unit != Length.Unit.None) return new Length(token.Value, unit);
                            break;
                        }
                    case TokenType.Number when token.Value == 0f:
                        return Length.Zero;
                }
            }

            return null;
        }

        public static Resolution? ToResolution(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is not { Type: TokenType.Dimension } token) return null;

            var unit = Resolution.GetUnit(token.Unit);

            if (unit != Resolution.Unit.None) return new Resolution(token.Value, unit);

            return null;
        }

        public static Time? ToTime(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is not { Type: TokenType.Dimension } token) return null;

            var unit = Time.GetUnit(token.Unit);

            if (unit != Time.Unit.None) return new Time(token.Value, unit);

            return null;
        }

        public static Length? ToBorderWidth(this IReadOnlyList<Token> value)
        {
            var length = value.ToLength();

            if (length != null) return length;

            if (value.Is(Keywords.Thin)) return Length.Thin;

            if (value.Is(Keywords.Medium)) return Length.Medium;

            return value.Is(Keywords.Thick) ? Length.Thick : length;
        }

        public static List<List<Token>> ToItems(this IReadOnlyList<Token> value)
        {
            var list = new List<List<Token>>();
            var current = new List<Token>();
            var nested = 0;
            list.Add(current);

            for (var i = 0; i < value.Count; i++)
            {
                var token = value[i];
                var whitespace = token.Type == TokenType.Whitespace;
                var newItem = token.Type == TokenType.String || token.Type == TokenType.Url ||
                              token.Type == TokenType.Function;

                if (nested == 0 && (whitespace || newItem))
                {
                    if (current.Count != 0)
                    {
                        current = new List<Token>();
                        list.Add(current);
                    }

                    if (whitespace) continue;
                }
                else if (token.Type == TokenType.RoundBracketOpen)
                {
                    nested++;
                }
                else if (token.Type == TokenType.RoundBracketClose)
                {
                    nested--;
                }

                current.Add(token);
            }

            return list;
        }

        public static void Trim(this List<Token> value)
        {
            var begin = 0;
            var end = value.Count - 1;

            while (begin < end)
                if (value[begin].Type == TokenType.Whitespace)
                    begin++;
                else if (value[end].Type == TokenType.Whitespace)
                    end--;
                else
                    break;

            value.RemoveRange(++end, value.Count - end);
            value.RemoveRange(0, begin);
        }

        public static List<List<Token>> ToList(this IReadOnlyList<Token> value)
        {
            var list = new List<List<Token>>();
            var current = new List<Token>();
            var nested = 0;
            list.Add(current);

            for (var i = 0; i < value.Count; i++)
            {
                var token = value[i];

                if (nested == 0 && token.Type == TokenType.Comma)
                {
                    current = new List<Token>();
                    list.Add(current);
                    continue;
                }

                switch (token.Type)
                {
                    case TokenType.RoundBracketOpen:
                        nested++;
                        break;
                    case TokenType.RoundBracketClose:
                        nested--;
                        break;
                    case TokenType.Whitespace when current.Count == 0:
                        continue;
                }

                current.Add(token);
            }

            foreach (var token in list) token.Trim();

            return list;
        }

        public static string ToText(this IReadOnlyList<Token> value)
        {
            var sb = Pool.NewStringBuilder();

            for (var i = 0; i < value.Count; i++)
            {
                sb.Append(value[i].ToValue());
            }

            return sb.ToPool();
        }

        public static bool ContainsFunction(this IReadOnlyList<Token> tokens, string functionName)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token is { Type: TokenType.Function } function &&
                    (function.Data.Isi(functionName) || function.ArgumentTokens.ContainsFunction(functionName)))
                {
                    return true;
                }
            }

            return false;
        }

        public static Color? ToColor(this IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            if (element is { Type: TokenType.Ident } identToken) return Color.FromName(identToken.Data);

            if (element is { Type: TokenType.Color } colorToken && !colorToken.IsValid)
            {
                return Color.FromHex(colorToken.Data);
            }

            return null;
        }
    }
}
