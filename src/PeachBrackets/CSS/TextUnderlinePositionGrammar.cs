#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Shared grammar for <c>text-underline-position</c>'s compound value
    /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-underline-position-property">css-text-decor-4
    /// §2.5</see>): <c>auto | [ from-font | under ] || [ left | right ]</c>. <c>auto</c> is the grammar's
    /// other top-level alternative and so stands alone - it never combines with <c>left</c>/<c>right</c>,
    /// unlike the (also-optional) <c>from-font</c>/<c>under</c> keyword. Used by both Layer A
    /// (<see cref="TextUnderlinePositionCompoundConverter"/>, to validate/accept-or-reject at parse time)
    /// and the generated <c>text-underline-position</c> validator/setter in <c>css-properties.json</c>, so
    /// the two never independently re-derive the grammar - the <see cref="PositionValueGrammar"/>
    /// precedent.
    /// </summary>
    internal static class TextUnderlinePositionGrammar
    {
        /// <summary>
        /// Resolves the token stream into its <see cref="TextUnderlinePosition"/> (the <c>from-font</c>/
        /// <c>under</c> half) and <see cref="TextUnderlineSide"/> (the <c>left</c>/<c>right</c> half),
        /// each defaulting to <c>Auto</c> when its own keyword is omitted.
        /// </summary>
        internal static bool TryParse(IReadOnlyList<Token> tokens, out TextUnderlinePosition position, out TextUnderlineSide side)
        {
            position = TextUnderlinePosition.Auto;
            side = TextUnderlineSide.Auto;

            var toks = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (toks.Length is 0 or > 2) return false;

            // A bare "auto" is the grammar's other top-level alternative - accepted only alone; the
            // general loop below rejects it in combination with anything else (including itself twice).
            if (toks.Length == 1 && toks[0].Type == TokenType.Ident && toks[0].Data.Isi(Keywords.Auto))
                return true;

            var positionSet = false;
            var sideSet = false;

            foreach (var token in toks)
            {
                if (token.Type != TokenType.Ident) return false;

                var text = token.Data.ToString();

                if (Map.TextUnderlinePositions.TryGetValue(text, out var matchedPosition))
                {
                    if (matchedPosition == TextUnderlinePosition.Auto || positionSet) return false;
                    position = matchedPosition;
                    positionSet = true;
                }
                else if (Map.TextUnderlineSides.TryGetValue(text, out var matchedSide))
                {
                    if (sideSet) return false;
                    side = matchedSide;
                    sideSet = true;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Convenience overload that tokenizes <paramref name="value"/> first.</summary>
        internal static bool TryParse(string value, out TextUnderlinePosition position, out TextUnderlineSide side)
        {
            using var pooledTokens = global::PeachPDF.Html.Core.Parse.CssValueParser.GetCssTokensPooled(value);
            List<Token> tokens = pooledTokens;
            return TryParse(tokens, out position, out side);
        }

        /// <summary>
        /// Re-serializes a resolved position/side pair back into its canonical, always-lowercase value -
        /// the text both <see cref="CssProperty{T}"/> fields (<c>TextUnderlinePosition</c> and
        /// <c>TextUnderlineSide</c>) store, matching <see cref="CssProperty{T}.FromCssText"/>'s own
        /// canonicalization convention (issue #598).
        /// </summary>
        internal static string CanonicalCssText(TextUnderlinePosition position, TextUnderlineSide side)
        {
            if (position == TextUnderlinePosition.Auto && side == TextUnderlineSide.Auto) return Keywords.Auto;

            var parts = new List<string>(2);

            if (position == TextUnderlinePosition.FromFont) parts.Add(Keywords.FromFont);
            else if (position == TextUnderlinePosition.Under) parts.Add(Keywords.Under);

            if (side == TextUnderlineSide.Left) parts.Add(Keywords.Left);
            else if (side == TextUnderlineSide.Right) parts.Add(Keywords.Right);

            return string.Join(" ", parts);
        }
    }
}
