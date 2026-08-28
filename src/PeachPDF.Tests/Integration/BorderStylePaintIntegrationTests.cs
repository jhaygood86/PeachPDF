using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies border-style painting actually produces the right geometry/color, not just that it
    /// doesn't crash. <c>double</c>/<c>groove</c>/<c>ridge</c> previously threw
    /// <see cref="System.ArgumentOutOfRangeException"/> at paint time in
    /// <c>BordersDrawHandler.GetPen</c> despite being documented as fully supported - a substring/
    /// token-presence check on PDF output would not have caught that, since the render simply never
    /// completed. Uses <see cref="TestRecordingGraphics"/> to assert the actual draw-call sequence,
    /// per this repo's painting-test convention (see <c>MarkerStylingIntegrationTests</c>).
    /// </summary>
    public class BorderStylePaintIntegrationTests
    {
        [Theory]
        [InlineData("dotted")]
        [InlineData("dashed")]
        [InlineData("solid")]
        [InlineData("double")]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("inset")]
        [InlineData("outset")]
        public async Task BorderStyle_AllCss1Keywords_DoNotThrowWhenPainted(string style)
        {
            var (root, container) = await BuildAndLayout(Wrap(
                $"<div id='b' style='border: 12px {style} rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            var exception = await Record.ExceptionAsync(async () => FragmentPaintHarness.PaintBox(container, div, g));

            Assert.Null(exception);
        }

        [Fact]
        public async Task BorderStyleDouble_DrawsTwoEqualWidthStripesWithGap()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: double; border-top-width: 12pt; border-top-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(2, lines.Count);
            var outer = lines[0];
            var inner = lines[1];

            // double = two same-color stripes, each floor(width/3), with the remainder as a gap.
            Assert.Equal(RColor.FromArgb(51, 51, 51), outer.Color);
            Assert.Equal(RColor.FromArgb(51, 51, 51), inner.Color);
            Assert.Equal(4, outer.Width, 1);
            Assert.Equal(4, inner.Width, 1);

            var outerFarEdge = outer.Y1 + outer.Width / 2;
            var innerNearEdge = inner.Y1 - inner.Width / 2;
            Assert.True(innerNearEdge > outerFarEdge, "expected a visible gap between the two double-border stripes");
        }

        [Fact]
        public async Task BorderStyleGroove_OuterStripeIsDarker_InnerStripeIsBaseColor()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: groove; border-top-width: 12px; border-top-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(2, lines.Count);

            Assert.Equal(RColor.FromArgb(25, 25, 25), lines[0].Color);
            Assert.Equal(RColor.FromArgb(51, 51, 51), lines[1].Color);
        }

        [Fact]
        public async Task BorderRightStyleDouble_DrawsTwoEqualWidthVerticalStripesWithGap()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-right-style: double; border-right-width: 12pt; border-right-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(2, lines.Count);
            var outer = lines[0];
            var inner = lines[1];

            // A right-edge border stripe is vertical: constant X, varying Y - the mirror image of the
            // top-edge case (BorderStyleDouble_DrawsTwoEqualWidthStripesWithGap) on the other axis.
            Assert.Equal(outer.X1, outer.X2, 1);
            Assert.Equal(inner.X1, inner.X2, 1);
            Assert.Equal(RColor.FromArgb(51, 51, 51), outer.Color);
            Assert.Equal(RColor.FromArgb(51, 51, 51), inner.Color);
            Assert.Equal(4, outer.Width, 1);
            Assert.Equal(4, inner.Width, 1);

            var outerNearEdge = outer.X1 - outer.Width / 2;
            var innerFarEdge = inner.X1 + inner.Width / 2;
            Assert.True(outerNearEdge > innerFarEdge, "expected a visible gap between the two double-border stripes");
        }

        [Fact]
        public async Task BorderLeftStyleGroove_OuterStripeIsDarker_InnerStripeIsBaseColor()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-left-style: groove; border-left-width: 12px; border-left-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(2, lines.Count);

            Assert.Equal(RColor.FromArgb(25, 25, 25), lines[0].Color);
            Assert.Equal(RColor.FromArgb(51, 51, 51), lines[1].Color);
        }

        [Fact]
        public async Task BorderColorPerSide_ResolvesDistinctColorPerEdge_IncludingCurrentColor()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:40px; height:40px; border-style:solid; border-width:4px; color: rgb(9,9,9); "
                + "border-top-color: rgb(1,0,0); border-right-color: rgb(0,1,0); "
                + "border-bottom-color: rgb(0,0,1); border-left-color: currentcolor'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            Assert.Equal(4, polys.Count);

            static (double X, double Y) Centroid(TestRecordingGraphics.DrawPolygonCall p) =>
                (p.Points.Average(pt => pt.X), p.Points.Average(pt => pt.Y));

            var withCentroids = polys.Select(p => (Poly: p, Centroid: Centroid(p))).ToList();
            var top = withCentroids.OrderBy(t => t.Centroid.Y).First();
            var bottom = withCentroids.OrderByDescending(t => t.Centroid.Y).First();
            var left = withCentroids.OrderBy(t => t.Centroid.X).First();
            var right = withCentroids.OrderByDescending(t => t.Centroid.X).First();

            Assert.Equal(RColor.FromArgb(1, 0, 0), top.Poly.Color);
            Assert.Equal(RColor.FromArgb(0, 1, 0), right.Poly.Color);
            Assert.Equal(RColor.FromArgb(0, 0, 1), bottom.Poly.Color);
            Assert.Equal(RColor.FromArgb(9, 9, 9), left.Poly.Color);
        }

        [Fact]
        public async Task BorderStyleRidge_IsMirrorImageOfGroove()
        {
            var (grooveRoot, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: groove; border-top-width: 12px; border-top-color: rgb(51,51,51)'>x</div>"));
            var grooveDiv = FindById(grooveRoot, "b")!;
            var grooveG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, grooveDiv, grooveG);
            var grooveLines = grooveG.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();

            var (ridgeRoot, ridgeContainer) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: ridge; border-top-width: 12px; border-top-color: rgb(51,51,51)'>x</div>"));
            var ridgeDiv = FindById(ridgeRoot, "b")!;
            var ridgeG = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(ridgeContainer, ridgeDiv, ridgeG);
            var ridgeLines = ridgeG.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();

            Assert.Equal(2, grooveLines.Count);
            Assert.Equal(2, ridgeLines.Count);

            // Exactly the class of bug a substring test would miss: visually-identical-but-swapped
            // stripe colors. groove's outer stripe must equal ridge's inner stripe, and vice versa.
            Assert.Equal(grooveLines[0].Color, ridgeLines[1].Color);
            Assert.Equal(grooveLines[1].Color, ridgeLines[0].Color);
            Assert.NotEqual(grooveLines[0].Color, grooveLines[1].Color);
        }

        [Fact]
        public async Task BorderStyleDoubleWithBorderRadius_FallsBackToSingleSolidStroke()
        {
            // GetRoundedBorderPath has no double/groove/ridge concept (border-radius is CSS2/3
            // territory) - this locks in the documented narrowing: a rounded double/groove/ridge
            // border degrades to a single solid-colored stroke rather than crashing.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: double; border-top-width: 12px; border-top-color: rgb(51,51,51); border-radius: 8px'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            var exception = await Record.ExceptionAsync(async () => FragmentPaintHarness.PaintBox(container, div, g));

            Assert.Null(exception);
            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.NotEmpty(g.Log.OfType<TestRecordingGraphics.DrawPathCall>());
        }

        // ─── border-style 2-value shorthand + per-side suppression (Acid2's "[class~=one].first.one") ──
        // "border-style: none solid" must expand to top=bottom=none, left=right=solid (CSS2.1's 1/2/3/4-
        // value box-shorthand expansion), and only the solid sides may actually paint.

        [Fact]
        public async Task BorderStyleTwoValueShorthand_OnlyPaintsTheSolidSides()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:40px; height:40px; border-width:4px; border-color:rgb(51,51,51); border-style: none solid'>x</div>"));
            var div = FindById(root, "b")!;

            Assert.Equal(LineStyle.None, div.BorderTopStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderRightStyle.Value);
            Assert.Equal(LineStyle.None, div.BorderBottomStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderLeftStyle.Value);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // Solid borders paint as a mitered quad (BordersDrawHandler.SetInOutsetRectanglePoints),
            // not a single line - see BordersDrawHandler's own doc comment on why (the classic CSS
            // "border triangle" technique, which Acid2's own nose diamond relies on, needs a real
            // diagonal miter at each corner, not a thick straight line that just overlaps whichever
            // adjacent border painted before it).
            var polys = g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().ToList();
            // Two sides painted (left, right), each as a mitered quad - top/bottom (none) draw nothing.
            Assert.Equal(2, polys.Count);
            Assert.All(polys, p => Assert.Equal(RColor.FromArgb(51, 51, 51), p.Color));

            // A vertical (left/right) side quad spans more in Y than X; a horizontal one would be the
            // reverse.
            Assert.All(polys, p =>
            {
                var minX = p.Points.Min(pt => pt.X);
                var maxX = p.Points.Max(pt => pt.X);
                var minY = p.Points.Min(pt => pt.Y);
                var maxY = p.Points.Max(pt => pt.Y);
                Assert.True(maxY - minY > maxX - minX,
                    "expected only vertical (left/right) border quads, none horizontal");
            });
        }

        // ─── border-color/border-width 4-value and 2-value expansion resolve per-side ──
        // Acid2's ".nose div div:before { border-color: red yellow black yellow; border-width: 1em; }"
        // (4-value color) and ".picture p { ... }" style earlier "border-width: 0 2em" (2-value) shapes.

        [Fact]
        public async Task BorderColorFourValueShorthand_ResolvesTopRightBottomLeftPerSide()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-style:solid; border-width:1px; border-color: rgb(1,0,0) rgb(0,1,0) rgb(0,0,1) rgb(1,1,0)'>x</div>"));
            var div = FindById(root, "b")!;

            Assert.Equal("rgb(1, 0, 0)", div.BorderTopColor);
            Assert.Equal("rgb(0, 1, 0)", div.BorderRightColor);
            Assert.Equal("rgb(0, 0, 1)", div.BorderBottomColor);
            Assert.Equal("rgb(1, 1, 0)", div.BorderLeftColor);
        }

        [Fact]
        public async Task BorderWidthTwoValueShorthand_ThenLaterOneValue_OverridesAllSidesPerSpecificity()
        {
            // Mirrors the fixture's own "border-width: 0 2em" (2-value: top/bottom=0, left/right=2em)
            // followed later by a same-specificity "border-width: 1em" (all sides) - the later rule
            // must win outright on every side, not merge/leave the 2-value expansion partially intact.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-style:solid'></div>"
                + "<style>#b { border-width: 0 2em; } #b { border-width: 1em; }</style>"));
            var div = FindById(root, "b")!;

            Assert.Equal("1em", div.BorderTopWidth);
            Assert.Equal("1em", div.BorderRightWidth);
            Assert.Equal("1em", div.BorderBottomWidth);
            Assert.Equal("1em", div.BorderLeftWidth);
        }

        // ─── deprecated presentational `border` HTML attribute always resolves solid ──
        // DomParser.CascadeApplyStyles' HtmlConstants.Border case: a non-zero `border` attribute forces
        // every side's style to solid (CssProperty<LineStyle>.FromValue(Keywords.Solid, LineStyle.Solid)
        // / the shared SolidBorderStyle constant). Only the plain-element path is covered here - the
        // table-to-cell cascade (ApplyTableBorder/SetForAllCells, issue #636) is covered by
        // PresentationalAttributeIntegrationTests.BorderAttribute_OnTable_CascadesASolidBorderToCells.

        [Fact]
        public async Task PresentationalBorderAttribute_OnAPlainElement_ForcesSolidOnAllSides()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='b' border='1'>x</div>"));
            var div = FindById(root, "b")!;

            Assert.Equal(LineStyle.Solid, div.BorderTopStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderRightStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderBottomStyle.Value);
            Assert.Equal(LineStyle.Solid, div.BorderLeftStyle.Value);
        }

        // ─── issue #851: pen stroke width ignores non-default PixelsPerInch ────────

        [Fact]
        public async Task BorderStyleDotted_PenWidth_IsInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='b' style='border: 12pt dotted rgb(51,51,51)'>x</div>";

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var divDefault = LayoutHarness.FindById(rootDefault, "b");
            Assert.NotNull(divDefault);
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, divDefault!, gDefault);
            var widthsDefault = gDefault.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var divScaled = LayoutHarness.FindById(rootScaled, "b");
            Assert.NotNull(divScaled);
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, divScaled!, gScaled);
            var widthsScaled = gScaled.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            Assert.NotEmpty(widthsDefault);
            Assert.Equal(widthsDefault.Count, widthsScaled.Count);
            for (var i = 0; i < widthsDefault.Count; i++)
                Assert.Equal(widthsDefault[i], widthsScaled[i], 3);

            // Sanity: the pen width should actually equal the declared 12pt border width.
            Assert.All(widthsDefault, w => Assert.Equal(12, w, 1));
        }

        [Fact]
        public async Task BorderStyleDoubleAndGroove_StripeWidths_AreInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='b' style='border-top-style: double; border-top-width: 12pt; " +
                                 "border-top-color: rgb(51,51,51); border-left-style: groove; " +
                                 "border-left-width: 12pt; border-left-color: rgb(51,51,51)'>x</div>";

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var divDefault = LayoutHarness.FindById(rootDefault, "b");
            Assert.NotNull(divDefault);
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, divDefault!, gDefault);
            var widthsDefault = gDefault.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var divScaled = LayoutHarness.FindById(rootScaled, "b");
            Assert.NotNull(divScaled);
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, divScaled!, gScaled);
            var widthsScaled = gScaled.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            // double (top) contributes 2 stripes, groove (left) contributes 2 more.
            Assert.Equal(4, widthsDefault.Count);
            Assert.Equal(widthsDefault.Count, widthsScaled.Count);
            for (var i = 0; i < widthsDefault.Count; i++)
                Assert.Equal(widthsDefault[i], widthsScaled[i], 3);
        }

        [Fact]
        public async Task CollapsedBorderStyleDouble_StripeWidths_AreInvariantUnderNonDefaultPixelsPerInch()
        {
            // BordersDrawHandler.DrawDoubleOrGrooveRidgeSegment is the collapsed-table-border twin of
            // DrawDoubleOrGrooveRidgeBorder covered above - a separate code path (CollapsedBorderModel's
            // resolved segments, not a box's own DrawBoxBorders) with its own pen-width divisions.
            var html = LayoutHarness.Wrap(
                "<table style='border-collapse:collapse'><tr><td style='border:12pt double rgb(51,51,51)'>x</td></tr></table>");

            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(html);
            var gDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintPage(containerDefault, gDefault);
            var widthsDefault = gDefault.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(html, pixelsPerPoint: 2.0);
            var gScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintPage(containerScaled, gScaled);
            var widthsScaled = gScaled.Log.OfType<TestRecordingGraphics.DrawLineCall>().Select(l => l.Width).ToList();

            Assert.NotEmpty(widthsDefault);
            Assert.Equal(widthsDefault.Count, widthsScaled.Count);
            for (var i = 0; i < widthsDefault.Count; i++)
                Assert.Equal(widthsDefault[i], widthsScaled[i], 3);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
