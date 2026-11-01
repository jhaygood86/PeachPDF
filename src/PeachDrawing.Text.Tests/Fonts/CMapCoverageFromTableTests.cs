using System;
using System.IO;
using System.Linq;
using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The font-fallback scan reads coverage from the <c>cmap</c> bytes alone (so it never keeps every installed
    /// font); it must agree with the coverage the fully parsed font gives.
    /// </summary>
    public class CMapCoverageFromTableTests
    {
        public static TheoryData<string> Fonts() =>
        [
            BundledFonts.Ttf, BundledFonts.Otf, BundledFonts.Emoji, BundledFonts.Hebrew, BundledFonts.Arabic,
            BundledFonts.Devanagari, BundledFonts.Cjk, BundledFonts.Khmer,
        ];

        [Theory]
        [MemberData(nameof(Fonts))]
        public void TableReader_MatchesTheParsedFontsCoverage(string path)
        {
            var expected = CMapCoverage.Extract(FontFileData.GetOrCreateFrom(File.ReadAllBytes(path)).Fontface.cmap);
            Assert.NotEmpty(expected);

            Assert.Equal(expected, FontResolver.ReadSystemCoverage(path, 0));
        }

        [Fact]
        public void UnreadableFile_CoversNothing()
        {
            Assert.Empty(FontResolver.ReadSystemCoverage(Path.Combine(AppContext.BaseDirectory, "no-such-font.ttf"), 0));
        }

        [Fact]
        public void NotAFont_CoversNothing()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[64]);
                Assert.Empty(FontResolver.ReadSystemCoverage(path, 0));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TruncatedOrMalformedTables_CoverNothing()
        {
            Assert.Empty(CMapCoverage.ExtractFromTable([]));
            Assert.Empty(CMapCoverage.ExtractFromTable([0, 0, 0, 9]));                       // more records than bytes
            Assert.Empty(CMapCoverage.ExtractFromTable([0, 0, 0, 1, 0, 3, 0, 1, 0, 0, 0, 12])); // offset past the end of the table
            // A format 4 subtable cut off inside its segment arrays.
            Assert.Empty(CMapCoverage.ExtractFromTable([0, 0, 0, 1, 0, 3, 0, 1, 0, 0, 0, 12, 0, 4, 0, 0, 0, 0, 0, 40]));
            // A format 12 subtable claiming more groups than it holds.
            Assert.Empty(CMapCoverage.ExtractFromTable(
                [0, 0, 0, 1, 0, 3, 0, 10, 0, 0, 0, 12, 0, 12, 0, 0, 0, 0, 0, 28, 0, 0, 0, 0, 0, 0, 0, 5]));
        }

        [Fact]
        public void Format12Only_CoversTheBmpToo()
        {
            // One Windows full-repertoire (3,10) format 12 subtable with a single group U+0041..U+0043.
            byte[] table =
            [
                0, 0, 0, 1, 0, 3, 0, 10, 0, 0, 0, 12,
                0, 12, 0, 0, 0, 0, 0, 28, 0, 0, 0, 0, 0, 0, 0, 1,
                0, 0, 0, 0x41, 0, 0, 0, 0x43, 0, 0, 0, 1,
            ];

            var ranges = CMapCoverage.ExtractFromTable(table);

            var range = Assert.Single(ranges);
            Assert.Equal(0x41, range.Start.Value);
            Assert.Equal(0x43, range.End.Value);
        }

        [Fact]
        public void RepeatedScan_IsServedFromTheSharedCache()
        {
            var first = FontResolver.ReadSystemCoverage(BundledFonts.Ttf, 0);
            var second = FontResolver.ReadSystemCoverage(BundledFonts.Ttf, 0);
            Assert.True(first.SequenceEqual(second));
        }
    }
}
