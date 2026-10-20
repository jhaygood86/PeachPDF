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

            Assert.Contains("sh (missing shading)", unsupported);
            Assert.Contains("Tf (missing font)", unsupported);
        }

        [Fact]
        public void PageSize_SwapsForQuarterTurns()
        {
            var doc = PdfSourceDocument.Open(Pdf("", pageExtras: "/Rotate 90", w: 200, h: 100));

            Assert.Equal((100.0, 200.0), PdfPagePainter.GetPageSize(doc, doc.Pages[0]));
        }

        private static void AssertNear((int R, int G, int B, int A) expected, (byte R, byte G, byte B, byte A) actual, int tolerance = 8)
        {
            // An image scaled up is resampled smoothly, so a pixel is near its sample's colour rather than exactly it.
            Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance && Math.Abs(expected.A - actual.A) <= tolerance,
                $"expected about {expected}, was {actual}");
        }

        private static string ImageObject(string dictExtras, string hex) =>
            $"<< /Type /XObject /Subtype /Image {dictExtras} /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}>\nendstream";

        [Fact]
        public void RgbImage_IsDrawnIntoTheUnitSquare_TopRowAtTheTop()
        {
            // 2x2: red green / blue white, scaled to fill the 100x100 page.
            var image = ImageObject("/Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8", "FF0000 00FF00 0000FF FFFFFF");
            var pdf = Pdf("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: image);

            AssertNear((255, 0, 0, 255), Px(pdf, 25, 25));
            AssertNear((0, 255, 0, 255), Px(pdf, 75, 25));
            AssertNear((0, 0, 255, 255), Px(pdf, 25, 75));
            AssertNear((255, 255, 255, 255), Px(pdf, 75, 75));
        }

        [Fact]
        public void ImageMask_IsPaintedInTheFillColourWhereTheSampleIsZero()
        {
            // 2x1 stencil: bit 0 = painted, bit 1 = transparent -> 0b01.......
            var image = ImageObject("/Width 2 /Height 1 /ImageMask true", "40");
            var pdf = Pdf("0 0 1 rg 100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: image);

            AssertNear((0, 0, 255, 255), Px(pdf, 25, 50));
            Assert.InRange(Px(pdf, 75, 50).A, 0, 8);
        }

        [Fact]
        public void SoftMask_SetsTheAlphaOfTheImage()
        {
            var smask = ImageObject("/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "FF00");
            var image = ImageObject("/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R", "FF0000 FF0000");
            var pdf = Pdf("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: image + "|||" + smask);

            AssertNear((255, 0, 0, 255), Px(pdf, 25, 50));
            Assert.InRange(Px(pdf, 75, 50).A, 0, 8);
        }

        [Fact]
        public void IndexedOneBitImage_UsesThePalette_AndAColourKeyMaskMakesAnIndexTransparent()
        {
            // 2x1, palette: green, red; bits 0,1 -> green, red. Colour key masks index 0.
            var image = ImageObject("/Width 2 /Height 1 /ColorSpace [/Indexed /DeviceRGB 1 <00FF00FF0000>] /BitsPerComponent 1 /Mask [0 0]", "40");
            var pdf = Pdf("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: image);

            Assert.InRange(Px(pdf, 25, 50).A, 0, 8);
            AssertNear((255, 0, 0, 255), Px(pdf, 75, 50));
        }

        [Fact]
        public void ImageFilterThatIsNotDecodedYet_IsReported()
        {
            var image = "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /JBIG2Decode /Length 1 >>\nstream\nx\nendstream";
            Render(Pdf("1 0 0 1 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: image), 1, 1, out var unsupported);

            Assert.Contains("image: JBIG2Decode image", unsupported);
        }

        private const string RedToBlue = "<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>";

        private static string AxialShading(string function = RedToBlue) =>
            $"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 100 0] /Function {function} /Extend [true true] >>";

        private static byte[] PatternPdf(string content, string function = RedToBlue, string extraObjects = "") =>
            Pdf(content, resources: "/Pattern << /P0 5 0 R >>", extraObjects: $"<< /PatternType 2 /Shading {AxialShading(function)} >>" + extraObjects);

        [Fact]
        public void AxialShadingPattern_FillsWithTheGradient()
        {
            var pdf = PatternPdf("/Pattern cs /P0 scn 0 0 100 100 re f");

            AssertNear((250, 0, 5, 255), Px(pdf, 2, 50), 14);
            AssertNear((128, 0, 128, 255), Px(pdf, 50, 50), 14);
            AssertNear((5, 0, 250, 255), Px(pdf, 97, 50), 14);
        }

        [Fact]
        public void ShadingPattern_StaysInPatternSpace_WhateverTheCurrentTransformIs()
        {
            // The rectangle is drawn under a 0.5 scale, but the pattern's space is the page's: the gradient still spans x = 0..100 of the page.
            var pdf = PatternPdf("0.5 0 0 0.5 0 0 cm /Pattern cs /P0 scn 0 0 200 200 re f");

            AssertNear((250, 0, 5, 255), Px(pdf, 2, 50), 14);
            AssertNear((5, 0, 250, 255), Px(pdf, 97, 50), 14);
        }

        [Fact]
        public void ShOperator_PaintsTheShadingOverTheClip()
        {
            var pdf = Pdf("0 0 50 100 re W n /S0 sh", resources: $"/Shading << /S0 {AxialShading()} >>");

            AssertNear((250, 0, 5, 255), Px(pdf, 2, 50), 14);
            Assert.InRange(Px(pdf, 75, 50).A, 0, 8);
        }

        [Fact]
        public void StitchingFunction_SwitchesSubfunctionAtItsBound()
        {
            var stitched = "<< /FunctionType 3 /Domain [0 1] /Bounds [0.5] /Encode [0 1 0 1] /Functions [" +
                "<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 1 0] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0 1 0] /C1 [0 0 1] /N 1 >>] >>";
            var pdf = PatternPdf("/Pattern cs /P0 scn 0 0 100 100 re f", stitched);

            AssertNear((128, 128, 0, 255), Px(pdf, 25, 50), 16);
            AssertNear((0, 255, 0, 255), Px(pdf, 50, 50), 24);
            AssertNear((0, 128, 128, 255), Px(pdf, 75, 50), 16);
        }

        [Fact]
        public void PostScriptFunction_IsEvaluated()
        {
            // { dup 1 exch sub 0 } maps t to (t, 1 - t, 0): green on the left, red on the right.
            var body = "{ dup 1 exch sub 0 }";
            var function = "6 0 R";
            var stream = "|||<< /FunctionType 4 /Domain [0 1] /Range [0 1 0 1 0 1] /Length " + body.Length + " >>" + "\nstream\n" + body + "\nendstream";
            var pdf = PatternPdf("/Pattern cs /P0 scn 0 0 100 100 re f", function, stream);

            AssertNear((5, 250, 0, 255), Px(pdf, 2, 50), 14);
            AssertNear((250, 5, 0, 255), Px(pdf, 97, 50), 14);
        }

        [Fact]
        public void TilingPattern_IsReportedNotDrawn()
        {
            var pdf = Pdf("/Pattern cs /P0 scn 0 0 100 100 re f", resources: "/Pattern << /P0 5 0 R >>", extraObjects: "<< /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 1 1] /XStep 1 /YStep 1 /Resources << >> /Length 0 >>" + "\nstream\n\nendstream");
            Render(pdf, 1, 1, out var unsupported);

            Assert.Contains("pattern type 1", unsupported);
        }

        [Fact]
        public void CmykImage_AndACmykStencilFill_ConvertToRgb()
        {
            // One pure-cyan pixel; and a 1x1 stencil painted in a CMYK fill colour (pure magenta).
            var cyan = ImageObject("/Width 1 /Height 1 /ColorSpace /DeviceCMYK /BitsPerComponent 8", "FF000000");
            var cyanPdf = Pdf("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: cyan);
            var p = Px(cyanPdf, 50, 50);
            Assert.True(p.R < 60 && p.B > 150 && p.A == 255, $"cyan: {p}");

            var stencil = ImageObject("/Width 1 /Height 1 /ImageMask true", "00");
            var magentaPdf = Pdf("0 1 0 0 k 100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: stencil);
            var m = Px(magentaPdf, 50, 50);
            Assert.True(m.R > 150 && m.G < 60 && m.B > 150, $"magenta: {m}");
        }

        [Fact]
        public void ImageInterpolation_FollowsTheInterpolateEntry()
        {
            const string pixels = "FF0000 00FF00 0000FF FFFFFF";
            const string basic = "/Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8";
            var crisp = Pdf("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: ImageObject(basic, pixels));
            var smooth = Pdf("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: ImageObject(basic + " /Interpolate true", pixels));

            var nearEdge = Px(crisp, 47, 25);          // just inside the red pixel's right edge
            Assert.True(nearEdge.R > 250 && nearEdge.G < 5, $"pixels stay crisp by default: {nearEdge}");
            var blended = Px(smooth, 47, 25);
            Assert.True(blended.G > 40, $"/Interpolate true smooths across the edge: {blended}");
        }
    }
}
