using PeachDrawing.Abstractions;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Adapters
{
    /// <summary>
    /// <c>RenderContext.GetPen(PaintColor)</c> hands back a pen cached per color, so two unrelated strokes in the
    /// same color get the same object. The cache is an allocation optimization, not a state carrier -
    /// these pin that it behaves like one.
    /// </summary>
    public class PenCacheStateTests
    {
        [Fact]
        public void GetPen_SameColorTwice_ReturnsTheSameCachedInstance()
        {
            var adapter = new TestGraphicsAdapter();
            var g = new TestRecordingGraphics();

            // Sanity for the tests below: without a shared instance there would be nothing to leak.
            Assert.Same(adapter.GetPen(PaintColor.FromArgb(1, 2, 3)), adapter.GetPen(PaintColor.FromArgb(1, 2, 3)));
            Assert.Same(g.GetPen(PaintColor.FromArgb(1, 2, 3)), g.GetPen(PaintColor.FromArgb(1, 2, 3)));
        }

        [Fact]
        public void GetPen_AfterADottedStrokeConfiguredThePen_HandsBackASolidButtCappedPen()
        {
            // The real failure this guards: a dotted border sets a round cap and a zero-length dash
            // array, which is how a dot is expressed in PDF. A later same-colored stroke that only sets
            // its width - a list marker's ring, a form field's separator - would inherit both and paint
            // a row of dots instead of a line.
            var g = new TestRecordingGraphics();
            var color = PaintColor.FromArgb(51, 51, 51);

            var dotted = g.GetPen(color);
            dotted.Width = 8;
            dotted.LineCap = LineCap.Round;
            dotted.SetDashPattern([0, 16], 0);

            var reused = g.GetPen(color);

            Assert.Same(dotted, reused);
            Assert.Equal(LineCap.Butt, reused.LineCap);
            Assert.Equal(DashStyle.Solid, reused.DashStyle);
            Assert.Empty(reused.DashPattern);
            Assert.Equal(1, reused.Width);
        }

        [Fact]
        public void GetPen_DoesNotResetAPenTheCallerIsStillConfiguring()
        {
            // The reset happens on retrieval, not on use - so a caller that configures a pen and then
            // strokes with it keeps everything it set.
            var g = new TestRecordingGraphics();
            var pen = g.GetPen(PaintColor.FromArgb(9, 9, 9));

            pen.Width = 4;
            pen.LineCap = LineCap.Round;
            pen.SetDashPattern([0, 8], 0);

            Assert.Equal(4, pen.Width);
            Assert.Equal(LineCap.Round, pen.LineCap);
            Assert.NotEmpty(pen.DashPattern);
            Assert.Equal([0d, 8d], pen.DashPattern);
        }
    }
}
