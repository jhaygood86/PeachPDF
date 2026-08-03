#nullable disable

using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The shared grammar for the <c>text-indent</c> value
    /// (<see href="https://www.w3.org/TR/css-text-3/#text-indent-property">CSS Text 3 §3</see>:
    /// <c>&lt;length-percentage&gt; &amp;&amp; hanging? &amp;&amp; each-line?</c>). Used by both Layer A
    /// (to validate/accept-or-reject at parse time) and Layer B (to resolve the used indent/keyword
    /// flags during layout), so the grammar is defined once - the <see cref="AspectRatioGrammar"/>
    /// precedent.
    /// </summary>
    internal static class TextIndentGrammar
    {
        /// <summary>
        /// Validates the token stream as a <c>text-indent</c> value. On success, <paramref name="length"/>
        /// wraps the single token that is the value's mandatory <c>&lt;length-percentage&gt;</c> component
        /// (so <c>.Text</c> reconstructs its exact original spelling, including a <c>calc()</c>
        /// expression's internal whitespace), and <paramref name="hasHanging"/>/<paramref name="hasEachLine"/>
        /// report whether the optional <c>hanging</c>/<c>each-line</c> keywords were present, in any order.
        /// Returns false for anything that is not a valid value: a missing length-percentage, a duplicate
        /// keyword or length token, or a length-percentage-shaped token that doesn't actually validate
        /// (e.g. <c>none</c>).
        /// </summary>
        internal static bool TryParse(IReadOnlyList<Token> tokens, out TokenValue length, out bool hasHanging,
            out bool hasEachLine)
        {
            length = null;
            hasHanging = false;
            hasEachLine = false;

            Token lengthToken = null;

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Whitespace) continue;

                if (IsHanging(token))
                {
                    if (hasHanging) return false;
                    hasHanging = true;
                    continue;
                }

                if (IsEachLine(token))
                {
                    if (hasEachLine) return false;
                    hasEachLine = true;
                    continue;
                }

                if (lengthToken is not null) return false;
                lengthToken = token;
            }

            if (lengthToken is null) return false;

            // Reuse the shared length/percentage/calc() grammar rather than re-deriving what counts as a
            // length here - this also rejects a non-length token (e.g. `none`) that isn't `hanging`/
            // `each-line` either.
            if (Converters.LengthOrPercentConverter.Convert(new[] { lengthToken }) is null) return false;

            length = new TokenValue(new[] { lengthToken });
            return true;
        }

        private static bool IsHanging(Token token) => token.Type == TokenType.Ident && token.Data.Isi(Keywords.Hanging);

        private static bool IsEachLine(Token token) => token.Type == TokenType.Ident && token.Data.Isi(Keywords.EachLine);
    }
}
