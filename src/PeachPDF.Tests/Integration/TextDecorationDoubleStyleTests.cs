using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>text-decoration-style: double</c> draws two strokes. It used to resolve to a solid pen and
    /// paint one, which is the same output <c>solid</c> produces - so an accounting-style double rule
    /// under a total came out as a single line with nothing to distinguish it.
    /// <para>
    /// <see href="https://www.w3.org/TR/css-text-decor-3/#text-decoration-style-property">css-text-decor-3
    /// §2.2</see> defines the styles by cross-reference to
    /// <see href="https://www.w3.org/TR/css-backgrounds-3/#border-style">css-backgrounds-3</see>'s
    /// <c>double</c>: "the sum of the two lines and the space between them equals" the total. Each
    /// stroke here is <c>Max(thickness / 3, one CSS pixel)</c> - literally a third of the resolved
    /// <c>text-decoration-thickness</c> once that third is wide enough to still read as a visible line,
    /// and only wider than a third (so the total exceeds the declared thickness) below that floor. See
    /// <c>FragmentPainter.StrokeDecorationSegment</c> and issue #1121.
    /// </para>
    /// </summary>
    public class TextDecorationDoubleStyleTests
    {
        /// <summary>
        /// <c>FragmentPainter.MinimumVisibleDoubleStrokeWidth</c>'s own formula, mirrored here so a test
        /// expresses "what a double stroke should measure" rather than a number that would need
        /// re-deriving by hand if the floor or the thirds-based split ever changed.
        /// </summary>
        private static double ExpectedStrokeWidth(double totalThickness) =>
            System.Math.Max(totalThickness / 3, PeachPDF.CSS.Length.PointsPerPx); // PixelsPerPoint is 1.0 in BuildAndLayout

        [Fact]
        public async Task Double_DrawsTwoStrokes_WhereSolidDrawsOne()
        {
            var solid = await StrokesOf("<span id='s' style='text-decoration:underline solid'>total</span>");
            var doubled = await StrokesOf("<span id='s' style='text-decoration:underline double'>total</span>");

            Assert.Single(solid);
            Assert.Equal(2, doubled.Count);

            var expectedStrokeWidth = ExpectedStrokeWidth(solid[0].Width);

            // Each stroke is narrower than the single line it replaces (clamped to the minimum here,
            // since the default `auto` thickness is well under 3x the one-CSS-pixel floor), but still
            // covers the same run and dash style.
            Assert.All(doubled, stroke =>
            {
                Assert.Equal(expectedStrokeWidth, stroke.Width, 3);
                Assert.Equal(solid[0].X1, stroke.X1, 3);
                Assert.Equal(solid[0].X2, stroke.X2, 3);
                Assert.Equal(solid[0].DashStyle, stroke.DashStyle);
            });
        }

        /// <summary>
        /// The pair is two strokes of a third the resolved thickness (floored) with a gap the same size,
        /// and an underline's grows downward from where the single stroke sits - its top edge is what is
        /// kept below the alphabetic baseline, so expanding upward would push the line back into the
        /// glyphs.
        /// </summary>
        [Fact]
        public async Task Double_KeepsTheFirstStrokeWhereASingleOneSits_AndGrowsAwayFromTheText()
        {
            var solid = await StrokesOf("<span id='s' style='text-decoration:underline solid'>total</span>");
            var doubled = await StrokesOf("<span id='s' style='text-decoration:underline double'>total</span>");

            var strokeWidth = ExpectedStrokeWidth(solid[0].Width);

            Assert.Equal(solid[0].Y1, doubled[0].Y1, 3);
            Assert.Equal(solid[0].Y1 + 2 * strokeWidth, doubled[1].Y1, 3);
        }

        /// <summary>
        /// A line-through grows downward, the same direction an underline does - the contrast against
        /// the overline test below, which shows the direction is decided per line rather than hardcoded.
        /// Both directions are what Chrome 141 does, measured at 300dpi: it keeps the single stroke's
        /// position and adds the second above for an overline and below for an underline/line-through.
        /// </summary>
        [Fact]
        public async Task Double_GrowsDownwardForALineThrough()
        {
            var solidThrough = await StrokesOf("<span id='s' style='text-decoration:line-through solid'>total</span>");
            var doubleThrough = await StrokesOf("<span id='s' style='text-decoration:line-through double'>total</span>");

            var strokeWidth = ExpectedStrokeWidth(solidThrough[0].Width);

            Assert.Equal(solidThrough[0].Y1, doubleThrough[0].Y1, 3);
            Assert.Equal(solidThrough[0].Y1 + 2 * strokeWidth, doubleThrough[1].Y1, 3);
        }

        /// <summary>
        /// An overline grows upward instead. Unlike the line-through/underline cases above, a double
        /// overline's near stroke cannot be compared against a separately laid-out <c>solid</c>
        /// document's own stroke position: issue #1124 reserves extra ascent-side headroom specifically
        /// for a <c>double</c> overline, which shifts the whole line (and so the box's own laid-out
        /// rectangle) down relative to where an equivalent <c>solid</c> span would sit - that reserved
        /// room is exactly what lets the outer stroke fit above the page without clipping (see
        /// <c>TextDecorationDoublePdfClipTests</c>). The near stroke sitting at the box's own rectangle
        /// top is the invariant that reservation is designed to preserve, so this asserts against the
        /// box's own laid-out rectangle instead.
        /// </summary>
        [Fact]
        public async Task Double_GrowsUpwardForAnOverline_KeepingTheNearStrokeAtTheBoxsOwnTop()
        {
            var (doubleOver, rect) = await StrokesAndRectOf("<span id='s' style='text-decoration:overline double'>total</span>");

            var strokeWidth = ExpectedStrokeWidth(1.0); // the default `auto` thickness

            Assert.Equal(rect.Top, doubleOver[1].Y1, 3);
            Assert.Equal(rect.Top - 2 * strokeWidth, doubleOver[0].Y1, 3);
        }

        /// <summary>
        /// The reservation still applies when <c>overline</c> is not the first keyword in a
        /// space-separated <c>text-decoration-line</c> list - <c>DoubleOverlineExtraReachAbove</c>'s own
        /// keyword-membership scan must not stop (or look) only at the first token.
        /// </summary>
        [Fact]
        public async Task Double_ReservesHeadroom_WhenOverlineIsNotTheFirstDecorationLineKeyword()
        {
            var (doubleOver, rect) = await StrokesAndRectOf(
                "<span id='s' style='text-decoration-line:underline overline; text-decoration-style:double'>total</span>");

            var strokeWidth = ExpectedStrokeWidth(1.0);
            var overlineStrokes = doubleOver.Where(l => l.Y1 < rect.Top + rect.Height / 2).ToList();

            Assert.Equal(2, overlineStrokes.Count);
            Assert.Equal(rect.Top, overlineStrokes[1].Y1, 3);
            Assert.Equal(rect.Top - 2 * strokeWidth, overlineStrokes[0].Y1, 3);
        }

        /// <summary>
        /// A thickness wide enough that a third of it already clears the minimum-visible floor is drawn
        /// spec-literally: each stroke is exactly a third of the declared thickness, and the pair's own
        /// total span (two strokes plus the gap) sums back to it, per the border-style cross-reference.
        /// A thickness too thin for that (the 1pt case here, and the default `auto` case covered by the
        /// tests above) is drawn at the floor instead, so the total exceeds what was declared.
        /// </summary>
        [Fact]
        public async Task Double_SeparatesItsStrokesByAThirdOfTheResolvedThickness_OnceAboveTheVisibleFloor()
        {
            var thin = await StrokesOf(
                "<span id='s' style='text-decoration:underline double; text-decoration-thickness:1pt'>total</span>");
            var thick = await StrokesOf(
                "<span id='s' style='text-decoration:underline double; text-decoration-thickness:4pt'>total</span>");

            // PixelsPerPoint is 1.0 in BuildAndLayout, so a pt length resolves directly to that many units.
            // 1pt/3 (~0.333) is below the 0.75pt (1 CSS pixel) floor, so it clamps; 4pt/3 (~1.333) clears it.
            Assert.Equal(ExpectedStrokeWidth(1.0), thin[0].Width, 3);
            Assert.Equal(4.0 / 3, thick[0].Width, 3);

            Assert.Equal(2 * ExpectedStrokeWidth(1.0), thin[1].Y1 - thin[0].Y1, 3);
            Assert.Equal(2 * (4.0 / 3), thick[1].Y1 - thick[0].Y1, 3);

            // The unclamped (4pt) case is spec-literal: two strokes plus the gap between them sum back
            // to the declared total. The clamped (1pt) case does not - that is the deliberate deviation.
            var thickStrokeWidth = thick[0].Width;
            var thickGap = thick[1].Y1 - thick[0].Y1 - thickStrokeWidth;
            Assert.Equal(4.0, 2 * thickStrokeWidth + thickGap, 3);

            var thinStrokeWidth = thin[0].Width;
            var thinGap = thin[1].Y1 - thin[0].Y1 - thinStrokeWidth;
            Assert.True(2 * thinStrokeWidth + thinGap > 1.0);
        }

        /// <summary>
        /// The contrast case: <c>dotted</c> and <c>dashed</c> are still one patterned stroke, not two.
        /// Without this, a change that simply doubled every decoration would pass everything above.
        /// <c>wavy</c> is not part of this contrast - see <c>TextDecorationWavyStyleTests</c> - since it
        /// is not a single <c>DrawLine</c> stroke at all (a stroked path instead), unlike every style here.
        /// </summary>
        [Theory]
        [InlineData("dotted", nameof(RDashStyle.Dot))]
        [InlineData("dashed", nameof(RDashStyle.Dash))]
        [InlineData("solid", nameof(RDashStyle.Solid))]
        public async Task OtherStyles_AreStillASingleStroke(string style, string expected)
        {
            var strokes = await StrokesOf($"<span id='s' style='text-decoration:underline {style}'>total</span>");

            var stroke = Assert.Single(strokes);
            Assert.Equal(expected, stroke.DashStyle.ToString());
        }

        /// <summary>Every decoration stroke painted for the document, top to bottom.</summary>
        private static async Task<List<TestRecordingGraphics.DrawLineCall>> StrokesOf(string body)
        {
            var (strokes, _) = await StrokesAndRectOf(body);
            return strokes;
        }

        /// <summary>As <see cref="StrokesOf"/>, plus the decorated box's own laid-out rectangle.</summary>
        private static async Task<(List<TestRecordingGraphics.DrawLineCall> Strokes, RRect Rect)> StrokesAndRectOf(string body)
        {
            var (root, container) = await BuildAndLayout(
                $"<!DOCTYPE html><html><body style='margin:0'>{body}</body></html>");
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var strokes = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().OrderBy(l => l.Y1).ToList();
            return (strokes, s.Rectangles.Values.Single());
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
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
            if (box.HtmlTag?.TryGetAttribute("id", "") is { } val &&
                val.Equals(id, System.StringComparison.OrdinalIgnoreCase)) return box;

            foreach (var child in box.Boxes)
            {
                if (FindById(child, id) is { } found) return found;
            }

            return null;
        }
    }
}
