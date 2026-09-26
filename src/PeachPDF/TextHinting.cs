namespace PeachPDF
{
    /// <summary>
    /// Whether text that PeachPDF draws into a bitmap is fitted to the bitmap's pixel grid by the font's own hinting, which makes small
    /// text sharper. See <see cref="PdfGenerateConfig.TextHinting"/>.
    /// </summary>
    /// <remarks>
    /// Hinting means something only where there are pixels: the text of the PDF itself is always the embedded font, drawn by the viewer,
    /// and is never hinted. It applies to the text inside the regions PeachPDF rasterizes (see
    /// <see cref="PdfGenerateConfig.RasterizationDpi"/>), such as under a <c>filter</c> or when flattening transparency.
    /// </remarks>
    public enum TextHinting
    {
        /// <summary>No hinting: glyph outlines are the design of the font, scaled. The default.</summary>
        None = 0,

        /// <summary>
        /// TrueType hinting in FreeType's default mode, which fits glyphs vertically only and leaves their horizontal metrics alone. Suited to
        /// anti-aliased text.
        /// </summary>
        Standard = 1,

        /// <summary>TrueType hinting in the original mode, which fits both directions, as for text drawn without anti-aliasing.</summary>
        Monochrome = 2,
    }
}
