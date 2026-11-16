using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The two quirks of the Quirks Mode Standard that change what a style sheet's value means: the hashless hex color (CSS Color 4, Appendix B: a
    /// <c>color</c> of <c>ff0000</c> or <c>123456</c> is a hex color) and the unitless length (CSS Values 4, Appendix C: a <c>width</c> of <c>100</c> is
    /// <c>100px</c>). Both are tried only when the value is not valid as written, so a value that means something otherwise keeps that
    /// meaning, and only for the properties the appendices list, and only for the values of a longhand or of a shorthand that is itself on the
    /// list (never inside <c>background</c>, a function or <c>calc()</c>, which these tokens never are).
    /// </summary>
    internal static class QuirkyValues
    {
        private static readonly FrozenSet<string> LengthProperties = new[]
        {
            "background-position", "border-spacing", "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
            "border-width", "bottom", "clip", "font-size", "height", "left", "letter-spacing", "margin-right", "margin-left", "margin-top",
            "margin-bottom", "margin", "max-height", "max-width", "min-height", "min-width", "padding-top", "padding-right", "padding-bottom",
            "padding-left", "padding", "right", "text-indent", "top", "vertical-align", "width", "word-spacing"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> ColorProperties = new[]
        {
            "background-color", "border-color", "border-top-color", "border-right-color", "border-bottom-color", "border-left-color", "color"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The value <paramref name="value"/> of <paramref name="propertyName"/> would have if a quirks-mode document were reading it, or
        /// <see langword="null"/> when neither quirk changes it.
        /// </summary>
        internal static TokenValue? Rewrite(string propertyName, TokenValue value)
        {
            if (LengthProperties.Contains(propertyName)) return Rewrite(value, RewriteLength);
            if (ColorProperties.Contains(propertyName)) return RewriteColors(value);
            return null;
        }

        private static TokenValue? Rewrite(TokenValue value, Func<Token, Token?> rewriteToken)
        {
            List<Token>? tokens = null;
            for (var i = 0; i < value.Count; i++)
            {
                var token = value[i];
                if (rewriteToken(token) is { } rewritten)
                {
                    tokens ??= [.. value];
                    tokens[i] = rewritten;
                }
            }

            return tokens is null ? null : new TokenValue(tokens);
        }

        // A bare number is that many CSS pixels. A zero already means zero length, and so never reaches here.
        private static Token? RewriteLength(Token token) =>
            token.Type == TokenType.Number ? Token.NewUnit(TokenType.Dimension, token.Data.ToString().AsMemory(), "px", token.Position) : null;

        /// <summary>
        /// The hashless hex color (CSS Color 4, Appendix B). The tokenizer cuts <c>00ff00</c> into the dimension <c>00ff</c> and the number <c>00</c>,
        /// so a color is read from a run of adjacent identifier, number and dimension tokens as the text they make together: an identifier of exactly
        /// three or six hex digits, or a number or dimension (an integer, with its unit) of at most six, padded with zeros on the left to six.
        /// </summary>
        private static TokenValue? RewriteColors(TokenValue value)
        {
            var changed = false;
            var output = new List<Token>(value.Count);
            var i = 0;

            while (i < value.Count)
            {
                if (!IsColorPart(value[i]))
                {
                    output.Add(value[i++]);
                    continue;
                }

                var start = i;
                var text = new System.Text.StringBuilder();
                while (i < value.Count && IsColorPart(value[i]))
                {
                    text.Append(value[i].Type == TokenType.Dimension ? value[i].Data.ToString() + value[i].Unit : value[i].Data.ToString());
                    i++;
                }

                if (HexDigitsOf(value[start], i - start, text.ToString()) is { } digits)
                {
                    changed = true;
                    output.Add(Token.NewColor(digits.AsMemory(), value[start].Position));
                }
                else
                {
                    for (var j = start; j < i; j++) output.Add(value[j]);
                }
            }

            return changed ? new TokenValue(output) : null;
        }

        private static bool IsColorPart(Token token) => token.Type is TokenType.Ident or TokenType.Number or TokenType.Dimension;

        private static string? HexDigitsOf(Token first, int tokenCount, string text)
        {
            if (!IsHex(text)) return null;

            // An identifier of exactly three or six hex digits.
            if (first.Type == TokenType.Ident) return tokenCount == 1 && text.Length is 3 or 6 ? text : null;

            // A number or a dimension: up to six digits, and a number on its own must be an integer.
            if (text.Length > 6 || (first.Type == TokenType.Number && !IsDecimal(text))) return null;

            return text.PadLeft(6, '0');
        }

        private static bool IsDecimal(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return false;
            foreach (var c in text)
            {
                if (c is < '0' or > '9') return false;
            }

            return true;
        }

        private static bool IsHex(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return false;
            foreach (var c in text)
            {
                if (!char.IsAsciiHexDigit(c)) return false;
            }

            return true;
        }
    }
}
