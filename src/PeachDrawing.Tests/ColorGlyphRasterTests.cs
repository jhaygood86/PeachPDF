using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Tests
{
    /// <summary>
    /// COLR/CPAL color glyphs on the raster canvas. Each test would fail if color glyphs fell back to a plain outline in the text
    /// color (which is what the canvas did before it painted color fonts): the assertions look for the palette's own colors.
    /// </summary>
    public class ColorGlyphRasterTests
    {
        private const double FontSize = 60;
        private static readonly PaintColor Black = PaintColor.FromArgb(255, 0, 0, 0);
        private static readonly PaintColor Magenta = PaintColor.FromArgb(255, 255, 0, 255);

        private static async Task<(RasterRenderContext Context, Font Font)> LoadAsync(string path)
        {
            var context = new RasterRenderContext();
            var name = "ColorRaster-" + Guid.NewGuid().ToString("N");
            await using var stream = File.OpenRead(path);
            await context.AddFont(stream, name);
            return (context, context.GetFont(name, FontSize, PaintFontStyle.Regular)!);
        }

        private static async Task<RasterCanvas> DrawAsync(string path, string text, FontPalette? palette = null)
        {
            var (context, font) = await LoadAsync(path);
            var canvas = context.CreateCanvas(140, 100);
            canvas.DrawString(text, font, Black, new PaintPoint(10, 10), new Size(120, 80), fontPalette: palette);
            return canvas;
        }

        private static int Count(RasterCanvas canvas, Func<int, int, int, bool> test)
        {
            var buffer = canvas.ToPixelBuffer();
            var span = buffer.PremultipliedRgba.Span;
            var count = 0;
            for (var i = 0; i < span.Length; i += 4)
            {
                if (span[i + 3] == 255 && test(span[i], span[i + 1], span[i + 2]))
                    count++;
            }

            return count;
        }

        private static bool IsRed(int r, int g, int b) => r > 200 && g < 60 && b < 60;

        private static bool IsGreen(int r, int g, int b) => g > 100 && r < 60 && b < 60;

        private static bool IsBlue(int r, int g, int b) => b > 200 && r < 60 && g < 60;

        private static bool IsYellow(int r, int g, int b) => r > 200 && g > 200 && b < 60;

        private static bool IsBlack(int r, int g, int b) => r < 20 && g < 20 && b < 20;

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task LayeredColorGlyph_PaintsEachLayerInItsPaletteColor(bool colrV1)
        {
            using var canvas = await DrawAsync(colrV1 ? BundledFonts.ColorV1 : BundledFonts.ColorV0, "A");

            Assert.True(Count(canvas, IsRed) > 200, "the red box layer");
            Assert.True(Count(canvas, IsGreen) > 100, "the green triangle layer");
            Assert.Equal(0, Count(canvas, IsBlack));
        }

        [Fact]
        public async Task SingleLayerColorGlyph_IsBlue()
        {
            using var canvas = await DrawAsync(BundledFonts.ColorV0, "B");

            Assert.True(Count(canvas, IsBlue) > 200);
            Assert.Equal(0, Count(canvas, IsBlack));
        }

        [Fact]
        public async Task PlainGlyphInAColorFont_UsesTheTextColor()
        {
            using var canvas = await DrawAsync(BundledFonts.ColorV0, "X");

            Assert.True(Count(canvas, IsBlack) > 200);
            Assert.Equal(0, Count(canvas, IsRed));
        }

        [Fact]
        public async Task FontPaletteOverride_ReplacesAPaletteEntry()
        {
            var palette = new FontPalette(0, [new KeyValuePair<int, PaintColor>(0, Magenta)]);
            using var canvas = await DrawAsync(BundledFonts.ColorV0, "A", palette);

            Assert.Equal(0, Count(canvas, IsRed));
            Assert.True(Count(canvas, (r, g, b) => r > 200 && g < 60 && b > 200) > 200, "the box, in the override color");
            Assert.True(Count(canvas, IsGreen) > 100, "entries not overridden keep the palette's color");
        }

        [Fact]
        public async Task LinearGradientPaint_RunsFromTheFirstStopToTheLast()
        {
            using var canvas = await DrawAsync(BundledFonts.ColorV1, "G");
            var buffer = canvas.ToPixelBuffer();
            var ink = Enumerable.Range(0, buffer.Width * buffer.Height)
                .Where(i => buffer.PremultipliedRgba.Span[i * 4 + 3] == 255)
                .Select(i => (X: i % buffer.Width, R: (int)buffer.PremultipliedRgba.Span[i * 4], B: (int)buffer.PremultipliedRgba.Span[i * 4 + 2]))
                .ToList();

            var minX = ink.Min(p => p.X);
            var maxX = ink.Max(p => p.X);
            var left = ink.Where(p => p.X < minX + 8).ToList();
            var right = ink.Where(p => p.X > maxX - 8).ToList();
            Assert.True(left.Average(p => p.R) > right.Average(p => p.R) + 100, "red fades out to the right");
            Assert.True(right.Average(p => p.B) > left.Average(p => p.B) + 100, "blue fades in to the right");
        }

        [Fact]
        public async Task RadialGradientPaint_ChangesFromTheCenterToTheEdge()
        {
            using var canvas = await DrawAsync(BundledFonts.ColorV1, "R");

            Assert.True(Count(canvas, (r, g, b) => r > 200 && b < 80) > 20, "red near the center");
            Assert.True(Count(canvas, (r, g, b) => b > 150 && r < 100) > 20, "blue toward the edge");
        }

        [Fact]
        public async Task SweepGradientPaint_ShowsEveryStopColor()
        {
            using var canvas = await DrawAsync(BundledFonts.ColorV1, "S");

            Assert.True(Count(canvas, (r, g, b) => r > 180 && g < 90 && b < 90) > 20, "red");
            Assert.True(Count(canvas, (r, g, b) => g > 90 && r < 90 && b < 90) > 20, "green");
            Assert.True(Count(canvas, (r, g, b) => b > 180 && r < 90 && g < 90) > 20, "blue");
        }

        [Fact]
        public async Task CompositePaint_MultipliesTheSourceOverTheBackdrop()
        {
            using var canvas = await DrawAsync(BundledFonts.ColorV1, "M");

            Assert.True(Count(canvas, IsYellow) > 100, "the box, where the triangle is not");
            Assert.True(Count(canvas, IsBlack) > 100, "yellow multiplied by blue is black");
        }

        [Fact]
        public async Task TransformPaint_MovesTheGlyphsArtwork()
        {
            using var moved = await DrawAsync(BundledFonts.ColorV1, "T");
            using var plain = await DrawAsync(BundledFonts.ColorV0, "Y");

            Assert.True(Count(moved, IsYellow) > 100, "the translated yellow triangle");
            Assert.Equal(0, Count(plain, IsYellow));
        }

        [Fact]
        public async Task DrawParagraph_PaintsColorGlyphsToo()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.ColorV0, new AddOptions { FamilyName = "ColorParagraph-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            var layout = new ParagraphBuilder(new RunStyle(match.Typeface, FontSize)).AddText("AB").Build().Layout(300);
            using var canvas = new RasterRenderContext().CreateCanvas(160, 100);

            canvas.DrawParagraph(layout, new PaintPoint(5, 5), Black);

            Assert.True(Count(canvas, IsRed) > 200);
            Assert.True(Count(canvas, IsGreen) > 100);
            Assert.True(Count(canvas, IsBlue) > 200);
            Assert.Equal(0, Count(canvas, IsBlack));
        }

        [Fact]
        public async Task CffColorFont_DestOutLeavesAHoleWhereTheSourceIs_AndRadialConeIsDrawn()
        {
            // 'B': a yellow box with the blue triangle cut out of it (the complement clip of the shared walker's canvas target).
            var canvas = await DrawAsync(BundledFonts.ColorCff, "B");
            Assert.True(Count(canvas, IsYellow) > 100, "the box outside the triangle");
            Assert.Equal(0, Count(canvas, IsBlue));

            // 'E': a gradient between circles with different centers, so the two-circle brush runs.
            var cone = await DrawAsync(BundledFonts.ColorCff, "E");
            Assert.True(Count(cone, IsBlue) > 100);
        }

        private static bool IsWhite(int r, int g, int b) => r > 235 && g > 235 && b > 235;

        [Fact]
        public async Task CffColorFont_PlusAddsTheSourceToTheBackdrop()
        {
            // 'D': a blue triangle PLUS a yellow box. Source-over would leave the triangle blue; adding blue to yellow gives white.
            using var canvas = await DrawAsync(BundledFonts.ColorCff, "D");

            Assert.True(Count(canvas, IsWhite) > 300, "yellow + blue is white where the triangle overlaps the box");
            Assert.True(Count(canvas, IsYellow) > 100, "the box outside the triangle stays yellow");
            Assert.Equal(0, Count(canvas, IsBlue));
        }

        private static (int R, int G, int B) PixelAt(RasterCanvas canvas, int x, int y)
        {
            var buffer = canvas.ToPixelBuffer();
            var span = buffer.PremultipliedRgba.Span;
            int p = (y * buffer.Width + x) * 4;
            return (span[p], span[p + 1], span[p + 2]);
        }

        [Fact]
        public async Task CffColorFont_ConeRepeatAndReflectContinuePastTheOuterCircle_WherePadHoldsTheLastColor()
        {
            using var pad = await DrawAsync(BundledFonts.ColorCff, "E");
            using var repeat = await DrawAsync(BundledFonts.ColorCff, "N");
            using var reflect = await DrawAsync(BundledFonts.ColorCff, "O");

            // The box's top-right corner (design 900,800) lies outside the outer circle (center 600,400, radius 300).
            var (pr, _, pb) = PixelAt(pad, 62, 12);
            Assert.True(pb > 200 && pr < 60, "pad holds the last stop, blue");

            var (rr, _, rb) = PixelAt(repeat, 62, 12);
            Assert.False(rb > 200 && rr < 60, "repeat starts the ramp over, so the corner is no longer pure blue");

            var (fr, _, fb) = PixelAt(reflect, 62, 12);
            Assert.False(fb > 200 && fr < 60, "reflect runs the ramp back, so the corner is no longer pure blue");

            Assert.True(Count(repeat, IsRed) > Count(pad, IsRed), "the repeated ramp brings red back");
        }
    }
}
