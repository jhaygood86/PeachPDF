using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

using PeachDrawing.Text.Internal.Fonts.OpenType;

using Xunit;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Exercised against a real CJK font, because the point of subsetting a CFF is the font that is too
    /// big to embed whole, and because a font that simple to synthesise would not have the shapes that
    /// make this hard: thousands of glyphs, several font dictionaries, and local subroutines that sit
    /// nowhere near the private dictionary naming them.
    /// </summary>
    public class CffSubsetterTests
    {
        // The source Noto Sans CJK CFF in the PR evidence was 15,458,582 bytes.
        private const int OriginalNotoCffLength = 15_458_582;

        private static byte[] LoadCff()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "NotoSansCJK-Subset-CFF.cff.gz");
            using var file = File.OpenRead(path);
            using var compressed = new GZipStream(file, CompressionMode.Decompress);
            using var expanded = new MemoryStream();
            compressed.CopyTo(expanded);
            return expanded.ToArray();
        }

        private static int[] NonEmptyGlyphs(CffIndex charStrings)
        {
            // The checked-in CFF is itself the small output of the original real-font
            // subset. Its unused glyphs are one-byte endchar programs; retain the glyphs
            // that still have outlines so repeated subsetting exercises real charstrings.
            return Enumerable.Range(0, charStrings.Count)
                .Where(glyph => charStrings[glyph].Length > 1)
                .ToArray();
        }

        [Fact]
        public void Subset_KeepsTheGlyphsAskedFor()
        {
            var cff = LoadCff();
            var original = new CffTable(cff, 0);
            Assert.True(original.IsSupported);
            Assert.True(original.IsCidKeyed);
            int[] wanted = NonEmptyGlyphs(original.CharStrings);

            var subset = CffSubsetter.Subset(cff, wanted);

            Assert.NotNull(subset);

            var rewritten = new CffTable(subset!, 0);

            Assert.True(rewritten.IsSupported);
            Assert.Equal(original.IsCidKeyed, rewritten.IsCidKeyed);
            Assert.True(rewritten.IsCidKeyed);

            // The glyph count cannot change: the PDF refers to glyphs by the
            // ids the layout was built with.
            Assert.Equal(original.CharStrings.Count, rewritten.CharStrings.Count);

            foreach (var gid in wanted)
                Assert.True(original.CharStrings[gid].SequenceEqual(rewritten.CharStrings[gid]));
        }

        [Fact]
        public void Subset_EmptiesTheGlyphsNotAskedFor()
        {
            var cff = LoadCff();
            var original = new CffTable(cff, 0);
            var nonEmpty = NonEmptyGlyphs(original.CharStrings);
            Assert.True(nonEmpty.Length > 2);
            var keep = nonEmpty.Take(2).ToArray();
            var subset = CffSubsetter.Subset(cff, keep);

            Assert.NotNull(subset);

            var rewritten = new CffTable(subset!, 0);

            // 0x0E is endchar: the glyph is still there and draws nothing.
            foreach (var glyph in nonEmpty.Skip(2))
                Assert.Equal([0x0E], rewritten.CharStrings[glyph].ToArray());
        }

        [Fact]
        public void Subset_RemainsSmallComparedToTheOriginalCjkCff()
        {
            var cff = LoadCff();
            var original = new CffTable(cff, 0);
            var subset = CffSubsetter.Subset(cff, NonEmptyGlyphs(original.CharStrings).Take(1).ToArray());

            Assert.NotNull(subset);

            // The fixture is the output of subsetting the 15.4MB source font in
            // the PR evidence. Keep the deliberately loose bound: this guards
            // against embedding the original CFF table whole, not against a few
            // kilobytes of drift in a repeated subset.
            Assert.True(
                subset!.Length < OriginalNotoCffLength / 10,
                $"The subset is {subset.Length:N0} bytes; the original CFF was {OriginalNotoCffLength:N0}.");
        }

        [Fact]
        public void Subset_NotAFont_ReturnsNull()
        {
            Assert.Null(CffSubsetter.Subset([1, 2, 3], [0]));
        }
    }
}
