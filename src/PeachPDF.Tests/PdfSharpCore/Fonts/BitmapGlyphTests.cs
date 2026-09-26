using PeachDrawing.Text.Internal.Fonts;
using PeachPDF.Adapters;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>Bitmap colour glyphs (CBDT/CBLC and sbix): parsing, and drawing through the PDF and raster backends.</summary>
    public class BitmapGlyphTests
    {
        private static byte[] BaseFont() => File.ReadAllBytes(BundledFonts.Ttf);

        private static BitmapGlyphSource? Source(byte[] font) => FontFileData.GetOrCreateFrom(font).Fontface.bitmap;

        private static byte[] Png(int w, int h, byte r, byte g, byte b) => RasterPngFixture.MakeSolidRgbaPngBytes(w, h, r, g, b);

        // ---- parsing -------------------------------------------------------------------------------------

        [Fact]
        public void AFontWithoutBitmapTables_HasNoBitmapGlyphs()
        {
            Assert.Null(Source(BaseFont()));
        }

        [Fact]
        public void Cbdt_Format17_ExposesThePictureAndItsBearings()
        {
            var font = BaseFont();
            var a = BitmapGlyphFontFixture.GlyphId(font, 'A');
            var withBitmaps = BitmapGlyphFontFixture.WithCbdt(font,
                new BitmapGlyphFontFixture.Picture(20, a, Png(8, 6, 255, 0, 0), 8, 6, 1, 5));

            var source = Source(withBitmaps);
            Assert.NotNull(source);
            Assert.True(source!.HasGlyph(a));
            Assert.False(source.HasGlyph(BitmapGlyphFontFixture.GlyphId(font, 'B')));

            Assert.True(source.TryGet(a, 20, out var glyph));
            Assert.Equal(20, glyph.Ppem);
            Assert.Equal(8, glyph.Width);
            Assert.Equal(6, glyph.Height);
            Assert.Equal(1, glyph.BearingX);
            Assert.Equal(5, glyph.BearingTop);
            Assert.Equal(Png(8, 6, 255, 0, 0), glyph.Data);
        }

        [Theory]
        [InlineData(18)]
        [InlineData(19)]
        public void Cbdt_BigMetricsFormats_AreReadToo(int imageFormat)
        {
            var font = BaseFont();
            var a = BitmapGlyphFontFixture.GlyphId(font, 'A');
            var withBitmaps = BitmapGlyphFontFixture.WithCbdt(font,
                new BitmapGlyphFontFixture.Picture(24, a, Png(10, 12, 0, 0, 255), 10, 12, -2, 11, imageFormat));

            Assert.True(Source(withBitmaps)!.TryGet(a, 24, out var glyph));
            Assert.Equal(10, glyph.Width);
            Assert.Equal(12, glyph.Height);
            Assert.Equal(-2, glyph.BearingX);
            Assert.Equal(11, glyph.BearingTop);
        }

        [Theory]
        [InlineData(17, 1)]
        [InlineData(17, 3)]
        [InlineData(17, 4)]
        [InlineData(18, 3)]
        [InlineData(19, 2)]
        [InlineData(19, 5)]
        public void EveryIndexSubtableFormat_LocatesThePicture(int imageFormat, int indexFormat)
        {
            var font = BaseFont();
            var a = BitmapGlyphFontFixture.GlyphId(font, 'A');
            var withBitmaps = BitmapGlyphFontFixture.WithCbdt(font,
                new BitmapGlyphFontFixture.Picture(24, a, Png(10, 12, 0, 0, 255), 10, 12, 3, 9, imageFormat, indexFormat));
            var source = Source(withBitmaps)!;

            Assert.True(source.TryGet(a, 24, out var glyph));
            Assert.Equal(Png(10, 12, 0, 0, 255), glyph.Data);
            Assert.Equal(3, glyph.BearingX);
            Assert.Equal(9, glyph.BearingTop);
            Assert.False(source.HasGlyph(a + 1));
        }

        [Fact]
        public void TheStrikeChosen_IsTheSmallestAtLeastTheSize_ElseTheLargest()
        {
            var font = BaseFont();
            var a = BitmapGlyphFontFixture.GlyphId(font, 'A');
            var withBitmaps = BitmapGlyphFontFixture.WithCbdt(font,
                new BitmapGlyphFontFixture.Picture(16, a, Png(4, 4, 1, 1, 1), 4, 4, 0, 4),
                new BitmapGlyphFontFixture.Picture(32, a, Png(8, 8, 2, 2, 2), 8, 8, 0, 8),
                new BitmapGlyphFontFixture.Picture(64, a, Png(16, 16, 3, 3, 3), 16, 16, 0, 16));
            var source = Source(withBitmaps)!;

            Assert.True(source.TryGet(a, 10, out var small));
            Assert.Equal(16, small.Ppem);
            Assert.True(source.TryGet(a, 20, out var medium));
            Assert.Equal(32, medium.Ppem);
            Assert.True(source.TryGet(a, 32, out var exact));
            Assert.Equal(32, exact.Ppem);
            Assert.True(source.TryGet(a, 500, out var huge));
            Assert.Equal(64, huge.Ppem);
        }

        [Fact]
        public void Sbix_ExposesPicturesAndFollowsDupeRedirects()
        {
            var font = BaseFont();
            var a = BitmapGlyphFontFixture.GlyphId(font, 'A');
            var b = BitmapGlyphFontFixture.GlyphId(font, 'B');
            var withBitmaps = BitmapGlyphFontFixture.WithSbix(font,
                [new BitmapGlyphFontFixture.Picture(40, a, Png(20, 10, 0, 255, 0), 20, 10, 2, 3)],
                new Dictionary<(int, int), int> { [(40, b)] = a });

            var source = Source(withBitmaps)!;

            Assert.True(source.TryGet(a, 40, out var glyph));
            Assert.Equal(40, glyph.Ppem);
            Assert.Equal(20, glyph.Width);
            Assert.Equal(10, glyph.Height);
            Assert.Equal(2, glyph.BearingX);
            // sbix gives the lower-left corner's offset: the top is that plus the picture's height.
            Assert.Equal(13, glyph.BearingTop);

            Assert.True(source.TryGet(b, 40, out var duplicate));
            Assert.Equal(glyph.Data, duplicate.Data);
        }

        [Fact]
        public void AMalformedTable_IsTheSameAsNoTable()
        {
            var font = BaseFont();
            var truncated = SyntheticFontTables.InsertTableDirectoryEntry(SyntheticFontTables.InsertTableDirectoryEntry(font, "CBLC", [0, 3, 0, 0, 0, 0, 0, 9]), "CBDT", [0, 3, 0, 0]);

            Assert.Null(Source(truncated));
        }

        [Fact]
        public void TheDescriptor_ReportsBitmapGlyphs_AndTreatsThemAsAColourFont()
        {
            var font = BaseFont();
            var a = BitmapGlyphFontFixture.GlyphId(font, 'A');
            var withBitmaps = BitmapGlyphFontFixture.WithCbdt(font, new BitmapGlyphFontFixture.Picture(20, a, Png(8, 6, 255, 0, 0), 8, 6, 1, 5));
            var face = FontFileData.GetOrCreateFrom(withBitmaps).Fontface;
            var descriptor = new OpenTypeDescriptor("bitmap", "bitmap", face);

            Assert.True(descriptor.HasBitmapGlyphs);
            Assert.True(descriptor.IsColorFont);
            Assert.True(descriptor.HasBitmapGlyph(a));
            Assert.False(descriptor.TryGetBitmapGlyph(a + 1000 > 60000 ? 1 : a + 1000, 20, out _));

            var plain = FontFileData.GetOrCreateFrom(font).Fontface;
            Assert.False(new OpenTypeDescriptor("plain", "plain", plain).HasBitmapGlyphs);
        }

        // ---- drawing -------------------------------------------------------------------------------------

        private static async Task<(PdfSharpAdapter Adapter, string Family, byte[] Font)> Register(string family, Func<byte[], byte[]> addTables)
        {
            var adapter = new PdfSharpAdapter();
            var bytes = addTables(BaseFont());
            using var stream = new MemoryStream(bytes);
            await adapter.AddFont(stream, family);
            return (adapter, family, bytes);
        }

        [Fact]
        public async Task TheRasterBackend_DrawsTheBitmapGlyph_InsteadOfAnOutline()
        {
            var (adapter, family, font) = await Register("BitmapRaster", bytes =>
            {
                var a = BitmapGlyphFontFixture.GlyphId(bytes, 'A');
                return BitmapGlyphFontFixture.WithCbdt(bytes, new BitmapGlyphFontFixture.Picture(20, a, Png(16, 16, 255, 0, 0), 16, 16, 0, 16));
            });
            var surface = new RasterSurface(80, 60, 0, 0, 1, 1);
            var g = new RasterGraphics(adapter, surface, 1);
            var f = adapter.GetFont(family, 20, RFontStyle.Regular)!;

            g.DrawString("A", f, RColor.FromArgb(255, 0, 0, 0), new RPoint(10, 10), g.MeasureString("A", f));

            // A 16 x 16 strike pixel red square at 20 ppem and 20pt: 16 x 16 device pixels, its lower edge on the baseline; no black outline ink.
            var red = 0;
            var black = 0;
            for (var i = 0; i < surface.Pixels.Length; i += 4)
            {
                if (surface.Pixels[i + 3] < 200)
                    continue;

                if (surface.Pixels[i] > 200 && surface.Pixels[i + 1] < 40)
                    red++;
                else
                    black++;
            }

            Assert.InRange(red, 200, 300);
            Assert.Equal(0, black);
            _ = font;
        }

        private static PdfGenerateConfig Config() => new()
        {
            PageSize = PageSize.A4,
            CompressContentStreams = false,
            MarginLeft = 0,
            MarginTop = 0,
            MarginRight = 0,
            MarginBottom = 0,
        };

        private static async Task<string> RenderPdf(string html, byte[] font)
        {
            var generator = new PdfGenerator();
            using (var stream = new MemoryStream(font))
                await generator.AddFontFromStream(stream);

            var doc = await generator.GeneratePdf(html, Config());
            var ms = new MemoryStream();
            doc.Save(ms);
            return System.Text.Encoding.Latin1.GetString(ms.ToArray());
        }

        private static string FamilyOf(byte[] font)
        {
            var path = Path.Combine(Path.GetTempPath(), $"bitmapglyph-{Guid.NewGuid():N}.ttf");
            File.WriteAllBytes(path, font);
            try
            {
                return PeachDrawing.Text.Internal.Fonts.TtfFontDescription.LoadDescription(path).FontFamilyInvariantCulture;
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task InAPdf_ABitmapGlyphIsAnImage_WithItsTextStillSelectable()
        {
            var bytes = BaseFont();
            var a = BitmapGlyphFontFixture.GlyphId(bytes, 'A');
            var font = BitmapGlyphFontFixture.WithCbdt(bytes, new BitmapGlyphFontFixture.Picture(20, a, Png(16, 16, 255, 0, 0), 16, 16, 0, 16));

            var pdf = await RenderPdf($"<html><body style=\"margin:0\"><p style=\"font-family:'{FamilyOf(font)}';font-size:40px;margin:0\">A</p></body></html>", font);

            Assert.Single(Regex.Matches(pdf, @"/I\d+\s+Do"));
            Assert.Matches(@"/Subtype\s*/Image", pdf);
            Assert.Contains("3 Tr", pdf);
        }

        [Fact]
        public async Task ARepeatedBitmapGlyph_SharesOneImageObject()
        {
            var bytes = BaseFont();
            var a = BitmapGlyphFontFixture.GlyphId(bytes, 'A');
            var font = BitmapGlyphFontFixture.WithCbdt(bytes, new BitmapGlyphFontFixture.Picture(20, a, Png(16, 16, 255, 0, 0), 16, 16, 0, 16));

            var pdf = await RenderPdf($"<html><body style=\"margin:0\"><p style=\"font-family:'{FamilyOf(font)}';font-size:40px;margin:0\">AAAA</p></body></html>", font);

            Assert.Equal(4, Regex.Matches(pdf, @"/I\d+\s+Do").Count);
            Assert.Single(Regex.Matches(pdf, @"/Subtype\s*/Image"));
        }

        [Fact]
        public async Task AGlyphWithoutAPicture_StillDrawsItsOutline()
        {
            var bytes = BaseFont();
            var a = BitmapGlyphFontFixture.GlyphId(bytes, 'A');
            var font = BitmapGlyphFontFixture.WithCbdt(bytes, new BitmapGlyphFontFixture.Picture(20, a, Png(16, 16, 255, 0, 0), 16, 16, 0, 16));

            var pdf = await RenderPdf($"<html><body style=\"margin:0\"><p style=\"font-family:'{FamilyOf(font)}';font-size:40px;margin:0\">AB</p></body></html>", font);

            // The picture for A, and B as ordinary text (visible).
            Assert.Single(Regex.Matches(pdf, @"/I\d+\s+Do"));
        }
    }
}
