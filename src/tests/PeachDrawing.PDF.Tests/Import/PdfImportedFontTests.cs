using PeachDrawing;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// Fonts of an imported page: simple TrueType fonts (encodings, Differences, widths, ToUnicode) and Type0 fonts (CIDToGIDMap, widths),
    /// built around a real font file embedded in a hand-written PDF, and the reasons a font cannot be drawn.
    /// </summary>
    public class PdfImportedFontTests
    {
        private static readonly string FontFile = TestPdf.Binary(File.ReadAllBytes(BundledFonts.Ttf));

        /// <summary>A PDF whose page resources name font /F0, defined by <paramref name="fontDict"/> (object 5) and its supporting objects (6...).</summary>
        private static (PdfSourceDocument Doc, PdfDictionary Font, PdfDictionary Resources) Open(string fontDict, params string[] more)
        {
            var doc = PdfSourceDocument.Open(TestPdf.Build("", resources: "/Font << /F0 5 0 R >>", extraObjects: [fontDict, .. more]));
            var resources = (PdfDictionary)doc.InheritedAttribute(doc.Pages[0], "Resources")!;
            var font = doc.DictOf(doc.DictOf(resources, "Font")!, "F0")!;
            return (doc, font, resources);
        }

        private static PdfImportedFont? Load(string fontDict, out string? why, params string[] more)
        {
            var (doc, font, _) = Open(fontDict, more);
            return PdfImportedFont.Load(doc, font, new RasterRenderContext(), out why);
        }

        private const string Descriptor = "<< /Type /FontDescriptor /FontName /Test /Flags 32 /MissingWidth 500 /FontFile2 6 0 R >>";

        private static string FontStream() => TestPdf.Stream("/Length1 " + FontFile.Length, FontFile);

        private static string SimpleFont(string extra = "") =>
            "<< /Type /Font /Subtype /TrueType /BaseFont /Test /FirstChar 65 /LastChar 66 /Widths [700 800] /FontDescriptor 7 0 R " + extra + " >>";

        [Fact]
        public void SimpleTrueTypeFont_MapsCodesThroughItsEncodingAndDifferences()
        {
            var font = Load(SimpleFont("/Encoding << /BaseEncoding /WinAnsiEncoding /Differences [67 /B /uni0041 /space /Euro] >>"), out var why, FontStream(), Descriptor)!;
            // Objects: 5 = font, 6 = the font program, 7 = the descriptor. The descriptor's /FontFile2 names 6.
            Assert.Null(why);

            var glyphs = font.Decode("ABCDEF").ToList();

            Assert.Equal([0.7, 0.8], [glyphs[0].Width, glyphs[1].Width]);              // from /Widths
            Assert.Equal("B", glyphs[2].Text);                                          // Differences: 67 -> /B
            Assert.Equal("A", glyphs[3].Text);                                          // 68 -> /uni0041
            Assert.Equal(" ", glyphs[4].Text);                                          // 69 -> /space
            Assert.False(glyphs[4].IsWordSpace);                                        // only code 32 is a word space
            Assert.NotEqual(0, glyphs[3].GlyphIndex);                                   // /uni0041 finds the font's A
            Assert.Equal("€", glyphs[5].Text);                                     // 70 (F) -> /Euro... differences continue: 70 is /Euro
            Assert.All(glyphs, g => Assert.True(g.Width > 0, $"code {g.Code} has width {g.Width}"));  // codes outside /Widths take the glyph's own advance
        }

        [Fact]
        public void SimpleFont_WithWinAnsiHighCodes_AndASpaceCode()
        {
            var font = Load(SimpleFont("/Encoding /WinAnsiEncoding"), out _, FontStream(), Descriptor)!;

            var glyphs = font.Decode("\u0080\u0095 ").ToList();

            Assert.Equal("€", glyphs[0].Text);
            Assert.Equal("•", glyphs[1].Text);
            Assert.True(glyphs[2].IsWordSpace);
        }

        [Fact]
        public void ToUnicode_TakesPrecedenceOverTheEncoding()
        {
            var cmap = TestPdf.Stream("", "1 beginbfchar <41> <005A> endbfchar");
            var font = Load(SimpleFont("/ToUnicode 8 0 R"), out _, FontStream(), Descriptor, cmap)!;

            Assert.Equal("Z", font.Decode("A").Single().Text);
            Assert.Equal("B", font.Decode("B").Single().Text);
        }

        [Theory]
        [InlineData("/Subtype /Type3", "font /Type3")]
        [InlineData("/Subtype /Type1 /FontDescriptor 6 0 R", "no embedded TrueType, OpenType, CFF or Type 1 program")]
        [InlineData("/Subtype /Type0 /Encoding /UniJIS-UCS2-H", "Type0 encoding /UniJIS-UCS2-H")]
        [InlineData("/Subtype /Type0 /Encoding /Identity-H", "Type0 without descendant")]
        public void FontsThatCannotBeDrawn_SayWhy(string entries, string expected)
        {
            var load = Load($"<< /Type /Font {entries} >>", out var why, "<< /Type /FontDescriptor /FontName /X >>");

            Assert.Null(load);
            Assert.Equal(expected, why);
        }

        [Fact]
        public void EmbeddedProgramThatIsNotAnSfnt_IsRefused()
        {
            var load = Load(SimpleFont(), out var why, TestPdf.Stream("", "this is not a font program at all, not even close"), Descriptor);

            Assert.Null(load);
            Assert.Equal("embedded font is not an sfnt, CFF or Type 1 program", why);
        }

        [Fact]
        public void SfntThatCannotBeLoaded_IsReportedNotThrown()
        {
            // The right magic number and a table directory that points nowhere.
            var broken = TestPdf.Binary([0, 1, 0, 0, 0, 1, 0, 16, 0, 0, 0, 0, 0x68, 0x65, 0x61, 0x64, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xFF]);
            var load = Load(SimpleFont(), out var why, TestPdf.Stream("", broken), Descriptor);

            Assert.Null(load);
            Assert.NotNull(why);
        }

        [Fact]
        public void Type0Font_ReadsWidthsAndTheCidToGidMap()
        {
            // CID 1 -> glyph 5, CID 2 -> glyph 6 (two bytes each); widths: cid 1 = 600, cids 3..5 = 800, others the default 500.
            var map = TestPdf.Stream("", TestPdf.Binary([0, 0, 0, 5, 0, 6]));
            var cid = "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Test /DW 500 /W [1 [600] 3 5 800] /CIDToGIDMap 8 0 R /FontDescriptor 7 0 R >>";
            var font = Load("<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding /Identity-H /DescendantFonts [6 0 R] >>", out var why, cid, Descriptor.Replace("6 0 R", "9 0 R"), map, FontStream())!;

            Assert.Null(why);
            var glyphs = font.Decode("\0\u0001\0\u0002\0\u0004\0c").ToList();

            Assert.Equal([5, 6], [glyphs[0].GlyphIndex, glyphs[1].GlyphIndex]);
            Assert.Equal([0.6, 0.5, 0.8, 0.5], glyphs.Select(g => g.Width));
            Assert.Equal(0x63, glyphs[3].Code);
            Assert.Equal(0x63, glyphs[3].GlyphIndex);               // past the map: the CID is the glyph
        }

        [Fact]
        public void Type0Font_WithoutACidToGidMap_UsesIdentity_AndReadsItsToUnicode()
        {
            var cmap = TestPdf.Stream("", "1 beginbfrange <0010> <0012> <0061> endbfrange");
            var cid = "<< /Type /Font /Subtype /CIDFontType2 /FontDescriptor 7 0 R >>";
            var font = Load("<< /Type /Font /Subtype /Type0 /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 8 0 R >>", out _, cid, Descriptor.Replace("6 0 R", "9 0 R"), cmap, FontStream())!;

            var glyphs = font.Decode("\0\u0010\0\u0012\0\u0013").ToList();

            Assert.Equal("a", glyphs[0].Text);
            Assert.Equal("c", glyphs[1].Text);
            Assert.Equal("", glyphs[2].Text);
            Assert.Equal(1.0, glyphs[0].Width);                      // /DW defaults to 1000
        }

        [Fact]
        public void TheFontIsOfferedAtAnySize_AndRegisteredOncePerContext()
        {
            var (doc, font, _) = Open(SimpleFont(), FontStream(), Descriptor);
            var context = new RasterRenderContext();

            var first = PdfImportedFont.Load(doc, font, context, out _)!;
            var second = PdfImportedFont.Load(doc, font, context, out _)!;

            Assert.Equal(12, first.GetFont(12)!.Size);
            Assert.Equal(30, second.GetFont(30)!.Size);
        }
    }
}
