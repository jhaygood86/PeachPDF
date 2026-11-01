namespace PeachDrawing.Core
{
    /// <summary>One glyph, addressed directly by its font glyph index (not a Unicode character) and
    /// placed at an explicit position - see <see cref="Canvas.DrawGlyphs"/>.</summary>
    /// <param name="GlyphIndex">the glyph's index in the font</param>
    /// <param name="X">the glyph origin's x position</param>
    /// <param name="Y">the glyph origin's baseline y position</param>
    /// <param name="Text">
    /// the text the glyph stands for, or <see langword="null"/> when it has none (a stretchy-operator part, say). A backend that
    /// records text for extraction (a PDF's ToUnicode map) uses it, so a glyph that is a ligature, or one of several glyphs for one
    /// character, still copies out as the characters it came from. It does not affect what is drawn.
    /// </param>
    public readonly record struct GlyphPlacement(int GlyphIndex, double X, double Y, string? Text = null);
}
