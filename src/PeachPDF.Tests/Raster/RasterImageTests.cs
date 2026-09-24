using PeachImage;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Raster
{
    /// <summary>Decoded images drawn into a raster surface: every source kind the PDF embedder distinguishes has to yield pixels.</summary>
    public class RasterImageTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static RasterGraphics NewGraphics(int width, int height)
        {
            var surface = new RasterSurface(width, height, 0, 0, 1, 1);
            return new RasterGraphics(Adapter, surface, 1);
        }

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private static ImageAdapter Load(byte[] bytes) => new(XImage.FromStream(() => new MemoryStream(bytes)));

        /// <summary>A 2 x 2 image: red, green / blue, white.</summary>
        private static byte[] QuadrantPng() => RasterPngFixture.MakeRgbaPngBytes(2, 2, (x, y) => (x, y) switch
        {
            (0, 0) => (255, 0, 0, 255),
            (1, 0) => (0, 255, 0, 255),
            (0, 1) => (0, 0, 255, 255),
            _ => (255, 255, 255, 255),
        });

        [Fact]
        public void PngWithAlpha_IsDrawnScaledToItsDestination()
        {
            var g = NewGraphics(20, 20);
            var image = Load(QuadrantPng());
            image.Interpolate = false;

            g.DrawImage(image, new RRect(0, 0, 20, 20));

            // Nearest-neighbour: each source pixel becomes a crisp 10 x 10 block.
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(g, 4, 4));
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(g, 15, 4));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(g, 4, 15));
            Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(g, 15, 15));
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(g, 9, 9));
        }

        [Fact]
        public void Interpolation_BlendsAcrossTheBoundaryBetweenSourcePixels()
        {
            var g = NewGraphics(20, 20);
            var image = Load(QuadrantPng());
            image.Interpolate = true;

            g.DrawImage(image, new RRect(0, 0, 20, 20));

            var edge = Pixel(g, 10, 4);
            Assert.InRange(edge[0], 60, 200);
            Assert.InRange(edge[1], 60, 200);
        }

        [Fact]
        public void SourceRectangle_SelectsPartOfTheImage()
        {
            var g = NewGraphics(10, 10);
            var image = Load(QuadrantPng());
            image.Interpolate = false;

            // Only the right-hand column (green over white), drawn to fill the destination.
            g.DrawImage(image, new RRect(0, 0, 10, 10), new RRect(1, 0, 1, 2));

            Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(g, 5, 2));
            Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(g, 5, 8));
        }

        [Fact]
        public void TransparentPixels_LeaveTheBackdropShowing()
        {
            var g = NewGraphics(4, 4);
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 255)), 0, 0, 4, 4);
            var image = Load(RasterPngFixture.MakeSolidRgbaPngBytes(4, 4, 255, 0, 0, 0));

            g.DrawImage(image, new RRect(0, 0, 4, 4));

            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(g, 2, 2));
        }

        [Fact]
        public void OpaqueTruecolorPng_TakesThePassThroughPath_AndStillDecodes()
        {
            var g = NewGraphics(4, 4);

            g.DrawImage(Load(RasterPngFixture.MakeIndexedPngBytes(4, 4, (_, _) => (10, 200, 30))), new RRect(0, 0, 4, 4));

            Assert.Equal(new byte[] { 10, 200, 30, 255 }, Pixel(g, 2, 2));
        }

        [Fact]
        public void InterlacedPng_FallsBackToADecodedSource_AndDraws()
        {
            var g = NewGraphics(4, 4);

            g.DrawImage(Load(RasterPngFixture.MakeInterlacedPngBytes(4, 4, 50, 60, 70)), new RRect(0, 0, 4, 4));

            Assert.Equal(new byte[] { 50, 60, 70, 255 }, Pixel(g, 2, 2));
        }

        [Fact]
        public void ChromaKeyPng_HonoursItsTransparentColour()
        {
            var g = NewGraphics(8, 8);

            g.DrawImage(Load(RasterPngFixture.MakeTrnsPngBytesWithTransparentRegion(8, 8, 200, 0, 0, (0, 0, 0))), new RRect(0, 0, 8, 8));

            var pixels = Enumerable.Range(0, 8).SelectMany(y => Enumerable.Range(0, 8).Select(x => Pixel(g, x, y)[3])).ToList();
            Assert.Contains((byte)0, pixels);
            Assert.Contains((byte)255, pixels);
        }

        [Fact]
        public void GifImages_Decode_WithAndWithoutTransparency()
        {
            var opaque = NewGraphics(6, 6);
            opaque.DrawImage(Load(RasterGifFixture.MakeSmallPaletteGifBytes(6, 6)), new RRect(0, 0, 6, 6));
            Assert.Equal(255, Pixel(opaque, 3, 3)[3]);

            var transparent = NewGraphics(6, 6);
            transparent.DrawImage(Load(RasterGifFixture.MakeTransparentGifBytes(6, 6)), new RRect(0, 0, 6, 6));
            var alphas = Enumerable.Range(0, 6).SelectMany(y => Enumerable.Range(0, 6).Select(x => Pixel(transparent, x, y)[3])).ToList();
            Assert.Contains((byte)0, alphas);

            var interlaced = NewGraphics(6, 6);
            interlaced.DrawImage(Load(RasterGifFixture.MakeInterlacedGifBytes(6, 6)), new RRect(0, 0, 6, 6));
            Assert.Equal(255, Pixel(interlaced, 3, 3)[3]);
        }

        [Fact]
        public void BmpAndJpeg_TheGenericDecodedSources_Draw()
        {
            using var rgb = Image.Create(4, 4, PixelFormat.Rgba32);
            var span = rgb.GetPixelSpan();
            for (var i = 0; i < span.Length; i += 4)
            {
                span[i] = 20;
                span[i + 1] = 120;
                span[i + 2] = 220;
                span[i + 3] = 255;
            }

            using var bmp = new MemoryStream();
            rgb.Save(bmp, "bmp", new PeachImage.Formats.Bmp.BmpEncoderOptions());
            var g = NewGraphics(4, 4);
            g.DrawImage(Load(bmp.ToArray()), new RRect(0, 0, 4, 4));
            Assert.Equal(new byte[] { 20, 120, 220, 255 }, Pixel(g, 2, 2));

            using var jpeg = new MemoryStream();
            rgb.Save(jpeg, "jpeg", new PeachImage.Formats.Jpeg.JpegEncoderOptions { Quality = 95 });
            var h = NewGraphics(4, 4);
            h.DrawImage(Load(jpeg.ToArray()), new RRect(0, 0, 4, 4));
            var p = Pixel(h, 2, 2);
            Assert.InRange(p[0], 12, 30);
            Assert.InRange(p[2], 210, 230);
        }

        [Fact]
        public void CmykImage_IsDrawnThroughANaiveRgbConversion()
        {
            var g = NewGraphics(4, 4);

            g.DrawImage(Load(CmykJpegFixture.NoIccBytes), new RRect(0, 0, 4, 4));

            // Opaque: the PDF keeps the CMYK, the bitmap gets a colour-unmanaged RGB rendition of it.
            Assert.Equal(255, Pixel(g, 2, 2)[3]);
        }

        [Fact]
        public void CmykRasterImage_IsDrawnThroughTheNaiveConversion()
        {
            var g = NewGraphics(4, 4);

            // C=0, M=255, Y=255, K=0: full magenta and yellow, the naive conversion of which is red.
            g.DrawImage(Load(CmykTiffFixture.Build(2, 2, 0, 255, 255, 0)), new RRect(0, 0, 4, 4));

            var p = Pixel(g, 2, 2);
            Assert.True(p[0] > 240 && p[1] < 15 && p[2] < 15, $"{p[0]},{p[1]},{p[2]}");
            Assert.Equal(255, p[3]);

            // K=255 alone is black whatever the other inks are.
            var black = NewGraphics(4, 4);
            black.DrawImage(Load(CmykTiffFixture.Build(2, 2, 0, 0, 0, 255)), new RRect(0, 0, 4, 4));
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(black, 2, 2));
        }

        [Fact]
        public void StrongMinification_AveragesInsteadOfAliasing()
        {
            var g = NewGraphics(8, 8);
            var checker = Load(RasterPngFixture.MakeRgbaPngBytes(64, 64, (x, y) => (x + y) % 2 == 0 ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)255)));

            g.DrawImage(checker, new RRect(0, 0, 8, 8));

            var p = Pixel(g, 4, 4);
            Assert.InRange(p[0], 100, 156);
        }

        [Fact]
        public void DecodedBitmaps_AreCachedPerImage()
        {
            var image = Load(QuadrantPng());

            Assert.Same(ImageBitmaps.Get(image.Image), ImageBitmaps.Get(image.Image));
        }

        [Fact]
        public void DecodeFailureOrUnsupportedImage_IsNotDrawn()
        {
            var g = NewGraphics(4, 4);
            var (tileGraphics, tile) = g.CreateTile(4, 4)!.Value;
            tileGraphics.Dispose();

            g.DrawImage(tile, new RRect(0, 0, 0, 0));
            g.DrawImage(tile, new RRect(0, 0, 4, 4), new RRect(0, 0, 0, 0));

            Assert.Equal(0, Pixel(g, 2, 2)[3]);
        }

        [Fact]
        public void PremultiplyThenUnpremultiply_RoundTripsWithinRounding()
        {
            var straight = new byte[] { 200, 100, 50, 128, 10, 20, 30, 255, 99, 99, 99, 0 };
            var work = (byte[])straight.Clone();

            Bitmap.Premultiply(work);
            var back = new byte[work.Length];
            Bitmap.Unpremultiply(work, back);

            Assert.InRange(back[0], 198, 202);
            Assert.InRange(back[1], 98, 102);
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, back[4..8]);
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, back[8..12]);
        }

        [Fact]
        public void RasterImage_ReportsItsLayoutSizeNotItsPixelCount()
        {
            var g = NewGraphics(20, 20);

            var (_, image) = g.CreateTile(7, 5)!.Value;

            Assert.Equal(7, image.Width);
            Assert.Equal(5, image.Height);
            Assert.True(image.Interpolate);
            image.Interpolate = false;
            Assert.False(image.Interpolate);
            image.Dispose();
        }
    }
}
