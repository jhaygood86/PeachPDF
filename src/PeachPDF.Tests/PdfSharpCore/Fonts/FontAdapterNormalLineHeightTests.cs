using PeachPDF.Adapters;
using PeachDrawing.Text;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// <c>FontAdapter.NormalLineHeight</c> scaling (issue #956): the selection of the raw <c>hhea</c> triple or the OS/2
    /// typo triple is the engine's, and is covered by <c>NormalLineHeightMetricsTests</c> in the engine's own tests.
    /// </summary>
    public class FontAdapterNormalLineHeightTests
    {
        // Regression guard for a bug caught in review: FontAdapter.NormalLineHeight originally rounded
        // each ascent/descent/gap component to a whole CSS pixel *after* multiplying by PixelsPerPoint,
        // instead of before - correct only by coincidence at the default PixelsPerPoint=1 every other test
        // in this repo uses, since Length.PointsPerPx is a fixed pt-per-CSS-px ratio, not a
        // pt-per-internal-layout-unit one. Two FontAdapters wrapping the *same* XFont (so both share
        // identical ascent/descent/gap component values in real-point space, unaffected by
        // PixelsPerPoint - see FontAdapter's own remarks on why XFont.Size is a true, unscaled point size)
        // isolate the fix precisely: with each component correctly rounded once, in real-point space,
        // before the single final multiply, NormalLineHeight must scale by *exactly* PixelsPerPoint. The
        // bug this guards against instead re-rounds at a different (wrong) granularity per PixelsPerPoint
        // value, so the ratio would generally miss 2.0 by a fraction of a CSS pixel.
        [Fact]
        public void NormalLineHeight_ScalesExactlyLinearlyWithPixelsPerPoint()
        {
            // A set holding only the bundled fixture, so the metrics do not depend on which fonts the machine has installed.
            var fontSet = new FontSet();
            fontSet.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = "bundled" });
            var font = TestFonts.Create("bundled", 20, fontSet: fontSet);

            var unscaled = new FontAdapter(font, pixelsPerPoint: 1.0);
            var scaled = new FontAdapter(font, pixelsPerPoint: 2.0);

            Assert.Equal(2.0 * unscaled.NormalLineHeight, scaled.NormalLineHeight, 9);
        }
    }
}
