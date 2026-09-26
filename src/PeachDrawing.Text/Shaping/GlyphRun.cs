using System.Collections.Generic;

namespace PeachDrawing.Text.Shaping
{
    /// <summary>
    /// The result of shaping one run of text: its glyphs, in the order they are drawn, and the face they belong to.
    /// </summary>
    public sealed class GlyphRun
    {
        internal GlyphRun(Typeface typeface, IReadOnlyList<PlacedGlyph> glyphs)
        {
            Typeface = typeface;
            Glyphs = glyphs;
        }

        /// <summary>The face the run was shaped in.</summary>
        public Typeface Typeface { get; }

        /// <summary>The glyphs, in the order they are drawn.</summary>
        public IReadOnlyList<PlacedGlyph> Glyphs { get; }

        /// <summary>
        /// The horizontal distance the pen travels along the run, in design units: the advance of every glyph plus its
        /// positioning adjustment, exactly as the values are and without rounding.
        /// </summary>
        public double Advance
        {
            get
            {
                double advance = 0;
                foreach (var glyph in Glyphs)
                {
                    advance += Typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta;
                }

                return advance;
            }
        }
    }
}
