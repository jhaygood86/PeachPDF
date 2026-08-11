using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>outline</c>/<c>outline-color</c>/<c>outline-style</c>/<c>outline-width</c>/
    /// <c>outline-offset</c> actually paint the right geometry/color/order (issue #697), not just that
    /// they parse or that painting doesn't throw - per this repo's painting-test convention (see
    /// <c>BorderStylePaintIntegrationTests</c>). Uses <see cref="TestRecordingGraphics"/> to assert the
    /// real draw-call sequence, plus one real-PDF content-stream test
    /// (<see cref="OutlineColorInvert_ProducesADifferenceBlendModeExtGStateInTheRealPdf"/>) proving the
    /// new <see cref="PeachPDF.Html.Adapters.RGraphics.PushBlendMode"/>/<see cref="PeachPDF.Html.Adapters.RGraphics.PopBlendMode"/>
    /// primitive actually reaches the PDF-writing layer, not just the test mock.
    /// </summary>
    public class OutlineStylePaintIntegrationTests
    {
        [Fact]
        public async Task Outline_PaintsRingOutsetByWidthAndOffset_OutsideTheBorderEdge()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:50pt; height:30pt; outline: 6pt solid rgb(10,20,30); outline-offset: 4pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);
            Assert.All(polys, p => Assert.Equal(RColor.FromArgb(10, 20, 30), p.Color));

            const double offset = 4;
            const double width = 6;
            const double reach = offset + width;

            var top = polys.OrderBy(p => p.Points.Average(pt => pt.Y)).First();
            var ys = top.Points.Select(pt => pt.Y).ToList();
            var xs = top.Points.Select(pt => pt.X).ToList();
            Assert.Equal(rect.Top - offset, ys.Max(), 1);
            Assert.Equal(rect.Top - reach, ys.Min(), 1);
            Assert.Equal(rect.Left - reach, xs.Min(), 1);
            Assert.Equal(rect.Right + reach, xs.Max(), 1);

            var right = polys.OrderByDescending(p => p.Points.Average(pt => pt.X)).First();
            var rxs = right.Points.Select(pt => pt.X).ToList();
            Assert.Equal(rect.Right + offset, rxs.Min(), 1);
            Assert.Equal(rect.Right + reach, rxs.Max(), 1);
        }

        [Fact]
        public async Task OutlineOffset_Negative_PullsTheRingInwardPastTheBorderEdge()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:50pt; height:30pt; outline: 6pt solid rgb(10,20,30); outline-offset: -3pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            var top = polys.OrderBy(p => p.Points.Average(pt => pt.Y)).First();
            var ys = top.Points.Select(pt => pt.Y).ToList();

            // offset = -3, width = 6: the ring's inner boundary sits 3pt *inside* the border edge and
            // its outer boundary sits 3pt outside it - straddling the box's own edge.
            Assert.Equal(rect.Top + 3, ys.Max(), 1);
            Assert.Equal(rect.Top - 3, ys.Min(), 1);
        }

        [Fact]
        public async Task OutlineStyleAuto_PaintsIdenticallyToSolid()
        {
            var (autoRoot, autoContainer) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:40pt; height:40pt; outline-style: auto; outline-width: 5pt; outline-color: rgb(9,9,9)'>x</div>"));
            var autoDiv = LayoutHarness.FindById(autoRoot, "b")!;
            Assert.Equal(OutlineStyle.Auto, autoDiv.OutlineStyle.Value);

            var autoG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(autoContainer, autoDiv, autoG);

            var (solidRoot, solidContainer) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:40pt; height:40pt; outline-style: solid; outline-width: 5pt; outline-color: rgb(9,9,9)'>x</div>"));
            var solidDiv = LayoutHarness.FindById(solidRoot, "b")!;

            var solidG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(solidContainer, solidDiv, solidG);

            var autoPolys = autoG.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            var solidPolys = solidG.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, autoPolys.Count);
            Assert.Equal(solidPolys.Select(p => p.Color), autoPolys.Select(p => p.Color));
            Assert.Equal(solidPolys.Select(p => p.Points), autoPolys.Select(p => p.Points));
        }

        [Fact]
        public async Task OutlineColorCurrentColor_ResolvesToTheElementsOwnColor()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; color: rgb(7,8,9); outline: 3pt solid'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            // CssUtils.ApplyCurrentColor already rewrites the initial "currentcolor" to the box's own
            // resolved color during cascade (the same mechanism border-*-color relies on).
            Assert.Equal("rgb(7, 8, 9)", div.OutlineColor);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);
            Assert.All(polys, p => Assert.Equal(RColor.FromArgb(7, 8, 9), p.Color));
        }

        [Fact]
        public async Task OutlineColorInvert_PaintsWhiteWrappedInADifferenceBlendMode()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 3pt solid invert'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            Assert.Equal("invert", div.OutlineColor);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var pushIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.PushBlendModeCall);
            var popIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.PopBlendModeCall);
            Assert.True(pushIndex >= 0, "expected a PushBlendMode call");
            Assert.True(popIndex > pushIndex, "expected PopBlendMode to follow PushBlendMode");
            Assert.Equal(RBlendMode.Difference, ((TestRecordingGraphics.PushBlendModeCall)g.Log[pushIndex]).Mode);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);
            Assert.All(polys, p => Assert.Equal(RColor.White, p.Color));

            // Every outline draw call sits inside the push/pop bracket.
            var firstPolyIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawPolygonCall);
            var lastPolyIndex = g.Log.FindLastIndex(e => e is TestRecordingGraphics.DrawPolygonCall);
            Assert.True(firstPolyIndex > pushIndex);
            Assert.True(lastPolyIndex < popIndex);
        }

        [Fact]
        public async Task OutlineColorInvert_ProducesADifferenceBlendModeExtGStateInTheRealPdf()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 40pt; height: 40pt; background: #ff0000; outline: 6pt solid invert; outline-offset: 2pt;"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/BM /Difference", pdfText);
        }

        [Fact]
        public async Task Outline_PaintsAfterBorder_InPaintOrder()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; border: 2pt solid rgb(1,1,1); outline: 3pt solid rgb(2,2,2)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            var lastBorderIndex = g.Log.FindLastIndex(e => e is TestRecordingGraphics.DrawPolygonCall p && p.Color == RColor.FromArgb(1, 1, 1));
            var firstOutlineIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawPolygonCall p && p.Color == RColor.FromArgb(2, 2, 2));

            Assert.True(lastBorderIndex >= 0);
            Assert.True(firstOutlineIndex >= 0);
            Assert.True(firstOutlineIndex > lastBorderIndex, "expected outline to paint after border");
        }

        [Fact]
        public async Task Outline_PaintsAfterItsOwnTextAndDescendants_NotJustAfterBorder()
        {
            // CSS-UI-4 §4: outline "is drawn 'over' a box" - CSS2.1 Appendix E draws it as the very last
            // step for the element, after its own generated content AND its whole stacking context (every
            // descendant). A negative outline-offset large enough to reach over the box's own text makes
            // this observable: the outline ring must still end up on top of that text, not underneath it.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:100pt; height:30pt; font:10pt Arial; outline: 20pt solid rgb(2,2,2); " +
                "outline-offset: -25pt; color: rgb(9,9,9)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var lastTextIndex = g.Log.FindLastIndex(e => e is TestRecordingGraphics.DrawStringCall);
            var firstOutlineIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawPolygonCall p && p.Color == RColor.FromArgb(2, 2, 2));

            Assert.True(lastTextIndex >= 0);
            Assert.True(firstOutlineIndex >= 0);
            Assert.True(firstOutlineIndex > lastTextIndex, "expected outline to paint after the box's own text");
        }

        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        public async Task OutlineStyleDottedOrDashed_DrawsOneMidBandLinePerSide(string style)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:20pt; height:20pt; outline: 8pt {style} rgb(3,3,3); outline-offset: 2pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var expectedDashStyle = style == "dotted" ? RDashStyle.Dot : RDashStyle.Dash;
            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(4, lines.Count);
            Assert.All(lines, l => Assert.Equal(expectedDashStyle, l.DashStyle));
            Assert.All(lines, l => Assert.Equal(RColor.FromArgb(3, 3, 3), l.Color));
            Assert.All(lines, l => Assert.Equal(8, l.Width, 1));
        }

        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        public async Task OutlineStyleDottedOrDashed_LinesReachTheRingsOuterCorner_NoGap(string style)
        {
            // Unlike border (which bands inward, so a line left at the bare box edges still meets its
            // neighbor within the box), outline bands outward - a line whose span stops at the box's own
            // edge leaves the outward-facing corner square entirely uncovered. Each side's line must
            // extend by its own mid-band distance so it actually reaches its neighbor's endpoint.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:20pt; height:20pt; outline: 8pt {style} rgb(3,3,3); outline-offset: 2pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            const double offset = 2;
            const double width = 8;
            var mid = offset + width / 2;

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            var top = lines.Single(l => l.Y1 == l.Y2 && l.Y1 < rect.Top);
            var left = lines.Single(l => l.X1 == l.X2 && l.X1 < rect.Left);

            // Top's leading endpoint (X1,Y1) and Left's leading endpoint (X1,Y1) must land on the exact
            // same corner point - (rect.Left - mid, rect.Top - mid) - for the two lines to actually meet
            // with no hole between them.
            Assert.Equal(rect.Left - mid, top.X1, 1);
            Assert.Equal(rect.Top - mid, top.Y1, 1);
            Assert.Equal(top.X1, left.X1, 1);
            Assert.Equal(top.Y1, left.Y1, 1);
        }

        [Fact]
        public async Task OutlineStyleDouble_DrawsTwoEqualWidthStripesWithGap()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 12pt double rgb(51,51,51)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // Every horizontal line strictly above the box's own top edge belongs to the top side's pair
            // of stripes - unambiguous, since the bottom side's stripes sit strictly below rect.Bottom.
            // Draw order (not Y order) distinguishes them: the near-the-box stripe is always drawn
            // first (OutlineDrawHandler.DrawDoubleOrGrooveRidge), mirroring BordersDrawHandler's own
            // outer-then-inner order.
            var topLines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .Where(l => l.Y1 == l.Y2 && l.Y1 < rect.Top).ToList();
            Assert.Equal(2, topLines.Count);

            var nearBox = topLines[0];
            var farFromBox = topLines[1];
            Assert.Equal(RColor.FromArgb(51, 51, 51), nearBox.Color);
            Assert.Equal(RColor.FromArgb(51, 51, 51), farFromBox.Color);
            Assert.Equal(4, nearBox.Width, 1);
            Assert.Equal(4, farFromBox.Width, 1);

            var nearBoxFarEdge = nearBox.Y1 - nearBox.Width / 2;
            var farFromBoxNearEdge = farFromBox.Y1 + farFromBox.Width / 2;
            Assert.True(farFromBoxNearEdge < nearBoxFarEdge, "expected a visible gap between the two double-outline stripes");
        }

        [Fact]
        public async Task OutlineStyleGroove_OuterStripeIsDarker_InnerStripeIsBaseColor()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 12pt groove rgb(51,51,51)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var rect = FragmentPaintHarness.FragmentOf(container, div).Lines[0].Rect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // Draw order (not Y order): outer (near the box) is drawn first for groove/ridge too.
            var topLines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .Where(l => l.Y1 == l.Y2 && l.Y1 < rect.Top).ToList();
            Assert.Equal(2, topLines.Count);

            Assert.Equal(RColor.FromArgb(25, 25, 25), topLines[0].Color);
            Assert.Equal(RColor.FromArgb(51, 51, 51), topLines[1].Color);
        }

        [Fact]
        public async Task OutlineStyleInset_DarkensTopAndLeft_NormalOnRightAndBottom()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 6pt inset rgb(100,100,100)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);

            var top = polys.OrderBy(p => p.Points.Average(pt => pt.Y)).First();
            var left = polys.OrderBy(p => p.Points.Average(pt => pt.X)).First();
            var right = polys.OrderByDescending(p => p.Points.Average(pt => pt.X)).First();
            var bottom = polys.OrderByDescending(p => p.Points.Average(pt => pt.Y)).First();

            Assert.Equal(RColor.FromArgb(50, 50, 50), top.Color);
            Assert.Equal(RColor.FromArgb(50, 50, 50), left.Color);
            Assert.Equal(RColor.FromArgb(100, 100, 100), right.Color);
            Assert.Equal(RColor.FromArgb(100, 100, 100), bottom.Color);
        }

        [Fact]
        public async Task OutlineStyleOutset_DarkensRightAndBottom_NormalOnTopAndLeft()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 6pt outset rgb(100,100,100)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);

            var top = polys.OrderBy(p => p.Points.Average(pt => pt.Y)).First();
            var left = polys.OrderBy(p => p.Points.Average(pt => pt.X)).First();
            var right = polys.OrderByDescending(p => p.Points.Average(pt => pt.X)).First();
            var bottom = polys.OrderByDescending(p => p.Points.Average(pt => pt.Y)).First();

            Assert.Equal(RColor.FromArgb(100, 100, 100), top.Color);
            Assert.Equal(RColor.FromArgb(100, 100, 100), left.Color);
            Assert.Equal(RColor.FromArgb(50, 50, 50), right.Color);
            Assert.Equal(RColor.FromArgb(50, 50, 50), bottom.Color);
        }

        [Fact]
        public async Task OutlineStyleNone_ZeroesActualOutlineWidth_EvenWithANonZeroDeclaredWidth()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='outline-style: none; outline-width: 6pt'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            // DerivedStyle.ActualOutlineWidth zeroes itself when the style is None, independent of
            // OutlineDrawHandler's own early-out on style - a direct property read (not painting)
            // exercises that branch.
            Assert.Equal(0, div.ActualOutlineWidth);
        }

        [Theory]
        [InlineData("thin")]
        [InlineData("medium")]
        [InlineData("thick")]
        public async Task OutlineWidthKeywords_ResolveTheSamePixelValuesAsBorder(string keyword)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='outline-style: solid; outline-width: {keyword}; border-style: solid; border-width: {keyword}'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            // Both reuse CssValueParser.GetActualBorderWidth against the same keyword, so they must
            // resolve to the exact same pixel value as each other, whatever it is.
            Assert.Equal(div.ActualBorderTopWidth, div.ActualOutlineWidth, 3);
            Assert.True(div.ActualOutlineWidth > 0);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("hidden")]
        public async Task OutlineStyleNoneOrHidden_PaintsNothing(string style)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='b' style='width:20pt; height:20pt; outline-style: {style}; outline-width: 6pt; outline-color: red'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task OutlineWidthZero_PaintsNothing()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; outline: 0 solid red'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        [Fact]
        public async Task OutlineOnAWrappingInlineElement_OnlyPaintsTheEdgesEachLineOwns()
        {
            // A span forced onto three lines - mirrors BoxDecorationBreakPaintIntegrationTests' own
            // "Slice_WrappingInline_DrawsNoBorderAtABreak", since outline is gated by the exact same
            // per-line HasLeftEdge/HasRightEdge flags FragmentPainter already computes for border.
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='outline:2pt solid #00f'>Alpha<br>Beta<br>Gamma</span></div>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, span, g);

            var blue = RColor.FromArgb(0, 0, 255);
            var vertical = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>()
                .Where(p => p.Color == blue && IsVerticalEdge(p.Points))
                .ToList();

            // The leading edge draws once (first line) and the trailing edge once (last line) - never
            // at either of the two internal wrap points.
            Assert.Equal(2, vertical.Count);
        }

        [Fact]
        public async Task Outline_DoesNotAffectLayout()
        {
            var withoutOutline = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='width:30pt; height:20pt;'>x</div>" +
                "<div id='b' style='width:30pt; height:20pt;'>y</div>"));
            var aWithout = LayoutHarness.FindById(withoutOutline.Root, "a")!;
            var bWithout = LayoutHarness.FindById(withoutOutline.Root, "b")!;

            var withOutline = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a' style='width:30pt; height:20pt; outline: 20pt solid red; outline-offset: 15pt;'>x</div>" +
                "<div id='b' style='width:30pt; height:20pt;'>y</div>"));
            var aWith = LayoutHarness.FindById(withOutline.Root, "a")!;
            var bWith = LayoutHarness.FindById(withOutline.Root, "b")!;

            // A large outline (offset+width = 35pt, comfortably wider than the box itself) visually
            // overlaps the sibling below, but must not shift it - outline is layout-neutral by spec.
            Assert.Equal(aWithout.Location, aWith.Location);
            Assert.Equal(aWithout.Size, aWith.Size);
            Assert.Equal(bWithout.Location, bWith.Location);
            Assert.Equal(bWithout.Size, bWith.Size);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static bool IsVerticalEdge(System.Collections.Generic.IReadOnlyList<RPoint> points)
        {
            var width = points.Max(p => p.X) - points.Min(p => p.X);
            var height = points.Max(p => p.Y) - points.Min(p => p.Y);
            return height > width;
        }

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
