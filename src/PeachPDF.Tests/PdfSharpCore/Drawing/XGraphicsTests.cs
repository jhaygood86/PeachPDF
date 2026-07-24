using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;
using StbImageWriteSharp;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XGraphicsTests
    {
        static byte[] MakePngBytes(int width = 4, int height = 4)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 10; pixels[i + 1] = 20; pixels[i + 2] = 30; pixels[i + 3] = 255;
            }
            using var ms = new MemoryStream();
            new ImageWriter().WritePng(pixels, width, height, ColorComponents.RedGreenBlueAlpha, ms);
            return ms.ToArray();
        }

        static (PdfDocument Document, PdfPage Page) NewPage()
        {
            var document = new PdfDocument();
            var page = document.AddPage();
            return (document, page);
        }

        [Fact]
        public void FromPdfPage_NullPage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => XGraphics.FromPdfPage(null!));
        }

        [Fact]
        public void FromPdfPage_SecondGraphicsForSamePage_Throws()
        {
            var (_, page) = NewPage();
            using var gfx = XGraphics.FromPdfPage(page);

            Assert.Throws<InvalidOperationException>(() => XGraphics.FromPdfPage(page));
        }

        [Fact]
        public void FromPdfPage_WithReplaceOption_Succeeds()
        {
            var (_, page) = NewPage();

            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Replace);

            Assert.NotNull(gfx);
        }

        [Fact]
        public void FromPdfPage_WithPrependOption_Succeeds()
        {
            var (_, page) = NewPage();

            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Prepend);

            Assert.NotNull(gfx);
        }

        [Fact]
        public void FromPdfPage_WithPageDirection_Succeeds()
        {
            var (_, page) = NewPage();

            using var gfx = XGraphics.FromPdfPage(page, XPageDirection.Downwards);

            Assert.NotNull(gfx);
        }

        [Fact]
        public void FromImage_IsUnimplementedStub_AlwaysReturnsNull()
        {
            // XGraphics.FromImage(XImage, XGraphicsUnit) has an empty `if (bmImage != null) { }`
            // body and unconditionally `return null;` -- it is an unimplemented stub regardless of
            // the image passed in. Real, pre-existing behavior; documented here rather than fixed.
            var bytes = MakePngBytes();
            using var image = XImage.FromStream(() => new MemoryStream(bytes));

            var gfx = XGraphics.FromImage(image);

            Assert.Null(gfx);
        }

        [Fact]
        public void CreateMeasureContext_CreatesUsableGraphics()
        {
            using var gfx = XGraphics.CreateMeasureContext(new XSize(200, 200), XGraphicsUnit.Point, XPageDirection.Downwards);

            var font = new XFont("Times New Roman", 12, XFontStyle.Regular, new FontResolver());
            var size = gfx.MeasureString("Hello", font);

            Assert.True(size.Width > 0);
            Assert.True(size.Height > 0);
        }

        [Fact]
        public void MeasureString_EmptyString_ReturnsNonNegativeSize()
        {
            using var gfx = XGraphics.CreateMeasureContext(new XSize(200, 200), XGraphicsUnit.Point, XPageDirection.Downwards);
            var font = new XFont("Times New Roman", 12, XFontStyle.Regular, new FontResolver());

            var size = gfx.MeasureString("", font);

            Assert.True(size.Width >= 0);
        }

        [Fact]
        public void Save_Restore_RoundTripsGraphicsState()
        {
            var (document, page) = NewPage();
            using var gfx = XGraphics.FromPdfPage(page);

            var state = gfx.Save();
            gfx.RotateTransform(45);
            gfx.Restore(state);

            using var stream = new MemoryStream();
            gfx.Dispose();
            document.Save(stream);
            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void Restore_WithoutPriorSave_Throws()
        {
            var (_, page) = NewPage();
            using var gfx = XGraphics.FromPdfPage(page);

            Assert.Throws<InvalidOperationException>(() => gfx.Restore());
        }

        [Fact]
        public void BeginContainer_EndContainer_Succeeds()
        {
            var (document, page) = NewPage();
            using var gfx = XGraphics.FromPdfPage(page);

            var container = gfx.BeginContainer();
            gfx.DrawLine(XPens.Black, 0, 0, 10, 10);
            gfx.EndContainer(container);

            using var stream = new MemoryStream();
            gfx.Dispose();
            document.Save(stream);
            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void WriteComment_DoesNotThrow()
        {
            var (_, page) = NewPage();
            using var gfx = XGraphics.FromPdfPage(page);

            var ex = Record.Exception(() => gfx.WriteComment("a test comment"));

            Assert.Null(ex);
        }

        [Fact]
        public void DrawEverything_ProducesNonEmptySavedDocument()
        {
            var (document, page) = NewPage();
            var width = page.Width.Point;
            var height = page.Height.Point;

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var pen = new XPen(XColors.Red, 1.5);
                var brush = XBrushes.Blue;
                var font = new XFont("Times New Roman", 14, XFontStyle.Bold, new FontResolver());

                gfx.TranslateTransform(1, 1);
                gfx.ScaleTransform(1.0);
                gfx.ScaleTransform(1.0, 1.0);
                gfx.RotateTransform(0);
                gfx.RotateAtTransform(0, new XPoint(width / 2, height / 2));
                gfx.ShearTransform(0, 0);
                gfx.SkewAtTransform(0, 0, width / 2, height / 2);
                gfx.MultiplyTransform(PeachPDF.PdfSharpCore.Drawing.XMatrix.Identity);

                gfx.DrawLine(pen, 0, 0, width, height);
                gfx.DrawLines(pen, [new XPoint(0, 0), new XPoint(10, 10), new XPoint(20, 0)]);
                gfx.DrawBezier(pen, 0, 0, 10, 20, 20, 20, 30, 0);
                gfx.DrawBeziers(pen, [new XPoint(0, 0), new XPoint(10, 20), new XPoint(20, 20), new XPoint(30, 0)]);
                gfx.DrawCurve(pen, [new XPoint(0, 0), new XPoint(10, 10), new XPoint(20, 0)]);
                gfx.DrawArc(pen, 10, 10, 40, 40, 0, 90);

                gfx.DrawRectangle(pen, 5, 5, 20, 20);
                gfx.DrawRectangle(brush, 5, 5, 20, 20);
                gfx.DrawRectangle(pen, brush, 5, 5, 20, 20);
                gfx.DrawRectangles(pen, [new XRect(0, 0, 10, 10), new XRect(20, 20, 10, 10)]);

                gfx.DrawRoundedRectangle(pen, 5, 5, 30, 30, 5, 5);
                gfx.DrawRoundedRectangle(brush, 5, 5, 30, 30, 5, 5);
                gfx.DrawRoundedRectangle(pen, brush, 5, 5, 30, 30, 5, 5);

                gfx.DrawEllipse(pen, 5, 5, 20, 20);
                gfx.DrawEllipse(brush, 5, 5, 20, 20);
                gfx.DrawEllipse(pen, brush, 5, 5, 20, 20);

                gfx.DrawPolygon(pen, [new XPoint(0, 0), new XPoint(10, 0), new XPoint(5, 10)]);
                gfx.DrawPolygon(brush, [new XPoint(0, 0), new XPoint(10, 0), new XPoint(5, 10)], XFillMode.Winding);
                gfx.DrawPolygon(pen, brush, [new XPoint(0, 0), new XPoint(10, 0), new XPoint(5, 10)], XFillMode.Winding);

                gfx.DrawPie(pen, 5, 5, 30, 30, 0, 90);
                gfx.DrawPie(brush, 5, 5, 30, 30, 0, 90);
                gfx.DrawPie(pen, brush, 5, 5, 30, 30, 0, 90);

                gfx.DrawClosedCurve(pen, [new XPoint(0, 0), new XPoint(10, 10), new XPoint(20, 0)]);
                gfx.DrawClosedCurve(brush, [new XPoint(0, 0), new XPoint(10, 10), new XPoint(20, 0)]);
                gfx.DrawClosedCurve(pen, brush, [new XPoint(0, 0), new XPoint(10, 10), new XPoint(20, 0)]);

                var path = new XGraphicsPath();
                path.AddRectangle(new XRect(0, 0, 10, 10));
                gfx.DrawPath(pen, path);
                gfx.DrawPath(brush, path);
                gfx.DrawPath(pen, brush, path);

                gfx.DrawString("Hello, World!", font, brush, new XPoint(10, 10));
                gfx.DrawString("Hello, World!", font, brush, new XRect(0, 0, width, height), XStringFormats.Center);

                var imgBytes = MakePngBytes();
                using var image = XImage.FromStream(() => new MemoryStream(imgBytes));
                gfx.DrawImage(image, 50, 50);
                gfx.DrawImage(image, new XRect(60, 60, 20, 20));
                gfx.DrawImage(image, new XRect(80, 80, 20, 20), new XRect(0, 0, 4, 4), XGraphicsUnit.Point);

                gfx.IntersectClip(new XRect(0, 0, width, height));

                var innerPath = new XGraphicsPath();
                innerPath.AddEllipse(new XRect(0, 0, 10, 10));
                gfx.IntersectClip(innerPath);
            }

            using var stream = new MemoryStream();
            document.Save(stream);

            Assert.True(stream.Length > 0);
        }
    }
}
