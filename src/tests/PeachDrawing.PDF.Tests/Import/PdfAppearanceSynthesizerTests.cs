using PeachDrawing;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// Annotations with no appearance stream: a viewer has to make the look itself. Each test paints a hand-written page with annotations drawn
    /// (<see cref="PdfAnnotationPaint.All"/>) and reads pixels back, so a synthesizer that drew nothing, or in the wrong place, fails.
    /// </summary>
    public class PdfAppearanceSynthesizerTests
    {
        private static (byte R, byte G, byte B, byte A)[,] Render(string annotation, string pageContent = "", string catalogExtras = "", params string[] more)
        {
            var pdf = TestPdf.Build(pageContent, pageExtras: "/Annots [5 0 R]", catalogExtras: catalogExtras, extraObjects: [annotation, .. more]);
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            PdfPagePainter.Paint(doc, doc.Pages[0], canvas, PdfAnnotationPaint.All);
            var buffer = canvas.ToPixelBuffer();
            var span = buffer.PremultipliedRgba.Span;
            var pixels = new (byte, byte, byte, byte)[100, 100];
            for (int y = 0; y < 100; y++)
                for (int x = 0; x < 100; x++)
                {
                    int i = (y * buffer.Width + x) * 4;
                    pixels[x, y] = (span[i], span[i + 1], span[i + 2], span[i + 3]);
                }

            return pixels;
        }

        private static int Ink((byte R, byte G, byte B, byte A)[,] pixels, int x0, int y0, int x1, int y1)
        {
            int count = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    if (pixels[x, y].A > 100 && (pixels[x, y].R < 160 || pixels[x, y].G < 160 || pixels[x, y].B < 160)) count++;
            return count;
        }

        [Fact]
        public void Highlight_WithoutAppearance_IsAMultipliedFillOfItsQuad_YellowByDefault()
        {
            // Over a cyan square, yellow multiplied is green: the blend mode is applied, not just a yellow fill.
            var p = Render("<< /Type /Annot /Subtype /Highlight /Rect [10 10 50 30] /QuadPoints [10 30 50 30 10 10 50 10] >>", pageContent: "0 1 1 rg 0 0 100 100 re f");

            Assert.Equal((0, 255, 0, 255), p[30, 80]);                 // inside the quad (page y 20 is canvas y 80)
            Assert.Equal((0, 255, 255, 255), p[70, 80]);               // outside it
        }

        [Fact]
        public void Highlight_TakesItsColorFromC()
        {
            var p = Render("<< /Type /Annot /Subtype /Highlight /Rect [10 10 50 30] /C [0 0 1] /QuadPoints [10 30 50 30 10 10 50 10] >>");

            Assert.Equal((0, 0, 255, 255), p[30, 80]);
        }

        [Fact]
        public void Underline_StrikeOut_AndSquiggly_DrawTheirLines()
        {
            var under = Render("<< /Type /Annot /Subtype /Underline /Rect [10 10 50 30] /QuadPoints [10 30 50 30 10 10 50 10] >>");
            var strike = Render("<< /Type /Annot /Subtype /StrikeOut /Rect [10 10 50 30] /QuadPoints [10 30 50 30 10 10 50 10] >>");
            var squiggle = Render("<< /Type /Annot /Subtype /Squiggly /Rect [10 10 50 30] /C [1 0 0] /QuadPoints [10 30 50 30 10 10 50 10] >>");

            Assert.True(Ink(under, 10, 85, 50, 95) > 30 && Ink(under, 10, 60, 50, 80) == 0, "the underline sits along the bottom of the quad");
            Assert.True(Ink(strike, 10, 75, 50, 85) > 30 && Ink(strike, 10, 90, 50, 100) == 0, "the strike-out runs through the middle");
            Assert.True(squiggle[30, 85].A > 0 || squiggle[30, 88].A > 0 || squiggle[30, 90].A > 0, "the squiggle is drawn near the bottom");
        }

        [Fact]
        public void CheckBox_WithoutAppearance_ShowsItsMarkOnlyWhenOn()
        {
            const string field = "<< /Type /Annot /Subtype /Widget /FT /Btn /Rect [30 30 70 70] /F 4 /MK << /CA (l) >> /AS {0} /V {0} >>";

            var on = Render(string.Format(field, "/Yes"));
            var off = Render(string.Format(field, "/Off"));

            Assert.Equal(0, on[50, 50].R);                              // the circle's center is black
            Assert.True(on[50, 50].A > 200);
            Assert.Equal(0, off[50, 50].A);
        }

        [Fact]
        public void FieldBackgroundAndBorder_FromMk_AreDrawn()
        {
            var p = Render("<< /Type /Annot /Subtype /Widget /FT /Tx /Rect [20 20 80 60] /MK << /BG [1 0 0] /BC [0 0 1] >> /BS << /W 2 >> >>");

            Assert.Equal((255, 0, 0, 255), p[50, 60]);                  // the fill, inside the border
            Assert.Equal((0, 0, 255, 255), p[21, 60]);                  // the border along the left edge
        }

        [Fact]
        public void TextField_ShowsItsValue_TruncatedToMaxLen_AndMaskedForAPassword()
        {
            string Field(string extra) => $"<< /Type /Annot /Subtype /Widget /FT /Tx /Rect [10 40 90 70] /DA (0 g /Helv 12 Tf) /V (Hello) {extra} >>";

            var plain = Render(Field(""));
            var shortened = Render(Field("/MaxLen 2"));
            var masked = Render(Field("/Ff 8192"));

            int all = Ink(plain, 10, 30, 90, 60), two = Ink(shortened, 10, 30, 90, 60), stars = Ink(masked, 10, 30, 90, 60);
            Assert.True(all > 40, $"the value is drawn ({all} dark pixels)");
            Assert.True(two > 0 && two < all, $"MaxLen 2 shows fewer characters ({two} < {all})");
            Assert.True(stars > 0 && stars != all, "a password shows asterisks, not the text");
        }

        [Fact]
        public void TextField_QuaddingMovesTheValue()
        {
            string Field(int q) => $"<< /Type /Annot /Subtype /Widget /FT /Tx /Rect [10 40 90 70] /DA (0 g /Helv 12 Tf) /V (Hi) /Q {q} >>";

            int Left(int q)
            {
                var p = Render(Field(q));
                for (int x = 10; x < 90; x++)
                    if (Ink(p, x, 30, x + 1, 60) > 0) return x;
                return -1;
            }

            int left = Left(0), center = Left(1), right = Left(2);
            Assert.True(left >= 0 && left < center && center < right, $"left {left}, center {center}, right {right}");
        }

        [Fact]
        public void ListBox_ShowsItsOptions_AndHighlightsTheChosenOne()
        {
            var p = Render("<< /Type /Annot /Subtype /Widget /FT /Ch /Rect [10 10 90 90] /DA (0 g /Helv 12 Tf) /Opt [(Apple) (Banana) (Cherry)] /V (Banana) >>");

            Assert.True(Ink(p, 10, 10, 90, 30) > 20, "the first item is drawn at the top");
            bool blue = false;
            for (int y = 10; y < 60 && !blue; y++) blue = p[60, y].B > 100 && p[60, y].R < 60;
            Assert.True(blue, "the chosen item has the selection color behind it");
        }

        [Fact]
        public void AnnotationWithItsOwnAppearance_IsNotRemade()
        {
            var appearance = TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 40 20]", "0 1 0 rg 0 0 40 20 re f");
            var p = Render("<< /Type /Annot /Subtype /Highlight /Rect [10 10 50 30] /QuadPoints [10 30 50 30 10 10 50 10] /AP << /N 6 0 R >> >>", more: appearance);

            Assert.Equal((0, 255, 0, 255), p[30, 80]);                  // the stream's green, not the synthesized yellow
        }
    }
}
