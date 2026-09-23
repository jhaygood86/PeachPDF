namespace PeachPDF.CSS
{
    /// <summary>
    /// The font measurements a font-relative length unit is defined by (CSS Values and Units 4 §6.1.1):
    /// the x-height (<c>ex</c>), the advance of the "0" glyph (<c>ch</c>), the cap height (<c>cap</c>),
    /// the advance of the "水" ideograph (<c>ic</c>) and the used line-height (<c>lh</c>).
    /// </summary>
    internal enum FontMetric
    {
        Ex,
        Ch,
        Cap,
        Ic,
        Lh,
    }

    /// <summary>
    /// Supplies a font's measurements to <see cref="Length.ToPixels"/>, each as a ratio of that same
    /// font's em (so a length simply multiplies it by whichever em basis its caller already carries).
    /// Lookups are lazy: only a unit that actually needs a measurement makes the implementation touch a
    /// font, which is what keeps this off the hot path of every ordinary length.
    /// </summary>
    internal interface IFontMetricSource
    {
        /// <param name="metric">The measurement wanted.</param>
        /// <param name="rootElement"><see langword="false"/> for the element's own font (<c>ex</c>/<c>ch</c>/...),
        /// <see langword="true"/> for the root element's (<c>rex</c>/<c>rch</c>/...).</param>
        /// <returns>The measurement as a multiple of that font's em; an implementation with no answer returns
        /// <see cref="FontMetricRatios.Approximate"/>.</returns>
        double GetRatio(FontMetric metric, bool rootElement);
    }

    /// <summary>
    /// The spec's fallbacks for a measurement that is "impossible or impractical to determine": used
    /// wherever there is no font in scope (a media query, <c>@page</c> geometry) or the font lacks the data.
    /// </summary>
    internal static class FontMetricRatios
    {
        /// <summary>
        /// <c>ex</c> and <c>ch</c> are 0.5em and <c>ic</c> is 1em, each by the spec's own wording. <c>cap</c>'s
        /// fallback is the font's ascent, which is unknowable without a font, so a typical Latin cap height
        /// stands in. <c>lh</c> is the conventional <c>line-height: normal</c> approximation.
        /// </summary>
        public static double Approximate(FontMetric metric) => metric switch
        {
            FontMetric.Ex => 0.5,
            FontMetric.Ch => 0.5,
            FontMetric.Cap => 0.7,
            FontMetric.Ic => 1.0,
            FontMetric.Lh => 1.2,
            _ => 1.0,
        };
    }
}
