using PeachPDF.Adapters;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>text-decoration-style: wavy</c> strokes a wavy <see cref="PeachPDF.Html.Adapters.RGraphicsPath"/>
    /// instead of drawing a straight <c>DrawLine</c> - it used to resolve to a solid pen and paint a
    /// straight line, the same output <c>solid</c> produces, which is an unambiguous spec deviation
    /// (css-text-decor-3 §2.2 defines <c>wavy</c> in its own words: "Draw a wavy line"). See
    /// <see cref="PeachPDF.Html.Core.Utils.WavyDecorationRenderer"/> for the wave's geometry and issue #1114.
    /// </summary>
    public class TextDecorationWavyStyleTests
    {
        [Fact]
        public async Task Wavy_StrokesAPath_NotALine()
        {
            var solid = await Paint("underline solid");
            var wavy = await Paint("underline wavy");

            Assert.Single(Lines(solid));
            Assert.Empty(Lines(wavy));
            Assert.True(Assert.Single(Paths(wavy)).Stroked);
        }

        [Theory]
        [InlineData("underline")]
        [InlineData("overline")]
        [InlineData("line-through")]
        public async Task Wavy_StrokesAPath_ForEveryLineKeyword(string line)
        {
            var g = await Paint($"{line} wavy");

            var path = Assert.Single(Paths(g));
            Assert.True(path.Points.Count >= 3, "at least one Bézier curve (3 points) should have been added");
        }

        /// <summary>
        /// <c>text-decoration-thickness: 0</c> must not hang: without a floor on the thickness used for
        /// the wave's own <c>wavelength</c>, the per-period loop in
        /// <see cref="PeachPDF.Html.Core.Utils.WavyDecorationRenderer.StrokeWavyLine"/> would never
        /// advance (<c>x += 0</c>). This test itself is the regression guard - it would never complete
        /// if that floor were removed.
        /// </summary>
        [Fact]
        public async Task Wavy_WithZeroThickness_DoesNotHang()
        {
            var g = await Paint("underline wavy; text-decoration-thickness:0");

            var path = Assert.Single(Paths(g));
            Assert.Equal(0, path.StrokeWidth);
        }

        /// <summary>
        /// The floor that keeps <c>wavelength</c> (and so the loop above) sane applies only to the
        /// wave's own proportions - the stroke itself always renders at the true declared thickness,
        /// matching every other <c>text-decoration-style</c>, even when that thickness is thinner than
        /// the floor.
        /// </summary>
        [Fact]
        public async Task Wavy_WithVerySmallThickness_StrokesAtTheDeclaredWidth_NotTheGeometryFloor()
        {
            var g = await Paint("underline wavy; text-decoration-thickness:0.1pt");

            var path = Assert.Single(Paths(g));
            Assert.Equal(0.1, path.StrokeWidth, 3);
            Assert.True(path.Points.Count >= 3, "the wave should still have a visible shape");
        }

        /// <summary>
        /// The wave's centerline grows away from the text the same direction <c>double</c>'s second
        /// stroke does - see <c>FragmentPainter.StrokeDecorationSegment</c>'s remarks. Every full or
        /// partial period the wave draws contributes points symmetric around its own centerline (the
        /// start/end of each Bézier sits exactly on it, the two control points equally above/below it),
        /// so the mean of every recorded point - including the path's own starting point, also on the
        /// centerline - is the centerline itself, regardless of how many periods a given segment drew.
        /// </summary>
        [Fact]
        public async Task Wavy_GrowsAwayFromTheText_LikeDoublesSecondStroke()
        {
            var solidUnder = await Paint("underline solid");
            var wavyUnder = await Paint("underline wavy");
            var solidOver = await Paint("overline solid");
            var wavyOver = await Paint("overline wavy");

            var solidUnderY = Assert.Single(Lines(solidUnder)).Y1;
            var solidOverY = Assert.Single(Lines(solidOver)).Y1;
            var wavyUnderY = Assert.Single(Paths(wavyUnder)).Points.Average(p => p.Y);
            var wavyOverY = Assert.Single(Paths(wavyOver)).Points.Average(p => p.Y);

            Assert.True(wavyUnderY > solidUnderY, "an underline's wave should sit below the solid position");
            Assert.True(wavyOverY < solidOverY, "an overline's wave should sit above the solid position");
        }

        /// <summary>
        /// At a non-default <c>PixelsPerInch</c>, <c>WavyDecorationRenderer</c> has to divide both the
        /// path's own coordinates and the stroking pen's width by <c>PixelsPerPoint</c> itself - unlike
        /// <c>DrawLine</c>, <c>DrawPath</c> never does that division on its own (issue #812; see
        /// <c>RoundedBorderStrokePixelsPerPointIntegrationTests</c> for the same regression on rounded
        /// borders). A forgotten division would render the stroke twice as wide and the path twice as
        /// far along the page as the word it decorates.
        /// </summary>
        [Fact]
        public async Task Wavy_DividesPathCoordinatesAndPenWidthByPixelsPerPoint()
        {
            const double ppp = 2.0;

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<span id='s' style='text-decoration:underline wavy; text-decoration-thickness:4pt'>total</span>"),
                pixelsPerPoint: ppp);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics { PixelsPerPointOverride = ppp };
            FragmentPaintHarness.PaintBox(container, s, g);

            var path = Assert.Single(Paths(g));

            // 4pt, not 4 * ppp.
            Assert.Equal(4, path.StrokeWidth, 1);

            // The path's own X coordinates land in the same divided-down ("points") space as the word's
            // own rectangle - allowing a wavelength's worth of overshoot past the segment's own end,
            // which the caller clips rather than the path itself trimming (see
            // WavyDecorationRenderer's remarks).
            var rect = s.Rectangles.Values.Single();
            var wavelength = 4.5 * path.StrokeWidth;
            Assert.All(path.Points, p => Assert.InRange(p.X, rect.Left / ppp - 1, rect.Right / ppp + wavelength));
        }

        /// <summary>
        /// <c>RAdapter.GetPen(RColor)</c> returns a cached, mutable pen keyed only by color.
        /// <c>WavyDecorationRenderer.StrokeWavyLine</c> calls it too, for the same color, and sets its
        /// own (divided) <c>Width</c> on it - the very same pen object <c>PaintDecoration</c> is still
        /// holding a reference to for the rest of its per-keyword loop. Before <c>thickness</c> was
        /// captured as a local (rather than re-read from <c>pen.Width</c> for each keyword/segment), a
        /// second wavy line sharing the first's color came out with the first's already-divided width
        /// divided a second time.
        /// </summary>
        [Fact]
        public async Task Wavy_TwoLineKeywords_AtNonDefaultPixelsPerInch_KeepTheSameStrokeWidth()
        {
            const double ppp = 2.0;

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<span id='s' style='text-decoration:underline overline wavy; text-decoration-thickness:4pt'>total</span>"),
                pixelsPerPoint: ppp);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics { PixelsPerPointOverride = ppp };
            FragmentPaintHarness.PaintBox(container, s, g);

            var paths = Paths(g);
            Assert.Equal(2, paths.Count);
            Assert.All(paths, p => Assert.Equal(4, p.StrokeWidth, 1));
        }

        /// <summary>
        /// <c>text-decoration-skip-ink</c> segments a wavy underline exactly like a solid one - each
        /// segment restarts its own phase at its own start (see
        /// <see cref="PeachPDF.Html.Core.Utils.WavyDecorationRenderer"/>'s remarks on why), so a broken
        /// wavy line is still one stroked path per segment.
        /// </summary>
        [Fact]
        public async Task Wavy_SkipsInkInSeveralIndependentSegments()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div style=\"width:400pt; font:20pt 'SkipInkWavyTestFont'\">"
                    + "<span id='s' style='text-decoration:underline wavy'>nnnjnnn</span></div>"),
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, "SkipInkWavyTestFont"));
            var s = LayoutHarness.FindById(root, "s")!;

            using var g = new InkAwareRecordingGraphics((PdfSharpAdapter)container.Adapter);
            FragmentPaintHarness.PaintBox(container, s, g);

            var paths = Paths(g);
            Assert.True(paths.Count >= 2,
                $"an underline across a descender should be drawn as several wavy segments, got {paths.Count}");
        }

        [Fact]
        public async Task Wavy_BreaksAroundAnAtomicInline()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline wavy'>AA "
                + "<span style='display:inline-block; width:60pt'>hidden</span> BB</div>"));
            var d = LayoutHarness.FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            Assert.Equal(2, Paths(g).Count);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static List<TestRecordingGraphics.DrawLineCall> Lines(TestRecordingGraphics g) =>
            [.. g.Log.OfType<TestRecordingGraphics.DrawLineCall>()];

        private static List<TestRecordingGraphics.DrawPathCall> Paths(TestRecordingGraphics g) =>
            [.. g.Log.OfType<TestRecordingGraphics.DrawPathCall>().Where(p => p.Stroked)];

        private static async Task<TestRecordingGraphics> Paint(string decoration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<span id='s' style='text-decoration:{decoration}'>total</span>"));
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);
            return g;
        }
    }
}
