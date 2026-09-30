using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The <c>SVG </c> table: documents for glyphs, ranges, compression, and a table that is damaged or hostile.
    /// </summary>
    public class SvgGlyphTests
    {
        private static byte[] FixtureBytes() => File.ReadAllBytes(BundledFonts.SvgTest);

        private static Typeface Load(byte[] font)
        {
            var set = new FontSet();
            var family = set.AddData(font, new AddOptions { FamilyName = "Svg-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static ushort Glyph(Typeface face, char c)
        {
            Assert.True(face.TryMapRune(new Rune(c), out var glyph));
            return glyph;
        }

        private static (int Offset, int Length) TableOf(byte[] font, string tag)
        {
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (Encoding.ASCII.GetString(font, record, 4) == tag)
                {
                    int offset = (font[record + 8] << 24) | (font[record + 9] << 16) | (font[record + 10] << 8) | font[record + 11];
                    int length = (font[record + 12] << 24) | (font[record + 13] << 16) | (font[record + 14] << 8) | font[record + 15];
                    return (offset, length);
                }
            }

            throw new InvalidOperationException(tag);
        }

        [Fact]
        public void AFontWithAnSvgTable_HasSvgGlyphs()
        {
            Assert.True(Load(FixtureBytes()).HasSvgGlyphs);
        }

        [Fact]
        public void AFontWithoutOne_HasNone_AndNoGlyphHasADocument()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = "NoSvg-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));

            Assert.False(match.Typeface.HasSvgGlyphs);
            Assert.False(match.Typeface.TryGetSvgGlyph(5, out _));
        }

        [Fact]
        public void AnUncompressedDocument_IsReturnedForItsGlyph()
        {
            var face = Load(FixtureBytes());
            var a = Glyph(face, 'A');

            Assert.True(face.TryGetSvgGlyph(a, out var svg));

            Assert.StartsWith("<svg", svg.Document);
            Assert.Contains("id=\"glyph2\"", svg.Document);
            Assert.Contains("var(--color0", svg.Document);
            Assert.Equal("glyph2", svg.ElementId);
            Assert.Equal((2, 2), (svg.FirstGlyph, svg.LastGlyph));
            Assert.True(svg.CoversOneGlyph);
            Assert.Equal(1000, svg.UnitsPerEm);
        }

        [Fact]
        public void ACompressedDocument_CoveringARange_IsInflated_AndSharedByItsGlyphs()
        {
            var face = Load(FixtureBytes());

            Assert.True(face.TryGetSvgGlyph(Glyph(face, 'B'), out var b));
            Assert.True(face.TryGetSvgGlyph(Glyph(face, 'C'), out var c));

            Assert.StartsWith("<svg", b.Document);
            Assert.Contains("context-fill", b.Document);
            Assert.Equal(b.Document, c.Document);
            Assert.Equal("glyph3", b.ElementId);
            Assert.Equal("glyph4", c.ElementId);
            Assert.Equal((3, 4), (b.FirstGlyph, b.LastGlyph));
            Assert.False(b.CoversOneGlyph);
        }

        [Fact]
        public void AByteOrderMark_IsNotPartOfTheDocument()
        {
            var face = Load(FixtureBytes());

            Assert.True(face.TryGetSvgGlyph(Glyph(face, 'D'), out var d));

            Assert.StartsWith("<svg", d.Document);
            Assert.DoesNotContain("glyph5", d.Document);
        }

        [Fact]
        public void AGlyphWithoutADocument_HasNone()
        {
            var face = Load(FixtureBytes());

            Assert.False(face.TryGetSvgGlyph(Glyph(face, 'A') == 2 ? (ushort)1 : (ushort)0, out _));
            Assert.False(face.TryGetSvgGlyph(ushort.MaxValue, out _));
        }

        [Fact]
        public void ACompressedDocumentThatInflatesPastTheLimit_IsRefused()
        {
            var face = Load(FixtureBytes());

            Assert.False(face.TryGetSvgGlyph(Glyph(face, 'E'), out _));
            // The others are unharmed.
            Assert.True(face.TryGetSvgGlyph(Glyph(face, 'B'), out _));
        }

        [Fact]
        public void TheSameDocument_IsGivenEachTime()
        {
            var face = Load(FixtureBytes());
            var a = Glyph(face, 'A');
            face.TryGetSvgGlyph(a, out var first);
            face.TryGetSvgGlyph(a, out var second);

            Assert.Equal(first, second);
        }

        // ---- damaged tables ---------------------------------------------------------------------------------------------------------

        [Fact]
        public void ATableWithAnUnknownVersion_IsIgnored()
        {
            var font = FixtureBytes();
            var (offset, _) = TableOf(font, "SVG ");
            font[offset + 1] = 1;

            Assert.False(Load(font).HasSvgGlyphs);
        }

        [Fact]
        public void ARecordThatPointsOutsideTheTable_MakesTheTableIgnored()
        {
            var font = FixtureBytes();
            var (offset, _) = TableOf(font, "SVG ");
            int list = offset + ((font[offset + 2] << 24) | (font[offset + 3] << 16) | (font[offset + 4] << 8) | font[offset + 5]);
            font[list + 2 + 4] = 0x7F;      // the first record's document offset

            Assert.False(Load(font).HasSvgGlyphs);
        }

        [Fact]
        public void RecordsOutOfOrder_MakeTheTableIgnored()
        {
            var font = FixtureBytes();
            var (offset, _) = TableOf(font, "SVG ");
            int list = offset + ((font[offset + 2] << 24) | (font[offset + 3] << 16) | (font[offset + 4] << 8) | font[offset + 5]);
            // The second record's first glyph becomes 1, before the first record's 2.
            font[list + 2 + 12] = 0;
            font[list + 2 + 13] = 1;

            Assert.False(Load(font).HasSvgGlyphs);
        }

        [Fact]
        public void ATruncatedTable_IsIgnored()
        {
            var font = FixtureBytes();
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                if (Encoding.ASCII.GetString(font, record, 4) == "SVG ")
                {
                    font[record + 12] = font[record + 13] = font[record + 14] = 0;
                    font[record + 15] = 8;
                }
            }

            Assert.False(Load(font).HasSvgGlyphs);
        }

        [Fact]
        public void ADamagedGzipStream_GivesNoDocument_ForThatGlyphOnly()
        {
            var font = FixtureBytes();
            var (offset, _) = TableOf(font, "SVG ");
            int list = offset + ((font[offset + 2] << 24) | (font[offset + 3] << 16) | (font[offset + 4] << 8) | font[offset + 5]);
            int record = list + 2 + 12;        // B and C's record
            int docOffset = (font[record + 4] << 24) | (font[record + 5] << 16) | (font[record + 6] << 8) | font[record + 7];
            int at = list + docOffset;
            for (int i = 12; i < 40; i++)
            {
                font[at + i] ^= 0xFF;
            }

            var face = Load(font);

            Assert.False(face.TryGetSvgGlyph(Glyph(face, 'B'), out _));
            Assert.True(face.TryGetSvgGlyph(Glyph(face, 'A'), out _));
        }

        [Fact]
        public void AnyOneDamagedByteInTheTable_NeverThrows()
        {
            var original = FixtureBytes();
            var (offset, length) = TableOf(original, "SVG ");
            for (int at = offset; at < offset + Math.Min(length, 200); at++)
            {
                foreach (byte value in new byte[] { 0x00, 0xFF, 0x80 })
                {
                    var font = (byte[])original.Clone();
                    font[at] = value;
                    var face = Load(font);
                    for (ushort glyph = 0; glyph < 8; glyph++)
                    {
                        face.TryGetSvgGlyph(glyph, out _);
                    }
                }
            }
        }
    }
}
