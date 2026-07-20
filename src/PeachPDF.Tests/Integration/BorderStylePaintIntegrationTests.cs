using PeachPDF.Adapters;
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
            var (root, _) = await BuildAndLayout(Wrap(
                $"<div id='b' style='border: 12px {style} rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            var exception = await Record.ExceptionAsync(async () => await div.Paint(g));

            Assert.Null(exception);
        }

        [Fact]
        public async Task BorderStyleDouble_DrawsTwoEqualWidthStripesWithGap()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: double; border-top-width: 12pt; border-top-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            await div.Paint(g);

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
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: groove; border-top-width: 12px; border-top-color: rgb(51,51,51)'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            await div.Paint(g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(2, lines.Count);

            Assert.Equal(RColor.FromArgb(25, 25, 25), lines[0].Color);
            Assert.Equal(RColor.FromArgb(51, 51, 51), lines[1].Color);
        }

        [Fact]
        public async Task BorderStyleRidge_IsMirrorImageOfGroove()
        {
            var (grooveRoot, _) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: groove; border-top-width: 12px; border-top-color: rgb(51,51,51)'>x</div>"));
            var grooveDiv = FindById(grooveRoot, "b")!;
            var grooveG = new TestRecordingGraphics();
            await grooveDiv.Paint(grooveG);
            var grooveLines = grooveG.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();

            var (ridgeRoot, _) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: ridge; border-top-width: 12px; border-top-color: rgb(51,51,51)'>x</div>"));
            var ridgeDiv = FindById(ridgeRoot, "b")!;
            var ridgeG = new TestRecordingGraphics();
            await ridgeDiv.Paint(ridgeG);
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
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-top-style: double; border-top-width: 12px; border-top-color: rgb(51,51,51); border-radius: 8px'>x</div>"));
            var div = FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            var exception = await Record.ExceptionAsync(async () => await div.Paint(g));

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
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='b' style='width:40px; height:40px; border-width:4px; border-color:rgb(51,51,51); border-style: none solid'>x</div>"));
            var div = FindById(root, "b")!;

            Assert.Equal(CssConstants.None, div.BorderTopStyle);
            Assert.Equal(CssConstants.Solid, div.BorderRightStyle);
            Assert.Equal(CssConstants.None, div.BorderBottomStyle);
            Assert.Equal(CssConstants.Solid, div.BorderLeftStyle);

            var g = new TestRecordingGraphics();
            await div.Paint(g);

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
            var (root, _) = await BuildAndLayout(Wrap(
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
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='b' style='border-style:solid'></div>"
                + "<style>#b { border-width: 0 2em; } #b { border-width: 1em; }</style>"));
            var div = FindById(root, "b")!;

            Assert.Equal("1em", div.BorderTopWidth);
            Assert.Equal("1em", div.BorderRightWidth);
            Assert.Equal("1em", div.BorderBottomWidth);
            Assert.Equal("1em", div.BorderLeftWidth);
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
