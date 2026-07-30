#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `Coverage` table (formats 1 and 2) - a font-wide primitive used by
// GSUB/GPOS/GDEF lookups to test whether a glyph id participates in a lookup and, if so, at
// which coverage index.
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/chapter2#coverage-table
//
#endregion

using System;

namespace PeachPDF.Fonts.OpenType
{
    internal sealed class CoverageTable
    {
        private readonly ushort[]? _glyphArray;
        private readonly (ushort Start, ushort End, ushort StartCoverageIndex)[]? _ranges;

        private CoverageTable(ushort[]? glyphArray, (ushort, ushort, ushort)[]? ranges)
        {
            _glyphArray = glyphArray;
            _ranges = ranges;
        }

        /// <summary>Reads a Coverage table at <paramref name="offset"/> (an absolute byte position in the font).</summary>
        public static CoverageTable Read(OpenTypeFontface face, int offset)
        {
            face.Position = offset;
            int format = face.ReadUShort();

            if (format == 1)
            {
                int glyphCount = face.ReadUShort();
                var glyphs = new ushort[glyphCount];
                for (int i = 0; i < glyphCount; i++)
                    glyphs[i] = face.ReadUShort();
                return new CoverageTable(glyphs, null);
            }

            if (format == 2)
            {
                int rangeCount = face.ReadUShort();
                var ranges = new (ushort, ushort, ushort)[rangeCount];
                for (int i = 0; i < rangeCount; i++)
                {
                    ushort start = face.ReadUShort();
                    ushort end = face.ReadUShort();
                    ushort startCoverageIndex = face.ReadUShort();
                    ranges[i] = (start, end, startCoverageIndex);
                }
                return new CoverageTable(null, ranges);
            }

            // Unknown coverage format - empty coverage (matches no glyph) rather than throwing,
            // so one unsupported subtable doesn't abort the whole GSUB parse.
            return new CoverageTable([], null);
        }

        /// <summary>Returns the coverage index of <paramref name="glyphId"/>, or -1 if it isn't covered.</summary>
        public int IndexOfGlyph(ushort glyphId)
        {
            if (_glyphArray is not null)
            {
                // Format 1's glyph array is required by spec to be sorted in numeric order.
                int index = Array.BinarySearch(_glyphArray, glyphId);
                return index >= 0 ? index : -1;
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
                    else return range.StartCoverageIndex + (glyphId - range.Start);
                }
            }

            return -1;
        }
    }
}
