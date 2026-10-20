using PeachDrawing;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// CFF fonts in an imported page: a bare CFF program (<c>/Type1C</c> and <c>/CIDFontType0C</c>), and a CID-keyed CFF inside an OpenType
    /// file. The programs are taken from the fonts the repository already tests the font engine with (a name-keyed and a CID-keyed one).
    /// </summary>
    public class PdfCffFontTests
    {
        private static string BaseDir => AppContext.BaseDirectory;

        private static byte[] NameKeyedOtf => File.ReadAllBytes(Path.Combine(BaseDir, "HintingCff.otf"));

        private static byte[] CidKeyedOtf => File.ReadAllBytes(BundledFonts.HintingCffCid);

        private static byte[] CffTable(byte[] otf) => PdfImportedFont.SfntTable(otf, "CFF ")!;

        /// <summary>The indexes of the glyphs of <paramref name="otf"/> that have an outline (some glyphs of these test fonts are blank).</summary>
        private static List<int> GlyphsWithOutlines(byte[] otf)
        {
            var context = new RasterRenderContext();
            var name = "probe-" + Guid.NewGuid().ToString("N");
            context.AddFontFromBytes(name, otf).GetAwaiter().GetResult();
            var typeface = context.GetFont(name, 20, PaintFontStyle.Regular)!.Typeface!;
            return Enumerable.Range(0, 400).Where(g => typeface.TryGetOutline((ushort)g, out var o) && !o.IsEmpty).ToList();
        }

        // ------------------------------------------------------------ glyph names

        [Fact]
        public void StandardStrings_AreInSidOrder()
        {
            Assert.Equal(229, PdfGlyphNames.StandardStrings.Length);
            Assert.Equal(".notdef", PdfGlyphNames.StandardStrings[0]);
            Assert.Equal("space", PdfGlyphNames.StandardStrings[1]);
            Assert.Equal("A", PdfGlyphNames.StandardStrings[34]);
            Assert.Equal("a", PdfGlyphNames.StandardStrings[66]);
            Assert.Equal("exclamdown", PdfGlyphNames.StandardStrings[96]);
            Assert.Equal("fi", PdfGlyphNames.StandardStrings[109]);
            Assert.Equal("zcaron", PdfGlyphNames.StandardStrings[228]);
        }

        [Fact]
        public void EveryStandardName_HasAUnicodeValue()
        {
            foreach (var name in PdfGlyphNames.StandardStrings.Skip(1))
                Assert.True(PdfGlyphNames.ToText(name) is { Length: > 0 }, $"{name} has no text");
        }

        [Theory]
        [InlineData("A", "A")]
        [InlineData("space", " ")]
        [InlineData("eacute", "é")]
        [InlineData("quoteright", "’")]
        [InlineData("fi", "ﬁ")]
        [InlineData("uni0041", "A")]
        [InlineData("uni00410042", "AB")]
        [InlineData("u1F600", "\U0001F600")]
        [InlineData("f_f_i", "ffi")]
        [InlineData("a.sc", "a")]
        [InlineData("Euro", "€")]
        public void GlyphNames_BecomeText(string name, string expected)
        {
            Assert.Equal(expected, PdfGlyphNames.ToText(name));
        }

        [Theory]
        [InlineData(".notdef")]
        [InlineData("")]
        [InlineData("g123")]
        [InlineData("uniD800")]
        [InlineData("uniZZZZ")]
        [InlineData("f_foo")]
        [InlineData("uD800")]
        public void UnknownGlyphNames_HaveNoText(string name)
        {
            Assert.Null(PdfGlyphNames.ToText(name));
        }

        // ------------------------------------------------------------ the CFF reader

        [Fact]
        public void NameKeyedCff_ListsItsGlyphNames_AndTheFontMatrix()
        {
            var cff = CffFont.Parse(CffTable(NameKeyedOtf));

            Assert.NotNull(cff);
            Assert.False(cff!.IsCid);
            Assert.True(cff.GlyphCount > 1);
            Assert.Equal(cff.GlyphCount, cff.GlyphNames.Length);
            Assert.Equal(".notdef", cff.GlyphNames[0]);
            Assert.Contains(cff.GlyphNames, n => n is not null && PdfGlyphNames.ToText(n) is not null);
            Assert.Equal(0.001, cff.FontMatrix[0], 6);
        }

        [Fact]
        public void CidKeyedCff_MapsCidsToGlyphs()
        {
            var cff = CffFont.Parse(CffTable(CidKeyedOtf));

            Assert.NotNull(cff);
            Assert.True(cff!.IsCid);
            Assert.Equal(cff.GlyphCount, cff.CidOfGlyph.Length);
            var map = cff.GlyphOfCid();
            Assert.Equal(0, map[0]);                               // CID 0 is .notdef
            Assert.All(map, kv => Assert.Equal(kv.Key, cff.CidOfGlyph[kv.Value]));
        }

        [Theory]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { 1, 0, 4 })]
        [InlineData(new byte[] { 2, 0, 4, 1, 0, 0 })]
        [InlineData(new byte[] { 1, 0, 4, 1, 0, 1, 1, 1, 0xFF })]
        public void NotACff_IsNull(byte[] data)
        {
            Assert.Null(CffFont.Parse(data));
        }

        [Fact]
        public async Task BareCff_WrappedAsOpenType_IsAcceptedByTheFontLoader_WithItsGlyphsAndNames()
        {
            var cff = CffTable(NameKeyedOtf);
            var info = CffFont.Parse(cff)!;
            var wrapped = SfntRepair.FromCff(cff, info, "wrapped-test");

            var context = new RasterRenderContext();
            await context.AddFontFromBytes("wrapped-test", wrapped);
            var font = context.GetFont("wrapped-test", 20, PaintFontStyle.Regular);

            Assert.NotNull(font);
            var typeface = font!.Typeface!;
            // A glyph that has a Unicode name is reachable through the cmap built from the names.
            var g = GlyphsWithOutlines(NameKeyedOtf).First(i => i < info.GlyphNames.Length && info.GlyphNames[i] is { } n && PdfGlyphNames.ToText(n) is { Length: 1 });
            var rune = new System.Text.Rune(PdfGlyphNames.ToText(info.GlyphNames[g]!)![0]);
            Assert.True(typeface.TryMapRune(rune, out var mapped));
            Assert.Equal(g, mapped);
            Assert.True(typeface.TryGetOutline(mapped, out var outline));
            Assert.False(outline.IsEmpty);
        }

        // ------------------------------------------------------------ in a PDF

        private static string Stream(string entries, byte[] data) => TestPdf.Stream(entries, TestPdf.Binary(data));

        private static int Ink(byte[] pdf)
        {
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            var unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            Assert.Empty(unsupported);
            return CountInk(canvas);
        }

        private static int CountInk(RasterCanvas canvas)
        {
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            int ink = 0;
            for (var i = 3; i < span.Length; i += 4) if (span[i] > 100) ink++;
            return ink;
        }

        [Fact]
        public void Type1CSimpleFont_IsDrawn_ThroughItsGlyphNamesAndTheEncodingDifferences()
        {
            var cffBytes = CffTable(NameKeyedOtf);
            var info = CffFont.Parse(cffBytes)!;
            var name = GlyphsWithOutlines(NameKeyedOtf).Where(i => i > 0 && i < info.GlyphNames.Length).Select(i => info.GlyphNames[i]!).First(n => PdfGlyphNames.ToText(n) is { Length: 1 });
            var font = $"<< /Type /Font /Subtype /Type1 /BaseFont /Test /FirstChar 65 /LastChar 65 /Widths [600] /Encoding << /Differences [65 /{name}] >> /FontDescriptor 6 0 R >>";
            var descriptor = "<< /Type /FontDescriptor /FontName /Test /Flags 32 /FontFile3 7 0 R >>";
            var pdf = TestPdf.Build("BT /F0 60 Tf 20 30 Td (A) Tj ET", resources: "/Font << /F0 5 0 R >>", extraObjects:
                [font, descriptor, Stream("/Subtype /Type1C", cffBytes)]);

            Assert.True(Ink(pdf) > 40, "the glyph was drawn from the CFF outline");
        }

        [Fact]
        public void Type1CFont_WithNoEncoding_ReadsCodesAsLatinText()
        {
            var cffBytes = CffTable(NameKeyedOtf);
            var info = CffFont.Parse(cffBytes)!;
            // Find an ASCII letter the font has by name, and show that code with no /Encoding at all.
            var letter = GlyphsWithOutlines(NameKeyedOtf).Where(i => i < info.GlyphNames.Length).Select(i => info.GlyphNames[i]).First(n => n is { Length: 1 } && char.IsAsciiLetter(n[0]))!;
            var font = $"<< /Type /Font /Subtype /Type1 /BaseFont /Test /FirstChar {(int)letter[0]} /LastChar {(int)letter[0]} /Widths [600] /FontDescriptor 6 0 R >>";
            var descriptor = "<< /Type /FontDescriptor /FontName /Test /Flags 32 /FontFile3 7 0 R >>";
            var pdf = TestPdf.Build($"BT /F0 60 Tf 20 30 Td ({letter}) Tj ET", resources: "/Font << /F0 5 0 R >>", extraObjects:
                [font, descriptor, Stream("/Subtype /Type1C", cffBytes)]);

            Assert.True(Ink(pdf) > 40);
        }

        private static byte[] CidPdf(string subtype, byte[] program, int cid)
        {
            var type0 = "<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding /Identity-H /DescendantFonts [6 0 R] >>";
            var cidFont = "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /Test /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 1000 /FontDescriptor 7 0 R >>";
            var descriptor = $"<< /Type /FontDescriptor /FontName /Test /Flags 4 /FontFile3 8 0 R >>";
            var hex = cid.ToString("X4");
            return TestPdf.Build($"BT /F0 60 Tf 20 30 Td <{hex}> Tj ET", resources: "/Font << /F0 5 0 R >>", extraObjects:
                [type0, cidFont, descriptor, Stream($"/Subtype /{subtype}", program)]);
        }

        [Fact]
        public void CidKeyedCff_BareAndInsideOpenType_IsDrawnByCid()
        {
            var otf = CidKeyedOtf;
            var cff = CffTable(otf);
            var info = CffFont.Parse(cff)!;
            // A CID that is not its own glyph index, so the charset mapping is what finds the glyph.
            var drawn = GlyphsWithOutlines(otf).Where(g => g > 0 && g < info.CidOfGlyph.Length).ToList();
            var cid = drawn.Select(g => info.CidOfGlyph[g]).Where(c => c != 0).First();
            Assert.Contains(drawn, g => info.CidOfGlyph[g] == cid);

            Assert.True(Ink(CidPdf("CIDFontType0C", cff, cid)) > 40, "bare CID-keyed CFF");
            Assert.True(Ink(CidPdf("OpenType", otf, cid)) > 40, "CID-keyed CFF inside an OpenType file");
        }

        [Fact]
        public void CffThatCannotBeParsed_IsReported()
        {
            var font = "<< /Type /Font /Subtype /Type1 /BaseFont /Test /FirstChar 65 /LastChar 65 /Widths [600] /FontDescriptor 6 0 R >>";
            var descriptor = "<< /Type /FontDescriptor /FontName /Test /Flags 32 /FontFile3 7 0 R >>";
            var pdf = TestPdf.Build("BT /F0 60 Tf 20 30 Td (A) Tj ET", resources: "/Font << /F0 5 0 R >>", extraObjects:
                [font, descriptor, Stream("/Subtype /Type1C", [1, 2, 3, 4, 5])]);
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);

            var unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);

            Assert.Contains("font: embedded font is not an sfnt or CFF program", unsupported);
        }

        [Theory]
        [InlineData("CIDFontType0C")]
        [InlineData("OpenType")]
        public void CidKeyedCff_MapsEachCidToTheGlyphThatCarriesIt(string subtype)
        {
            var otf = CidKeyedOtf;
            var cff = CffTable(otf);
            var info = CffFont.Parse(cff)!;
            var gid = GlyphsWithOutlines(otf).First(g => g > 0 && g < info.CidOfGlyph.Length);      // (in this font a CID is its own glyph index: the charset is the identity)
            var cid = info.CidOfGlyph[gid];

            var doc = PdfSourceDocument.Open(CidPdf(subtype, subtype == "OpenType" ? otf : cff, cid));
            var resources = (PdfDictionary)doc.InheritedAttribute(doc.Pages[0], "Resources")!;
            var fontDict = doc.DictOf(doc.DictOf(resources, "Font")!, "F0")!;
            var font = PdfImportedFont.Load(doc, fontDict, new RasterRenderContext(), out var why)!;

            Assert.Null(why);
            var glyph = font.Decode(((char)(cid >> 8)).ToString() + (char)(cid & 0xFF)).Single();
            Assert.Equal(cid, glyph.Code);
            Assert.Equal(gid, glyph.GlyphIndex);
        }
    }
}
