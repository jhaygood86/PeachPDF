using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves a CSS <c>font-stretch</c> value (one of the nine keywords, or a percentage) to the percentage of the normal width it
    /// stands for (CSS Fonts 4 section 3.3: <c>ultra-condensed</c> is 50%, <c>normal</c> 100%, <c>ultra-expanded</c> 200%), which is
    /// the scale of a variable font's <c>wdth</c> axis and what every font-creation call takes as its width.
    /// </summary>
    internal static class FontStretchResolver
    {
        /// <summary>The percentage of <c>font-stretch: normal</c>.</summary>
        internal const double Normal = 100;

        /// <summary>
        /// Resolves a raw <c>font-stretch</c> value string (e.g. an SVG presentation attribute, or an <c>@font-face</c> descriptor - see
        /// <see cref="FontFaceDescriptorResolver.ResolveStretch"/> - a CSS-OM string, not a
        /// <see cref="Dom.CssBox"/>'s cascaded value): a keyword or a percentage, and normal for anything else.
        /// </summary>
        internal static double Resolve(string fontStretchValue) =>
            TryResolve(fontStretchValue, out var percent) ? percent : Normal;

        /// <summary>
        /// Resolves a raw <c>font-stretch</c> value string: a keyword or a non-negative percentage.
        /// </summary>
        /// <returns><see langword="false"/> for anything that is neither.</returns>
        internal static bool TryResolve(string fontStretchValue, out double percent)
        {
            var trimmed = fontStretchValue.Trim();
            if (Map.FontStretches.TryGetValue(trimmed, out var keyword))
            {
                percent = Resolve(keyword);
                return true;
            }

            return CssValueParser.TryParseNonNegativePercentage(trimmed, out percent);
        }

        internal static double Resolve(FontStretch fontStretch) => fontStretch switch
        {
            FontStretch.UltraCondensed => 50,
            FontStretch.ExtraCondensed => 62.5,
            FontStretch.Condensed => 75,
            FontStretch.SemiCondensed => 87.5,
            FontStretch.SemiExpanded => 112.5,
            FontStretch.Expanded => 125,
            FontStretch.ExtraExpanded => 150,
            FontStretch.UltraExpanded => 200,
            _ => Normal
        };

        /// <summary>Resolves a parsed <c>font-stretch</c> value: a keyword, or the percentage that was written.</summary>
        internal static double Resolve(CssKeywordOrValue<FontStretch, double> fontStretch) =>
            fontStretch.Value is { } percent ? percent
            : fontStretch.Keyword is { } keyword ? Resolve(keyword)
            : Normal;
    }
}
