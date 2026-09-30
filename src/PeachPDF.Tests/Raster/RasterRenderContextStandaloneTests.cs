using PeachDrawing.Core;
using PeachImage.Formats.Png;
using PeachDrawing;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// Proves <see cref="RasterRenderContext"/>/<see cref="RasterCanvas"/> work as a fully standalone
    /// drawing surface - constructed, drawn on, and saved with zero <c>HtmlContainerInt</c>, CSS, layout, or
    /// PDF document involved anywhere in the call chain, and with no PdfSharpCore type anywhere either (font
    /// creation goes through <c>TypefaceFont</c>, image decoding through <c>DecodedImage</c>). This is the
    /// concrete evidence for the plan's "Land the standalone construction/export API" step: a future
    /// <c>PeachDrawing</c> package's own standalone consumer does exactly this shape of thing.
    /// </summary>
    public class RasterRenderContextStandaloneTests
    {
        [Fact]
        public void CreateCanvas_DrawAndReadBack_NeedsNoHtmlCssOrPdf()
        {
            var ctx = new RasterRenderContext();
            using var canvas = ctx.CreateCanvas(20, 10);

            var red = PaintColor.FromArgb(255, 255, 0, 0);
            canvas.DrawRectangle(canvas.GetSolidBrush(red), 0, 0, 20, 10);

            var buffer = canvas.ToPixelBuffer();
            Assert.Equal(20, buffer.Width);
            Assert.Equal(10, buffer.Height);

            var pixel = buffer.PremultipliedRgba.Span.Slice((5 * 20 + 5) * 4, 4);
            Assert.Equal(255, pixel[0]);
            Assert.Equal(0, pixel[1]);
            Assert.Equal(0, pixel[2]);
            Assert.Equal(255, pixel[3]);
        }

        [Fact]
        public async Task CreateCanvas_DrawsTextThroughAStandaloneRegisteredFont()
        {
            var ctx = new RasterRenderContext();
            using var stream = File.OpenRead(BundledFonts.Ttf);
            await ctx.AddFont(stream, "RasterStandaloneSans");

            using var canvas = ctx.CreateCanvas(120, 40);
            var font = ctx.GetFont("RasterStandaloneSans", 24, PaintFontStyle.Regular)!;
            var black = PaintColor.FromArgb(255, 0, 0, 0);

            canvas.DrawString("Hi", font, black, new PaintPoint(5, 5), new Size(60, 30));

            // At least one pixel actually got painted - the font resolved and glyphs rasterized, not a no-op.
            var pixels = canvas.ToPixelBuffer().PremultipliedRgba.ToArray();
            var anyInk = false;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 0)
                {
                    anyInk = true;
                    break;
                }
            }

            Assert.True(anyInk);
        }

        [Fact]
        public void Save_EncodesAValidPng()
        {
            var ctx = new RasterRenderContext();
            using var canvas = ctx.CreateCanvas(8, 8);
            canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 0, 128, 255)), 0, 0, 8, 8);

            using var stream = new MemoryStream();
            canvas.Save(stream, "png", new PngEncoderOptions());

            var bytes = stream.ToArray();
            Assert.True(bytes.Length > 8);
            // PNG signature: 0x89 'P' 'N' 'G' \r \n \x1A \n
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], bytes[..4]);
        }

        [Fact]
        public async Task SaveAsync_EncodesAValidPng()
        {
            var ctx = new RasterRenderContext();
            using var canvas = ctx.CreateCanvas(8, 8);
            canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 0, 128, 255)), 0, 0, 8, 8);

            using var stream = new MemoryStream();
            await canvas.SaveAsync(stream, "png", new PngEncoderOptions(), TestContext.Current.CancellationToken);

            var bytes = stream.ToArray();
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], bytes[..4]);
        }

        [Fact]
        public void ImageFromStream_DecodesThroughDecodedImageWithNoXImage()
        {
            var ctx = new RasterRenderContext();
            var png = RasterPngFixture.MakeRgbaPngBytes(2, 2, (x, y) => (x, y) switch
            {
                (0, 0) => ((byte)255, (byte)0, (byte)0, (byte)255),
                (1, 0) => ((byte)0, (byte)255, (byte)0, (byte)255),
                (0, 1) => ((byte)0, (byte)0, (byte)255, (byte)255),
                _ => ((byte)255, (byte)255, (byte)255, (byte)255),
            });

            using var image = ctx.ImageFromStream(new MemoryStream(png));

            Assert.Equal(2, image.Width);
            Assert.Equal(2, image.Height);

            var pixels = image.GetPixels();
            Assert.NotNull(pixels);
            Assert.Equal(2, pixels!.Value.Width);
            Assert.Equal(2, pixels.Value.Height);

            using var canvas = ctx.CreateCanvas(4, 4);
            canvas.DrawImage(image, new Rect(0, 0, 4, 4));
            var buffer = canvas.ToPixelBuffer().PremultipliedRgba.ToArray();

            // Top-left quadrant of the 4x4 upscale should be the source's red pixel.
            Assert.Equal(255, buffer[0]);
            Assert.Equal(0, buffer[1]);
            Assert.Equal(0, buffer[2]);
        }

        [Fact]
        public void ImageFromStream_ThrowsOnUnrecognisedBytes()
        {
            var ctx = new RasterRenderContext();

            Assert.Throws<InvalidDataException>(() => ctx.ImageFromStream(new MemoryStream([1, 2, 3, 4])));
        }
    }
}
