using PeachDrawing;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// CCITT fax, JBIG2 and JPEG 2000 images. The fixtures are 16x8 pictures made by external encoders: the fax and JBIG2 ones hold
    /// black quadrants at the top right and bottom left; the JPEG 2000 one is red on the left half and blue on the right.
    /// </summary>
    public class PdfBilevelAndJpxImageTests
    {
        private static readonly byte[] Group4 = Convert.FromBase64String("JqL/5mL+ACAC");

        // A JBIG2 page-information segment (30 bytes with its header) and an immediate generic region coded with MMR - the Group 4 data
        // above - as embedded in a PDF.
        private static readonly byte[] Jbig2 = Convert.FromBase64String("AAAAADAAAQAAABMAAAAQAAAACAAAAAAAAAAAAAAAAAAAASYAAQAAABsAAAAQAAAACAAAAAAAAAAAAAEmov/mYv4AIAI=");

        private static readonly byte[] Jpx = Convert.FromBase64String("AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAAAtanAyaAAAABZpaGRyAAAACAAAABAAAwcHAAAAAAAPY29scgEAAAAAABAAAAE8anAyY/9P/1EALwAAAAAAEAAAAAgAAAAAAAAAAAAAABAAAAAIAAAAAAAAAAAAAwcBAQcBAQcBAf9SAAwAAAABAAAEBAAB/1wABEBA/2QAJQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAAxAAB/5PfgqA7DQ0p72yuT4mgAAMI/I206AABhHOP8fopwj7lNEfcpoj7lNEfcpoj7lNEfcpoj7lNEfcpoj7lNEfcpoj7lNEfcpoj7lNEfcpoj7lNEfcpoj7lNEffgDARUFSup3/fgpgRUFMZCqxczXPC5gAAYSEoGgAAAAAwjWH2P0U4R9ymiPuU0R9ymiPuU0R9ymiPuU0R9ymiPuU0R9ymiPuU0R9ymiPuU0R9ymiPuU0R9ymiPuU0R//Z");

        private static byte[] Page(string imageEntries, byte[] data, string content = "q 100 0 0 50 0 0 cm /Im0 Do Q", params string[] more) =>
            TestPdf.Build(content, resources: "/XObject << /Im0 5 0 R >>",
                extraObjects: [TestPdf.Stream("/Type /XObject /Subtype /Image /Width 16 /Height 8 " + imageEntries, TestPdf.Binary(data)), .. more]);

        private static (byte R, byte G, byte B, byte A) At(byte[] pdf, int pdfX, int pdfY, out IReadOnlyCollection<string> unsupported)
        {
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            var i = ((99 - pdfY) * 100 + pdfX) * 4;
            return (span[i], span[i + 1], span[i + 2], span[i + 3]);
        }

        private static void Is((byte R, byte G, byte B, byte A) p, int r, int g, int b) =>
            Assert.True(Math.Abs(p.R - r) < 40 && Math.Abs(p.G - g) < 40 && Math.Abs(p.B - b) < 40 && p.A > 200, $"expected ({r},{g},{b}), was {p}");

        /// <summary>The picture is drawn 100x50 at the bottom of the page: its top half is y 25..50, its bottom half y 0..25.</summary>
        private static void AssertQuadrants(byte[] pdf, int dark, int light)
        {
            Is(At(pdf, 25, 40, out var unsupported), dark, dark, dark);      // top left
            Assert.Empty(unsupported);
            Is(At(pdf, 75, 40, out _), light, light, light);                 // top right
            Is(At(pdf, 25, 10, out _), light, light, light);                 // bottom left
            Is(At(pdf, 75, 10, out _), dark, dark, dark);                    // bottom right
        }

        [Fact]
        public void CcittGroup4_IsDecodedToTheDrawnPicture()
        {
            var pdf = Page("/ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 16 /Rows 8 >>", Group4);

            AssertQuadrants(pdf, dark: 0, light: 255);
        }

        [Fact]
        public void CcittBlackIs1_ReversesTheSamples()
        {
            // The same fax data, but the filter output now has 1 for black: read as DeviceGray that is white, so the picture inverts.
            var pdf = Page("/ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 16 /Rows 8 /BlackIs1 true >>", Group4);

            AssertQuadrants(pdf, dark: 255, light: 0);
        }

        [Fact]
        public void CcittStencilMask_PaintsTheFillColourWhereTheDataIsBlack()
        {
            var pdf = Page("/ImageMask true /Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 16 /Rows 8 >>", Group4, "1 0 0 rg q 100 0 0 50 0 0 cm /Im0 Do Q");

            Is(At(pdf, 25, 40, out var unsupported), 255, 0, 0);
            Assert.Empty(unsupported);
            Assert.True(At(pdf, 75, 40, out _).A < 30, "the white quadrant is left unpainted");
        }

        [Fact]
        public void Jbig2_IsDecodedToTheDrawnPicture()
        {
            var pdf = Page("/ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /JBIG2Decode", Jbig2);

            AssertQuadrants(pdf, dark: 0, light: 255);
        }

        [Fact]
        public void Jbig2WithGlobals_ReadsTheGlobalSegmentsToo()
        {
            // The page-information segment moves to the globals stream; the region stays in the image.
            var globals = Jbig2.AsSpan(0, 11 + 19).ToArray();
            var region = Jbig2.AsSpan(11 + 19).ToArray();
            var pdf = Page("/ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /JBIG2Decode /DecodeParms << /JBIG2Globals 6 0 R >>", region,
                more: TestPdf.Stream("", TestPdf.Binary(globals)));

            AssertQuadrants(pdf, dark: 0, light: 255);
        }

        [Fact]
        public void Jbig2StencilMask_SetsTheAlphaOfAnImage()
        {
            // A solid green RGB image whose /Mask is the JBIG2 picture: a stencil mask hides the pixels whose sample is 1 (white).
            var green = new byte[16 * 8 * 3];
            for (var i = 0; i < green.Length; i += 3)
                green[i + 1] = 255;
            var mask = TestPdf.Stream("/Type /XObject /Subtype /Image /Width 16 /Height 8 /ImageMask true /Filter /JBIG2Decode", TestPdf.Binary(Jbig2));
            var pdf = TestPdf.Build("q 100 0 0 50 0 0 cm /Im0 Do Q", resources: "/XObject << /Im0 5 0 R >>",
                extraObjects: [TestPdf.Stream("/Type /XObject /Subtype /Image /Width 16 /Height 8 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask 6 0 R", TestPdf.Binary(green)), mask]);

            var shown = At(pdf, 75, 40, out var unsupported);
            var hidden = At(pdf, 25, 40, out _);
            Assert.Empty(unsupported);
            Assert.NotEqual(shown.A < 30, hidden.A < 30);          // exactly one of the two quadrants is masked out
            Is(shown.A > hidden.A ? shown : hidden, 0, 255, 0);
        }

        [Fact]
        public void Jpx_IsDecodedWithItsOwnColour()
        {
            var pdf = Page("/Filter /JPXDecode", Jpx);

            Is(At(pdf, 25, 25, out var unsupported), 255, 0, 0);
            Assert.Empty(unsupported);
            Is(At(pdf, 75, 25, out _), 0, 0, 255);
        }

        [Theory]
        [InlineData("/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 16 /Rows 8 >> /ColorSpace /DeviceGray /BitsPerComponent 1", "image: CCITTFaxDecode image")]
        [InlineData("/Filter /JBIG2Decode /ColorSpace /DeviceGray /BitsPerComponent 1", "image: JBIG2Decode image")]
        [InlineData("/Filter /JPXDecode", "image: image data could not be decoded")]
        public void DataThatCannotBeDecoded_IsReported(string entries, string expected)
        {
            var pdf = Page(entries, [1, 2, 3, 4]);

            At(pdf, 5, 5, out var unsupported);

            Assert.Contains(expected, unsupported);
        }
    }
}
