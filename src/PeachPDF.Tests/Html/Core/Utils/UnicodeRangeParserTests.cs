using PeachPDF.Html.Core.Utils;
using System.Text;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class UnicodeRangeParserTests
    {
        [Fact]
        public void Parse_Null_Or_Blank_ReturnsNull()
        {
            Assert.Null(UnicodeRangeParser.Parse(null));
            Assert.Null(UnicodeRangeParser.Parse(""));
            Assert.Null(UnicodeRangeParser.Parse("   "));
        }

        [Fact]
        public void Parse_SingleValue_ReturnsInclusiveSinglePoint()
        {
            var ranges = UnicodeRangeParser.Parse("U+41");
            Assert.NotNull(ranges);
            Assert.Single(ranges!);
            Assert.True(UnicodeRangeParser.Covers(ranges!, new Rune('A')));
            Assert.False(UnicodeRangeParser.Covers(ranges!, new Rune('B')));
        }

        [Fact]
        public void Parse_Range_IsInclusiveOnBothEnds()
        {
            var ranges = UnicodeRangeParser.Parse("U+41-5A");
            Assert.NotNull(ranges);
            Assert.True(UnicodeRangeParser.Covers(ranges!, new Rune('A')));  // U+41
            Assert.True(UnicodeRangeParser.Covers(ranges!, new Rune('Z')));  // U+5A, inclusive end
            Assert.False(UnicodeRangeParser.Covers(ranges!, new Rune('a'))); // U+61
        }

        [Fact]
        public void Parse_MultipleCommaSeparatedRanges()
        {
            var ranges = UnicodeRangeParser.Parse("U+0-FF, U+2600-26FF");
            Assert.NotNull(ranges);
            Assert.Equal(2, ranges!.Count);
            Assert.True(UnicodeRangeParser.Covers(ranges, new Rune(0x00)));
            Assert.True(UnicodeRangeParser.Covers(ranges, new Rune(0xFF)));
            Assert.True(UnicodeRangeParser.Covers(ranges, new Rune(0x2600)));
            Assert.True(UnicodeRangeParser.Covers(ranges, new Rune(0x26FF)));
            Assert.False(UnicodeRangeParser.Covers(ranges, new Rune(0x100)));
        }

        [Fact]
        public void Parse_Wildcard_ExpandsToBounds()
        {
            var ranges = UnicodeRangeParser.Parse("U+26??");
            Assert.NotNull(ranges);
            Assert.True(UnicodeRangeParser.Covers(ranges!, new Rune(0x2600)));
            Assert.True(UnicodeRangeParser.Covers(ranges!, new Rune(0x26FF)));
            Assert.False(UnicodeRangeParser.Covers(ranges!, new Rune(0x2700)));
        }

        [Fact]
        public void Parse_Garbage_ReturnsNull()
        {
            Assert.Null(UnicodeRangeParser.Parse("not-a-range"));
        }
    }
}
