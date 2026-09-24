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
        /// The typeface of a variable font at a location (<see cref="Typeface.WithAxes"/>) is written as a static font: its glyphs have
        /// the location's variations applied and carry no hinting instructions, since a PDF cannot embed a variable font.
        /// A TrueType font is cut down, and a font with CFF outlines is returned whole, which
        /// <see cref="ExportedFont.IsSubset"/> reports; the glyphs asked for are not looked at then.
        /// The exception is a variable font with CFF2 outlines, which is always written afresh, whatever its location, as a static
        /// font with CFF outlines that holds only the glyphs asked for: each outline is drawn at the location and written as a
        /// charstring of lines and curves over whole-number coordinates, with no hints and no subroutines, in a CID-keyed CFF font
        /// (a glyph index is its own CID).
        /// </remarks>
        /// <param name="typeface">The typeface to cut.</param>
        /// <param name="glyphs">The glyph indices to keep.</param>
        /// <param name="keepCharacterMap">
        /// Whether the font keeps its character map. A font whose text is encoded as glyph indices has no use for one and
        /// is smaller without it.
        /// </param>
        /// <returns>The font file and what it holds.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="typeface"/> or <paramref name="glyphs"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A glyph index is not one of the typeface's glyphs (a face with TrueType or CFF2 outlines only).</exception>
        public static ExportedFont ExportSubset(Typeface typeface, IEnumerable<int> glyphs, bool keepCharacterMap)
        {
            ArgumentNullException.ThrowIfNull(typeface);
            ArgumentNullException.ThrowIfNull(glyphs);

            OpenTypeFontface face = typeface.Face.Fontface;

            // A variable font's CFF2 outlines are written afresh as those of a static CFF font: a PDF has no use for the CFF2 table.
            // (The outlines are those the descriptor draws, so a font that also has a CFF table, which it should not, is a CFF font.)
            bool isCff2 = face.loca == null && face.glyf == null && face.cff is not { IsSupported: true }
                && face.cff2 is { IsSupported: true } && face.maxp.numGlyphs > 0;

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

            if (isCff2)
            {
                byte[] cff = StaticCffFontBuilder.Build(face, typeface.Face.Descriptor, wanted.Keys, keepCharacterMap);
                return new ExportedFont(cff, hasCffOutlines: true, isSubset: true);
            }

            if (face.loca == null)
            {
                // A CFF1 font keeps its outlines in its CFF table, so the glyf/loca subsetting the
                // branch below does cannot touch it - which used to mean the whole face went into the
                // PDF. A CID-keyed CFF can be rewritten to hold only the glyphs asked for, and is
                // emitted as the bare CFF table that is what a CIDFontType0 descendant is defined to
                // carry (leaving behind the layout and mapping tables, which in a CJK font are
                // megabytes a viewer never reads). A plain CFF has to stay inside the OpenType file
                // that holds its mapping, and anything that cannot be taken apart is embedded whole.
                byte[]? subsetCff = TrySubsetCff(face, wanted.Keys);
                if (subsetCff is not null)
                {
                    return new ExportedFont(subsetCff, hasCffOutlines: true, isSubset: true);
                }

                return new ExportedFont(face.FontSource.Bytes, hasCffOutlines: true, isSubset: false);
            }

            OpenTypeFontface subset = face.CreateFontSubSet(wanted, cidFont: !keepCharacterMap, typeface.Face.Variation);
            return new ExportedFont(subset.FontSource.Bytes, hasCffOutlines: false, isSubset: true);
        }

        /// <summary>
        /// Rewrites a CID-keyed CFF table down to the glyphs a document draws, or returns null when it
        /// cannot be rewritten safely: not CID-keyed, no CFF table, or a font the subsetter cannot take
        /// apart.
        /// </summary>
        private static byte[]? TrySubsetCff(OpenTypeFontface face, IEnumerable<int> usedGlyphs)
        {
            if (face.cff is not { IsCidKeyed: true }
                || !face.TableDictionary.TryGetValue("CFF ", out TableDirectoryEntry? entry)
                || entry is null)
            {
                return null;
            }

            var source = face.FontSource.Bytes;
            if (entry.Offset < 0 || entry.Length <= 0 || entry.Offset + entry.Length > source.Length)
            {
                return null;
            }

            var cff = new byte[entry.Length];
            Array.Copy(source, entry.Offset, cff, 0, entry.Length);

            return CffSubsetter.Subset(cff, [.. usedGlyphs]);
        }
    }
}
