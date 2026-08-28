using PeachPDF.Adapters;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #812 (reopened): a rounded border stroke is built by
    /// <c>BordersDrawHandler.GetRoundedBorderPath</c> - a path builder entirely independent of
    /// <c>RenderUtils.GetRoundRect</c>, sharing no code with it - from raw, un-divided layout-space
    /// coordinates. At the library's default <c>PixelsPerInch = 72</c> (<c>PixelsPerPoint = 1.0</c>) a
    /// missing division is invisible; at any other value the rounded border stroke renders too large and
    /// mis-positioned relative to the rest of the page (the reported "border leaking past its own box").
    /// Asserted on the actual per-corner radii/positions a painted stroke path was built with (see
    /// <see cref="RecordingGraphicsPath.Arcs"/>/<see cref="RecordingGraphicsPath.Points"/>), not just that
    /// a rounded stroke happened, per this repo's own painting-test convention.
    /// </summary>
    public class RoundedBorderStrokePixelsPerPointIntegrationTests
    {
        [Fact]
        public async Task RoundedBorderStroke_ArcRadiiAndPosition_AreInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='box' style='width:100pt;height:100pt;" +
                                 "border:6pt solid black;border-radius:14pt;'></div>";

            var (defaultPaths, scaledPaths) = await LayoutAndPaintAtDefaultAndScaled(html);

            Assert.NotEmpty(defaultPaths);
            AssertPathsMatch(defaultPaths, scaledPaths);
        }

        [Fact]
        public async Task RoundedBorderStroke_NonDefaultPixelsPerInch_StaysWithinBoxBounds()
        {
            // Direct regression guard for the reported symptom: before the fix, an un-divided path put
            // the stroke's corners well outside the box's own (correctly-scaled) bounds at a non-default
            // PixelsPerInch - here, everything must land inside a small margin of the 100x100pt box.
            const string html = "<div id='box' style='width:100pt;height:100pt;" +
                                 "border:6pt solid black;border-radius:14pt;'></div>";

            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter()) { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(container, box!, recording);

            Assert.NotEmpty(recording.StrokedPaths);
            var allPoints = recording.StrokedPaths.SelectMany(p => p.Points).ToList();
            Assert.NotEmpty(allPoints);

            // The box (with its default 20pt margin) sits well within a generous bound - before the fix,
            // an un-divided (2x too large) path put points ~100pt+ outside this range.
            Assert.All(allPoints, p =>
            {
                Assert.InRange(p.X, 0, 200);
                Assert.InRange(p.Y, 0, 200);
            });
        }

        /// <summary>
        /// <c>Border.Left</c>/<c>Border.Right</c>'s <c>noTop</c>/<c>noBottom</c> mitre-avoidance branches
        /// (when the adjacent top/bottom edge is <c>none</c>/<c>hidden</c>, that side's own left/right
        /// stroke takes the corner arc a mitred edge would otherwise cut) only run when an adjacent edge
        /// is actually suppressed - exercised here by disabling the top and bottom borders, hitting all
        /// four corner-arc branches (<c>Border.Right</c>'s top/bottom arcs, <c>Border.Left</c>'s
        /// bottom/top arcs) the other two facts in this file never reach.
        /// </summary>
        [Fact]
        public async Task RoundedBorderStroke_MitreAvoidanceBranches_AreInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='box' style='width:100pt;height:100pt;border:6pt solid black;" +
                                 "border-top-style:none;border-bottom-style:none;border-radius:14pt;'></div>";

            var (defaultPaths, scaledPaths) = await LayoutAndPaintAtDefaultAndScaled(html);

            // Only the left/right strokes are drawn (top/bottom are `none`).
            Assert.Equal(2, defaultPaths.Count);
            AssertPathsMatch(defaultPaths, scaledPaths);
        }

        /// <summary>
        /// Issue #853: <c>GetRoundedBorderPath</c>'s <c>Border.Right</c> case offset the top-right arc's
        /// endpoint by half the <b>left</b> border's width instead of the right border's own - a
        /// copy-paste typo unrelated to <c>PixelsPerInch</c>, only visible when the left and right border
        /// widths differ. Asymmetric widths plus <c>border-top: none</c> (the <c>noTop</c> branch that
        /// reaches the buggy line) isolate exactly the arc endpoint the fix changes.
        /// </summary>
        [Fact]
        public async Task RoundedBorderStroke_TopRightArcEndpoint_UsesRightBorderOwnWidth()
        {
            const string html = "<div id='box' style='width:100pt;height:100pt;border-radius:14pt;" +
                                 "border-left:10pt solid black;border-right:2pt solid black;" +
                                 "border-top:none;border-bottom:6pt solid black;'></div>";

            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new RecordingGraphics(new PdfSharpAdapter()) { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(container, box!, recording);

            // Paint order (DrawBoxBorders) is Top/Left/Bottom/Right; Top is suppressed (none), so the
            // right border's path is the third one recorded.
            Assert.Equal(3, recording.StrokedPaths.Count);
            var rightBorderPath = recording.StrokedPaths[2];

            // Start, then the top-right ArcTo endpoint (noTop is true here), then a LineTo - no
            // bottom-right arc, since the bottom border is present (noBottom is false).
            Assert.Equal(3, rightBorderPath.Points.Count);
            var topRightArcEndpoint = rightBorderPath.Points[1];

            var expectedX = box!.ActualRight - box.ActualBorderRightWidth / 2;
            var wrongX = box.ActualRight - box.ActualBorderLeftWidth / 2;
            Assert.NotEqual(wrongX, expectedX, 3);
            Assert.Equal(expectedX, topRightArcEndpoint.X, 3);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Lays <paramref name="html"/> out and paints its <c>#box</c> element once at the default
        /// <c>PixelsPerPoint</c> (1.0) and once at a doubled value (2.0, simulating
        /// <c>PixelsPerInch = 144</c>) - every internal layout coordinate in the second pass is inflated
        /// by that factor relative to the first. Returns each pass's recorded stroked paths for the
        /// caller to compare.
        /// </summary>
        private static async Task<(IReadOnlyList<RecordingGraphicsPath> Default, IReadOnlyList<RecordingGraphicsPath> Scaled)>
            LayoutAndPaintAtDefaultAndScaled(string html)
        {
            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var boxDefault = LayoutHarness.FindById(rootDefault, "box");
            Assert.NotNull(boxDefault);
            var recordingDefault = new RecordingGraphics(new PdfSharpAdapter()) { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, boxDefault!, recordingDefault);

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var boxScaled = LayoutHarness.FindById(rootScaled, "box");
            Assert.NotNull(boxScaled);
            var recordingScaled = new RecordingGraphics(new PdfSharpAdapter()) { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, boxScaled!, recordingScaled);

            return (recordingDefault.StrokedPaths, recordingScaled.StrokedPaths);
        }

        /// <summary>
        /// Asserts <paramref name="expected"/> and <paramref name="actual"/> hold the same number of
        /// paths, and each corresponding pair has identical arc radii and point coordinates (3 decimal
        /// places) - the PixelsPerPoint-invariance this repo's #812 fix guarantees.
        /// </summary>
        private static void AssertPathsMatch(IReadOnlyList<RecordingGraphicsPath> expected, IReadOnlyList<RecordingGraphicsPath> actual)
        {
            Assert.Equal(expected.Count, actual.Count);

            for (var i = 0; i < expected.Count; i++)
            {
                AssertSequenceEqual(expected[i].Arcs, actual[i].Arcs, (e, a) =>
                {
                    Assert.Equal(e.RadiusX, a.RadiusX, 3);
                    Assert.Equal(e.RadiusY, a.RadiusY, 3);
                });

                AssertSequenceEqual(expected[i].Points, actual[i].Points, (e, a) =>
                {
                    Assert.Equal(e.X, a.X, 3);
                    Assert.Equal(e.Y, a.Y, 3);
                });
            }
        }

        private static void AssertSequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, System.Action<T, T> assertItem)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
                assertItem(expected[i], actual[i]);
        }
    }
}
