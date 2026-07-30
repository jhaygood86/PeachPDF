using System.IO;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for the OpenType <c>Coverage</c> table formats <see cref="CoverageTable"/> reads.
    /// Source Sans 3's real GSUB data only exercises format 1 (a single-glyph coverage set) - format
    /// 2 (glyph ranges) and the unknown-format fallback have no bundled fixture that uses them, so
    /// this hand-crafts the bytes directly. Rather than build a whole new font, it appends the bytes
    /// past the end of a real, already-valid font file (SFNT table lengths are absolute offsets, not
    /// end-of-file-relative, so nothing in a normal parse ever looks past them) and reads directly at
    /// that offset - the same "borrow a real font's validity, add a probe region" idea as
    /// GsubTableSyntheticTests uses for the rest of GSUB.
    /// </summary>
    public class CoverageTableTests
    {
        private static OpenTypeFontface Face(byte[] bytes) => XFontSource.GetOrCreateFrom(bytes).Fontface;

        private static byte[] AppendBytes(byte[] fontBytes, params byte[][] chunks)
        {
            int extra = 0;
            foreach (var c in chunks) extra += c.Length;
            var combined = new byte[fontBytes.Length + extra];
            fontBytes.CopyTo(combined, 0);
            int pos = fontBytes.Length;
            foreach (var c in chunks)
            {
                c.CopyTo(combined, pos);
                pos += c.Length;
            }
            return combined;
        }

        private static byte[] U16(int v) => [(byte)(v >> 8), (byte)v];

        [Fact]
        public void Format2_RangeRecords_ResolveCoverageIndexAcrossAndBetweenRanges()
        {
            var fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int offset = fontBytes.Length;

            // format=2, rangeCount=2, ranges: [100..105]->0, [200..200]->6
            byte[] coverageBytes = AppendBytes([],
                U16(2), U16(2),
                U16(100), U16(105), U16(0),
                U16(200), U16(200), U16(6));

            var face = Face(AppendBytes(fontBytes, coverageBytes));
            var coverage = CoverageTable.Read(face, offset);

            Assert.Equal(-1, coverage.IndexOfGlyph(99));   // before the first range
            Assert.Equal(0, coverage.IndexOfGlyph(100));   // first glyph of the first range
            Assert.Equal(2, coverage.IndexOfGlyph(102));   // mid-range
            Assert.Equal(5, coverage.IndexOfGlyph(105));   // last glyph of the first range
            Assert.Equal(-1, coverage.IndexOfGlyph(106));  // gap between ranges
            Assert.Equal(6, coverage.IndexOfGlyph(200));   // single-glyph second range
            Assert.Equal(-1, coverage.IndexOfGlyph(201));  // after the last range
        }

        [Fact]
        public void UnknownFormat_ProducesEmptyCoverage_NotAThrow()
        {
            var fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int offset = fontBytes.Length;

            byte[] coverageBytes = AppendBytes([], U16(3), U16(0)); // format=3 doesn't exist

            var face = Face(AppendBytes(fontBytes, coverageBytes));
            var coverage = CoverageTable.Read(face, offset);

            Assert.Equal(-1, coverage.IndexOfGlyph(0));
            Assert.Equal(-1, coverage.IndexOfGlyph(65535));
        }
    }
}
