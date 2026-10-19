using PeachDrawing.PDF.Engine.Rendering;
using PeachDrawing.Text.Shaping;
using System;

namespace PeachPDF.Tests.PdfEngineTests
{
    /// <summary>Which source text a colour glyph carries as its <c>/ActualText</c>, for the clusters shaping can produce.</summary>
    public class ColorGlyphActualTextTests
    {
        [Fact]
        public void ActualTextOwnership_UsesPositionallyAlignedLogicalTextForBidiTransformedRun()
        {
            // Mirrors CharacterMapInfo.AddShapedText's contract. The displayed RTL run was reversed/mirrored
            // from source "(AB)" into "(BA)"; ReverseRunes(source) = ")BA(" is the logical source
            // aligned position-for-position with the displayed glyphs.
            PlacedGlyph[] glyphs =
            [
                new(1, 0, 1),
                new(2, 1, 1),
                new(3, 2, 1),
                new(4, 3, 1)
            ];

            string?[] actualText = ColorGlyphPainter.BuildActualTextByGlyph("(BA)", ")BA(", glyphs);

            Assert.Equal(new string?[] { ")", "B", "A", "(" }, actualText);
        }

        [Fact]
        public void ActualTextOwnership_DisjointClusters_DoesNotAllocatePerGlyphBuildersOrSortState()
        {
            const int glyphCount = 200;
            var glyphs = new PlacedGlyph[glyphCount];
            for (int i = 0; i < glyphs.Length; i++)
                glyphs[i] = new PlacedGlyph(i + 1, i, 1);
            string text = new('A', glyphCount);

            for (int i = 0; i < 3; i++)
                ColorGlyphPainter.BuildActualTextByGlyph(text, null, glyphs);

            const int passes = 100;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < passes; i++)
                ColorGlyphPainter.BuildActualTextByGlyph(text, null, glyphs);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(allocated < 1_200_000,
                $"Mapping {passes * glyphCount:N0} disjoint clusters allocated {allocated:N0} bytes. "
                + "The common path should not allocate a sort list, comparison closure, or one "
                + "StringBuilder per glyph.");
        }

        [Fact]
        public void ActualTextOwnership_OverlappingClusterPrefersWidestSourceSpan()
        {
            PlacedGlyph[] glyphs =
            [
                new(1, 1, 1),
                new(2, 0, 3),
            ];

            string?[] actualText = ColorGlyphPainter.BuildActualTextByGlyph("ABC", null, glyphs);

            Assert.Equal(new string?[] { null, "ABC" }, actualText);
        }

        [Fact]
        public void ActualTextOwnership_AttachesDeletedCharactersToAdjacentCluster()
        {
            PlacedGlyph[] trailingGap = [new(1, 0, 1)];
            PlacedGlyph[] leadingGap = [new(1, 1, 1)];

            string?[] trailingActualText = ColorGlyphPainter.BuildActualTextByGlyph("A\uFE0F", null, trailingGap);
            string?[] leadingActualText = ColorGlyphPainter.BuildActualTextByGlyph("\u200DA", null, leadingGap);

            Assert.Equal(new string?[] { "A\uFE0F" }, trailingActualText);
            Assert.Equal(new string?[] { "\u200DA" }, leadingActualText);
        }
    }
}
