#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Shared, layer-agnostic grammar for the CSS <c>text-shadow</c> value (CSS Text Decoration Level 3 §4.1): the
    /// keyword <c>none</c>, or a comma-separated list of shadows, each
    /// <c>[ &lt;color&gt;? &amp;&amp; &lt;length&gt;{2,3} ]</c> (offset-x, offset-y, blur radius). It is
    /// <see cref="BoxShadowGrammar"/>'s production minus <c>inset</c> and the spread distance, so it reuses that
    /// grammar's classifier, colour validation and comma splitting rather than deriving a second one.
    /// </summary>
    /// <remarks>
    /// Like <see cref="BoxShadowGrammar"/>, each layer's lengths and colour are kept as raw authored strings, since a
    /// length may be font-relative and only the paint layer can resolve it against the box.
    /// </remarks>
    internal static class TextShadowGrammar
    {
        /// <summary>
        /// One parsed shadow. <see cref="Blur"/> is <c>"0"</c> when omitted; <see cref="Color"/> is null when omitted,
        /// meaning "the element's own colour" (<c>currentColor</c>) at paint time.
        /// </summary>
        internal sealed class ShadowLayer
        {
            public string OffsetX { get; init; }
            public string OffsetY { get; init; }
            public string Blur { get; init; }
            public string Color { get; init; }
        }

        /// <summary>
        /// Parses a <c>text-shadow</c> value's tokens into its shadows (first-declared first), or returns null when the
        /// value is not a valid <c>text-shadow</c>. The keyword <c>none</c> returns an <b>empty list</b>, distinct from a
        /// null (invalid) result, matching <see cref="BoxShadowGrammar.TryParse"/>.
        /// </summary>
        internal static List<ShadowLayer> TryParse(IReadOnlyList<Token> tokens)
        {
            var significant = BoxShadowGrammar.NormalizeHexColorTokens(tokens.Where(t => t.Type != TokenType.Whitespace).ToArray());

            if (significant.Count == 0) return null;

            if (significant is [{ Type: TokenType.Ident } ident] && ident.Data.Isi(Keywords.None))
                return [];

            var layers = new List<ShadowLayer>();

            foreach (var group in BoxShadowGrammar.SplitByComma(significant))
            {
                if (group.Count == 0) return null; // leading/trailing/doubled comma

                var layer = ParseLayer(group);
                if (layer is null) return null;

                layers.Add(layer);
            }

            return layers;
        }

        private static ShadowLayer ParseLayer(IReadOnlyList<Token> group)
        {
            if (!BoxShadowGrammar.TryClassify(group, allowInset: false, out _, out var lengths, out var colorTokens))
                return null;

            // offset-x, offset-y, [blur-radius]: no spread, and the blur radius may not be negative.
            if (lengths.Count is < 2 or > 3) return null;
            if (lengths.Count == 3 && BoxShadowGrammar.LengthValue(lengths[2]) < 0) return null;

            string color = null;
            if (colorTokens.Count > 0)
            {
                if (!BoxShadowGrammar.IsValidColor(colorTokens)) return null;
                color = string.Concat(colorTokens.Select(t => t.ToValue()));
            }

            return new ShadowLayer
            {
                OffsetX = lengths[0].ToValue(),
                OffsetY = lengths[1].ToValue(),
                Blur = lengths.Count == 3 ? lengths[2].ToValue() : "0",
                Color = color,
            };
        }
    }
}
