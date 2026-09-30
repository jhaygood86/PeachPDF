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

namespace PeachDrawing.Core
{
    /// <summary>
    /// A PDF separable/non-separable blend mode (PDF 32000-1 §11.3.5), used with
    /// <see cref="Canvas.PushBlendMode"/>/<see cref="Canvas.PopBlendMode"/> to composite
    /// subsequent drawing against whatever is already painted underneath it.
    /// </summary>
    public enum PaintBlendMode
    {
        /// <summary>The source paints over the backdrop with no mixing.</summary>
        Normal,
        /// <summary>The source and backdrop colors are multiplied together, always darkening.</summary>
        Multiply,
        /// <summary>The inverse of multiplying the inverted colors, always lightening.</summary>
        Screen,
        /// <summary>Multiplies dark backdrop areas and screens light ones - the inverse of <see cref="HardLight"/> with source and backdrop swapped.</summary>
        Overlay,
        /// <summary>Selects the darker of the source and backdrop colors, per channel.</summary>
        Darken,
        /// <summary>Selects the lighter of the source and backdrop colors, per channel.</summary>
        Lighten,
        /// <summary>Brightens the backdrop to reflect the source, like a photographic dodge.</summary>
        ColorDodge,
        /// <summary>Darkens the backdrop to reflect the source, like a photographic burn.</summary>
        ColorBurn,
        /// <summary>Multiplies or screens the backdrop depending on the source color, as if the source were a harsh spotlight.</summary>
        HardLight,
        /// <summary>Darkens or lightens the backdrop depending on the source color, more gently than <see cref="HardLight"/>.</summary>
        SoftLight,
        /// <summary>Subtracts the darker color from the lighter one, per channel.</summary>
        Difference,
        /// <summary>Similar to <see cref="Difference"/> but with lower contrast.</summary>
        Exclusion,
        /// <summary>Takes the hue of the source with the saturation and luminosity of the backdrop.</summary>
        Hue,
        /// <summary>Takes the saturation of the source with the hue and luminosity of the backdrop.</summary>
        Saturation,
        /// <summary>Takes the hue and saturation of the source with the luminosity of the backdrop.</summary>
        Color,
        /// <summary>Takes the luminosity of the source with the hue and saturation of the backdrop - the inverse of <see cref="Color"/>.</summary>
        Luminosity
    }
}
