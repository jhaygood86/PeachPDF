using PeachDrawing.Text.Outlines;

namespace PeachDrawing.Core.ColorGlyphs
{
    /// <summary>
    /// Where a <see cref="ColorGlyphPainter"/> draws. <see cref="CanvasColorGlyphTarget"/> draws onto any
    /// <see cref="Canvas"/>; a backend with its own way of drawing (a PDF writer that also wants to measure the ink first, say)
    /// implements this directly. Every rectangle and point is in the target's space, the space the placement given to
    /// <see cref="ColorGlyphPainter.Paint"/> maps design units into.
    /// </summary>
    public interface IColorGlyphTarget
    {
        /// <summary>Fills the outline of one glyph, mapped through <paramref name="transform"/>, with a solid color (a COLR v0 layer, or a plain glyph).</summary>
        /// <param name="outline">the glyph outline, in design units</param>
        /// <param name="transform">design units to target space</param>
        /// <param name="color">the fill color</param>
        void FillOutline(GlyphOutline outline, Affine2x3 transform, PaintColor color);

        /// <summary>Restricts everything drawn until the matching <see cref="PopClip"/> to the outline of one glyph, mapped through <paramref name="transform"/>.</summary>
        /// <param name="outline">the glyph outline, in design units</param>
        /// <param name="transform">design units to target space</param>
        void PushOutlineClip(GlyphOutline outline, Affine2x3 transform);

        /// <summary>Ends the clip begun by the last unmatched <see cref="PushOutlineClip"/>.</summary>
        void PopClip();

        /// <summary>Fills <paramref name="region"/>, within the current clip, with <paramref name="paint"/>.</summary>
        /// <param name="region">a rectangle covering the clipped glyph's ink, in target space</param>
        /// <param name="paint">what to fill it with</param>
        void FillRegion(Rect region, ColorGlyphPaint paint);

        /// <summary>Composites everything drawn until the matching <see cref="PopBlendMode"/> over what is beneath it with <paramref name="mode"/>.</summary>
        /// <param name="mode">the blend mode</param>
        void PushBlendMode(PaintBlendMode mode);

        /// <summary>Ends the blend begun by the last unmatched <see cref="PushBlendMode"/>.</summary>
        void PopBlendMode();
    }
}
