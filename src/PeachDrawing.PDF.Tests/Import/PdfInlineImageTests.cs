using PeachDrawing;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Inline images (<c>BI</c> ... <c>ID</c> ... <c>EI</c>) in a content stream: the picture is drawn and the stream carries on after it.</summary>
    public class PdfInlineImageTests
    {
        private static string Bytes(params int[] values) => new(values.Select(v => (char)v).ToArray());

        private static (byte R, byte G, byte B, byte A) At(string content, int x, int yFromTop, out IReadOnlyCollection<string> unsupported)
        {
            var doc = PdfSourceDocument.Open(TestPdf.Build(content));
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var buffer = canvas.ToPixelBuffer();
            var i = (yFromTop * buffer.Width + x) * 4;
            var span = buffer.PremultipliedRgba.Span;
            return (span[i], span[i + 1], span[i + 2], span[i + 3]);
        }

        private static void Is((byte R, byte G, byte B, byte A) p, int r, int g, int b) =>
            Assert.True(Math.Abs(p.R - r) < 40 && Math.Abs(p.G - g) < 40 && Math.Abs(p.B - b) < 40 && p.A > 200, $"expected ({r},{g},{b}), was {p}");

        private const string Place = "q 100 0 0 100 0 0 cm\n";

        [Fact]
        public void RawRgbImage_IsDrawnInTheUnitSquare()
        {
            // 2x2: red, green / blue, yellow (top row first).
            var content = Place + "BI /W 2 /H 2 /BPC 8 /CS /RGB ID " + Bytes(255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0) + "\nEI Q";

            Is(At(content, 25, 25, out var unsupported), 255, 0, 0);
            Assert.Empty(unsupported);
            Is(At(content, 75, 25, out _), 0, 255, 0);
            Is(At(content, 25, 75, out _), 0, 0, 255);
            Is(At(content, 75, 75, out _), 255, 255, 0);
        }

        [Fact]
        public void LongNames_AndTheContentAfterTheImage_Work()
        {
            var content = Place + "BI /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray ID " + Bytes(255) + " EI Q 0 0 1 rg 0 0 10 10 re f";

            Is(At(content, 50, 50, out var unsupported), 255, 255, 255);
            Assert.Empty(unsupported);
            Is(At(content, 5, 95, out _), 0, 0, 255);                     // drawn after the image, so the stream was not thrown off
        }

        [Fact]
        public void DataThatContainsTheLettersEI_DoesNotEndTheImageEarly()
        {
            // Four gray pixels whose bytes spell " EI " - a scan for the first EI would stop inside them.
            var content = Place + "BI /W 2 /H 2 /BPC 8 /CS /G ID " + Bytes(0x20, 'E', 'I', 0x20) + "\nEI Q 1 0 0 rg 0 0 10 10 re f";

            Is(At(content, 5, 95, out var unsupported), 255, 0, 0);
            Assert.Empty(unsupported);
        }

        [Fact]
        public void FilteredImage_IsDecodedThroughItsFilter()
        {
            var content = Place + "BI /W 2 /H 1 /BPC 8 /CS /RGB /F /AHx ID FF0000 0000FF> EI Q";

            Is(At(content, 25, 50, out var unsupported), 255, 0, 0);
            Assert.Empty(unsupported);
            Is(At(content, 75, 50, out _), 0, 0, 255);
        }

        [Fact]
        public void ImageMask_IsPaintedWithTheFillColour()
        {
            // 8x1 stencil: bits 11110000 - sample 0 paints, so the right half is painted.
            var content = "1 0 0 rg " + Place + "BI /IM true /W 8 /H 1 /BPC 1 ID " + Bytes(0xF0) + " EI Q";

            Is(At(content, 75, 50, out var unsupported), 255, 0, 0);
            Assert.Empty(unsupported);
            Assert.True(At(content, 25, 50, out _).A < 30, "the left half is left unpainted");
        }

        [Fact]
        public void IndexedImage_UsesItsPalette()
        {
            // 2x1, one bit each: index 0 then index 1 of a two-entry palette (green, blue).
            var content = Place + "BI /W 2 /H 1 /BPC 1 /CS [/I /RGB 1 <00FF00 0000FF>] ID " + Bytes(0x40) + " EI Q";

            Is(At(content, 25, 50, out var unsupported), 0, 255, 0);
            Assert.Empty(unsupported);
            Is(At(content, 75, 50, out _), 0, 0, 255);
        }

        [Fact]
        public void ImageWithoutAnEnd_IsReported_AndDoesNotHang()
        {
            At(Place + "BI /W 2 /H 2 /BPC 8 /CS /G ID " + Bytes(1, 2, 3, 4), 5, 5, out var unsupported);

            Assert.Contains("BI (malformed)", unsupported);
        }
    }
}
