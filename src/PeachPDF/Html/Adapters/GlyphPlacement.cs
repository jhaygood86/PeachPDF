namespace PeachPDF.Html.Adapters
{
    /// <summary>One glyph, addressed directly by its font glyph index (not a Unicode character) and
    /// placed at an explicit position - see <see cref="RGraphics.DrawGlyphs"/>.</summary>
    internal readonly record struct GlyphPlacement(int GlyphIndex, double X, double Y);
}
