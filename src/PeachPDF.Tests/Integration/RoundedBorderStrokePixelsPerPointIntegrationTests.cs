using PeachPDF.Adapters;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Rounded border paths receive raw layout-space coordinates, while the backend expects points.
    /// At the default <c>PixelsPerInch = 72</c> (<c>PixelsPerPoint = 1.0</c>) a missing division is
    /// invisible; at any other value the border renders too large and mis-positioned relative to the
    /// rest of the page. These tests cover both the continuous uniform outline and the filled
    /// per-side bands used by non-uniform rounded borders.
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

        /// <summary>
        /// Issue #851: <c>BordersDrawHandler.GetPen</c> set a rounded border stroke's <c>RPen.Width</c>
        /// from a raw, un-divided layout-space value - unlike the path's own (correctly divided)
        /// coordinates asserted above, so at a non-default <c>PixelsPerInch</c> the stroke rendered
        /// correctly positioned but visibly thicker than declared.
        /// </summary>
        [Fact]
        public async Task RoundedBorderStroke_PenWidth_IsInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='box' style='width:100pt;height:100pt;" +
                                 "border:6pt solid black;border-radius:14pt;'></div>";

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

            Assert.NotEmpty(recordingDefault.StrokedPenWidths);
            Assert.Equal(recordingDefault.StrokedPenWidths.Count, recordingScaled.StrokedPenWidths.Count);
            for (var i = 0; i < recordingDefault.StrokedPenWidths.Count; i++)
                Assert.Equal(recordingDefault.StrokedPenWidths[i], recordingScaled.StrokedPenWidths[i], 3);

            // Sanity: the pen width should actually equal the declared 6pt border width at both
            // PixelsPerInch values, not just happen to agree with each other.
            Assert.All(recordingDefault.StrokedPenWidths, w => Assert.Equal(6, w, 1));
            Assert.All(recordingScaled.StrokedPenWidths, w => Assert.Equal(6, w, 1));
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
        /// When an adjacent top/bottom edge is <c>none</c>/<c>hidden</c>, the visible left/right side
        /// owns that corner's entire arc. This exercises all four omitted-neighbor branches.
        /// </summary>
        [Fact]
        public async Task RoundedBorderBands_OmittedTopAndBottom_AreInvariantUnderNonDefaultPixelsPerInch()
        {
            const string html = "<div id='box' style='width:100pt;height:100pt;border:6pt solid black;" +
                                 "border-top-style:none;border-bottom-style:none;border-radius:14pt;'></div>";

            var (defaultPaths, scaledPaths) = await LayoutAndPaintFilledAtDefaultAndScaled(html);

            // The same-colored left/right side bands share one fill operation.
            Assert.Single(defaultPaths);
            AssertFilledPathsMatch(defaultPaths, scaledPaths);
        }

        /// <summary>
        /// Issue #853: the top-right inner contour must use the right border's own width. Asymmetric
        /// widths plus <c>border-top: none</c> isolate that corner.
        /// </summary>
        [Fact]
        public async Task RoundedBorderBand_TopRightInnerContour_UsesRightBorderOwnWidth()
        {
            const string html = "<div id='box' style='width:100pt;height:100pt;border-radius:14pt;" +
                                 "border-left:10pt solid black;border-right:2pt solid black;" +
                                 "border-top:none;border-bottom:6pt solid black;'></div>";

            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var box = LayoutHarness.FindById(root, "box");
            Assert.NotNull(box);

            var recording = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(container, box!, recording);

            var borderPath = Assert.Single(
                recording.Log.OfType<TestRecordingGraphics.DrawPathCall>(),
                path => !path.Stroked);

            var expectedX = box!.ActualRight - box.ActualBorderRightWidth;
            var wrongX = box.ActualRight - box.ActualBorderLeftWidth;
            Assert.NotEqual(wrongX, expectedX, 3);
            Assert.Contains(borderPath.Points, point => Math.Abs(point.X - expectedX) < 0.001);
            Assert.DoesNotContain(borderPath.Points, point => Math.Abs(point.X - wrongX) < 0.001);
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

        private static async Task<(IReadOnlyList<TestRecordingGraphics.DrawPathCall> Default, IReadOnlyList<TestRecordingGraphics.DrawPathCall> Scaled)>
            LayoutAndPaintFilledAtDefaultAndScaled(string html)
        {
            var (rootDefault, containerDefault) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var boxDefault = LayoutHarness.FindById(rootDefault, "box");
            Assert.NotNull(boxDefault);
            var recordingDefault = new TestRecordingGraphics { PixelsPerPointOverride = 1.0 };
            FragmentPaintHarness.PaintBox(containerDefault, boxDefault!, recordingDefault);

            var (rootScaled, containerScaled) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), pixelsPerPoint: 2.0);
            var boxScaled = LayoutHarness.FindById(rootScaled, "box");
            Assert.NotNull(boxScaled);
            var recordingScaled = new TestRecordingGraphics { PixelsPerPointOverride = 2.0 };
            FragmentPaintHarness.PaintBox(containerScaled, boxScaled!, recordingScaled);

            return (
                recordingDefault.Log.OfType<TestRecordingGraphics.DrawPathCall>().Where(path => !path.Stroked).ToList(),
                recordingScaled.Log.OfType<TestRecordingGraphics.DrawPathCall>().Where(path => !path.Stroked).ToList());
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

        private static void AssertFilledPathsMatch(
            IReadOnlyList<TestRecordingGraphics.DrawPathCall> expected,
            IReadOnlyList<TestRecordingGraphics.DrawPathCall> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Points.Count, actual[i].Points.Count);
                for (var p = 0; p < expected[i].Points.Count; p++)
                {
                    Assert.Equal(expected[i].Points[p].X, actual[i].Points[p].X, 3);
                    Assert.Equal(expected[i].Points[p].Y, actual[i].Points[p].Y, 3);
                }
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
