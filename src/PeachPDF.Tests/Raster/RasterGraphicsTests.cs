using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;
using System.Numerics;

namespace PeachPDF.Tests.Raster
{
    public class RasterGraphicsTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static RasterGraphics NewGraphics(int width, int height, double pixelsPerUnit = 1, double pixelsPerPoint = 1)
        {
            var surface = new RasterSurface(width, height, 0, 0, pixelsPerUnit, pixelsPerUnit);
            return new RasterGraphics(Adapter, surface, pixelsPerPoint);
        }

        private static byte[] Pixel(RasterGraphics g, int x, int y)
        {
            var row = g.Surface.Row(y);
            return row.Slice(x * 4, 4).ToArray();
        }

        private static RBrush Solid(RGraphics g, byte a, byte r, byte green, byte b) => g.GetSolidBrush(RColor.FromArgb(a, r, green, b));

        [Fact]
        public void FilledRectangle_PaintsExactlyItsPixels()
        {
            var g = NewGraphics(10, 10);

            g.DrawRectangle(Solid(g, 255, 255, 0, 0), 2, 3, 4, 2);

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(g, 2, 3));
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(g, 5, 4));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(g, 1, 3));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(g, 6, 3));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(g, 2, 5));
        }

        [Fact]
        public void SemiTransparentFill_ProducesPremultipliedPixels()
        {
            var g = NewGraphics(4, 4);

            g.DrawRectangle(Solid(g, 128, 255, 0, 0), 0, 0, 4, 4);

            var p = Pixel(g, 1, 1);
            Assert.Equal(128, p[3]);
            Assert.InRange(p[0], 127, 129);
            Assert.Equal(0, p[1]);
        }

        [Fact]
        public void SourceOver_OfTwoHalfTransparentFills_AccumulatesAlpha()
        {
            var g = NewGraphics(2, 2);

            g.DrawRectangle(Solid(g, 128, 0, 0, 255), 0, 0, 2, 2);
            g.DrawRectangle(Solid(g, 128, 0, 0, 255), 0, 0, 2, 2);

            Assert.InRange(Pixel(g, 0, 0)[3], 190, 194);
        }

        [Fact]
        public void LayoutUnits_AreDividedByPixelsPerPoint_ThenScaledToPixels()
        {
            // 96 layout units per inch (ppp 96/72) painted at 288 dpi: 3 pixels per layout unit.
            using var scope = RasterSurfaceFactory.Create(Adapter, 96.0 / 72.0, new RRect(0, 0, 10, 10), 288, RasterGraphics.MaxTilePixels);

            Assert.NotNull(scope);
            Assert.Equal(30, scope.Surface.Width);
            Assert.Equal(30, scope.Surface.Height);

            scope.Graphics.DrawRectangle(Solid(scope.Graphics, 255, 0, 255, 0), 1, 1, 2, 2);

            Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(scope.Graphics, 3, 3));
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(scope.Graphics, 8, 8));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(scope.Graphics, 2, 3));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(scope.Graphics, 9, 3));
        }

        [Theory]
        [InlineData(72.0, 72.0, 300.0)]
        [InlineData(96.0, 96.0, 288.0)]
        [InlineData(150.0, 96.0, 600.0)]
        [InlineData(72.0, 200.0, 72.0)]
        public void Surface_IsExactlyOneInchAtTheRequestedDpi(double pixelsPerInch, double inchInLayoutUnits, double dpi)
        {
            var pixelsPerPoint = pixelsPerInch / 72.0;
            var bounds = new RRect(0, 0, inchInLayoutUnits, inchInLayoutUnits);

            using var scope = RasterSurfaceFactory.Create(Adapter, pixelsPerPoint, bounds, dpi, long.MaxValue);

            Assert.NotNull(scope);
            var layout = scope.Surface.LayoutRect;

            // The bitmap is placed at its own snapped rectangle; that rectangle is one inch wide in points
            // (within one pixel) and holds exactly dpi pixels per inch of it.
            var placedInches = layout.Width / pixelsPerPoint / 72.0;
            Assert.InRange(scope.Surface.Width / placedInches, dpi * 0.999, dpi * 1.001);
            Assert.InRange(layout.Width, bounds.Width, bounds.Width + 72.0 * pixelsPerPoint / dpi + 1e-6);
        }

        [Fact]
        public void SnappedSurface_CoversTheRequestedBounds_AndAdjacentSurfacesAbut()
        {
            const double ppp = 1;
            var left = RasterSurfaceFactory.Create(Adapter, ppp, new RRect(0.3, 0.3, 10.2, 5), 100, long.MaxValue)!;
            var right = RasterSurfaceFactory.Create(Adapter, ppp, new RRect(10.5, 0.3, 4, 5), 100, long.MaxValue)!;

            Assert.True(left.Surface.LayoutRect.Left <= 0.3 + 1e-9);
            Assert.True(left.Surface.LayoutRect.Right >= 10.5 - 1e-9);

            // Same grid: a shared pixel column means identical layout edges.
            var leftRight = left.Surface.LayoutRect.Right;
            var rightLeft = right.Surface.LayoutRect.Left;
            Assert.True(rightLeft <= 10.5 + 1e-9 && leftRight >= 10.5 - 1e-9);
            var pitch = 72.0 / 100.0;
            Assert.Equal(0, ((rightLeft / pitch) % 1 + 1) % 1, 6);
            Assert.Equal(0, ((leftRight / pitch) % 1 + 1) % 1, 6);
        }

        [Fact]
        public void OversizedRegion_LowersTheResolutionButKeepsThePlacedSize()
        {
            var bounds = new RRect(0, 0, 720, 720);

            using var scope = RasterSurfaceFactory.Create(Adapter, 1, bounds, 1200, 1_000_000);

            Assert.NotNull(scope);
            Assert.True((long)scope.Surface.Width * scope.Surface.Height <= 1_000_000);
            Assert.InRange(scope.Surface.LayoutRect.Width, 720, 730);
        }

        [Fact]
        public void EmptyOrInvalidBounds_YieldNoSurface()
        {
            Assert.Null(RasterSurfaceFactory.Create(Adapter, 1, new RRect(0, 0, 0, 10), 300, long.MaxValue));
            Assert.Null(RasterSurfaceFactory.Create(Adapter, 1, new RRect(0, 0, double.NaN, 10), 300, long.MaxValue));
            Assert.Null(RasterSurfaceFactory.Create(Adapter, 1, new RRect(0, 0, 10, 10), 0, long.MaxValue));
        }

        [Fact]
        public void PushedClip_LimitsWhatIsPainted_AndPopRestoresIt()
        {
            var g = NewGraphics(10, 10);
            var red = Solid(g, 255, 255, 0, 0);

            g.PushClip(new RRect(2, 2, 3, 3));
            g.DrawRectangle(red, 0, 0, 10, 10);
            g.PopClip();

            Assert.Equal(255, Pixel(g, 3, 3)[3]);
            Assert.Equal(0, Pixel(g, 1, 3)[3]);
            Assert.Equal(0, Pixel(g, 5, 5)[3]);

            g.DrawRectangle(red, 0, 9, 10, 1);
            Assert.Equal(255, Pixel(g, 0, 9)[3]);
        }

        [Fact]
        public void PathClip_MasksToItsShape()
        {
            var g = NewGraphics(10, 10);
            var path = g.GetGraphicsPath();
            path.Start(0, 0);
            path.LineTo(10, 0);
            path.LineTo(0, 10);
            path.CloseFigure();

            g.PushClip(path);
            g.DrawRectangle(Solid(g, 255, 0, 0, 255), 0, 0, 10, 10);
            g.PopClip();

            Assert.Equal(255, Pixel(g, 1, 1)[3]);
            Assert.Equal(0, Pixel(g, 9, 9)[3]);
        }

        [Fact]
        public void PushTransform_TranslatesSubsequentDrawing()
        {
            var g = NewGraphics(10, 10);

            g.PushTransform(new RMatrix(1, 0, 0, 1, 4, 5));
            g.DrawRectangle(Solid(g, 255, 255, 0, 0), 0, 0, 2, 2);
            g.PopTransform();

            Assert.Equal(255, Pixel(g, 4, 5)[3]);
            Assert.Equal(255, Pixel(g, 5, 6)[3]);
            Assert.Equal(0, Pixel(g, 0, 0)[3]);
        }

        [Fact]
        public void PushTransform_DividesOnlyTheTranslationByPixelsPerPoint()
        {
            // ppp 2: a translation of 8 layout units is 4 points; a 2x scale stays 2x.
            var g = NewGraphics(20, 20, pixelsPerUnit: 1, pixelsPerPoint: 2);

            g.PushTransform(new RMatrix(2, 0, 0, 2, 8, 0));
            g.DrawRectangle(Solid(g, 255, 255, 0, 0), 0, 0, 2, 2);
            g.PopTransform();

            // rect 1 point square, scaled 2x = 2 points = 4 layout units = 4 pixels, starting at 4 points = 8 pixels.
            Assert.Equal(255, Pixel(g, 8, 0)[3]);
            Assert.Equal(255, Pixel(g, 11, 3)[3]);
            Assert.Equal(0, Pixel(g, 12, 0)[3]);
            Assert.Equal(0, Pixel(g, 7, 0)[3]);
        }

        [Fact]
        public void StrokedLine_UsesThePenWidthInUserSpace()
        {
            var g = NewGraphics(10, 10);
            var pen = g.GetPen(RColor.FromArgb(255, 0, 0, 0));
            pen.Width = 2;

            g.DrawLine(pen, 1, 5, 9, 5);

            Assert.Equal(255, Pixel(g, 4, 4)[3]);
            Assert.Equal(255, Pixel(g, 4, 5)[3]);
            Assert.Equal(0, Pixel(g, 4, 3)[3]);
            Assert.Equal(0, Pixel(g, 4, 6)[3]);
        }

        [Fact]
        public void LinearGradient_InterpolatesBetweenItsStops()
        {
            var g = NewGraphics(10, 1);
            var brush = g.GetLinearGradientBrush(new RPoint(0, 0), new RPoint(10, 0),
                [(RColor.FromArgb(255, 255, 0, 0), 0.0), (RColor.FromArgb(255, 0, 0, 255), 1.0)]);

            g.DrawRectangle(brush, 0, 0, 10, 1);

            var first = Pixel(g, 0, 0);
            var middle = Pixel(g, 5, 0);
            var last = Pixel(g, 9, 0);
            Assert.True(first[0] > 230 && first[2] < 25);
            Assert.InRange(middle[0], 100, 150);
            Assert.InRange(middle[2], 100, 150);
            Assert.True(last[2] > 230 && last[0] < 25);
        }

        [Fact]
        public void RadialGradient_IsCentreColourAtTheCentreAndEdgeColourAtTheRadius()
        {
            var g = NewGraphics(20, 20);
            var brush = g.GetRadialGradientBrush(new RPoint(10, 10), 10, 10,
                [(RColor.FromArgb(255, 255, 255, 255), 0.0), (RColor.FromArgb(255, 0, 0, 0), 1.0)]);

            g.DrawRectangle(brush, 0, 0, 20, 20);

            // Pixel centres sit half a pixel off the exact centre/edge, hence the margins.
            Assert.True(Pixel(g, 10, 10)[0] > 220);
            Assert.True(Pixel(g, 0, 10)[0] < 40);
        }

        [Fact]
        public void ConicGradient_StartsAtTwelveOClockAndGoesClockwise()
        {
            var g = NewGraphics(20, 20);
            var brush = g.GetConicGradientBrush(new RPoint(10, 10), 10,
                [RColor.FromArgb(255, 255, 0, 0), RColor.FromArgb(255, 0, 0, 255)], [0, Math.PI * 2]);

            g.DrawRectangle(brush, 0, 0, 20, 20);

            // Just right of 12 o'clock is still near the start colour; just left of it is near the end colour.
            Assert.True(Pixel(g, 11, 2)[0] > 200);
            Assert.True(Pixel(g, 9, 2)[2] > 200);
        }

        [Fact]
        public void ConicGradient_WithAStartAngle_WrapsTheEarlierAnglesToTheEndOfTheTurn()
        {
            // conic-gradient(from 20deg, red, blue): red starts at 20 degrees and blue is reached at 380 degrees.
            var start = 20 * Math.PI / 180;
            var g = NewGraphics(40, 40);
            var brush = g.GetConicGradientBrush(new RPoint(20, 20), 20,
                [RColor.FromArgb(255, 255, 0, 0), RColor.FromArgb(255, 0, 0, 255)], [start, start + 2 * Math.PI]);

            g.DrawRectangle(brush, 0, 0, 40, 40);

            // Just clockwise of the start (30 degrees): still nearly red. Just counter-clockwise of it, at 10 degrees
            // (which is 370 degrees into the turn): nearly blue - not padded red, which would hide the seam at 0.
            var thirty = Pixel(g, 20 + (int)Math.Round(15 * Math.Sin(30 * Math.PI / 180)), 20 - (int)Math.Round(15 * Math.Cos(30 * Math.PI / 180)));
            var ten = Pixel(g, 20 + (int)Math.Round(15 * Math.Sin(10 * Math.PI / 180)), 20 - (int)Math.Round(15 * Math.Cos(10 * Math.PI / 180)));
            Assert.True(thirty[0] > 200 && thirty[2] < 60);
            Assert.True(ten[2] > 200 && ten[0] < 60);
        }

        [Fact]
        public void MultiplyBlendMode_DarkensAgainstTheBackdrop()
        {
            var g = NewGraphics(2, 2);
            g.DrawRectangle(Solid(g, 255, 255, 255, 0), 0, 0, 2, 2);

            g.PushBlendMode(RBlendMode.Multiply);
            g.DrawRectangle(Solid(g, 255, 0, 255, 255), 0, 0, 2, 2);
            g.PopBlendMode();

            // yellow * cyan = green
            var p = Pixel(g, 0, 0);
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, p);
        }

        [Fact]
        public void Tile_CompositedWithOpacity_ScalesItsAlpha()
        {
            var g = NewGraphics(10, 10);
            var (tileGraphics, tileImage) = g.CreateTile(10, 10)!.Value;
            tileGraphics.DrawRectangle(Solid(tileGraphics, 255, 255, 0, 0), 0, 0, 10, 10);

            g.DrawImageWithOpacity(tileImage, new RRect(0, 0, 10, 10), 0.5);

            var p = Pixel(g, 5, 5);
            Assert.InRange(p[3], 126, 129);
            Assert.InRange(p[0], 126, 129);
        }

        [Fact]
        public void Tile_DrawnAtHalfSizeInASmallerRect_IsResampledNotClipped()
        {
            var g = NewGraphics(20, 20);
            var (tileGraphics, tileImage) = g.CreateTile(10, 10)!.Value;
            tileGraphics.DrawRectangle(Solid(tileGraphics, 255, 0, 0, 255), 0, 0, 10, 10);

            g.DrawImage(tileImage, new RRect(5, 5, 5, 5));

            Assert.Equal(255, Pixel(g, 7, 7)[3]);
            Assert.Equal(0, Pixel(g, 12, 12)[3]);
            Assert.Equal(0, Pixel(g, 3, 3)[3]);
        }

        [Fact]
        public void ColorMatrix_Grayscale_MixesChannels_ThePdfBackendCannotDo()
        {
            var g = NewGraphics(4, 4);
            var (tileGraphics, tileImage) = g.CreateTile(4, 4)!.Value;
            tileGraphics.DrawRectangle(Solid(tileGraphics, 255, 255, 0, 0), 0, 0, 4, 4);

            // Filter Effects 1 grayscale(1), transposed into ColorMatrix's row-vector convention.
            var matrix = new ColorMatrix(new Matrix4x4(
                0.2126f, 0.2126f, 0.2126f, 0,
                0.7152f, 0.7152f, 0.7152f, 0,
                0.0722f, 0.0722f, 0.0722f, 0,
                0, 0, 0, 1), Vector4.Zero);
            g.DrawImageWithColorMatrix(tileImage, new RRect(0, 0, 4, 4), matrix);

            var p = Pixel(g, 1, 1);
            Assert.Equal(p[0], p[1]);
            Assert.Equal(p[1], p[2]);
            Assert.InRange(p[0], 53, 55);
            Assert.Equal(255, p[3]);
        }

        [Fact]
        public void LuminosityMask_HidesWhereTheMaskIsBlack()
        {
            var g = NewGraphics(10, 10);
            var (content, contentImage) = g.CreateTile(10, 10)!.Value;
            content.DrawRectangle(Solid(content, 255, 255, 0, 0), 0, 0, 10, 10);
            var (mask, maskImage) = g.CreateTile(10, 10)!.Value;
            mask.DrawRectangle(Solid(mask, 255, 255, 255, 255), 0, 0, 5, 10);

            g.DrawImageMasked(contentImage, maskImage, new RRect(0, 0, 10, 10));

            Assert.Equal(255, Pixel(g, 2, 5)[3]);
            Assert.Equal(0, Pixel(g, 8, 5)[3]);
        }

        [Fact]
        public void AlphaMask_KeepsWhereTheMaskIsOpaque_AndInvertFlipsIt()
        {
            var g = NewGraphics(10, 10);
            var (content, contentImage) = g.CreateTile(10, 10)!.Value;
            content.DrawRectangle(Solid(content, 255, 0, 255, 0), 0, 0, 10, 10);
            var (mask, maskImage) = g.CreateTile(10, 10)!.Value;
            mask.DrawRectangle(Solid(mask, 255, 0, 0, 0), 0, 0, 5, 10);

            g.DrawImageAlphaMasked(contentImage, maskImage, new RRect(0, 0, 10, 10));
            Assert.Equal(255, Pixel(g, 2, 5)[3]);
            Assert.Equal(0, Pixel(g, 8, 5)[3]);

            var inverted = NewGraphics(10, 10);
            inverted.DrawImageAlphaMasked(contentImage, maskImage, new RRect(0, 0, 10, 10), invert: true);
            Assert.Equal(0, Pixel(inverted, 2, 5)[3]);
            Assert.Equal(255, Pixel(inverted, 8, 5)[3]);
        }

        [Fact]
        public void TileCreation_RefusesAbsurdSizes()
        {
            var g = NewGraphics(4, 4);

            Assert.Null(g.CreateTile(0, 10));
            Assert.Null(g.CreateTile(10, -1));
            Assert.Null(g.CreateTile(1e9, 1e9));
        }

        [Fact]
        public void EvenOddPathFill_LeavesAHole()
        {
            var g = NewGraphics(10, 10);
            var path = g.GetGraphicsPath();
            path.FillMode = RFillMode.EvenOdd;
            path.Start(0, 0);
            path.LineTo(10, 0);
            path.LineTo(10, 10);
            path.LineTo(0, 10);
            path.CloseFigure();
            path.AddMove(3, 3);
            path.LineTo(7, 3);
            path.LineTo(7, 7);
            path.LineTo(3, 7);
            path.CloseFigure();

            g.DrawPath(Solid(g, 255, 0, 0, 0), path);

            Assert.Equal(255, Pixel(g, 1, 1)[3]);
            Assert.Equal(0, Pixel(g, 5, 5)[3]);
        }
    }
}
