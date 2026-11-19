using PeachDrawing;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Colour spaces as the importer reads them: each is used to fill the page and the colour read back from a raster canvas.</summary>
    public class PdfColorSpaceTests
    {
        private static ((byte R, byte G, byte B, byte A) Pixel, IReadOnlyCollection<string> Unsupported) Fill(string colourOps, string resources = "", params string[] more)
        {
            var pdf = TestPdf.Build(colourOps + " 0 0 100 100 re f", resources: resources, extraObjects: more);
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            var unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            var i = (50 * 100 + 50) * 4;
            return ((span[i], span[i + 1], span[i + 2], span[i + 3]), unsupported);
        }

        private static void Near((int R, int G, int B) expected, (byte R, byte G, byte B, byte A) actual, int tolerance = 6) =>
            Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance && actual.A == 255,
                $"expected about {expected}, was {actual}");

        [Fact]
        public void CalibratedSpaces_ReadAsTheirDeviceCounterparts()
        {
            Near((255, 0, 0), Fill("/CS0 cs 1 0 0 sc", "/ColorSpace << /CS0 [/CalRGB << /WhitePoint [0.95 1 1.09] >>] >>").Pixel);
            Near((128, 128, 128), Fill("/CS0 cs 0.5 sc", "/ColorSpace << /CS0 [/CalGray << /WhitePoint [0.95 1 1.09] >>] >>").Pixel);
        }

        [Theory]
        [InlineData(1, "0.5 sc", 128, 128, 128)]
        [InlineData(3, "0 1 0 sc", 0, 255, 0)]
        [InlineData(4, "0 0 0 1 sc", 0, 0, 0)]
        public void IccBasedSpaces_AreReadByTheirComponentCount(int components, string set, int r, int g, int b)
        {
            var icc = TestPdf.Stream($"/N {components}", "profile");
            Near((r, g, b), Fill("/CS0 cs " + set, "/ColorSpace << /CS0 [/ICCBased 5 0 R] >>", icc).Pixel, 10);
        }

        [Fact]
        public void IccBasedSpaceWithAnUnusualComponentCount_FallsBackToItsAlternate()
        {
            var icc = TestPdf.Stream("/N 2 /Alternate /DeviceGray", "profile");
            Near((255, 255, 255), Fill("/CS0 cs 1 sc", "/ColorSpace << /CS0 [/ICCBased 5 0 R] >>", icc).Pixel);
        }

        private const string WhiteToRed = "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [1 0 0] /N 1 >>";

        [Theory]
        [InlineData("1 scn", 255, 0, 0)]
        [InlineData("0.5 scn", 255, 128, 128)]
        [InlineData("0 scn", 255, 255, 255)]
        [InlineData("", 255, 0, 0)]             // a Separation starts at full tint
        public void SeparationSpace_PassesItsTintThroughTheTintTransform(string set, int r, int g, int b) =>
            Near((r, g, b), Fill("/CS0 cs " + set, $"/ColorSpace << /CS0 [/Separation /Spot /DeviceRGB {WhiteToRed}] >>").Pixel);

        [Fact]
        public void SeparationNone_MarksNothing() =>
            Assert.Equal(0, Fill("/CS0 cs 1 scn", $"/ColorSpace << /CS0 [/Separation /None /DeviceRGB {WhiteToRed}] >>").Pixel.A);

        [Fact]
        public void DeviceNSpace_PassesAllItsColorantsThroughTheTintTransform()
        {
            // The function is { 0 }: two colorants on the stack and a zero pushed, the three on top being red, green and blue.
            var function = TestPdf.Stream("/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]", "{ 0 }");

            Near((51, 153, 0), Fill("/CS0 cs 0.2 0.6 scn", "/ColorSpace << /CS0 [/DeviceN [/A /B] /DeviceRGB 5 0 R] >>", function).Pixel);
        }

        [Fact]
        public void SeparationImage_IsReadThroughTheTintTransform()
        {
            var image = TestPdf.Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 " +
                $"/ColorSpace [/Separation /Spot /DeviceRGB {WhiteToRed}]", TestPdf.Binary([0xFF]));
            var doc = PdfSourceDocument.Open(TestPdf.Build("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: image));
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            var i = (50 * 100 + 50) * 4;

            Near((255, 0, 0), (span[i], span[i + 1], span[i + 2], span[i + 3]));
        }

        [Theory]
        [InlineData(0, 255, 255, 255)]      // palette entry 0 is FF 80 80: L = 100, a and b near 0
        [InlineData(1, 119, 119, 119)]      // entry 1 is 80 80 80: L about 50
        public void IndexedPaletteOverLab_ReadsItsBytesAcrossLabsOwnRanges(int index, int r, int g, int b)
        {
            const string space = "/ColorSpace << /CS0 [/Indexed [/Lab << /WhitePoint [0.9505 1 1.089] /Range [-128 127 -128 127] >>] 1 <FF8080808080>] >>";

            Near((r, g, b), Fill($"/CS0 cs {index} sc", space).Pixel, 8);
        }

        [Fact]
        public void LabImage_ReadsItsSamplesAcrossLabsOwnRanges()
        {
            // One pixel, three 8-bit samples FF 80 80: L = 100 and a, b near 0, which is white (not the near-black of L = 1).
            var image = TestPdf.Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 " +
                "/ColorSpace [/Lab << /WhitePoint [0.9505 1 1.089] /Range [-128 127 -128 127] >>]", TestPdf.Binary([0xFF, 0x80, 0x80]));

            var doc = PdfSourceDocument.Open(TestPdf.Build("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: image));
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            var i = (50 * 100 + 50) * 4;

            Near((255, 255, 255), (span[i], span[i + 1], span[i + 2], span[i + 3]), 8);
        }

        [Fact]
        public void LabSpace_ConvertsThroughXyzToRgb()
        {
            const string lab = "/ColorSpace << /CS0 [/Lab << /WhitePoint [0.9505 1 1.089] /Range [-128 127 -128 127] >>] >>";

            Near((255, 255, 255), Fill("/CS0 cs 100 0 0 sc", lab).Pixel, 12);
            Near((0, 0, 0), Fill("/CS0 cs 0 0 0 sc", lab).Pixel, 12);
            var warm = Fill("/CS0 cs 55 70 50 sc", lab).Pixel;
            Assert.True(warm.R > warm.G + 60 && warm.R > warm.B + 60, $"a positive a* and b* is reddish: {warm}");
            var cool = Fill("/CS0 cs 55 -60 -40 sc", lab).Pixel;
            Assert.True(cool.G > cool.R || cool.B > cool.R, $"a negative a* is not red: {cool}");
        }

        [Fact]
        public void IndexedSpace_ReadsItsPaletteFromAStringOrAStream()
        {
            Near((0, 0, 255), Fill("/CS0 cs 1 sc", "/ColorSpace << /CS0 [/Indexed /DeviceRGB 1 <FF00000000FF>] >>").Pixel);

            var palette = TestPdf.Stream("", TestPdf.Binary([255, 0, 0, 0, 255, 0]));
            Near((0, 255, 0), Fill("/CS0 cs 1 sc", "/ColorSpace << /CS0 [/Indexed /DeviceRGB 1 5 0 R] >>", palette).Pixel);
        }

        [Fact]
        public void IndexedSpace_ClampsAnOutOfRangeIndex_AndReadsShortPalettesAsBlack()
        {
            Near((0, 0, 255), Fill("/CS0 cs 9 sc", "/ColorSpace << /CS0 [/Indexed /DeviceRGB 1 <FF00000000FF>] >>").Pixel);
            Near((255, 0, 0), Fill("/CS0 cs 1 sc", "/ColorSpace << /CS0 [/Indexed /DeviceRGB 1 <FF0000>] >>").Pixel, 255); // second entry is missing: black
        }

        [Fact]
        public void NamedDeviceSpaces_AndAbbreviations_Resolve()
        {
            Near((0, 0, 255), Fill("/DeviceRGB cs 0 0 1 sc").Pixel);
            Near((128, 128, 128), Fill("/DeviceGray cs 0.5 sc").Pixel);
            Near((255, 255, 255), Fill("/DeviceCMYK cs 0 0 0 0 sc").Pixel);
            Near((255, 0, 0), Fill("/DeviceRGB cs 1 0 0 SC 1 0 0 sc").Pixel);
        }

        [Fact]
        public void ASeparationWhoseTintTransformCannotBeRead_IsReportedNotDrawnWrong()
        {
            var separation = Fill("/CS0 cs 1 sc", "/ColorSpace << /CS0 [/Separation /Spot /DeviceRGB /NotAFunction] >>");

            Assert.Contains("cs ", separation.Unsupported.Single());
        }

        [Fact]
        public void WrongNumberOfComponents_IsReported_AndAnUnknownSpaceName_IsReported()
        {
            Assert.Contains("sc (pattern or arity)", Fill("/DeviceRGB cs 1 sc").Unsupported);
            Assert.Contains("cs /Nope", Fill("/Nope cs").Unsupported);
        }

        [Fact]
        public void PatternSpace_WithoutAPattern_PaintsNothing()
        {
            var fill = Fill("/Pattern cs");

            Assert.Equal(0, fill.Pixel.A);
        }

        [Fact]
        public void TheDefaultColourOfASpace_IsItsInitialColour()
        {
            // cs selects a space and its initial colour: black for gray/RGB, all ink for CMYK.
            Near((0, 0, 0), Fill("/DeviceRGB cs").Pixel);
            Near((0, 0, 0), Fill("/DeviceCMYK cs").Pixel, 40);
        }
    }
}
