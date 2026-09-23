using System.Text;
using PeachPDF.CSS;

namespace PeachPDF.Html.Adapters
{
    /// <summary>
    /// Reads the measurements the font-relative CSS units are defined by (CSS Values and Units 4 §6.1.1)
    /// off an <see cref="RFont"/>, as fractions of that font's em. Shared by every engine that resolves
    /// those units (HTML boxes, SVG, MathML) so they cannot disagree about what "the width of 0" is.
    /// </summary>
    internal static class FontMetricMeasurement
    {
        /// <summary>U+6C34, the CJK "water" ideograph <c>ic</c> is defined by.</summary>
        public static readonly Rune WaterIdeograph = new(0x6C34);

        /// <summary>
        /// The measurement as a multiple of the font's em, or the spec's fallback
        /// (<see cref="FontMetricRatios.Approximate"/>) when the font is missing or lacks the data.
        /// </summary>
        /// <param name="font">The font to measure. For <c>ic</c> this should already be the font the
        /// ideograph is rendered with, which is not necessarily the element's primary face.</param>
        /// <param name="metric">What to measure.</param>
        /// <param name="pixelsPerPoint">Only <c>lh</c> needs it: <see cref="RFont.Size"/> is the size the font was
        /// requested at divided by <c>PixelsPerPoint</c>, while <see cref="RFont.NormalLineHeight"/> is in the
        /// requested-size space, so the ratio is the line height over <c>Size * PixelsPerPoint</c>. HTML does not
        /// use this for <c>lh</c> - it needs the used <c>line-height</c>, not the font's normal one - see
        /// <c>DerivedStyle.LineHeightRatio</c>.</param>
        public static double Ratio(RFont? font, FontMetric metric, double pixelsPerPoint = 1.0)
        {
            double? measured = null;

            if (font is not null)
            {
                measured = metric switch
                {
                    FontMetric.Ex => font.XHeightEm,
                    FontMetric.Ch => font.GetAdvanceEm(new Rune('0')),
                    FontMetric.Cap => font.CapHeightEm,
                    FontMetric.Ic => font.GetAdvanceEm(WaterIdeograph),
                    FontMetric.Lh => font.Size > 0 ? font.NormalLineHeight / (font.Size * pixelsPerPoint) : null,
                    _ => null,
                };
            }

            return measured is > 0 ? measured.Value : FontMetricRatios.Approximate(metric);
        }
    }
}
