#nullable disable

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The shared grammar for the <c>hyphenate-limit-chars</c> value (CSS Text 4 §6.3.4):
    /// <c>[ auto | &lt;integer [0,∞]&gt; ]{1,3}</c>. Used by both Layer A (to validate/accept-or-reject at
    /// parse time) and Layer B (to resolve the three limits during layout) — the <see cref="AspectRatioGrammar"/>
    /// precedent. The first value is the minimum total characters in a hyphenated word, the second the
    /// minimum before the break, the third the minimum after it; a missing third value takes the second's
    /// value, and a missing second value is <c>auto</c> (the UA-chosen minimum — see
    /// <see cref="PeachPDF.Html.Core.Dom.CssLayoutEngine"/>'s consumption, which lets the hyphenation
    /// pattern data's own per-language minimum govern whichever side is <c>auto</c> rather than
    /// substituting a hardcoded number).
    /// </summary>
    internal static class HyphenateLimitCharsGrammar
    {
        /// <summary>
        /// Validates the token stream as a <c>hyphenate-limit-chars</c> value. Each of <paramref name="wordMin"/>/
        /// <paramref name="beforeMin"/>/<paramref name="afterMin"/> is null for <c>auto</c> (or an omitted
        /// trailing value), or the parsed non-negative minimum.
        /// </summary>
        internal static bool TryParse(IReadOnlyList<Token> tokens, out int? wordMin, out int? beforeMin, out int? afterMin)
        {
            wordMin = null;
            beforeMin = null;
            afterMin = null;

            var toks = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (toks.Length is 0 or > 3) return false;

            if (!TryValue(toks[0], out wordMin)) return false;

            if (toks.Length >= 2)
            {
                if (!TryValue(toks[1], out beforeMin)) return false;
            }

            if (toks.Length == 3)
            {
                if (!TryValue(toks[2], out afterMin)) return false;
            }
            else
            {
                // "If the third value is missing, it is the same as the second value" (also auto, if the
                // second was itself omitted/auto).
                afterMin = beforeMin;
            }

            return true;
        }

        /// <summary>Convenience overload that tokenizes <paramref name="value"/> first.</summary>
        internal static bool TryParse(string value, out int? wordMin, out int? beforeMin, out int? afterMin) =>
            TryParse(global::PeachPDF.Html.Core.Parse.CssValueParser.GetCssTokens(value), out wordMin, out beforeMin, out afterMin);

        /// <summary>Re-serializes the three limits back into a canonical, always-fully-explicit value.</summary>
        internal static string Serialize(int? wordMin, int? beforeMin, int? afterMin) =>
            $"{Component(wordMin)} {Component(beforeMin)} {Component(afterMin)}";

        /// <summary>
        /// Returns <paramref name="current"/> (a raw, already-cascaded <c>hyphenate-limit-chars</c> value)
        /// with its "before" component replaced by <paramref name="beforeValueText"/> — the merge Prince's
        /// standalone <c>hyphenate-before</c>/<c>-prince-hyphenate-before</c> properties need, since CSS
        /// Text 4 has no separate before/after longhand of its own to alias onto.
        /// </summary>
        internal static string WithBefore(string current, string beforeValueText)
        {
            TryParse(current, out var word, out _, out var after);
            return Serialize(word, int.Parse(beforeValueText, CultureInfo.InvariantCulture), after);
        }

        /// <summary>The same merge as <see cref="WithBefore"/>, for the "after" component.</summary>
        internal static string WithAfter(string current, string afterValueText)
        {
            TryParse(current, out var word, out var before, out _);
            return Serialize(word, before, int.Parse(afterValueText, CultureInfo.InvariantCulture));
        }

        private static string Component(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? Keywords.Auto;

        private static bool TryValue(Token token, out int? value)
        {
            if (token.Type == TokenType.Ident && token.Data.Isi(Keywords.Auto))
            {
                value = null;
                return true;
            }

            if (token is NumberToken { IsInteger: true } number && number.IntegerValue >= 0)
            {
                value = number.IntegerValue;
                return true;
            }

            value = null;
            return false;
        }
    }
}
