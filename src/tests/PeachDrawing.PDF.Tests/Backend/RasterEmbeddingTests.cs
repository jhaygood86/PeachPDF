using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Advanced;

namespace PeachPDF.Tests.PdfBackendTests
{
    /// <summary>A raster surface embedded as a CMYK image follows the document's black generation.</summary>
    public class RasterEmbeddingTests
    {
        [Theory]
        [InlineData(false, new byte[] { 0, 0, 0, 255 })]
        [InlineData(true, new byte[] { 153, 102, 102, 255 })]
        public void ACmykBitmap_FollowsTheDocumentsBlackGenerationForNeutralPixels(bool richBlack, byte[] expectedBlack)
        {
            using var surface = new RasterSurface(2, 1, 0, 0, 1, 1);
            var p = surface.Pixels;
            // Black, then a chromatic red.
            p[3] = 255;
            p[4] = 255;
            p[7] = 255;

            var image = RasterEmbedding.ToXImage(surface, asCmyk: true, richBlack: richBlack);
            var cmyk = image.CmykRaster!.Value.Data;

            Assert.Equal(expectedBlack, cmyk[..4]);
            Assert.Equal(new byte[] { 0, 255, 255, 0 }, cmyk[4..8]);
        }
    }
}
