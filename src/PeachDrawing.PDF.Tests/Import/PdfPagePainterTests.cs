using System.Text;
using PeachDrawing;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// <see cref="PdfPagePainter"/>: hand-written one-page PDFs are painted onto a <see cref="RasterCanvas"/> and the
    /// pixels read back, so each test fails if an operator were a no-op or drawn in the wrong place.
    /// </summary>
    public class PdfPagePainterTests
    {
        private static byte[] Pdf(string content, string pageExtras = "", string resources = "", string extraObjects = "", int w = 100, int h = 100)
        {
            var sb = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            void Obj(string body) { offsets.Add(sb.Length); sb.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n"); }
            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Contents 4 0 R /Resources << {resources} >> {pageExtras} >>");
            Obj($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
            if (extraObjects.Length > 0) foreach (var o in extraObjects.Split("|||")) Obj(o);
            int xref = sb.Length;
            sb.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (int o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.Latin1.GetBytes(sb.ToString());
        }

        private static (byte R, byte G, byte B, byte A) Render(byte[] pdf, int x, int y, out IReadOnlyCollection<string> unsupported, int px = 100)
        {
            var doc = PdfSourceDocument.Open(pdf);
            var ctx = new RasterRenderContext();
            using var canvas = ctx.CreateCanvas(px, px);
            unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var buf = canvas.ToPixelBuffer();
            var span = buf.PremultipliedRgba.Span;
            int i = (y * buf.Width + x) * 4;
            return (span[i], span[i + 1], span[i + 2], span[i + 3]);
        }

        private static (byte R, byte G, byte B, byte A) Px(byte[] pdf, int x, int y) => Render(pdf, x, y, out _);

        [Fact]
        public void FilledRectangle_LandsWithPdfYUpMappedToCanvasYDown()
        {
            // A red square in the PDF's bottom-left corner appears at the canvas' bottom-left.
            var pdf = Pdf("1 0 0 rg 0 0 50 50 re f");

            Assert.Equal((255, 0, 0, 255), Px(pdf, 10, 90));
            Assert.Equal(0, Px(pdf, 10, 10).A);
            Assert.Equal(0, Px(pdf, 90, 90).A);
        }

        [Fact]
        public void ClipRestrictsLaterPaint_AndQRestoresIt()
        {
            var pdf = Pdf("q 0 0 20 20 re W n 0 0 1 rg 0 0 100 100 re f Q 0 1 0 rg 60 60 20 20 re f");

            Assert.Equal((0, 0, 255, 255), Px(pdf, 10, 90));   // inside the clip
            Assert.Equal(0, Px(pdf, 50, 50).A);                // blue was clipped away
            Assert.Equal((0, 255, 0, 255), Px(pdf, 70, 30));   // after Q, unclipped again
        }

        [Fact]
        public void CmTranslatesAndScalesLaterDrawing()
        {
            var pdf = Pdf("2 0 0 2 10 10 cm 1 0 0 rg 0 0 10 10 re f");

            Assert.Equal((255, 0, 0, 255), Px(pdf, 20, 70));   // x 10..30, y(pdf) 10..30 -> canvas y 70..90
            Assert.Equal(0, Px(pdf, 5, 95).A);
        }

        [Fact]
        public void EvenOddFill_LeavesTheHoleOpen_NonzeroFillsIt()
        {
            string rects = "0 0 100 100 re 25 25 50 50 re";
            Assert.Equal(0, Px(Pdf($"0 g {rects} f*"), 50, 50).A);
            Assert.Equal(255, Px(Pdf($"0 g {rects} f"), 50, 50).A);
        }

        [Fact]
        public void StrokeUsesTheStrokeColourAndWidth()
        {
            var pdf = Pdf("0 1 0 RG 10 w 0 50 m 100 50 l S");

            Assert.Equal((0, 255, 0, 255), Px(pdf, 50, 50));
            Assert.Equal(0, Px(pdf, 50, 30).A);
        }

        [Fact]
        public void CmykAndIndexedColours_ConvertToRgb()
        {
            // Pure cyan, then an Indexed palette entry (palette: black, red).
            var cyan = Px(Pdf("1 0 0 0 k 0 0 100 100 re f"), 50, 50);
            Assert.True(cyan.B > 150 && cyan.R < 100, $"cyan was {cyan}");

            var indexed = Pdf("/CS0 cs 1 sc 0 0 100 100 re f", resources: "/ColorSpace << /CS0 [/Indexed /DeviceRGB 1 <000000FF0000>] >>");
            Assert.Equal((255, 0, 0, 255), Px(indexed, 50, 50));
        }

        [Fact]
        public void FillAlpha_FromAnExtGState_BlendsTheColour()
        {
            var pdf = Pdf("/G0 gs 0 g 0 0 100 100 re f", resources: "/ExtGState << /G0 << /ca 0.5 >> >>");

            var p = Px(pdf, 50, 50);
            Assert.InRange(p.A, 120, 135);
        }

        [Fact]
        public void FormXObject_IsDrawnWithItsMatrixAndClippedToItsBBox()
        {
            string form = "<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Matrix [1 0 0 1 20 20] /Length 24 >>\nstream\n1 0 0 rg 0 0 100 100 re f\nendstream";
            var pdf = Pdf("/F0 Do", resources: "/XObject << /F0 5 0 R >>", extraObjects: form);

            Assert.Equal((255, 0, 0, 255), Px(pdf, 25, 75));   // inside bbox, after the matrix shift
            Assert.Equal(0, Px(pdf, 50, 50).A);                // outside the bbox: clipped
        }

        [Fact]
        public void Rotate90_TurnsThePageClockwise()
        {
            // Red square at the PDF's bottom-left; after a 90 degree clockwise rotation it is at the top-left.
            var pdf = Pdf("1 0 0 rg 0 0 40 40 re f", pageExtras: "/Rotate 90");

            Assert.Equal((255, 0, 0, 255), Px(pdf, 10, 10));
            Assert.Equal(0, Px(pdf, 10, 90).A);
        }

        [Fact]
        public void UnsupportedOperators_AreReportedNotThrown()
        {
            Render(Pdf("/Sh0 sh BT /F1 12 Tf (hi) Tj ET"), 1, 1, out var unsupported);

            Assert.Contains("sh", unsupported);
            Assert.Contains("Tf (missing font)", unsupported);
        }

        [Fact]
        public void PageSize_SwapsForQuarterTurns()
        {
            var doc = PdfSourceDocument.Open(Pdf("", pageExtras: "/Rotate 90", w: 200, h: 100));

            Assert.Equal((100.0, 200.0), PdfPagePainter.GetPageSize(doc, doc.Pages[0]));
        }
    }
}
