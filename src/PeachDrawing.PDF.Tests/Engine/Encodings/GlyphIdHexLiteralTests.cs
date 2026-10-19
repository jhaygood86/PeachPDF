using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Text.Shaping;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Internal;

namespace PeachPDF.Tests.PdfEngineTests.Encodings
{
    /// <summary>
    /// <see cref="PdfEncoders.FormatGlyphIdsAsHexLiteral"/> replaced a chain of string and byte-array conversions; it has to produce
    /// exactly the text that chain did, line breaks included.
    /// </summary>
    public class GlyphIdHexLiteralTests
    {
        private static string ViaTheLongRoute(IReadOnlyList<PlacedGlyph> glyphs)
        {
            var text = new string(glyphs.Select(g => (char)g.GlyphIndex).ToArray());
            var bytes = PdfEncoders.RawBigEndianUnicodeEncoding.GetBytes(text);
            bytes = PdfEncoders.FormatStringLiteral(bytes, true, false, true);

            return PdfEncoders.LatinOneByteEncoding.GetString(bytes, 0, bytes.Length);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(23)]
        [InlineData(24)]
        [InlineData(25)]
        [InlineData(26)]
        [InlineData(48)]
        [InlineData(49)]
        [InlineData(50)]
        [InlineData(73)]
        [InlineData(97)]
        [InlineData(300)]
        public void MatchesTheLongRouteAtEveryLength(int count)
        {
            var random = new Random(count);
            var glyphs = Enumerable.Range(0, count)
                .Select(_ => new PlacedGlyph(random.Next(0, 0x10000), 0, 1))
                .ToList();

            Assert.Equal(ViaTheLongRoute(glyphs), PdfEncoders.FormatGlyphIdsAsHexLiteral(glyphs));
        }

        [Fact]
        public void IsUppercaseHexInAngleBrackets()
        {
            var glyphs = new[] { new PlacedGlyph(0x00AB, 0, 1), new PlacedGlyph(0xBEEF, 1, 1), new PlacedGlyph(7, 2, 1) };

            Assert.Equal("<00ABBEEF0007>", PdfEncoders.FormatGlyphIdsAsHexLiteral(glyphs));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(24)]
        [InlineData(25)]
        [InlineData(300)]
        public void WritingIntoASpanGivesTheTextTheStringHas(int count)
        {
            var random = new Random(count + 1);
            var glyphs = Enumerable.Range(0, count)
                .Select(_ => new PlacedGlyph(random.Next(0, 0x10000), 0, 1))
                .ToList();

            Span<char> buffer = new char[PdfEncoders.GlyphIdsHexLiteralLength(count)];
            PdfEncoders.WriteGlyphIdsAsHexLiteral(glyphs, buffer);

            Assert.Equal(PdfEncoders.FormatGlyphIdsAsHexLiteral(glyphs), buffer.ToString());
        }

        // A run is written from a 512-character stack buffer, and one whose literal is longer than that takes an array instead.
        [Fact]
        public void AnIndexAboveSixteenBitsKeepsItsLowUnit_AsTheCharCastDid()
        {
            var glyphs = new[] { new PlacedGlyph(0x1ABCD, 0, 1) };

            Assert.Equal(ViaTheLongRoute(glyphs), PdfEncoders.FormatGlyphIdsAsHexLiteral(glyphs));
            Assert.Equal("<ABCD>", PdfEncoders.FormatGlyphIdsAsHexLiteral(glyphs));
        }
    }
}
