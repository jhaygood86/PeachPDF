#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `ClassDef` table (formats 1 and 2) - a font-wide primitive, byte-identical
// wherever it appears: GDEF's `GlyphClassDef`/`MarkAttachClassDef`, GSUB contextual-lookup format 2
// (Lookup Types 5/6), and GPOS Pair Adjustment format 2. One reader, shared by every consumer.
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/chapter2#class-definition-table
//
#endregion

namespace PeachPDF.Fonts.OpenType
{
    internal sealed class ClassDefTable
    {
        private readonly ushort _startGlyphId;
        private readonly ushort[]? _classValues;
        private readonly (ushort Start, ushort End, ushort Class)[]? _ranges;

        private ClassDefTable(ushort startGlyphId, ushort[]? classValues, (ushort, ushort, ushort)[]? ranges)
        {
            _startGlyphId = startGlyphId;
            _classValues = classValues;
            _ranges = ranges;
        }

        /// <summary>Reads a ClassDef table at <paramref name="offset"/> (an absolute byte position in the font).</summary>
        public static ClassDefTable Read(OpenTypeFontface face, int offset)
        {
            face.Position = offset;
            int format = face.ReadUShort();

            if (format == 1)
            {
                ushort startGlyphId = face.ReadUShort();
                int glyphCount = face.ReadUShort();
                var classValues = new ushort[glyphCount];
                for (int i = 0; i < glyphCount; i++)
                    classValues[i] = face.ReadUShort();
                return new ClassDefTable(startGlyphId, classValues, null);
            }

            if (format == 2)
            {
                int classRangeCount = face.ReadUShort();
                var ranges = new (ushort, ushort, ushort)[classRangeCount];
                for (int i = 0; i < classRangeCount; i++)
                {
                    ushort start = face.ReadUShort();
                    ushort end = face.ReadUShort();
                    ushort classValue = face.ReadUShort();
                    ranges[i] = (start, end, classValue);
                }
                return new ClassDefTable(0, null, ranges);
            }

            // Unknown ClassDef format - every glyph resolves to class 0 (unassigned) rather than
            // throwing, so one unsupported subtable doesn't abort the whole parse.
            return new ClassDefTable(0, [], null);
        }

        /// <summary>Returns <paramref name="glyphId"/>'s class, or 0 (the implicit "unassigned"
        /// class every ClassDef format defines) if it isn't explicitly listed.</summary>
        public int GetClass(ushort glyphId)
        {
            if (_classValues is not null)
            {
                int index = glyphId - _startGlyphId;
                return index >= 0 && index < _classValues.Length ? _classValues[index] : 0;
            }

            if (_ranges is not null)
            {
                int lo = 0, hi = _ranges.Length - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    var range = _ranges[mid];
                    if (glyphId < range.Start) hi = mid - 1;
                    else if (glyphId > range.End) lo = mid + 1;
                    else return range.Class;
                }
            }

            return 0;
        }
    }
}
