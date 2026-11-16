using PeachDrawing.Core;
using PeachDrawing.Core.Barcodes;

namespace PeachDrawing.Tests
{
    /// <summary>
    /// Draws barcodes on the raster canvas and reads the pixels back: each module center must be dark exactly where the
    /// symbol says, which fails if the path were empty, mis-scaled or offset.
    /// </summary>
    public class BarcodeRasterTests
    {
        private const double Module = 4;

        private static (RasterCanvas Canvas, Rect Bounds) Draw(Barcode barcode, PaintPoint origin)
        {
            var context = new RasterRenderContext();
            var canvas = context.CreateCanvas(700, 400);
            var bounds = canvas.DrawBarcode(barcode, origin, Module, PaintColor.Black, PaintColor.White);
            return (canvas, bounds);
        }

        private static bool DarkAt(PixelBuffer buffer, double x, double y)
        {
            var span = buffer.PremultipliedRgba.Span;
            int p = ((int)y * buffer.Width + (int)x) * 4;
            return span[p + 3] > 128 && span[p] < 100;
        }

        [Theory]
        [InlineData("https://peachpdf.net/barcodes")]
        [InlineData("HELLO WORLD")]
        public void QrModulesLandWhereTheSymbolSaysTheyAre(string text)
        {
            var qr = Barcode.CreateQrCode(text);
            var origin = new PaintPoint(10, 20);
            var (canvas, bounds) = Draw(qr, origin);
            using (canvas)
            {
                var buffer = canvas.ToPixelBuffer();
                for (int r = 0; r < qr.Rows; r++)
                    for (int c = 0; c < qr.Columns; c++)
                        Assert.Equal(qr.IsDark(r, c), DarkAt(buffer, origin.X + (4 + c + 0.5) * Module, origin.Y + (4 + r + 0.5) * Module));
                Assert.Equal((qr.Columns + 8) * Module, bounds.Width);
            }
        }

        [Fact]
        public void DataMatrixModulesLandWhereTheSymbolSaysTheyAre()
        {
            var dm = Barcode.CreateDataMatrix("PeachPDF 2026");
            var (canvas, _) = Draw(dm, new PaintPoint(8, 8));
            using (canvas)
            {
                var buffer = canvas.ToPixelBuffer();
                for (int r = 0; r < dm.Rows; r++)
                    for (int c = 0; c < dm.Columns; c++)
                        Assert.Equal(dm.IsDark(r, c), DarkAt(buffer, 8 + (1 + c + 0.5) * Module, 8 + (1 + r + 0.5) * Module));
            }
        }

        [Theory]
        [InlineData(BarcodeSymbology.Code128)]
        [InlineData(BarcodeSymbology.Ean13)]
        [InlineData(BarcodeSymbology.Ean8)]
        [InlineData(BarcodeSymbology.UpcA)]
        public void LinearBarsLandWhereTheSymbolSaysTheyAre(BarcodeSymbology symbology)
        {
            var options = new BarcodeOptions { ShowText = false };
            var code = symbology switch
            {
                BarcodeSymbology.Ean13 => Barcode.CreateEan13("5901234123457", options),
                BarcodeSymbology.Ean8 => Barcode.CreateEan8("96385074", options),
                BarcodeSymbology.UpcA => Barcode.CreateUpcA("036000291452", options),
                _ => Barcode.CreateCode128("PJJ123C", options),
            };
            var layout = code.CreateLayout();
            var origin = new PaintPoint(5, 5);
            var (canvas, _) = Draw(code, origin);
            using (canvas)
            {
                var buffer = canvas.ToPixelBuffer();
                double left = layout.SymbolBounds.X;
                for (int c = 0; c < code.Columns; c++)
                    Assert.Equal(code.IsDark(0, c), DarkAt(buffer, origin.X + (left + c + 0.5) * Module, origin.Y + 10 * Module));
            }
        }

        [Fact]
        public void NoBackgroundLeavesTheLightModulesTransparent()
        {
            var context = new RasterRenderContext();
            using var canvas = context.CreateCanvas(200, 200);
            canvas.DrawBarcode(Barcode.CreateQrCode("a"), new PaintPoint(0, 0), 2, PaintColor.Black);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            Assert.Equal(0, span[3]);
        }

        [Fact]
        public void AddBarcodeAddsOneClosedRectanglePerBar()
        {
            var context = new RasterRenderContext();
            using var canvas = context.CreateCanvas(100, 100);
            var path = canvas.GetGraphicsPath();
            var code = Barcode.CreateCode128("A");
            path.AddBarcode(code, new PaintPoint(0, 0), 1);
            Assert.Equal(code.CreateLayout().Bars.Count, path.GetCurveContours().Count);
        }
    }
}
