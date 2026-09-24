using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;
using PeachPDF.Raster.Filters;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Raster
{
    public class ShadowRenderingTests
    {
        private static IReadOnlyList<TextShadowGrammar.ShadowLayer>? Parse(string value)
        {
            using var tokens = PeachPDF.Html.Core.Parse.CssValueParser.GetCssTokensPooled(value);
            List<Token> list = tokens;
            return TextShadowGrammar.TryParse(list);
        }

        // ---- text-shadow grammar ----

        [Fact]
        public void TextShadowGrammar_ParsesLayersWithOptionalBlurAndColour()
        {
            var layers = Parse("1px 2px 3px red, -4px 5px #00f");

            Assert.NotNull(layers);
            Assert.Equal(2, layers.Count);
            Assert.Equal(("1px", "2px", "3px", "red"), (layers[0].OffsetX, layers[0].OffsetY, layers[0].Blur, layers[0].Color));
            Assert.Equal("0", layers[1].Blur);
            Assert.Equal("#00f", layers[1].Color);
        }

        [Fact]
        public void TextShadowGrammar_AcceptsAColourFirst_AndNone()
        {
            Assert.Equal("red", Parse("red 1px 1px")![0].Color);
            Assert.Empty(Parse("none")!);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1px")]
        [InlineData("1px 2px 3px 4px")]
        [InlineData("1px 2px -3px")]
        [InlineData("inset 1px 2px")]
        [InlineData("1px 2px, ")]
        [InlineData("1px 2px notacolor")]
        public void TextShadowGrammar_RejectsInvalidValues(string value)
        {
            Assert.Null(Parse(value));
        }

        // ---- erase ----

        private static RasterGraphics NewGraphics(int w, int h)
        {
            var surface = new RasterSurface(w, h, 0, 0, 1, 1);
            return new RasterGraphics(new PdfSharpAdapter(), surface, 1);
        }

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        [Fact]
        public void Erase_RemovesInkInsideAShape_AndKeepsAnEdgePixelsRemainder()
        {
            var g = NewGraphics(10, 10);
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 0, 0, 10, 10);

            g.EraseRectangle(new RRect(2, 2, 4.5, 4));

            Assert.Equal(0, Pixel(g, 3, 3)[3]);
            Assert.InRange(Pixel(g, 6, 3)[3], 120, 135);
            Assert.Equal(255, Pixel(g, 8, 3)[3]);
            Assert.Equal(255, Pixel(g, 3, 8)[3]);
        }

        [Fact]
        public void Erase_WithAPath_FollowsItsShape_AndRespectsTheClip()
        {
            var g = NewGraphics(20, 20);
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 0, 0, 20, 20);
            var triangle = g.GetGraphicsPath();
            triangle.Start(0, 0);
            triangle.LineTo(20, 0);
            triangle.LineTo(0, 20);
            triangle.CloseFigure();

            g.PushClip(new RRect(0, 0, 10, 20));
            g.Erase(triangle);
            g.PopClip();

            Assert.Equal(0, Pixel(g, 2, 2)[3]);
            Assert.Equal(255, Pixel(g, 18, 2)[3]);
            Assert.Equal(255, Pixel(g, 8, 18)[3]);
        }

        [Fact]
        public void Erase_WithAnEmptyClip_DoesNothing()
        {
            var g = NewGraphics(10, 10);
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 0, 0, 10, 10);

            g.PushClip(new RRect(50, 50, 2, 2));
            g.EraseRectangle(new RRect(0, 0, 10, 10));
            g.PopClip();

            Assert.Equal(255, Pixel(g, 5, 5)[3]);
        }

        // ---- the drop-shadow kernel ----

        [Fact]
        public void DropShadow_PutsATintedOffsetCopyOfTheAlphaUnderTheContent()
        {
            using var surface = new RasterSurface(30, 30, 0, 0, 1, 1);
            for (var y = 5; y < 12; y++)
                for (var x = 5; x < 12; x++)
                {
                    var p = (y * 30 + x) * 4;
                    surface.Pixels[p] = 255;
                    surface.Pixels[p + 3] = 255;
                }

            DropShadow.Apply(surface, 6, 6, 0, 0, 0, 0, 200, 200);

            // Content unchanged where it is; the shadow appears where the content is not, offset by 6.
            Assert.Equal(255, surface.Pixels[(8 * 30 + 8) * 4]);
            var shadow = surface.Row(15).Slice(15 * 4, 4).ToArray();
            Assert.Equal(new byte[] { 0, 0, 200, 200 }, shadow);
            Assert.Equal(0, surface.Row(20).Slice(20 * 4 + 3, 1)[0]);
        }

        [Fact]
        public void DropShadow_BlurSoftensItsEdge_AndKeepsTheColour()
        {
            using var surface = new RasterSurface(40, 40, 0, 0, 1, 1);
            for (var y = 10; y < 20; y++)
                for (var x = 10; x < 20; x++)
                    surface.Pixels[(y * 40 + x) * 4 + 3] = 255;

            DropShadow.Apply(surface, 8, 8, 3, 3, 0, 0, 0, 255);

            var edge = surface.Row(18).Slice(28 * 4, 4).ToArray();
            Assert.InRange(edge[3], 40, 215);
        }

        [Fact]
        public void DropShadow_ShiftedFullyOutside_LeavesTheContentAlone()
        {
            using var surface = new RasterSurface(10, 10, 0, 0, 1, 1);
            surface.Pixels[(5 * 10 + 5) * 4 + 3] = 255;

            DropShadow.Apply(surface, 100, 100, 0, 0, 0, 0, 0, 255);

            Assert.Equal(1, Enumerable.Range(0, 100).Count(i => surface.Pixels[i * 4 + 3] != 0));
        }

        // ---- painted through a real page layout onto a raster page ----

        private static async Task<RasterGraphics> PaintAsync(string html, int width = 160, int height = 120)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), margin: 0);
            var page = NewGraphics(width, height);
            FragmentPaintHarness.PaintPage(container, page);
            return page;
        }

        [Fact]
        public async Task HardTextShadow_IsTheTextDrawnAgainAtTheOffset()
        {
            var plain = await PaintAsync("<div style=\"font:30pt Arial;color:#000\">HI</div>");
            var shadowed = await PaintAsync("<div style=\"font:30pt Arial;color:#000;text-shadow:6pt 6pt 0 #f00\">HI</div>");

            int Ink(RasterGraphics g) => Enumerable.Range(0, g.Surface.Width * g.Surface.Height).Count(i => g.Surface.Pixels[i * 4 + 3] > 128);
            int Red(RasterGraphics g) => Enumerable.Range(0, g.Surface.Width * g.Surface.Height)
                .Count(i => g.Surface.Pixels[i * 4] > 200 && g.Surface.Pixels[i * 4 + 1] < 60 && g.Surface.Pixels[i * 4 + 3] > 200);

            Assert.Equal(0, Red(plain));
            Assert.True(Red(shadowed) > 100);
            Assert.True(Ink(shadowed) > Ink(plain) * 1.3);
        }

        [Fact]
        public async Task BlurredTextShadow_SpreadsBeyondTheGlyphsWithSoftEdges()
        {
            var page = await PaintAsync("<div style=\"font:30pt Arial;color:#fff;text-shadow:0 0 8pt #0000ff\">HI</div>");

            // Blue haze with partial alpha around the (white) text, and nothing far away.
            var partial = Enumerable.Range(0, page.Surface.Width * page.Surface.Height)
                .Count(i => page.Surface.Pixels[i * 4 + 2] > 20 && page.Surface.Pixels[i * 4 + 3] is > 20 and < 200);
            Assert.True(partial > 200);
            Assert.Equal(0, Pixel(page, 150, 110)[3]);
        }

        [Fact]
        public async Task MultipleTextShadows_PaintFirstListedOnTop()
        {
            var page = await PaintAsync("<div style=\"font:40pt Arial;color:transparent;text-shadow:0 0 0 #ff0000, 0 0 0 #0000ff\">M</div>", 120, 90);

            // Both are the same shape at the same place: the first-listed (red) must be the one on top.
            var reds = 0;
            var blues = 0;
            for (var i = 0; i < page.Surface.Width * page.Surface.Height; i++)
            {
                if (page.Surface.Pixels[i * 4 + 3] < 200) continue;
                if (page.Surface.Pixels[i * 4] > 200) reds++;
                if (page.Surface.Pixels[i * 4 + 2] > 200) blues++;
            }

            Assert.True(reds > 100);
            Assert.Equal(0, blues);
        }

        [Fact]
        public async Task TextShadow_IsInherited_ByChildren()
        {
            var page = await PaintAsync("<div style=\"text-shadow:5pt 5pt 0 #f00\"><span style=\"font:30pt Arial;color:#000\">A</span></div>");

            Assert.Contains(Enumerable.Range(0, page.Surface.Width * page.Surface.Height), i =>
                page.Surface.Pixels[i * 4] > 200 && page.Surface.Pixels[i * 4 + 1] < 60 && page.Surface.Pixels[i * 4 + 3] > 200);
        }

        [Fact]
        public async Task BlurredOuterBoxShadow_FadesOutwardAndIsKnockedOutUnderTheBox()
        {
            var page = await PaintAsync(
                "<div style=\"margin:30pt;width:60pt;height:40pt;background:transparent;box-shadow:0 0 12pt #000000\"></div>");

            // The box is (30,30)-(90,70). Under it: nothing (knocked out, even though the box is transparent).
            Assert.Equal(0, Pixel(page, 60, 50)[3]);
            // Just outside its edge: a haze that fades with distance.
            var near = Pixel(page, 25, 50)[3];
            var far = Pixel(page, 12, 50)[3];
            Assert.True(near > far);
            Assert.InRange(near, 30, 200);
            Assert.Equal(0, Pixel(page, 1, 1)[3]);
        }

        [Fact]
        public async Task RoundedBox_KnocksOutItsRoundedShape_NotItsBoundingRectangle()
        {
            var page = await PaintAsync(
                "<div style=\"margin:30pt;width:60pt;height:40pt;border-radius:20pt;box-shadow:0 0 24pt #000\"></div>");

            // The bounding box's corner pixel lies outside the rounded shape, so the wide haze shows there - a knock-out of
            // the bounding rectangle would have cleared it; the centre, inside the shape, stays clear.
            Assert.True(Pixel(page, 32, 32)[3] > 20);
            Assert.Equal(0, Pixel(page, 60, 50)[3]);
        }

        [Fact]
        public async Task BlurredInsetShadow_HugsTheInsideEdges_AndLeavesTheCentreClear()
        {
            var page = await PaintAsync(
                "<div style=\"margin:20pt;width:100pt;height:60pt;background:#ffffff;box-shadow:inset 0 0 12pt #000000\"></div>");

            var edge = Pixel(page, 22, 50)[3];
            var centre = Pixel(page, 70, 50)[3];
            Assert.True(edge > 0);
            Assert.True(page.Surface.Row(50)[70 * 4] > 240); // the white background still shows in the middle
            Assert.Equal(255, centre); // opaque white background, no shadow tint in the middle
            Assert.True(page.Surface.Row(50)[22 * 4] < 200); // darkened toward the edge
        }

        [Fact]
        public async Task BlurredInsetShadow_FollowsThePaddingEdgeCorner_NotTheBorderEdgeCorner()
        {
            // 30pt border-radius over a 10pt border leaves a 20pt padding-edge radius, whose corner arc passes
            // ~5.9pt in along the diagonal from the padding box's corner; the 30pt border-edge radius would only
            // reach ~8.8pt in. A full-cover shadow makes that difference the whole picture.
            var page = await PaintAsync(
                "<div style=\"margin:20pt;width:100pt;height:60pt;border:10pt solid #ffffff;border-radius:30pt;" +
                "background:#ffffff;box-shadow:inset 0 0 1pt 30pt #000000\"></div>");

            Assert.True(page.Surface.Row(37)[37 * 4] < 60, "inside the padding-edge arc, so shadowed");
            Assert.True(page.Surface.Row(32)[32 * 4] > 200, "outside the padding-edge arc, so left alone");
        }

        [Fact]
        public async Task BlurredInsetShadow_LitHoleFollowsThePaddingEdgeRadius()
        {
            // Same box, but a spread-0 shadow so the lit hole (the padding edge) exists. The hole's own corner is the
            // 20pt padding-edge arc, which reaches the diagonal at ~5.9pt; a hole built from the 30pt border radius
            // would only reach it at ~8.8pt and so leave (37,37), 7pt in, shadowed instead of lit.
            var page = await PaintAsync(
                "<div style=\"margin:20pt;width:100pt;height:60pt;border:10pt solid #ffffff;border-radius:30pt;" +
                "background:#ffffff;box-shadow:inset 0 0 1pt #000000\"></div>");

            Assert.True(page.Surface.Row(37)[37 * 4] > 200, "inside the lit hole, so not shadowed");
        }

        [Fact]
        public async Task DropShadow_FollowsTheGlyphShape_NotTheBoundingBox()
        {
            var page = await PaintAsync(
                "<div style=\"margin:20pt;font:60pt Arial;color:#000;width:40pt;filter:drop-shadow(8pt 8pt 0 #ff0000)\">I</div>", 140, 140);

            // "I" is a narrow stem: to the right of the stem, inside the (padded) box, the shadow's offset copy shows as red
            // only where the stem was moved to - a bounding-box shadow would fill the whole box area red.
            int Red(int x, int y) => page.Surface.Row(y)[x * 4] > 200 && page.Surface.Row(y)[x * 4 + 3] > 200 ? 1 : 0;
            var redInRow = Enumerable.Range(0, 140).Sum(x => Red(x, 70));
            var boxWidth = 40;
            Assert.True(redInRow > 0);
            Assert.True(redInRow < boxWidth / 2);
        }

        [Fact]
        public async Task WithoutRasterSupport_HardTextShadowStillPaints_AndBlurredOnesPaintNothing()
        {
            var (_, hardContainer) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div style=\"font:20pt Arial;text-shadow:3pt 3pt 0 #f00\">Hi</div>"), margin: 0);
            var hard = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(hardContainer, hard);

            var (_, blurContainer) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div style=\"font:20pt Arial;text-shadow:3pt 3pt 6pt #f00\">Hi</div>"), margin: 0);
            var blurred = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(blurContainer, blurred);

            Assert.Equal(2, hard.DrawnStrings.Count);
            Assert.Single(blurred.DrawnStrings);
        }
    }
}
