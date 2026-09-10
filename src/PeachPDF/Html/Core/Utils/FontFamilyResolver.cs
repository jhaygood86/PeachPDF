using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves a (possibly comma-separated) CSS font-family list to a real <see cref="RFont"/>, trying
    /// each candidate family in order via <see cref="RAdapter.GetFont"/> until one resolves. Shared by
    /// <c>CssBox.GetCachedFont</c> (in-flow content) and <c>MarginBoxRenderer.BuildFont</c> (@page margin
    /// boxes) so the font-stack-fallback algorithm exists in exactly one place.
    /// </summary>
    internal static class FontFamilyResolver
    {
        internal static RFont? Resolve(RAdapter adapter, string fontFamilyList, double fsize, RFontStyle style, int? weight = null, int? stretch = null, double? obliqueSkewSinus = null)
        {
            var families = fontFamilyList.Split(',');

            if (families.Length == 1)
            {
                return adapter.GetFont(fontFamilyList, fsize, style, weight, stretch, obliqueSkewSinus);
            }

            RFont? selectedFont = null;

            foreach (var family in families)
            {
                var selectedFamily = family.Trim().TrimStart('"', '\'').TrimEnd('"', '\'');

                selectedFont = adapter.GetFont(selectedFamily, fsize, style, weight, stretch, obliqueSkewSinus);

                if (selectedFont is not null)
                {
                    break;
                }
            }

            return selectedFont;
        }

        /// <summary>
        /// Codepoint-aware resolution: walks the <c>font-family</c> stack and returns the first family
        /// whose face both covers <paramref name="codepoint"/> (its <c>unicode-range</c>/cmap coverage)
        /// and actually contains a glyph for it - the browser "first available font that can render this
        /// character" rule. When nothing in the declared stack covers it, falls through to a last-resort
        /// system-fallback search (CSS Fonts 4 §5's final step) across every OTHER font this adapter
        /// knows about - see <see cref="RAdapter.GetSystemFallbackFontForCodepoint"/>. Returns null only
        /// when that also finds nothing (the caller then falls back to the box's own default font, which
        /// ultimately renders <c>.notdef</c>). The coverage filter on each candidate is a fast pre-narrow;
        /// the <see cref="RFont.HasGlyph"/> check is authoritative (it guards the rare cmap over-report).
        /// </summary>
        internal static RFont? Resolve(RAdapter adapter, string fontFamilyList, double fsize, RFontStyle style, System.Text.Rune codepoint, int? weight = null, int? stretch = null, double? obliqueSkewSinus = null)
        {
            foreach (var family in fontFamilyList.Split(','))
            {
                var selectedFamily = family.Trim().TrimStart('"', '\'').TrimEnd('"', '\'');

                var font = adapter.GetFontForCodepoint(selectedFamily, fsize, style, codepoint, weight, stretch, obliqueSkewSinus);

                if (font is not null && font.HasGlyph(codepoint))
                    return font;
            }

            var fallbackFont = adapter.GetSystemFallbackFontForCodepoint(fsize, style, codepoint, weight, stretch, obliqueSkewSinus);
            if (fallbackFont is not null && fallbackFont.HasGlyph(codepoint))
                return fallbackFont;

            return null;
        }
    }
}
