// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

namespace PeachPDF.Html.Adapters.Entities
{
    /// <summary>
    /// A PDF separable/non-separable blend mode (PDF 32000-1 §11.3.5), used with
    /// <see cref="RGraphics.PushBlendMode"/>/<see cref="RGraphics.PopBlendMode"/> to composite
    /// subsequent drawing against whatever is already painted underneath it.
    /// </summary>
    internal enum RBlendMode
    {
        Normal,
        Multiply,
        Screen,
        Overlay,
        Darken,
        Lighten,
        ColorDodge,
        ColorBurn,
        HardLight,
        SoftLight,
        Difference,
        Exclusion,
        Hue,
        Saturation,
        Color,
        Luminosity
    }
}
