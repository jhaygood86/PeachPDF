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
    }
}
