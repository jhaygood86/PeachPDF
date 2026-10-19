using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Tests
{
    /// <summary>
    /// The default <see cref="Canvas.DrawStringInRect"/> on the raster canvas. Each test locates the painted ink and would fail if
    /// the line ignored its alignment and always landed at the rect's top-left corner.
    /// </summary>
    public class DrawStringInRectTests
    {
        private const double FontSize = 24;
        private static readonly PaintColor Black = PaintColor.FromArgb(255, 0, 0, 0);
        private static readonly Rect Box = new(20, 20, 200, 100);

        private static async Task<(int MinX, int MaxX, int MinY, int MaxY)> InkBoundsAsync(BoxAlignment horizontal, BoxAlignment vertical, string text = "HH")
        {
            var context = new RasterRenderContext();
            var name = "InRect-" + Guid.NewGuid().ToString("N");
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
            {
                await context.AddFont(stream, name);
            }

            var font = context.GetFont(name, FontSize, PaintFontStyle.Regular)!;
            using var canvas = context.CreateCanvas(240, 140);
            canvas.DrawStringInRect(text, font, Black, Box, horizontal, vertical);

            return Scan(canvas);
        }

        private static (int MinX, int MaxX, int MinY, int MaxY) Scan(RasterCanvas canvas)
        {
            var buffer = canvas.ToPixelBuffer();
            var span = buffer.PremultipliedRgba.Span;
            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            for (var y = 0; y < buffer.Height; y++)
            {
                for (var x = 0; x < buffer.Width; x++)
                {
                    if (span[(y * buffer.Width + x) * 4 + 3] > 128)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            return (minX, maxX, minY, maxY);
        }

        [Fact]
        public async Task StartStart_PutsTheInkAtTheTopLeftOfTheRect()
        {
            var ink = await InkBoundsAsync(BoxAlignment.Start, BoxAlignment.Start);

            Assert.InRange(ink.MinX, 20, 30);
            Assert.InRange(ink.MinY, 20, 34);
            Assert.True(ink.MaxX < 100);
        }

        [Fact]
        public async Task EndEnd_PutsTheInkAtTheBottomRightOfTheRect()
        {
            var ink = await InkBoundsAsync(BoxAlignment.End, BoxAlignment.End);

            Assert.InRange(ink.MaxX, 210, 220);
            Assert.InRange(ink.MaxY, 100, 120);
        }

        [Fact]
        public async Task CenterCenter_PutsTheInkAroundTheMiddleOfTheRect()
        {
            var ink = await InkBoundsAsync(BoxAlignment.Center, BoxAlignment.Center);

            Assert.InRange((ink.MinX + ink.MaxX) / 2.0, 115, 125);
            Assert.InRange((ink.MinY + ink.MaxY) / 2.0, 62, 78);
        }

        [Fact]
        public async Task AnEmptyString_PaintsNothing()
        {
            var ink = await InkBoundsAsync(BoxAlignment.Center, BoxAlignment.Center, text: "");

            Assert.Equal(-1, ink.MaxX);
        }
    }
}
