using PeachPDF.Fonts.OpenType;

namespace PeachPDF.Text
{
    /// <summary>
    /// `lookupFlag`-driven mark filtering (GDEF's `GlyphClassDef`/`MarkAttachClassDef`/
    /// `MarkGlyphSetsDef`), shared by every GSUB/GPOS lookup-application path that needs to decide
    /// whether a glyph "participates" in matching - ligature component matching and GPOS mark-to-base/
    /// mark-to-mark base search (both in <see cref="GsubShaper"/>/<see cref="GposPositioner"/>).
    /// A lookup's own `markClass` (GPOS Type 4/5/6's <c>MarkArray</c>) is a different, lookup-local
    /// concept - not this - see <see cref="GposPositioner"/>'s own remarks.
    /// </summary>
    internal static class GlyphSequenceFilter
    {
        private const ushort IgnoreBaseGlyphs = 0x0002;
        private const ushort IgnoreLigatures = 0x0004;
        private const ushort IgnoreMarks = 0x0008;
        private const ushort UseMarkFilteringSet = 0x0010;
        private const ushort MarkAttachmentTypeShift = 8;

        /// <summary>
        /// Whether <paramref name="glyphId"/> participates in matching under <paramref name="lookupFlag"/>,
        /// given <paramref name="gdef"/>'s glyph classification. No GDEF (or an unclassified glyph)
        /// always participates - `lookupFlag` has nothing to filter without a GDEF glyph class.
        /// </summary>
        public static bool Participates(ushort glyphId, ushort lookupFlag, GdefTable? gdef, CoverageTable? markFilteringSet)
        {
            if (gdef is null)
                return true;

            int glyphClass = gdef.GetGlyphClass(glyphId);
            switch (glyphClass)
            {
                case 1: // Base
                    return (lookupFlag & IgnoreBaseGlyphs) == 0;
                case 2: // Ligature
                    return (lookupFlag & IgnoreLigatures) == 0;
                case 3: // Mark
                    if ((lookupFlag & IgnoreMarks) != 0)
                        return false;
                    if ((lookupFlag & UseMarkFilteringSet) != 0)
                        return markFilteringSet is not null && markFilteringSet.IndexOfGlyph(glyphId) >= 0;
                    int markAttachClass = lookupFlag >> MarkAttachmentTypeShift;
                    return markAttachClass == 0 || gdef.GetMarkAttachClass(glyphId) == markAttachClass;
                default: // 0 unclassified, 4 Component (never used to define lookupFlag filtering)
                    return true;
            }
        }
    }
}
