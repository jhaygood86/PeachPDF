using PeachDrawing;
using PeachDrawing.PDF.Import;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// ICC-based colour: a profile whose curves are linear (the fixture) is not sRGB, so a colour read through it comes out lighter than its
    /// device values - 50% in linear light is about 74% as sRGB. Each test would fail if the profile were ignored and the space read as its
    /// device alternate.
    /// </summary>
    public class PdfIccTests
    {
        private static (byte R, byte G, byte B, byte A) Paint(byte[] pdf)
        {
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            var i = (50 * 100 + 50) * 4;
            return (span[i], span[i + 1], span[i + 2], span[i + 3]);
        }

        private static string Profile(int n, byte[] bytes) => TestPdf.Stream($"/N {n}", TestPdf.Binary(bytes));

        private static byte[] Fill(string colourOps, string profileObject, int n = 3) =>
            TestPdf.Build(colourOps + " 0 0 100 100 re f", resources: "/ColorSpace << /CS0 [/ICCBased 5 0 R] >>", extraObjects: [profileObject]);

        [Fact]
        public void RgbProfile_ConvertsAColourToSrgb()
        {
            var p = Paint(Fill("/CS0 cs 0.5 0.5 0.5 sc", Profile(3, IccProfileFixture.BuildRgbProfile())));

            Assert.InRange(p.R, 178, 198);            // 0.5 in linear light is ~188 as sRGB; read as device RGB it would be 128
            Assert.InRange(p.G, 178, 198);
            Assert.InRange(p.B, 178, 198);
        }

        [Fact]
        public void GrayProfile_ConvertsAGrayLevelToSrgb()
        {
            var p = Paint(Fill("/CS0 cs 0.5 sc", Profile(1, IccProfileFixture.BuildGrayProfile())));

            Assert.InRange(p.R, 178, 198);
            Assert.Equal(p.R, p.G);
        }

        [Fact]
        public void IccImage_IsConvertedWholeThroughTheProfile()
        {
            // 2x1 image, both pixels (128,128,128): converted by the bulk path.
            var picture = TestPdf.Stream("/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace [/ICCBased 6 0 R] /BitsPerComponent 8 /Filter /ASCIIHexDecode", "808080 808080>");
            var pdf = TestPdf.Build("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>",
                extraObjects: [picture, Profile(3, IccProfileFixture.BuildRgbProfile())]);

            var p = Paint(pdf);

            Assert.InRange(p.R, 180, 200);
            Assert.InRange(p.B, 180, 200);
        }

        [Fact]
        public void IccImage_WithADecodeArray_IsConvertedPerPixel_AndKeepsItsSoftMask()
        {
            var picture = TestPdf.Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace [/ICCBased 6 0 R] /BitsPerComponent 8 /Decode [0 1 0 1 0 1] /Filter /ASCIIHexDecode", "808080>");
            var pdf = TestPdf.Build("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>",
                extraObjects: [picture, Profile(3, IccProfileFixture.BuildRgbProfile())]);

            Assert.InRange(Paint(pdf).R, 180, 200);

            var masked = TestPdf.Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace [/ICCBased 7 0 R] /BitsPerComponent 8 /SMask 6 0 R /Filter /ASCIIHexDecode", "808080>");
            var soft = TestPdf.Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /ASCIIHexDecode", "80>");
            var withMask = TestPdf.Build("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>",
                extraObjects: [masked, soft, Profile(3, IccProfileFixture.BuildRgbProfile())]);
            var m = Paint(withMask);
            Assert.InRange(m.A, 120, 135);
        }

        [Fact]
        public void IndexedSpaceOverAnIccBase_ConvertsItsPalette()
        {
            var pdf = TestPdf.Build("/CS1 cs 0 sc 0 0 100 100 re f", resources: "/ColorSpace << /CS1 [/Indexed [/ICCBased 5 0 R] 0 <808080>] >>",
                extraObjects: [Profile(3, IccProfileFixture.BuildRgbProfile())]);

            Assert.InRange(Paint(pdf).R, 180, 200);
        }

        [Fact]
        public void ProfileThatCannotBeUsed_IsReadAsItsDeviceAlternate()
        {
            var garbage = Profile(3, [1, 2, 3, 4, 5, 6, 7, 8]);

            var p = Paint(Fill("/CS0 cs 0.5 0.5 0.5 sc", garbage));

            Assert.InRange(p.R, 124, 132);            // 128: plain device RGB
        }

        [Fact]
        public void ProfileWhoseKindDisagreesWithTheComponentCount_IsReadAsDevice()
        {
            // An RGB profile on a one-component space.
            var p = Paint(Fill("/CS0 cs 0.5 sc", Profile(1, IccProfileFixture.BuildRgbProfile())));

            Assert.InRange(p.R, 124, 132);
        }

        [Fact]
        public void CmykProfile_StaysDeviceCmyk()
        {
            var pdf = TestPdf.Build("/CS0 cs 0 0 0 1 sc 0 0 100 100 re f", resources: "/ColorSpace << /CS0 [/ICCBased 5 0 R] >>",
                extraObjects: [Profile(4, IccProfileFixture.BuildCmykProfile())]);

            var p = Paint(pdf);

            Assert.True(p.R < 40 && p.G < 40 && p.B < 40, $"K = 1 is black: {p}");
        }

        [Fact]
        public void TheSameProfileIsParsedOncePerObject()
        {
            // Two uses of one profile object: the second finds the cached space and gives the same colour.
            var pdf = TestPdf.Build("/CS0 cs 0.5 0.5 0.5 sc 0 0 50 100 re f /CS0 cs 0.5 0.5 0.5 sc 50 0 50 100 re f",
                resources: "/ColorSpace << /CS0 [/ICCBased 5 0 R] >>", extraObjects: [Profile(3, IccProfileFixture.BuildRgbProfile())]);

            Assert.InRange(Paint(pdf).R, 178, 198);
        }

        [Fact]
        public void CmykImage_StaysDeviceCmyk_WhenDrawnOntoAPdfCanvas()
        {
            // A 2x1 CMYK image (cyan, magenta): re-drawn onto a PDF page it is a DeviceCMYK image, not converted to RGB.
            var picture = TestPdf.Stream("/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceCMYK /BitsPerComponent 8 /Filter /ASCIIHexDecode", "FF000000 00FF0000>");
            var source = PdfPageSource.Open(TestPdf.Build("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: [picture]));
            using var composer = new PdfComposer(new PdfRenderContext());
            var (w, h) = source.GetPageSize(0);
            var unsupported = source.Paint(0, composer.AddPage(w, h).Canvas);
            using var ms = new MemoryStream();
            composer.Save(ms);

            Assert.Empty(unsupported);
            var doc = PdfSourceDocument.Open(ms.ToArray());
            var resources = (PeachDrawing.PDF.Engine.PdfDictionary)doc.InheritedAttribute(doc.Pages[0], "Resources")!;
            var xobjects = doc.DictOf(resources, "XObject")!;
            var spaces = new List<string?>();
            foreach (var kv in xobjects)
                if (doc.Resolve(kv.Value) is PeachDrawing.PDF.Engine.PdfDictionary image)
                    spaces.Add(doc.NameOf(image.GetRaw("ColorSpace")));
            Assert.Contains("/DeviceCMYK", spaces);
        }

        [Fact]
        public void InvertedCmykDecode_IsApplied_BeforeTheImageIsHandedOver()
        {
            // /Decode [1 0 1 0 1 0 1 0] inverts every sample: 00 00 00 00 becomes full ink on all four channels = black.
            var picture = TestPdf.Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceCMYK /BitsPerComponent 8 /Decode [1 0 1 0 1 0 1 0] /Filter /ASCIIHexDecode", "00000000>");
            var pdf = TestPdf.Build("100 0 0 100 0 0 cm /Im0 Do", resources: "/XObject << /Im0 5 0 R >>", extraObjects: [picture]);

            var p = Paint(pdf);

            Assert.True(p.R < 40 && p.G < 40 && p.B < 40, $"inverted blank CMYK is black: {p}");
        }
    }
}
