using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves a CSS font-weight value (keyword or numeric) to a concrete CSS Fonts numeric weight
    /// (1-1000, fractions included), including the <c>bolder</c>/<c>lighter</c> relative keywords, which per CSS2.1/Fonts must
    /// step relative to the parent's own resolved (used) weight rather than always meaning a fixed
    /// "bold"/"normal". Extracted the same way <see cref="FontSizeResolver"/> was, so in-flow content and
    /// any future @page margin-box weight resolution share one implementation.
    /// </summary>
    internal static class FontWeightResolver
    {
        /// <summary>
        /// Overload for a caller with no typed <see cref="CssKeywordOrValue{TEnum,TValue}"/> in hand -
        /// today, only <c>MarginBoxRenderer</c>'s <c>@page</c> margin-box font resolution, which reads the
        /// CSS-OM <see cref="PeachPDF.CSS.StyleDeclaration"/>'s own raw <c>font-weight</c> string rather
        /// than a <c>CssBox</c>'s cascaded value.
        /// </summary>
        /// <param name="fontWeightValue">The raw CSS font-weight value (keyword or numeric string).</param>
        /// <param name="parentWeight">The parent's own resolved numeric weight, used by <c>bolder</c>/<c>lighter</c>.</param>
        internal static double Resolve(string fontWeightValue, double parentWeight)
        {
            if (CssValueParser.TryParseNumber(fontWeightValue, out var numeric))
                return Resolve(new CssKeywordOrValue<FontWeightKeyword, double>(null, numeric), parentWeight);

            return Resolve(
                Map.FontWeightKeywords.TryGetValue(fontWeightValue, out var keyword)
                    ? new CssKeywordOrValue<FontWeightKeyword, double>(keyword, null)
                    : default,
                parentWeight);
        }

        /// <param name="fontWeight">The parsed <c>font-weight</c> value.</param>
        /// <param name="parentWeight">The parent's own resolved numeric weight, used by <c>bolder</c>/<c>lighter</c>.</param>
        internal static double Resolve(CssKeywordOrValue<FontWeightKeyword, double> fontWeight, double parentWeight)
        {
            if (fontWeight.IsValue)
                return fontWeight.Value!.Value;

            return fontWeight.Keyword switch
            {
                FontWeightKeyword.Bold => 700,
                // CSS Fonts 4 §2.2.1's own table ("bolder"/"lighter" columns against an inherited weight
                // w, fractions included) - not a fixed "always bold"/"always normal" as this box's own
                // FontWeight text might otherwise suggest:
                //   w:       <100  [100,350)  [350,550)  [550,750)  [750,900)  >=900
                //   bolder:   400     400        700        900        900     no change
                //   lighter: no change 100        100        400        700       700
                // The two middle "no change" cells above (bolder's is the ceiling; lighter's is the
                // floor) are what makes the two switch expressions below collapse to three/four arms
                // each rather than mirroring the table's six rows one-for-one:
                //   - bolder's <100 and [100,350) rows both resolve to 400, and its [550,750) and
                //     [750,900) rows both resolve to 900, leaving only >=900 (no change) as the
                //     top branch;
                //   - lighter's [100,350) and [350,550) rows both resolve to 100, leaving <100 (no
                //     change) as the bottom branch.
                FontWeightKeyword.Bolder => parentWeight switch
                {
                    < 350 => 400,
                    < 550 => 700,
                    < 900 => 900,
                    _ => parentWeight
                },
                FontWeightKeyword.Lighter => parentWeight switch
                {
                    < 100 => parentWeight,
                    < 550 => 100,
                    < 750 => 400,
                    _ => 700
                },
                _ => 400
            };
        }
    }
}
