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
        Luminosity,
        /// <summary>Adds the premultiplied source to the backdrop and clamps the result - Porter-Duff <c>plus</c>, so overlapping translucent ink brightens. A backend with no additive blend ignores it and paints as <see cref="Normal"/>.</summary>
        Plus,
        /// <summary>
        /// Porter-Duff <c>source-in</c>: the source where the backdrop is, nothing elsewhere. Only the source's own coverage is composited unless the drawing is made in a
        /// layer whose <see cref="LayerOptions.Bounds"/> cover the region: the layer then replaces the backdrop across all of them, which is how a 2D canvas applies it.
        /// A backend that cannot composite this way (a PDF or SVG writer) paints <see cref="Normal"/>; see <see cref="Canvas.SupportsCompositeOperators"/>.
        /// </summary>
        SourceIn,
        /// <summary>Porter-Duff <c>source-out</c>: the source where the backdrop is not. See <see cref="SourceIn"/> for how far it reaches.</summary>
        SourceOut,
        /// <summary>Porter-Duff <c>source-atop</c>: the source where the backdrop is, with the backdrop kept elsewhere. See <see cref="SourceIn"/> for how far it reaches.</summary>
        SourceAtop,
        /// <summary>Porter-Duff <c>destination-over</c>: the source painted behind the backdrop.</summary>
        DestinationOver,
        /// <summary>Porter-Duff <c>destination-in</c>: the backdrop where the source is, nothing elsewhere. See <see cref="SourceIn"/> for how far it reaches.</summary>
        DestinationIn,
        /// <summary>Porter-Duff <c>destination-out</c>: the backdrop where the source is not, so the source erases. Unlike the other operators it reaches only as far as the source does.</summary>
        DestinationOut,
        /// <summary>Porter-Duff <c>destination-atop</c>: the backdrop where the source is, with the source kept elsewhere. See <see cref="SourceIn"/> for how far it reaches.</summary>
        DestinationAtop,
        /// <summary>Porter-Duff <c>xor</c>: the source and the backdrop each where the other is not. See <see cref="SourceIn"/> for how far it reaches.</summary>
        Xor,
        /// <summary>Porter-Duff <c>src</c>: the source replaces the backdrop, transparent source pixels included. See <see cref="SourceIn"/> for how far it reaches.</summary>
        Copy,
        /// <summary>Porter-Duff <c>clear</c>: the backdrop becomes transparent where the source reaches. See <see cref="SourceIn"/> for how far it reaches.</summary>
        Clear
    }
}
