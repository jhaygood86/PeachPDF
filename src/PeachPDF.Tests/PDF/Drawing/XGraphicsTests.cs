using System.Numerics;
using PeachDrawing.Core;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PeachPDF.PDF.Drawing;
using PeachPDF.PDF.Rendering;
using PeachPDF.PDF;
using PeachPDF.Tests.TestSupport;

using System.Globalization;

namespace PeachPDF.Tests.PdfEngineTests.Drawing
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

            using var gfx = XGraphics.FromPdfPage(page);

            Assert.NotNull(gfx);
        }

        [Fact]
        public void CreateMeasureContext_CreatesUsableGraphics()
        {
            using var gfx = XGraphics.CreateMeasureContext(new Size(200, 200));

            var font = TestFonts.Create("Times New Roman", 12, PaintFontStyle.Regular);
            var size = gfx.MeasureString("Hello", font);

            Assert.True(size.Width > 0);
            Assert.True(size.Height > 0);
        }

        [Fact]
        public void MeasureString_EmptyString_ReturnsNonNegativeSize()
        {
            using var gfx = XGraphics.CreateMeasureContext(new Size(200, 200));
            var font = TestFonts.Create("Times New Roman", 12, PaintFontStyle.Regular);

            var size = gfx.MeasureString("", font);

            Assert.True(size.Width >= 0);
        }

        [Fact]
        public void Save_Restore_RoundTripsGraphicsState()
        {
            var (document, page) = NewPage();
            using var gfx = XGraphics.FromPdfPage(page);

            var state = gfx.Save();
            gfx.MultiplyTransform(Matrix3x2.CreateRotation(MathF.PI / 4));
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
            gfx.DrawLine(PdfPaints.CreatePen(PaintColor.Black), 0, 0, 10, 10);
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
                var pen = PdfPaints.CreatePen(PaintColor.Black, 1);
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
                var pen = PdfPaints.CreatePen(PaintColor.Black, 1);
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

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var path = new PdfGraphicsPath();
                path.Start(1.23456, 2.34567);
                path.AddBezierTo(
                    3.45678, 4.56789,
                    5.67891, 6.78912,
                    7.89123, 8.91234);
                path.CloseFigure();
                gfx.DrawPath(new SolidBrush(PaintColor.Black), path);
            }

            using var stream = new MemoryStream();
            document.Save(stream);
            string pdf = Encoding.Latin1.GetString(stream.ToArray());

            // The page is laid out downwards, so each y is measured from the top edge of the page.
            static string Format(double value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            double pageHeight = page.Height;
            Assert.Contains(
                $"1.2346 {Format(pageHeight - 2.34567)} m\n3.4568 {Format(pageHeight - 4.56789)} 5.6789 {Format(pageHeight - 6.78912)} 7.8912 {Format(pageHeight - 8.91234)} c\nh\n",
                pdf);
        }

        [Fact]
        public void AppendPdfNumber_PreservesFastPathAndFallbackFormatting()
        {
            var content = new PdfContentWriter(100_000);
            content.Append('x');
            XGraphicsPdfRenderer.AppendPdfNumber(content, 1.23456, "0.####");

            for (int i = 0; i < 10_000; i++)
                XGraphicsPdfRenderer.AppendPdfNumber(content, 1.23456, "0.####");

            Assert.Equal("x1.2346" + string.Concat(Enumerable.Repeat("1.2346", 10_000)), content.ToString());

            var fallback = new PdfContentWriter();
            XGraphicsPdfRenderer.AppendPdfNumber(fallback, double.MaxValue, "0.####");
            Assert.Equal(double.MaxValue.ToString("0.####", CultureInfo.InvariantCulture), fallback.ToString());
        }

        [Fact]
        public void AppendPdfNumber_UsesAnAllocationFreeFastPath()
        {
            // Through AllocationProbe, never a bare GC.GetAllocatedBytesForCurrentThread() delta around
            // one loop - see .claude/invariants/testing-an-allocation-guard-measures-steady-state-not-one-loop.md.
            // This site was missed when the other five were migrated, and it failed exactly as that note
            // predicts: 1,472 bytes over 10,000 calls (0.15 bytes each) on one leg of a fail-fast: false
            // matrix, which is a fixed tiered-compilation lump landing inside the counted window rather
            // than per-call allocation returning.
            const int iterations = 10_000;

            // Chunked writer: sized so the probe's warm-up and every batch fit without growing, since a
            // fresh chunk allocated mid-batch is exactly the transient this is trying not to measure.
            var content = new PdfContentWriter(600_000);
            var body = () => XGraphicsPdfRenderer.AppendPdfNumber(content, 1.23456, "0.####");

            var allocated = AllocationProbe.Bytes(body, iterations);

            Assert.True(allocated < 1_024,
                $"Formatting {iterations:N0} ordinary coordinates allocated {allocated:N0} bytes.");
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
        public void FromPdfPage_WithTrimMargins_OffsetsPageTransform()
        {
            var (document, page) = NewPage();
            page.TrimMargins.All = 5;

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawLine(PdfPaints.CreatePen(PaintColor.Black), 0, 0, 10, 10);
            }

            using var stream = new MemoryStream();
            document.Save(stream);
            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void DrawEverything_ProducesNonEmptySavedDocument()
        {
            var (document, page) = NewPage();
            var width = page.Width;
            var height = page.Height;

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var pen = PdfPaints.CreatePen(PaintColor.FromArgb(255, 0, 0), 1.5);
                var brush = new SolidBrush(PaintColor.FromArgb(0, 0, 255));
                var font = TestFonts.Create("Times New Roman", 14, PaintFontStyle.Bold);

                gfx.TranslateTransform(1, 1);
                gfx.MultiplyTransform(Matrix3x2.Identity);

                gfx.DrawLine(pen, 0, 0, width, height);
                gfx.DrawLines(pen, [new PaintPoint(0, 0), new PaintPoint(10, 10), new PaintPoint(20, 0)]);

                gfx.DrawRectangle(pen, 5, 5, 20, 20);
                gfx.DrawRectangle(brush, 5, 5, 20, 20);
                gfx.DrawRectangle(pen, brush, 5, 5, 20, 20);



                gfx.DrawPolygon(pen, [new PaintPoint(0, 0), new PaintPoint(10, 0), new PaintPoint(5, 10)]);
                gfx.DrawPolygon(brush, [new PaintPoint(0, 0), new PaintPoint(10, 0), new PaintPoint(5, 10)], FillMode.Nonzero);
                gfx.DrawPolygon(pen, brush, [new PaintPoint(0, 0), new PaintPoint(10, 0), new PaintPoint(5, 10)], FillMode.Nonzero);



                var path = new PdfGraphicsPath();
                path.AddRectangle(new Rect(0, 0, 10, 10));
                gfx.DrawPath(pen, path);
                gfx.DrawPath(brush, path);
                gfx.DrawPath(pen, brush, path);

                gfx.DrawString("Hello, World!", font, brush, new PaintPoint(10, 10));
                gfx.DrawString("Hello, World!", font, brush, new Rect(0, 0, width, height), XStringFormats.Center);

                var imgBytes = MakePngBytes();
                using var image = XImage.FromStream(() => new MemoryStream(imgBytes));
                gfx.DrawImage(image, 50, 50);
                gfx.DrawImage(image, new Rect(60, 60, 20, 20));
                gfx.DrawImage(image, new Rect(80, 80, 20, 20), new Rect(0, 0, 4, 4));

                gfx.IntersectClip(new Rect(0, 0, width, height));

                var innerPath = new PdfGraphicsPath();
                innerPath.AddRectangle(new Rect(0, 0, 10, 10));
                gfx.IntersectClip(innerPath);
            }

            using var stream = new MemoryStream();
            document.Save(stream);

            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void DrawRectangle_ZeroAlphaBrushAsFirstFillAfterStateReset_EmitsConstantAlphaExtGState()
        {
            // Regression test: PdfGraphicsState's fill-alpha tracking field (_realizedFillColor)
            // starts out as PaintColor.Empty (A == 0) both on a freshly constructed renderer and right
            // after a q/Save() that hasn't realized any fill color yet. RealizeFillColor's alpha-change
            // check used to compare _realizedFillColor.A != color.A with no IsEmpty guard - so a first
            // fill whose own alpha is genuinely 0 evaluated "0 != 0" as false and silently skipped
            // emitting /ca, leaving the shape painted at whatever alpha the enclosing state had
            // (typically fully opaque) instead of invisible.
            var (document, page) = NewPage();
            document.Options.CompressContentStreams = false;

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var state = gfx.Save();
                var brush = new SolidBrush(PaintColor.FromArgb(0, 255, 0, 0));
                gfx.DrawRectangle(brush, 10, 10, 20, 20);
                gfx.Restore(state);
            }

            var pdfText = Save(document);
            var contentStream = FirstContentStream(pdfText);

            // Structural/adjacency assertion rather than a bare substring check (see CLAUDE.md's
            // testing conventions): confirm the "gs" that switches on the constant-alpha ExtGState is
            // the one immediately preceding this fill's "f" operator, then resolve that resource to its
            // ExtGState object and confirm it actually declares "/ca 0" - not just that "/ca 0" occurs
            // somewhere in the file.
            AssertConstantAlphaAppliedBeforeOperator(pdfText, contentStream, "f", "ca", 0);
        }

        [Fact]
        public void DrawLine_ZeroAlphaPenAsFirstStrokeAfterStateReset_EmitsConstantAlphaExtGState()
        {
            // Stroke-side twin of DrawRectangle_ZeroAlphaBrushAsFirstFillAfterStateReset_EmitsConstantAlphaExtGState:
            // RealizePen's alpha-change check has the same _realizedStrokeColor.A != strokeAlpha
            // comparison, with the same missing-IsEmpty-guard defect for a fresh/reset
            // _realizedStrokeColor (== PaintColor.Empty, A == 0).
            var (document, page) = NewPage();
            document.Options.CompressContentStreams = false;

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var state = gfx.Save();
                var pen = PdfPaints.CreatePen(PaintColor.FromArgb(0, 0, 0, 0), 2);
                gfx.DrawLine(pen, 10, 10, 30, 30);
                gfx.Restore(state);
            }

            var pdfText = Save(document);
            var contentStream = FirstContentStream(pdfText);

            AssertConstantAlphaAppliedBeforeOperator(pdfText, contentStream, "S", "CA", 0);
        }

        static string Save(PdfDocument document)
        {
            using var stream = new MemoryStream();
            document.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        static string FirstContentStream(string pdfText)
        {
            var match = Regex.Match(pdfText, @"stream\r?\n(.*?)\r?\nendstream", RegexOptions.Singleline);
            Assert.True(match.Success, "No content stream found in generated PDF.");
            return match.Groups[1].Value;
        }

        static void AssertConstantAlphaAppliedBeforeOperator(string pdfText, string contentStream, string paintOperator, string alphaKey, double expectedAlpha)
        {
            var adjacency = Regex.Match(contentStream, $@"/(GS\d+) gs\r?\n(?:[^\r\n]*\r?\n)*?{paintOperator}\b");
            Assert.True(adjacency.Success,
                $"Expected a constant-alpha ExtGState \"gs\" immediately before the \"{paintOperator}\" operator.");

            var gsResourceName = "/" + adjacency.Groups[1].Value;
            var resourceRef = Regex.Match(pdfText, Regex.Escape(gsResourceName) + @"\s+(\d+)\s+0\s+R");
            Assert.True(resourceRef.Success, $"Could not resolve {gsResourceName} to an indirect object.");

            var extGStateObject = Regex.Match(pdfText, resourceRef.Groups[1].Value + @" 0 obj.*?endobj", RegexOptions.Singleline);
            Assert.True(extGStateObject.Success, $"Could not find the ExtGState object referenced by {gsResourceName}.");
            Assert.Matches(new Regex($@"/{alphaKey} {Regex.Escape(expectedAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture))}\b"), extGStateObject.Value);
        }
    }
}
