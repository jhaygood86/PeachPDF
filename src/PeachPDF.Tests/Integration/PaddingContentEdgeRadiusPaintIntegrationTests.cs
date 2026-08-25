using PeachPDF.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>background-clip: padding-box</c>/<c>content-box</c> and the <c>overflow: hidden</c> descendant
    /// clip curve must reduce the box's declared <c>border-radius</c> by the border width (and, for the
    /// content edge, the padding too) before curving the smaller rectangle - CSS Backgrounds and Borders
    /// Level 3 §5.5 ("Corner Clipping"). Asserted on the actual per-corner radii a painted clip path was
    /// built with (<see cref="RecordingGraphicsPath.Arcs"/>), not just that a rounded clip happened - a
    /// token/kind-only check would pass just as well for the pre-fix, spec-incorrect radius.
    /// </summary>
    public class PaddingContentEdgeRadiusPaintIntegrationTests
    {
        [Fact]
        public async Task BackgroundClip_PaddingBox_CurveRadiusIsBorderRadiusMinusBorderWidth()
        {
            // border-radius: 14pt, border: 6pt solid - the padding-box background fill's own curve
            // must land at 14-6=8pt, not the raw 14pt.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border:6pt solid black;" +
                "border-radius:14pt;background-color:steelblue;background-clip:padding-box;'></div>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.Single(recording.DrawnPaths);
            var arcs = recording.DrawnPaths[0].Arcs;
            Assert.NotEmpty(arcs);
            Assert.All(arcs, arc =>
            {
                Assert.Equal(8.0, arc.RadiusX, 2);
                Assert.Equal(8.0, arc.RadiusY, 2);
            });
        }

        [Fact]
        public async Task BackgroundClip_ContentBox_CurveRadiusSubtractsBorderAndPadding()
        {
            // border-radius: 30pt, border: 5pt solid, padding: 10pt - the content-box background
            // fill's own curve must land at 30-5-10=15pt.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border:5pt solid black;padding:10pt;" +
                "border-radius:30pt;background-color:steelblue;background-clip:content-box;'></div>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.Single(recording.DrawnPaths);
            var arcs = recording.DrawnPaths[0].Arcs;
            Assert.NotEmpty(arcs);
            Assert.All(arcs, arc =>
            {
                Assert.Equal(15.0, arc.RadiusX, 2);
                Assert.Equal(15.0, arc.RadiusY, 2);
            });
        }

        [Fact]
        public async Task BackgroundClip_BorderBox_CurveRadiusIsUnaffected()
        {
            // border-box is the box's own outer curve - it must NOT be reduced by the border width,
            // unlike padding-box/content-box. Regression guard so a future change to ClipRadii cannot
            // accidentally apply the inner reduction to every box-model keyword.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border:6pt solid black;" +
                "border-radius:14pt;background-color:steelblue;background-clip:border-box;'></div>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.Single(recording.DrawnPaths);
            var arcs = recording.DrawnPaths[0].Arcs;
            Assert.NotEmpty(arcs);
            Assert.All(arcs, arc =>
            {
                Assert.Equal(14.0, arc.RadiusX, 2);
                Assert.Equal(14.0, arc.RadiusY, 2);
            });
        }

        [Fact]
        public async Task BackgroundClip_PaddingBox_BorderWiderThanRadius_PaintsSquareCorners()
        {
            // border-radius: 4pt, border: 10pt solid - the padding-edge inner radius clamps to zero
            // (CSS Backgrounds and Borders Level 3 §5.5), so the padding-box fill must paint as a
            // plain, un-rounded rectangle rather than bulging past the border's own square inner edge.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border:10pt solid black;" +
                "border-radius:4pt;background-color:steelblue;background-clip:padding-box;'></div>"));

            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, box!, recording);

            // box.IsRounded is still true (its own declared border-radius is nonzero), so a rounded
            // clip path is still built - but its own radii are all zero, i.e. a rectangle in practice.
            Assert.Single(recording.DrawnPaths);
            Assert.DoesNotContain(recording.DrawnPaths[0].Arcs, arc => arc.RadiusX > 0 || arc.RadiusY > 0);
        }

        [Fact]
        public async Task OverflowHidden_PushesClipCurveReducedByBorderWidth()
        {
            // overflow:hidden + border-radius:14pt + border:6pt solid - the descendant clip curve
            // (issue #812) must follow the same padding-edge reduction as background-clip, so a box's
            // own background and its content-clip curve never visibly disagree.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='clipper' style='overflow:hidden;border:6pt solid black;border-radius:14pt;" +
                "width:100pt;height:100pt;'>" +
                "<div id='fill' style='width:100%;height:100%;'></div></div>"));

            var fillBox = LayoutHarness.FindById(root, "fill");
            Assert.NotNull(fillBox);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, fillBox!, recording);

            Assert.Single(recording.PushedClipPaths);
            var arcs = recording.PushedClipPaths[0].Arcs;
            Assert.NotEmpty(arcs);
            Assert.All(arcs, arc =>
            {
                Assert.Equal(8.0, arc.RadiusX, 2);
                Assert.Equal(8.0, arc.RadiusY, 2);
            });
        }
    }
}
