namespace PeachDrawing.Text.Outlines
{
    /// <summary>
    /// The SVG document that draws a glyph in a font with an <c>SVG </c> table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A document can cover a range of glyphs. It draws one of them by the element whose <c>id</c> is <see cref="ElementId"/>, and a
    /// document that covers a single glyph may instead be drawn whole when it has no such element. The drawing is in the coordinate
    /// system of the glyph: the origin is the glyph's origin on the baseline, one unit is a design unit, and <c>y</c> points down, so
    /// a shape above the baseline has a negative <c>y</c>.
    /// </para>
    /// <para>
    /// The library returns the document and does not draw it, as it has no SVG renderer. The colours of the font's <c>CPAL</c>
    /// palette are what the document's <c>var(--color0)</c>, <c>var(--color1)</c> and so on stand for, and the text colour is what
    /// <c>context-fill</c> and <c>currentColor</c> stand for. The document comes from the font file and is untrusted: a compressed
    /// one is inflated up to 4 MiB and no further.
    /// </para>
    /// </remarks>
    /// <param name="Document">The text of the SVG document.</param>
    /// <param name="ElementId">The <c>id</c> of the element that draws the glyph asked for: <c>glyph</c> and the glyph's number.</param>
    /// <param name="FirstGlyph">The first glyph the document covers.</param>
    /// <param name="LastGlyph">The last glyph the document covers.</param>
    /// <param name="UnitsPerEm">The number of design units in an em, which is how many units of the drawing an em is.</param>
    public sealed record SvgGlyph(string Document, string ElementId, int FirstGlyph, int LastGlyph, int UnitsPerEm)
    {
        /// <summary>Whether the document covers just this one glyph, in which case it can be drawn whole when it has no element for it.</summary>
        public bool CoversOneGlyph => FirstGlyph == LastGlyph;
    }
}
