namespace PeachDrawing.Abstractions
{
    /// <summary>One glyph, addressed directly by its font glyph index (not a Unicode character) and
    /// placed at an explicit position - see <see cref="Canvas.DrawGlyphs"/>.</summary>
    public readonly record struct GlyphPlacement(int GlyphIndex, double X, double Y);
}
