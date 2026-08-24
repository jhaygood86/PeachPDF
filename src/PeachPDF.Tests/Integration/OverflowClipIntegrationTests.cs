using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies that overflow:hidden clips at the CSS padding edge, not the content edge.
    ///
    /// The bug: table cells inherit overflow:hidden from the PeachPDF default stylesheet.
    /// The old clip used ClientRectangle (content-box) which was occasionally too narrow for
    /// child elements that fill the content area, causing border-radius arcs to be cut off.
    /// The fix expands the clip to the padding-box (ClientRectangle + ActualPadding*).
    /// </summary>
    public class OverflowClipIntegrationTests
    {
        // --- Geometry tests (verify clip bounds after layout) ---

        [Fact]
        public async Task OverflowHidden_WithPadding_ChildBoundsWithinPaddingBoxClip()
        {
            // Container: overflow:hidden, padding:10px, 100px content width.
            // Child fills the content area.
            // New clip right = ClientRight + ActualPaddingRight (= padding-box right).
            var html = @"<!DOCTYPE html><html><head><style>
body { margin: 0; }
.outer { overflow: hidden; padding: 10px; width: 100px; height: 100px; }
.inner { height: 80px; border-radius: 20px; }
</style></head><body><div class='outer'><div class='inner'></div></div></body></html>";

            var root = await GetRootBox(html);
            var outer = FindFirst(root, b => b.HtmlTag?.Name == "div" && b.Overflow.Value == Overflow.Hidden);
            var inner = FindFirst(outer!, b => b.HtmlTag?.Name == "div" && b != outer);

            Assert.NotNull(outer);
            Assert.NotNull(inner);

            var paddingBoxRight = outer!.ClientRight + outer.ActualPaddingRight;

            Assert.True(inner!.ActualRight <= paddingBoxRight,
                $"Child right ({inner.ActualRight:F3}) exceeds padding-box clip right ({paddingBoxRight:F3})");
        }

        [Fact]
        public async Task RoundedBoxInTableCell_NonUniformRadius_BoundsWithinPaddingBoxClip()
        {
            // Reproduces the original bug: td gets overflow:hidden from the default stylesheet.
            // The div with a non-uniform border-radius (10px 30px) fills the td content area.
            // Its right edge must fit within the td's padding-box clip.
            var html = @"<!DOCTYPE html><html><head><style>
body { margin: 0; }
table { border-collapse: collapse; width: 300px; }
td { padding: 3px; }
</style></head><body>
<table><tr>
  <td><div style='border-radius: 10px 30px; height: 60px; border: 2px solid black;'></div></td>
</tr></table>
</body></html>";

            var root = await GetRootBox(html);
            var td = FindFirst(root, b => b.HtmlTag?.Name == "td");
            var div = FindFirst(td!, b => b.HtmlTag?.Name == "div");

            Assert.NotNull(td);
            Assert.NotNull(div);

            // td has overflow:hidden from the PeachPDF default stylesheet
            Assert.Equal(Overflow.Hidden, td!.Overflow.Value);

            var paddingBoxRight = td.ClientRight + td.ActualPaddingRight;

            Assert.True(div!.ActualRight <= paddingBoxRight,
                $"Div right ({div.ActualRight:F3}) exceeds padding-box clip right ({paddingBoxRight:F3})");
        }

        [Fact]
        public async Task RoundedBoxInTableCell_FourValueRadius_BoundsWithinPaddingBoxClip()
        {
            var html = @"<!DOCTYPE html><html><head><style>
body { margin: 0; }
table { border-collapse: collapse; width: 300px; }
td { padding: 3px; }
</style></head><body>
<table><tr>
  <td><div style='border-radius: 5px 15px 30px 45px; height: 60px; border: 2px solid black;'></div></td>
</tr></table>
</body></html>";

            var root = await GetRootBox(html);
            var td = FindFirst(root, b => b.HtmlTag?.Name == "td");
            var div = FindFirst(td!, b => b.HtmlTag?.Name == "div");

            Assert.NotNull(td);
            Assert.NotNull(div);

            var paddingBoxRight = td!.ClientRight + td.ActualPaddingRight;

            Assert.True(div!.ActualRight <= paddingBoxRight,
                $"Div right ({div.ActualRight:F3}) exceeds padding-box clip right ({paddingBoxRight:F3})");
        }

        [Fact]
        public async Task OverflowHidden_ZeroPadding_ClipEqualsContentBox()
        {
            // When padding is 0, padding-box == content-box.
            // The clip right should equal ClientRight (no expansion).
            var html = @"<!DOCTYPE html><html><head><style>
body { margin: 0; }
.outer { overflow: hidden; padding: 0; width: 100px; height: 100px; }
.inner { height: 80px; }
</style></head><body><div class='outer'><div class='inner'></div></div></body></html>";

            var root = await GetRootBox(html);
            var outer = FindFirst(root, b => b.HtmlTag?.Name == "div" && b.Overflow.Value == Overflow.Hidden);
            var inner = FindFirst(outer!, b => b.HtmlTag?.Name == "div" && b != outer);

            Assert.NotNull(outer);
            Assert.NotNull(inner);

            // Zero padding: padding-box right = ClientRight + 0 = ClientRight
            Assert.Equal(0.0, outer!.ActualPaddingRight, 3);
            var paddingBoxRight = outer.ClientRight + outer.ActualPaddingRight;
            Assert.Equal(outer.ClientRight, paddingBoxRight, 3);

            // Child still fits
            Assert.True(inner!.ActualRight <= paddingBoxRight + 0.01,
                $"Child right ({inner.ActualRight:F3}) exceeds content-box clip right ({paddingBoxRight:F3})");
        }

        // --- Rounded-clip curve tests (issue #812: overflow:hidden + border-radius must clip to the
        // curve, not just the padding-edge rectangle) ---

        [Fact]
        public async Task OverflowClipCurve_PopulatedForRoundedOverflowHiddenAncestor()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='track' style='overflow:hidden;border-radius:999pt;width:100pt;height:6pt;'>" +
                "<div id='fill' style='width:65%;height:100%;'></div></div>"));

            var fillBox = LayoutHarness.FindById(root, "fill");
            Assert.NotNull(fillBox);

            var fragment = FragmentPaintHarness.FirstFragmentOf(container, fillBox!);

            Assert.NotNull(fragment.OverflowClip);
            Assert.NotNull(fragment.OverflowClipCurve);
            Assert.True(fragment.OverflowClipCurve!.Radii.IsRounded);

            // border-radius:999pt on a 100pt x 6pt box is overconstrained on both axes, far more so on
            // height. The CSS spec's single joint reduction factor must land every radius at
            // height/2 = 3pt - a true semicircular cap, not an ellipse that only reduced Y this far
            // while X stayed near width/2 (the bug this fix corrects in DerivedStyle.ComputeRadii).
            Assert.Equal(3.0, fragment.OverflowClipCurve.Radii.TLX, 2);
            Assert.Equal(3.0, fragment.OverflowClipCurve.Radii.TLY, 2);
        }

        [Fact]
        public async Task OverflowClipCurve_NullForNonRoundedOverflowHiddenAncestor()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='track' style='overflow:hidden;width:100pt;height:6pt;'>" +
                "<div id='fill' style='width:65%;height:100%;'></div></div>"));

            var fillBox = LayoutHarness.FindById(root, "fill");
            Assert.NotNull(fillBox);

            var fragment = FragmentPaintHarness.FirstFragmentOf(container, fillBox!);

            Assert.NotNull(fragment.OverflowClip);
            Assert.Null(fragment.OverflowClipCurve);
        }

        [Fact]
        public async Task PaintingRoundedOverflowHiddenChild_PushesRectClipThenPathClip_BothPoppedBalanced()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='track' style='overflow:hidden;border-radius:999pt;width:100pt;height:6pt;'>" +
                "<div id='fill' style='width:65%;height:100%;'></div></div>"));

            var fillBox = LayoutHarness.FindById(root, "fill");
            Assert.NotNull(fillBox);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, fillBox!, recording);

            var curveIndex = recording.Log.FindIndex(op => op.Kind == PaintOpKind.PushClipPath);
            Assert.True(curveIndex > 0, "expected a rounded-curve clip to be pushed after a rectangular one");
            Assert.Equal(PaintOpKind.PushClip, recording.Log[curveIndex - 1].Kind);

            // Every push (rect and curve alike) must be matched by a pop - the exact failure mode
            // issue #812's symptom B describes (a stroke that "keeps going" past its clipped box).
            Assert.Equal(recording.PushCount, recording.PopCount);
        }

        [Fact]
        public async Task PaintingHoistedStackingContextChild_ThroughRoundedOverflowHiddenWrapper_PushesPathClip()
        {
            // `hoisted` establishes its own stacking context (opacity < 1), so StackingOrder.Flatten
            // paints it via the explicit ancestor-reapplication path (PaintStackingParticipant /
            // RenderUtils.PushAncestorOverflowClips / TryPushOverflowClip) rather than through
            // `clipper`'s own ordinary nested-children loop - the one other place a rounded overflow
            // clip is pushed, and the one this test isolates.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='clipper' style='overflow:hidden;border-radius:999pt;width:100pt;height:6pt;'>" +
                "<div id='hoisted' style='opacity:0.99;width:65%;height:100%;'></div></div>"));

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, recording);

            var curveIndex = recording.Log.FindIndex(op => op.Kind == PaintOpKind.PushClipPath);
            Assert.True(curveIndex >= 0,
                "expected the hoisted stacking-context child to re-apply its rounded ancestor's curve clip");
            Assert.Equal(recording.PushCount, recording.PopCount);
        }

        [Fact]
        public async Task OverflowClipCurve_PreservesEllipticalRadii()
        {
            // border-radius: 40pt / 10pt on a box large enough that no overlap-reduction kicks in - the
            // pushed curve's X and Y radii must stay distinct (elliptical), not collapse to one shared
            // value the way a bug in threading BorderRadii through OverflowClipCurve could.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='clipper' style='overflow:hidden;border-radius:40pt / 10pt;width:300pt;height:200pt;'>" +
                "<div id='fill' style='width:65%;height:100%;'></div></div>"));

            var fillBox = LayoutHarness.FindById(root, "fill");
            Assert.NotNull(fillBox);

            var fragment = FragmentPaintHarness.FirstFragmentOf(container, fillBox!);

            Assert.NotNull(fragment.OverflowClipCurve);
            Assert.Equal(40.0, fragment.OverflowClipCurve!.Radii.TLX, 2);
            Assert.Equal(10.0, fragment.OverflowClipCurve.Radii.TLY, 2);
        }

        [Fact]
        public async Task PaintingReplacedElement_InsideRoundedOverflowHiddenAncestor_PushesRectClipThenPathClip()
        {
            // Exercises ReplacedFragmentPainter's own pop-by-count loop (a second, independent copy of
            // the same pattern PaintBoxContent uses), which the plain-box tests above don't reach.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='clipper' style='overflow:hidden;border-radius:20pt;width:100pt;height:60pt;'>" +
                "<img id='pic' width='120' height='120' src='data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 viewBox=%220 0 100 100%22%3E%3Ccircle cx=%2250%22 cy=%2250%22 r=%2240%22 fill=%22red%22/%3E%3C/svg%3E'/></div>"));

            var picBox = LayoutHarness.FindById(root, "pic");
            Assert.NotNull(picBox);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, picBox!, recording);

            var curveIndex = recording.Log.FindIndex(op => op.Kind == PaintOpKind.PushClipPath);
            Assert.True(curveIndex > 0,
                "expected a rounded-curve clip pushed for a replaced element inside a rounded overflow:hidden ancestor");
            Assert.Equal(PaintOpKind.PushClip, recording.Log[curveIndex - 1].Kind);
            Assert.Equal(recording.PushCount, recording.PopCount);
        }

        [Fact]
        public async Task PaintingRectangularOverflowHiddenChild_PushesNoPathClip()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='track' style='overflow:hidden;width:100pt;height:6pt;'>" +
                "<div id='fill' style='width:65%;height:100%;'></div></div>"));

            var fillBox = LayoutHarness.FindById(root, "fill");
            Assert.NotNull(fillBox);

            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, fillBox!, recording);

            Assert.DoesNotContain(recording.Log, op => op.Kind == PaintOpKind.PushClipPath);
            Assert.Equal(recording.PushCount, recording.PopCount);
        }

        // --- Smoke tests (PDF generation must not throw) ---

        [Fact]
        public async Task RoundedBoxInTableCell_MultipleRadii_GeneratesPdf()
        {
            var html = @"<!DOCTYPE html><html><head><style>
table { border-collapse: collapse; width: 100%; }
td { padding: 3px; }
.rbox { height: 60px; background: steelblue; border: 2px solid #1a6b8a; }
</style></head><body>
<table><tr>
  <td><div class='rbox' style='border-radius: 20px;'></div></td>
  <td><div class='rbox' style='border-radius: 10px 30px;'></div></td>
  <td><div class='rbox' style='border-radius: 8px 20px 35px;'></div></td>
  <td><div class='rbox' style='border-radius: 5px 15px 30px 45px;'></div></td>
</tr></table>
</body></html>";

            var generator = new PdfGenerator();
            var ex = await Record.ExceptionAsync(() => generator.GeneratePdf(html, PageSize.A4));
            Assert.Null(ex);
        }

        // --- Helpers ---

        private static CssBox? FindFirst(CssBox box, Func<CssBox, bool> predicate)
        {
            if (predicate(box)) return box;
            foreach (var child in box.Boxes)
            {
                var found = FindFirst(child, predicate);
                if (found != null) return found;
            }
            return null;
        }

        private static async Task<CssBox> GetRootBox(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }
    }
}
