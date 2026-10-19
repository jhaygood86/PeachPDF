using PeachPDF.CSS;

namespace PeachPDF.Svg
{
    /// <summary>
    /// The font an SVG length is resolved against: what <c>1em</c>/<c>1rem</c> are in user units, plus - as an
    /// <see cref="IFontMetricSource"/> - the measurements <c>ex</c>/<c>ch</c>/<c>cap</c>/<c>ic</c>/<c>lh</c> and
    /// their root-element variants are defined by (CSS Values and Units 4 §6.1). A null basis means no font
    /// is in scope, which is <see cref="SvgValueParsers.DefaultEmPx"/> and each unit's spec fallback.
    /// </summary>
    internal interface ISvgLengthBasis : IFontMetricSource
    {
        /// <summary>The element's own <c>font-size</c> in user units, i.e. 1em.</summary>
        double EmPx { get; }

        /// <summary>The root element's <c>font-size</c> in user units, i.e. 1rem.</summary>
        double RootEmPx { get; }
    }
}
