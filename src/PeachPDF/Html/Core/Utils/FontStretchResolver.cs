using PeachPDF.CSS;
using PeachPDF.Fonts;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves a CSS <c>font-stretch</c> keyword to the 1-9 numeric scale matching the OpenType OS/2
    /// table's <c>usWidthClass</c> field directly (1=ultra-condensed ... 5=normal ... 9=ultra-expanded) -
    /// see <see cref="PeachPDF.Fonts.TtfFontDescription.Stretch"/>, which reads that same field for
    /// each registered face, so the two are directly comparable without any extra translation.
    /// </summary>
    internal static class FontStretchResolver
    {
        internal const int Normal = 5;

        /// <summary>
        /// Resolves a raw <c>font-stretch</c> descriptor string (e.g. an <c>@font-face</c> descriptor -
        /// see <see cref="FontFaceDescriptorResolver.ResolveStretch"/> - a CSS-OM string, not a
        /// <see cref="Dom.CssBox"/>'s cascaded value) rather than the typed <see cref="FontStretch"/> the
        /// <see cref="Resolve(FontStretch)"/> overload below consumes.
        /// </summary>
        internal static int Resolve(string fontStretchValue) => fontStretchValue switch
        {
            Keywords.UltraCondensed => 1,
            Keywords.ExtraCondensed => 2,
            Keywords.Condensed => 3,
            Keywords.SemiCondensed => 4,
            Keywords.SemiExpanded => 6,
            Keywords.Expanded => 7,
            Keywords.ExtraExpanded => 8,
            Keywords.UltraExpanded => 9,
            _ => Normal
        };

        internal static int Resolve(FontStretch fontStretch) => fontStretch switch
        {
            FontStretch.UltraCondensed => 1,
            FontStretch.ExtraCondensed => 2,
            FontStretch.Condensed => 3,
            FontStretch.SemiCondensed => 4,
            FontStretch.SemiExpanded => 6,
            FontStretch.Expanded => 7,
            FontStretch.ExtraExpanded => 8,
            FontStretch.UltraExpanded => 9,
            _ => Normal
        };
    }
}
