using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// Text drawn through <see cref="RasterGraphics"/>. Uses a bundled font on a private adapter so nothing depends on
    /// what the machine has installed, and a font with a single Regular face so bold and italic are the synthesized
    /// forms - the ones the PDF renderer draws with a stroke and a shear.
    /// </summary>
    public class RasterTextTests
    {
        private const string Family = "RasterTextTestSans";

        private sealed record Fixture(PdfSharpAdapter Adapter, RasterGraphics Graphics, Func<RFontStyle, RFont> Font);

        private static async Task<Fixture> NewFixture(int width = 300, int height = 80)
        {
            var adapter = new PdfSharpAdapter();
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, Family);

            var surface = new RasterSurface(width, height, 0, 0, 1, 1);
            var graphics = new RasterGraphics(adapter, surface, 1);
            return new Fixture(adapter, graphics, style => adapter.GetFont(Family, 24, style)!);
        }

        private static RColor Black => RColor.FromArgb(255, 0, 0, 0);

        private static (int MinX, int MinY, int MaxX, int MaxY, int Ink) InkBounds(RasterGraphics g)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, ink = 0;
            for (var y = 0; y < g.Surface.Height; y++)
            {
                var row = g.Surface.Row(y);
                for (var x = 0; x < g.Surface.Width; x++)
                {
                    if (row[x * 4 + 3] < 128)
                        continue;

                    ink++;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            return (minX, minY, maxX, maxY, ink);
        }

        private static void Draw(Fixture f, string text, RFont font, double x = 10, double y = 10, double letterSpacing = 0, RColor? color = null)
        {
            var size = f.Graphics.MeasureString(text, font);
            f.Graphics.DrawString(text, font, color ?? Black, new RPoint(x, y), size, letterSpacing);
        }

        [Fact]
        public async Task DrawString_PutsInkInsideTheMeasuredBoxOfTheRun()
        {
            var f = await NewFixture();
            var font = f.Font(RFontStyle.Regular);
            var width = f.Graphics.MeasureString("Hello", font).Width;

            Draw(f, "Hello", font);

            var (minX, minY, maxX, maxY, ink) = InkBounds(f.Graphics);
            Assert.True(ink > 100);
            Assert.InRange(minX, 9, 14);
            Assert.InRange(maxX, 10 + width - 6, 10 + width + 2);
            Assert.InRange(minY, 10, 10 + font.Height);
            Assert.True(maxY <= 10 + font.Height + 1);
        }

        [Fact]
        public async Task BaselineIsWhereThePdfRendererPutsIt()
        {
            var f = await NewFixture();
            var font = f.Font(RFontStyle.Regular);
            var real = ((FontAdapter)font).Font;

            Draw(f, "HHH", font);

            // A flat-bottomed capital sits on the baseline: y + cell ascent (in points), not the rounded RFont.Ascent.
            var baseline = 10 + real.GetHeight() * real.CellAscent / real.CellSpace;
            var (_, _, _, maxY, _) = InkBounds(f.Graphics);
            Assert.InRange(maxY + 1, baseline - 1, baseline + 1);
        }

        [Fact]
        public async Task TextColour_IsTheColourOfTheInk()
        {
            var f = await NewFixture();

            Draw(f, "MMM", f.Font(RFontStyle.Regular), color: RColor.FromArgb(255, 200, 0, 0));

            var inked = Enumerable.Range(0, 300).SelectMany(x => Enumerable.Range(0, 80).Select(y => (x, y)))
                .Where(p => f.Graphics.Surface.Row(p.y)[p.x * 4 + 3] == 255).ToList();
            Assert.NotEmpty(inked);
            var (px, py) = inked[inked.Count / 2];
            Assert.Equal(200, f.Graphics.Surface.Row(py)[px * 4]);
            Assert.Equal(0, f.Graphics.Surface.Row(py)[px * 4 + 1]);
        }

        [Fact]
        public async Task SynthesizedBold_AddsInkAroundEachGlyph()
        {
            var regular = await NewFixture();
            var bold = await NewFixture();

            Draw(regular, "Bold", regular.Font(RFontStyle.Regular));
            Draw(bold, "Bold", bold.Font(RFontStyle.Bold));

            Assert.True(InkBounds(bold.Graphics).Ink > InkBounds(regular.Graphics).Ink * 1.05);
        }

        [Fact]
        public async Task SynthesizedItalic_ShearsTheTopOfTheGlyphsRight()
        {
            var upright = await NewFixture();
            var italic = await NewFixture();

            Draw(upright, "IIII", upright.Font(RFontStyle.Regular));
            Draw(italic, "IIII", italic.Font(RFontStyle.Italic));

            double TopCentre(RasterGraphics g)
            {
                var (_, minY, _, maxY, _) = InkBounds(g);
                var y = minY + 2;
                var xs = Enumerable.Range(0, g.Surface.Width).Where(x => g.Surface.Row(y)[x * 4 + 3] > 128).ToList();
                return xs.Average();
            }

            double BottomCentre(RasterGraphics g)
            {
                var (_, _, _, maxY, _) = InkBounds(g);
                var y = maxY - 2;
                return Enumerable.Range(0, g.Surface.Width).Where(x => g.Surface.Row(y)[x * 4 + 3] > 128).Average();
            }

            var uprightLean = TopCentre(upright.Graphics) - BottomCentre(upright.Graphics);
            var italicLean = TopCentre(italic.Graphics) - BottomCentre(italic.Graphics);
            Assert.True(italicLean > uprightLean + 3, $"upright {uprightLean:F1}, italic {italicLean:F1}");
        }

        [Fact]
        public async Task LetterSpacing_WidensTheRun()
        {
            var tight = await NewFixture();
            var loose = await NewFixture();

            Draw(tight, "spacing", tight.Font(RFontStyle.Regular));
            Draw(loose, "spacing", loose.Font(RFontStyle.Regular), letterSpacing: 5);

            Assert.True(InkBounds(loose.Graphics).MaxX >= InkBounds(tight.Graphics).MaxX + 25);
        }

        [Fact]
        public async Task EmptyString_DrawsNothing()
        {
            var f = await NewFixture();

            f.Graphics.DrawString("", f.Font(RFontStyle.Regular), Black, new RPoint(10, 10), new RSize(0, 0));

            Assert.Equal(0, InkBounds(f.Graphics).Ink);
        }

        [Fact]
        public async Task DrawGlyphs_PlacesEachGlyphAtItsOwnBaselineOrigin()
        {
            var f = await NewFixture();
            var font = f.Font(RFontStyle.Regular);
            var index = font.GetGlyphIndex(new Rune('H'));
            Assert.True(index > 0);

            f.Graphics.DrawGlyphs([new GlyphPlacement(index, 20, 40), new GlyphPlacement(index, 120, 60)], font, Black);

            var (minX, minY, maxX, maxY, ink) = InkBounds(f.Graphics);
            Assert.True(ink > 100);
            Assert.InRange(minX, 18, 24);
            Assert.InRange(maxX, 130, 145);
            // Baselines at y = 40 and y = 60: the glyphs sit on them.
            Assert.InRange(maxY, 58, 61);
            Assert.True(minY < 40);
        }

        [Fact]
        public async Task GetTextOutline_ReturnsAFillableVectorOfTheRun_OrNullWhenThereIsNoInk()
        {
            var f = await NewFixture();
            var font = f.Font(RFontStyle.Regular);

            var outline = f.Graphics.GetTextOutline("Vector", font, new RPoint(10, 40));
            Assert.NotNull(outline);

            f.Graphics.DrawPath(f.Graphics.GetSolidBrush(Black), outline);
            Assert.True(InkBounds(f.Graphics).Ink > 100);

            Assert.Null(f.Graphics.GetTextOutline("   ", font, new RPoint(10, 40)));
        }

        [Fact]
        public async Task Measurement_AgreesWithTheLayoutEngine()
        {
            var f = await NewFixture();
            var font = f.Font(RFontStyle.Regular);
            var pdf = new PdfSharpCore.Drawing.XGraphicsMeasureProbe(font).Width("measure me");

            Assert.Equal(pdf, f.Graphics.MeasureString("measure me", font).Width, 6);
            Assert.Equal("measure me".Length, f.Graphics.CountShapedGlyphs("measure me", font));
            Assert.Throws<NotSupportedException>(() => f.Graphics.MeasureString("x", font, 10, out _, out _));
        }
    }
}

namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>Test-only: measures with the same routine <c>GraphicsAdapter</c> uses (an <c>XGraphics</c> measure context).</summary>
    internal sealed class XGraphicsMeasureProbe(PeachPDF.Html.Adapters.RFont font)
    {
        public double Width(string text)
        {
            var real = ((PeachPDF.Adapters.FontAdapter)font).Font;
            var size = FontHelper.MeasureString(text, real, XStringFormats.Default, PeachDrawing.Text.Shaping.ShapeSettings.Default);
            return size.Width;
        }
    }
}
