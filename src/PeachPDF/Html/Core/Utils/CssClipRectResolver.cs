using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Layer B of the legacy CSS 2.1 <c>clip</c> pipeline (§11.1.2): resolves a <c>clip</c> value (already
    /// validated and preserved verbatim by <see cref="Converters.ShapeConverter"/> at parse time) into an
    /// absolute-coordinate <see cref="RRect"/> the paint hook pushes directly via
    /// <c>RGraphics.PushClip(RRect)</c>. Unlike <see cref="CssClipPathResolver"/>, no
    /// <see cref="RGraphicsPath"/>/<c>PixelsPerPoint</c> division is needed here: <c>rect()</c> is always
    /// axis-aligned, and <c>PushClip(RRect)</c> already divides by <c>PixelsPerPoint</c> internally, unlike
    /// the <c>PushClip(RGraphicsPath)</c> overload <see cref="CssClipPathResolver"/> has to pre-divide for.
    /// </summary>
    internal static class CssClipRectResolver
    {
        /// <summary>
        /// Builds the clip rectangle for <paramref name="value"/> against <paramref name="referenceBox"/>
        /// (the absolute border-box rectangle, in paint coordinates).
        /// </summary>
        /// <returns>
        /// <c>true</c> with <paramref name="clipRect"/> populated for a <c>rect(...)</c> value;
        /// <c>false</c> for the bare keyword <c>auto</c> (the initial value - "do not clip") or anything
        /// invalid, in which case the caller pushes no clip.
        /// </returns>
        public static bool TryBuildClipRect(string value, RRect referenceBox, CssBox box, out RRect clipRect)
        {
            clipRect = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var tokens = CssValueParser.GetCssTokens(value);

            if (tokens is [KeywordToken { Data: Keywords.Auto }]) return false;
            if (tokens.Count != 1 || tokens[0] is not FunctionToken function) return false;
            if (!function.Data.Isi(FunctionNames.Rect)) return false;

            // rect()'s two legal separators (CSS 2.1 §11.1.2 allows both the comma and the older
            // space-separated form) each carry exactly one token per edge - a length, a calc()
            // (one FunctionToken, whose own Token.ToValue() reconstructs the full "calc(...)" text), or
            // the "auto" keyword - so stripping the separators and reading what's left positionally
            // handles both forms uniformly; no need to group multi-token arguments the way a
            // multi-token shape argument (e.g. a polygon vertex pair) would.
            var edges = function.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                .ToArray();

            if (edges.Length != 4) return false;

            // CSS 2.1 §11.1.2: top/bottom are offsets from the top border edge; right/left are offsets
            // from the left border edge - NOT a width/height pair. "auto" on an edge means "use the
            // box's own edge there" (i.e. don't clip that side).
            var top = ResolveEdge(edges[0], box, referenceBox.Height, referenceBox.Y, referenceBox.Y);
            var right = ResolveEdge(edges[1], box, referenceBox.Width, referenceBox.X, referenceBox.Right);
            var bottom = ResolveEdge(edges[2], box, referenceBox.Height, referenceBox.Y, referenceBox.Bottom);
            var left = ResolveEdge(edges[3], box, referenceBox.Width, referenceBox.X, referenceBox.X);

            clipRect = RRect.FromLTRB(left, top, right, bottom);
            return true;
        }

        /// <summary>Resolves one <c>rect()</c> edge: <c>auto</c> resolves to <paramref name="autoValue"/>
        /// (the box's own edge, already an absolute coordinate); otherwise <paramref name="origin"/> plus
        /// the parsed length (offset from that edge, per CSS 2.1 §11.1.2).</summary>
        private static double ResolveEdge(Token edge, CssBox box, double basis, double origin, double autoValue) =>
            edge is KeywordToken { Data: Keywords.Auto }
                ? autoValue
                : origin + CssValueParser.ParseLength(edge.ToValue(), basis, box);
    }
}
