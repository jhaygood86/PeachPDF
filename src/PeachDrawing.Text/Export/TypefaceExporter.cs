using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Export
{
    /// <summary>
    /// Cuts fonts down to the glyphs a document uses, so that a document can embed the font without embedding all of it.
    /// </summary>
    public static class TypefaceExporter
    {
        /// <summary>
        /// Makes a font file that holds only the given glyphs of a typeface.
        /// </summary>
        /// <remarks>
        /// The glyphs keep their indices, so text already encoded as glyph indices stays valid against the subset. The
        /// glyphs a composite glyph is built from come along, and so does the notdef glyph. A colour glyph with no outline of
        /// its own, whose visible shapes are its layers, is given a small outline so that a reader can still select it.
        /// A TrueType font is cut down, and a font with CFF outlines is returned whole, which
        /// <see cref="ExportedFont.IsSubset"/> reports; the glyphs asked for are not looked at then.
        /// </remarks>
        /// <param name="typeface">The typeface to cut.</param>
        /// <param name="glyphs">The glyph indices to keep.</param>
        /// <param name="keepCharacterMap">
        /// Whether the font keeps its character map. A font whose text is encoded as glyph indices has no use for one and
        /// is smaller without it.
        /// </param>
        /// <returns>The font file and what it holds.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="typeface"/> or <paramref name="glyphs"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A glyph index is not one of the typeface's glyphs (a TrueType face only).</exception>
        public static ExportedFont ExportSubset(Typeface typeface, IEnumerable<int> glyphs, bool keepCharacterMap)
        {
            ArgumentNullException.ThrowIfNull(typeface);
            ArgumentNullException.ThrowIfNull(glyphs);

            OpenTypeFontface face = typeface.Face.Fontface;
            if (face.loca == null)
            {
                return new ExportedFont(face.FontSource.Bytes, hasCffOutlines: true, isSubset: false);
            }

            int glyphCount = face.maxp.numGlyphs;
            var wanted = new Dictionary<int, object>();
            foreach (int glyph in glyphs)
            {
                if (glyph < 0 || glyph >= glyphCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(glyphs), glyph, "The typeface has no such glyph.");
                }

                wanted[glyph] = null!;
            }

            OpenTypeFontface subset = face.CreateFontSubSet(wanted, cidFont: !keepCharacterMap);
            return new ExportedFont(subset.FontSource.Bytes, hasCffOutlines: false, isSubset: true);
        }
    }
}
