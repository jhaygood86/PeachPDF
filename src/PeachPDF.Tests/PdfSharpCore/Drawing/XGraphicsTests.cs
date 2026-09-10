using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Drawing.Pdf;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;

using PeachPDF.Fonts;
using System.Globalization;
using System.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XGraphicsTests
    {
        static byte[] MakePngBytes(int width = 4, int height = 4) =>
            RasterPngFixture.MakeSolidRgbaPngBytes(width, height, 10, 20, 30);

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
        public void DrawArc_SweepGreaterThan90Degrees_TraversesMultipleQuadrants()
        {
            // A single-quadrant arc (<=90 degrees, e.g. a border-radius corner) takes
            // AppendPartialArc's smallAngle fast path and returns immediately. A wider sweep exercises
            // the do/while loop that walks multiple quadrants (AppendArcQuadrantSegment/NextQuadrant).
            var (document, page) = NewPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var pen = new XPen(XColors.Black, 1);
                gfx.DrawArc(pen, 10, 10, 40, 40, 30, 270);
            }

            using var stream = new MemoryStream();
            document.Save(stream);
            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void DrawArc_NegativeSweepGreaterThan90Degrees_TraversesMultipleQuadrantsCounterclockwise()
        {
            var (document, page) = NewPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var pen = new XPen(XColors.Black, 1);
                gfx.DrawArc(pen, 10, 10, 40, 40, 30, -270);
            }

            using var stream = new MemoryStream();
            document.Save(stream);
            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void DrawPath_WritesInvariantCoordinatesWithExistingPrecision()
        {
            var (document, page) = NewPage();
            document.Options.CompressContentStreams = false;

#pragma warning disable CS0618
            using (var gfx = XGraphics.FromPdfPage(page, XPageDirection.Upwards))
#pragma warning restore CS0618
            {
                var path = new XGraphicsPath();
                path.AddMove(1.23456, 2.34567);
                path.AddBezier(
                    1.23456, 2.34567,
                    3.45678, 4.56789,
                    5.67891, 6.78912,
                    7.89123, 8.91234);
                path.CloseFigure();
                gfx.DrawPath(XBrushes.Black, path);
            }

            using var stream = new MemoryStream();
            document.Save(stream);
            string pdf = Encoding.Latin1.GetString(stream.ToArray());

            Assert.Contains(
                "1.2346 -2.3457 m\n3.4568 -4.5679 5.6789 -6.7891 7.8912 -8.9123 c\nh\n",
                pdf);
        }

        [Fact]
        public void AppendPdfNumber_UsesAllocationFreeFastPathAndPreservesFallbackFormatting()
        {
            var content = new PdfContentWriter(100_000);
            content.Append('x');
            XGraphicsPdfRenderer.AppendPdfNumber(content, 1.23456, "0.####");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
                XGraphicsPdfRenderer.AppendPdfNumber(content, 1.23456, "0.####");
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal("x1.2346" + string.Concat(Enumerable.Repeat("1.2346", 10_000)), content.ToString());
            Assert.True(allocated < 1_024,
                $"Formatting 10,000 ordinary coordinates allocated {allocated:N0} bytes.");

            var fallback = new PdfContentWriter();
            XGraphicsPdfRenderer.AppendPdfNumber(fallback, double.MaxValue, "0.####");
            Assert.Equal(double.MaxValue.ToString("0.####", CultureInfo.InvariantCulture), fallback.ToString());
        }

        [Fact]
        public void AppendPdfNumber_MatchesInvariantCustomFormatting()
        {
            string[] formats = ["0.##", "0.###", "0.####", "0.#######", "0.##########"];
            var expected = new StringBuilder();
            var actual = new PdfContentWriter(128);
            var random = new Random(42);

            foreach (string format in formats)
            {
                double[] boundaryValues =
                [
                    -0.0,
                    0,
                    0.00004,
                    0.00005,
                    -0.00005,
                    1.23445,
                    1.23455,
                    double.NaN,
                    double.NegativeInfinity,
                    double.PositiveInfinity,
                ];
                foreach (double value in boundaryValues)
                    AppendExpectedAndActual(value, format);

                for (int i = 0; i < 1_000; i++)
                    AppendExpectedAndActual((random.NextDouble() - 0.5) * 1_000_000, format);
            }

            Assert.Equal(expected.ToString(), actual.ToString());

            void AppendExpectedAndActual(double value, string format)
            {
                expected.Append(value.ToString(format, CultureInfo.InvariantCulture)).Append('\n');
                XGraphicsPdfRenderer.AppendPdfNumber(actual, value, format);
                actual.Append('\n');
            }
        }

        [Fact]
        public void PdfContentWriter_PreservesRawEncodingAcrossChunksAndFormatting()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfContentWriter(0));

            var content = new PdfContentWriter(4);
            content.Append("AB").Append('\u0101').Append("CDEF".AsSpan());
            content.Append([(byte)'G', 0xFE]);
            content.AppendFormat(CultureInfo.InvariantCulture, " {0} {1:0.##}", "name", 1.234);
            content.AppendFormat(CultureInfo.InvariantCulture, "{0}{1}{2}", "X", "Y", "Z");

            Assert.Equal(
                [(byte)'A', (byte)'B', 0x01, (byte)'C', (byte)'D', (byte)'E', (byte)'F',
                    (byte)'G', 0xFE, (byte)' ', (byte)'n', (byte)'a', (byte)'m', (byte)'e',
                    (byte)' ', (byte)'1', (byte)'.', (byte)'2', (byte)'3',
                    (byte)'X', (byte)'Y', (byte)'Z'],
                content.ToArray());

            content.Clear();
            Assert.Equal(0, content.Length);
            Assert.Empty(content.ToArray());
        }

        [Fact]
        public void PresizedCoreGraphicsPath_DoesNotGrowOrCopyWhileBuilding()
        {
            var warmup = new CoreGraphicsPath(2);
            warmup.MoveTo(0, 0);
            warmup.LineTo(1, 1, false);

            var path = new CoreGraphicsPath(1_000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            path.MoveTo(0, 0);
            for (int i = 1; i < 1_000; i++)
                path.LineTo(i, i, false);
            ReadOnlySpan<XPoint> points = path.PathPointsSpan;
            ReadOnlySpan<byte> types = path.PathTypesSpan;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(1_000, points.Length);
            Assert.Equal(points.Length, types.Length);
            Assert.Equal(new XPoint(999, 999), points[^1]);
            Assert.Equal(0, allocated);
        }

        [Fact]
        public void FromPdfPage_UpwardsDirection_DrawsSuccessfully()
        {
            var (document, page) = NewPage();
            // XPageDirection.Upwards is marked obsolete ("not implemented - yagni") but the constructor
            // accepts it without validation (only the PageDirection property setter rejects it after
            // construction) - covers XGraphicsPdfRenderer.BeginPageUpwards, which is otherwise dead.
#pragma warning disable CS0618
            using (var gfx = XGraphics.FromPdfPage(page, XPageDirection.Upwards))
#pragma warning restore CS0618
            {
                gfx.DrawLine(XPens.Black, 0, 0, 10, 10);
            }

            using var stream = new MemoryStream();
            document.Save(stream);
            Assert.True(stream.Length > 0);
        }

        [Theory]
        [InlineData((int)XGraphicsUnit.Inch)]
        [InlineData((int)XGraphicsUnit.Millimeter)]
        [InlineData((int)XGraphicsUnit.Centimeter)]
        [InlineData((int)XGraphicsUnit.Presentation)]
        public void FromPdfPage_NonPointPageUnit_ScalesPageTransform(int unitValue)
        {
            var (document, page) = NewPage();
            using (var gfx = XGraphics.FromPdfPage(page, (XGraphicsUnit)unitValue))
            {
                gfx.DrawLine(XPens.Black, 0, 0, 1, 1);
            }

            using var stream = new MemoryStream();
            document.Save(stream);
            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void FromPdfPage_WithTrimMargins_OffsetsPageTransform()
        {
            var (document, page) = NewPage();
            page.TrimMargins.All = 5;

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawLine(XPens.Black, 0, 0, 10, 10);
            }

            using var stream = new MemoryStream();
            document.Save(stream);
            Assert.True(stream.Length > 0);
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
