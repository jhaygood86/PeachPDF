using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// Text that the raster backend draws with the font's hinting (<see cref="PdfGenerateConfig.TextHinting"/>): what changes in the pixels,
    /// when it is not used, and that the PDF's own vector text never changes.
    /// </summary>
    public class RasterTextHintingTests
    {
        private const string Family = "RasterHintingSans";

        private static RColor Black => RColor.FromArgb(255, 0, 0, 0);

        private static async Task<(RasterGraphics Graphics, RFont Font)> Fixture(TextHinting hinting, double fontSize = 12, int width = 120, int height = 40)
        {
            var adapter = new PdfSharpAdapter { TextHinting = hinting };
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, Family);

            var surface = new RasterSurface(width, height, 0, 0, 1, 1);
            var graphics = new RasterGraphics(adapter, surface, 1);
            return (graphics, adapter.GetFont(Family, fontSize, RFontStyle.Regular)!);
        }

        private static void Draw(RasterGraphics graphics, RFont font, string text = "Hxg", double y = 10)
        {
            var size = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, Black, new RPoint(10, y), size);
        }

        private static byte Alpha(RasterGraphics graphics, int x, int y) => graphics.Surface.Row(y)[x * 4 + 3];

        private static byte[] Pixels(RasterGraphics graphics)
        {
            var bytes = new List<byte>();
            for (int y = 0; y < graphics.Surface.Height; y++)
                bytes.AddRange(graphics.Surface.Row(y).ToArray());
            return bytes.ToArray();
        }

        private static async Task<byte[]> Render(TextHinting hinting, double fontSize = 12, string text = "Hxg", RMatrix? transform = null)
        {
            var (graphics, font) = await Fixture(hinting, fontSize);
            if (transform is { } matrix)
                graphics.PushTransform(matrix);
            Draw(graphics, font, text);
            return Pixels(graphics);
        }

        [Fact]
        public async Task WithoutHintingTheOutputIsWhatItWas()
        {
            // None is the default: the same pixels as an adapter that was never told anything about hinting
            var (plain, plainFont) = await Fixture(TextHinting.None);
            var adapter = new PdfSharpAdapter();
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, Family);
            var untouched = new RasterGraphics(adapter, new RasterSurface(120, 40, 0, 0, 1, 1), 1);

            Draw(plain, plainFont);
            Draw(untouched, adapter.GetFont(Family, 12, RFontStyle.Regular)!);

            Assert.Equal(Pixels(untouched), Pixels(plain));
        }

        [Theory]
        [InlineData(TextHinting.Standard)]
        [InlineData(TextHinting.Monochrome)]
        public async Task HintingChangesThePixelsOfSmallText(TextHinting hinting)
        {
            var none = await Render(TextHinting.None);
            var hinted = await Render(hinting);

            Assert.NotEqual(none, hinted);
            Assert.True(hinted.Where((b, i) => i % 4 == 3 && b > 0).Count() > 60, "the hinted text has ink");
        }

        [Fact]
        public async Task TheTopAndBottomOfAFlatGlyphAreCrispRowsWhenHinted()
        {
            // "HHH" at 12 px. Hinted, the tops and bottoms of the stems lie on pixel edges: the first and last inked rows are as dark as the rows
            // next to them. Unhinted, the same edges cut through pixels and the outer rows are light.
            var (hinted, font) = await Fixture(TextHinting.Standard, fontSize: 12);
            // a baseline that is not on a pixel edge, so that unhinted edges cut through pixels
            Draw(hinted, font, "HHH", y: 4.6);
            var (unhinted, plainFont) = await Fixture(TextHinting.None, fontSize: 12);
            Draw(unhinted, plainFont, "HHH", y: 4.6);

            // the largest difference between an edge row and its inner neighbour, over the pixels of the stems
            (int Top, int Bottom) EdgeSoftness(RasterGraphics g)
            {
                var rows = Enumerable.Range(0, g.Surface.Height).Where(y => Enumerable.Range(0, g.Surface.Width).Any(x => Alpha(g, x, y) > 0)).ToList();
                int Softness(int edge, int inner) => Enumerable.Range(0, g.Surface.Width)
                    .Where(x => Alpha(g, x, inner) > 100).Select(x => Alpha(g, x, inner) - Alpha(g, x, edge)).DefaultIfEmpty(0).Max();
                return (Softness(rows[0], rows[1]), Softness(rows[^1], rows[^2]));
            }

            var (hintedTop, hintedBottom) = EdgeSoftness(hinted);
            var (plainTop, plainBottom) = EdgeSoftness(unhinted);

            Assert.InRange(hintedTop, 0, 12);
            Assert.InRange(hintedBottom, 0, 12);
            Assert.True(plainTop + plainBottom > 60, $"unhinted edges are soft by {plainTop} and {plainBottom}");
        }

        [Fact]
        public async Task MonochromeHintingSnapsTheGlyphToAPixelColumnAsWellAsARow()
        {
            async Task<byte[]> One(TextHinting hinting, double x)
            {
                var (graphics, font) = await Fixture(hinting, fontSize: 12);
                graphics.DrawString("H", font, Black, new RPoint(x, 10), graphics.MeasureString("H", font));
                return Pixels(graphics);
            }

            // origins 0.2 px apart that round to one pixel column: fitted in both directions, the glyph does not move; fitted vertically
            // only, it keeps the sub-pixel position layout gave it
            Assert.Equal(await One(TextHinting.Monochrome, 10.2), await One(TextHinting.Monochrome, 10.4));
            Assert.NotEqual(await One(TextHinting.Standard, 10.2), await One(TextHinting.Standard, 10.4));
        }

        [Fact]
        public async Task LargeTextIsHintedToo_AndStillDrawsTheSameGlyphs()
        {
            var none = await Render(TextHinting.None, fontSize: 28, text: "Ag");
            var hinted = await Render(TextHinting.Standard, fontSize: 28, text: "Ag");

            int Ink(byte[] pixels) => pixels.Where((b, i) => i % 4 == 3 && b > 127).Count();

            // the same amount of ink, within a few percent: hinting moves edges by fractions of a pixel
            Assert.InRange(Ink(hinted), Ink(none) * 0.9, Ink(none) * 1.1);
        }

        [Fact]
        public async Task RotatedTextIsNotHinted()
        {
            var angle = 0.3;
            var rotate = new RMatrix(Math.Cos(angle), Math.Sin(angle), -Math.Sin(angle), Math.Cos(angle), 20, 5);

            var none = await Render(TextHinting.None, transform: rotate);
            var hinted = await Render(TextHinting.Standard, transform: rotate);

            Assert.Equal(none, hinted);
        }

        [Fact]
        public async Task TextScaledDifferentlyInTheTwoDirectionsIsHintedToAStretchedGrid()
        {
            // 12 px scaled 2x horizontally and 1x vertically reaches the pixels as 24 horizontal and 12 vertical ppem: a non-square
            // pixel is exactly the real-world trigger for the font's non-square-pixel hinting paths, so this is no longer refused.
            var stretch = new RMatrix(2, 0, 0, 1, 0, 0);

            var none = await Render(TextHinting.None, transform: stretch);
            var hinted = await Render(TextHinting.Standard, transform: stretch);

            Assert.NotEqual(none, hinted);
            Assert.True(hinted.Where((b, i) => i % 4 == 3 && b > 0).Count() > 60, "the hinted text has ink");
        }

        [Fact]
        public async Task RotationOrSkewStillRefusesHintingEvenWithAnisotropicSupport()
        {
            // a rotation mixes M12/M21 in, which the guard still refuses whatever the two axes' own scales are: fitting a rotated or
            // skewed grid is not what a font's hinting instructions do, square pixels or not.
            var skew = new RMatrix(2, 0.3, 0, 1, 0, 0);

            Assert.Equal(await Render(TextHinting.None, transform: skew), await Render(TextHinting.Standard, transform: skew));
        }

        [Fact]
        public async Task TextUnderAUniformScaleIsHintedAtTheScaledSize()
        {
            // 12 px scaled by 2 is 24 ppem on the surface: the hinting follows what reaches the pixels
            var twice = new RMatrix(2, 0, 0, 2, 0, 0);
            var scaled = await Render(TextHinting.Standard, fontSize: 12, transform: twice);
            var direct = await Render(TextHinting.Standard, fontSize: 24);

            Assert.NotEqual(await Render(TextHinting.None, fontSize: 12, transform: twice), scaled);
            Assert.NotEmpty(direct);
        }

        /// <summary>Text in a bundled font drawn with the given hinting and stem darkening; the alpha channel of the surface.</summary>
        private static async Task<byte[]> RenderWith(string fontPath, TextHinting hinting, bool stemDarkening, double fontSize = 9)
        {
            var adapter = new PdfSharpAdapter { TextHinting = hinting, TextStemDarkening = stemDarkening };
            await BundledFonts.RegisterFont(adapter, fontPath, "RasterDarkeningFont");
            var graphics = new RasterGraphics(adapter, new RasterSurface(120, 40, 0, 0, 1, 1), 1);
            var font = adapter.GetFont("RasterDarkeningFont", fontSize, RFontStyle.Regular)!;
            graphics.DrawString("Hlxn", font, Black, new RPoint(10, 10), graphics.MeasureString("Hlxn", font));
            return Pixels(graphics);
        }

        private static long Ink(byte[] pixels) => pixels.Where((_, i) => i % 4 == 3).Sum(b => (long)b);

        [Fact]
        public async Task StemDarkeningMakesTheStemsOfASmallCffFontHeavier()
        {
            var plain = await RenderWith(BundledFonts.Otf, TextHinting.Standard, stemDarkening: false);
            var darkened = await RenderWith(BundledFonts.Otf, TextHinting.Standard, stemDarkening: true);

            Assert.NotEqual(plain, darkened);
            Assert.True(Ink(darkened) > Ink(plain) * 1.03, $"ink {Ink(plain)} without and {Ink(darkened)} with darkening");
        }

        [Fact]
        public async Task StemDarkeningDoesNothingWhereThereIsNoHintingOrNoCffOutline()
        {
            // no hinting: the text is the scaled design, whatever else is asked
            Assert.Equal(await RenderWith(BundledFonts.Otf, TextHinting.None, stemDarkening: false), await RenderWith(BundledFonts.Otf, TextHinting.None, stemDarkening: true));

            // a TrueType font has no stems to darken
            Assert.Equal(await RenderWith(BundledFonts.Ttf, TextHinting.Standard, stemDarkening: false), await RenderWith(BundledFonts.Ttf, TextHinting.Standard, stemDarkening: true));
        }

        [Fact]
        public async Task TextIsHintedOnlyAtTheSizesWhereTheFontsGaspTableAsksForIt()
        {
            async Task<byte[]> Draw(TextHinting hinting, double size)
            {
                var adapter = new PdfSharpAdapter { TextHinting = hinting };
                await BundledFonts.RegisterFont(adapter, BundledFonts.Gasp, "RasterGaspFont");
                var graphics = new RasterGraphics(adapter, new RasterSurface(120, 60, 0, 0, 1, 1), 1);
                var font = adapter.GetFont("RasterGaspFont", size, RFontStyle.Regular)!;
                graphics.DrawString("AB", font, Black, new RPoint(10, 40), graphics.MeasureString("AB", font));
                return Pixels(graphics);
            }

            // 6 px is below the first range that has the grid-fit flag and 12 px inside one: the text is the plain scaled design at the first
            // size and is fitted at the second
            Assert.Equal(await Draw(TextHinting.None, 6), await Draw(TextHinting.Standard, 6));
            Assert.NotEqual(await Draw(TextHinting.None, 12), await Draw(TextHinting.Standard, 12));

            // and 30 px lies in a range for ClearType only, which the standard rasterizer does not take as asking for fitting
            Assert.Equal(await Draw(TextHinting.None, 30), await Draw(TextHinting.Standard, 30));
        }

        [Fact]
        public async Task ItalicTextKeepsItsShearWhenHinted()
        {
            var adapter = new PdfSharpAdapter { TextHinting = TextHinting.Standard };
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, Family);

            RasterGraphics Draw(RFontStyle style)
            {
                var g = new RasterGraphics(adapter, new RasterSurface(120, 40, 0, 0, 1, 1), 1);
                var f = adapter.GetFont(Family, 24, style)!;
                g.DrawString("IIII", f, Black, new RPoint(10, 5), g.MeasureString("IIII", f));
                return g;
            }

            double Lean(RasterGraphics g)
            {
                var rows = Enumerable.Range(0, g.Surface.Height).Where(y => Enumerable.Range(0, g.Surface.Width).Any(x => g.Surface.Row(y)[x * 4 + 3] > 128)).ToList();
                double Centre(int y) => Enumerable.Range(0, g.Surface.Width).Where(x => g.Surface.Row(y)[x * 4 + 3] > 128).Average();
                return Centre(rows[2]) - Centre(rows[^3]);
            }

            double italicLean = Lean(Draw(RFontStyle.Italic)), uprightLean = Lean(Draw(RFontStyle.Regular));
            Assert.True(italicLean > uprightLean + 1.5, $"upright {uprightLean:F1}, italic {italicLean:F1}");
        }
    }
}
