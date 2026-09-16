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
    /// §2.2</see> defines the styles by cross-reference: their "values have the same meaning as for the
    /// border-style properties". How much of that carries over is a judgement the painter documents; see
    /// <c>FragmentPainter.StrokeDecorationSegment</c> and issue #1121.
    /// </para>
    /// </summary>
    public class TextDecorationDoubleStyleTests
    {
        [Fact]
        public async Task Double_DrawsTwoStrokes_WhereSolidDrawsOne()
        {
            var solid = await StrokesOf("<span id='s' style='text-decoration:underline solid'>total</span>");
            var doubled = await StrokesOf("<span id='s' style='text-decoration:underline double'>total</span>");

            Assert.Single(solid);
            Assert.Equal(2, doubled.Count);

            // Same pen, and each stroke covers the same run as the single one it replaces.
            Assert.All(doubled, stroke =>
            {
                Assert.Equal(solid[0].Width, stroke.Width, 3);
                Assert.Equal(solid[0].X1, stroke.X1, 3);
                Assert.Equal(solid[0].X2, stroke.X2, 3);
                Assert.Equal(solid[0].DashStyle, stroke.DashStyle);
            });
        }

        /// <summary>
        /// The pair is two strokes of the resolved thickness with a gap of the same thickness, and an
        /// underline's grows downward from where the single stroke sits - its top edge is what is kept
        /// below the alphabetic baseline, so expanding upward would push the line back into the glyphs.
        /// </summary>
        [Fact]
        public async Task Double_KeepsTheFirstStrokeWhereASingleOneSits_AndGrowsAwayFromTheText()
        {
            var solid = await StrokesOf("<span id='s' style='text-decoration:underline solid'>total</span>");
            var doubled = await StrokesOf("<span id='s' style='text-decoration:underline double'>total</span>");

            var thickness = solid[0].Width;

            Assert.Equal(solid[0].Y1, doubled[0].Y1, 3);
            Assert.Equal(solid[0].Y1 + 2 * thickness, doubled[1].Y1, 3);
        }

        /// <summary>
        /// An overline grows the other way, which is the contrast showing the direction is decided per
        /// line rather than hardcoded downward. Both directions are what Chrome 141 does, measured at
        /// 300dpi: it keeps the single stroke's position and adds the second above for an overline and
        /// below for an underline and a line-through.
        /// </summary>
        [Fact]
        public async Task Double_GrowsUpwardForAnOverline_AndDownwardForALineThrough()
        {
            var solidOver = await StrokesOf("<span id='s' style='text-decoration:overline solid'>total</span>");
            var doubleOver = await StrokesOf("<span id='s' style='text-decoration:overline double'>total</span>");
            var solidThrough = await StrokesOf("<span id='s' style='text-decoration:line-through solid'>total</span>");
            var doubleThrough = await StrokesOf("<span id='s' style='text-decoration:line-through double'>total</span>");

            var thickness = solidOver[0].Width;

            Assert.Equal(solidOver[0].Y1 - 2 * thickness, doubleOver[0].Y1, 3);
            Assert.Equal(solidOver[0].Y1, doubleOver[1].Y1, 3);

            Assert.Equal(solidThrough[0].Y1, doubleThrough[0].Y1, 3);
            Assert.Equal(solidThrough[0].Y1 + 2 * thickness, doubleThrough[1].Y1, 3);
        }

        /// <summary>
        /// The separation follows the resolved thickness rather than a constant, so a heavier double rule
        /// keeps the same proportions instead of closing up into one band.
        /// </summary>
        [Fact]
        public async Task Double_SeparatesItsStrokesByTheResolvedThickness()
        {
            var thin = await StrokesOf(
                "<span id='s' style='text-decoration:underline double; text-decoration-thickness:1pt'>total</span>");
            var thick = await StrokesOf(
                "<span id='s' style='text-decoration:underline double; text-decoration-thickness:4pt'>total</span>");

            // PixelsPerPoint is 1.0 in BuildAndLayout, so a pt length resolves directly to that many units.
            Assert.Equal(1.0, thin[0].Width, 3);
            Assert.Equal(4.0, thick[0].Width, 3);

            Assert.Equal(2.0, thin[1].Y1 - thin[0].Y1, 3);
            Assert.Equal(8.0, thick[1].Y1 - thick[0].Y1, 3);
        }

        /// <summary>
        /// The contrast case: <c>dotted</c> and <c>dashed</c> are still one patterned stroke, not two.
        /// Without this, a change that simply doubled every decoration would pass everything above.
        /// </summary>
        [Theory]
        [InlineData("dotted", nameof(RDashStyle.Dot))]
        [InlineData("dashed", nameof(RDashStyle.Dash))]
        [InlineData("solid", nameof(RDashStyle.Solid))]
        // wavy has no dash pattern that could express it and still paints solid - see
        // TextDecorationStyleMapper. Pinned here so that gap is a stated expectation, not an accident.
        [InlineData("wavy", nameof(RDashStyle.Solid))]
        public async Task OtherStyles_AreStillASingleStroke(string style, string expected)
        {
            var strokes = await StrokesOf($"<span id='s' style='text-decoration:underline {style}'>total</span>");

            var stroke = Assert.Single(strokes);
            Assert.Equal(expected, stroke.DashStyle.ToString());
        }

        /// <summary>Every decoration stroke painted for the document, top to bottom.</summary>
        private static async Task<List<TestRecordingGraphics.DrawLineCall>> StrokesOf(string body)
        {
            var (root, container) = await BuildAndLayout(
                $"<!DOCTYPE html><html><body style='margin:0'>{body}</body></html>");

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, FindById(root, "s")!, g);

            return [.. g.Log.OfType<TestRecordingGraphics.DrawLineCall>().OrderBy(l => l.Y1)];
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
