using PeachPDF.Adapters;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.Adapters
{
    /// <summary>
    /// A Form XObject tile reports the exact point size it was created at, not a whole-point count.
    /// </summary>
    /// <remarks>
    /// <see cref="XImage.PixelWidth"/>/<see cref="XImage.PixelHeight"/> are <c>(int)</c> casts of the form's
    /// own view box, which is meaningless for vector content and destructive for a repeated one: a tiled
    /// background is placed at its natural size, so a truncation does not stay sub-point — it accumulates
    /// once per tile. Charts.css's grid lines are a <c>background-size: 100% calc(100% / 4)</c> tile at
    /// 32.65pt, and truncating to 32pt walked the fourth line 2.6pt clear of the axis it sits under.
    /// </remarks>
    public class FormTileNaturalSizeTests
    {
        [Fact]
        public void FormBackedImage_ReportsItsExactPointSize_NotAWholePointCount()
        {
            using var document = new PdfDocument();
            document.AddPage();

            var form = new XForm(document, new XSize(32.6518, 10.4));
            var image = new ImageAdapter(form);

            Assert.Equal(32.6518, image.Width, 4);
            Assert.Equal(10.4, image.Height, 4);
        }

        [Fact]
        public void FourTilesOfAQuarterHeight_StillSpanTheWholeBox()
        {
            // The property that actually matters, stated directly: `background-size: 100% calc(100% / 4)`
            // must tile to exactly the box it came from, so the fifth line lands on the far edge.
            const double boxHeight = 130.6071;

            using var document = new PdfDocument();
            document.AddPage();

            var tile = new ImageAdapter(new XForm(document, new XSize(100, boxHeight / 4)));

            Assert.Equal(boxHeight, tile.Height * 4, 3);
        }
    }
}
