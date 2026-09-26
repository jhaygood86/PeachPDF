namespace PeachDrawing.Text.Outlines
{
    /// <summary>
    /// How much a glyph outline is fitted to the pixel grid: whether the font's own hinting instructions are run, and in which
    /// dialect.
    /// </summary>
    /// <remarks>
    /// Hinting moves the points of an outline at one size so that stems, heights and curves land on whole pixels, which makes
    /// small text drawn into a pixel raster sharper. It means nothing for vector output, where text has no size until it is
    /// drawn. Only the vertical direction is fitted in <see cref="Standard"/> mode, so glyphs keep the horizontal positions
    /// and widths the font's design gives them; that is the mode to use for anti-aliased text.
    /// </remarks>
    public enum GridFitting
    {
        /// <summary>No hinting: the outline is the design of the font, scaled and nothing else.</summary>
        None = 0,

        /// <summary>
        /// The font's TrueType instructions are run in FreeType's default interpreter, which honours the vertical direction
        /// only ("minimal subpixel hinting"), with the compatibility adjustments modern fonts rely on. Fonts without TrueType
        /// instructions are not hinted.
        /// </summary>
        Standard = 1,

        /// <summary>
        /// The font's TrueType instructions are run in the original interpreter, which fits both directions, as for black and
        /// white text without anti-aliasing. Advances come from the font's <c>hdmx</c> table where it has one for the size.
        /// </summary>
        Monochrome = 2,
    }

    /// <summary>What a caller wants of an outline: at which size, and how much to fit it to the pixel grid.</summary>
    /// <remarks>
    /// The default value asks for what <see cref="Typeface.TryGetOutline(ushort, out GlyphOutline)"/> gives: the design of the
    /// font, in design units.
    /// </remarks>
    public readonly struct OutlineRequest
    {
        /// <summary>
        /// The size the outline will be drawn at, in pixels per em, for an outline that is grid-fitted. It may be fractional; the
        /// font's hinting works on the size in 1/64 pixel, except that a TrueType font that asks for whole pixels per em is fitted at the
        /// nearest whole size (see <see cref="GlyphOutline.PixelsPerEm"/>). Ignored, and the outline stays in design units, for
        /// <see cref="GridFitting.None"/>.
        /// </summary>
        public double PixelsPerEm { get; init; }

        /// <summary>How much to fit the outline to the pixel grid.</summary>
        public GridFitting GridFitting { get; init; }
    }
}
