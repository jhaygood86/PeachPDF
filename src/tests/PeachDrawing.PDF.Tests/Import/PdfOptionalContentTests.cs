using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Optional content (layers): what the default configuration hides is neither read nor, when painted, drawn.</summary>
    public class PdfOptionalContentTests
    {
        private const string Font = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

        private static string TextOf(string content, string catalogExtras, params string[] more)
        {
            var objects = new List<string> { Font, "<< /Type /OCG /Name (Shown) >>", "<< /Type /OCG /Name (Hidden) >>" };
            objects.AddRange(more);
            var pdf = TestPdf.Build(content, resources: "/Font << /F1 5 0 R >> /Properties << /MCOn 6 0 R /MCOff 7 0 R /MCM 8 0 R >> /XObject << /Fm0 8 0 R >>", width: 200, height: 100,
                catalogExtras: catalogExtras, extraObjects: [.. objects]);
            return PdfPageSource.Open(pdf).GetText(0).Text;
        }

        private const string Layers = "BT /F1 10 Tf 10 80 Td /OC /MCOn BDC (Shown) Tj EMC ET BT /F1 10 Tf 10 60 Td /OC /MCOff BDC (Hidden) Tj EMC ET BT /F1 10 Tf 10 40 Td (Plain) Tj ET";

        [Fact]
        public void TextInALayerThatIsOff_IsNotRead()
        {
            Assert.Equal("Shown\nPlain", TextOf(Layers, "/OCProperties << /OCGs [6 0 R 7 0 R] /D << /OFF [7 0 R] >> >>"));
        }

        [Fact]
        public void WithNoConfiguration_EverythingIsShown()
        {
            Assert.Equal("Shown\nHidden\nPlain", TextOf(Layers, ""));
        }

        [Fact]
        public void ABaseStateOfOff_HidesEveryLayerNotListedOn()
        {
            Assert.Equal("Hidden\nPlain", TextOf(Layers, "/OCProperties << /OCGs [6 0 R 7 0 R] /D << /BaseState /OFF /ON [7 0 R] >> >>"));
        }

        [Fact]
        public void TheTextAfterAHiddenLayer_StaysWhereItWas()
        {
            // The hidden span moves the pen like any other text, so what follows it on the line is placed after it.
            var text = TextOf("BT /F1 10 Tf 10 80 Td /OC /MCOff BDC (Gone) Tj EMC (Kept) Tj ET", "/OCProperties << /D << /OFF [7 0 R] >> >>");

            Assert.Equal("Kept", text);
        }

        [Theory]
        [InlineData("/P /AnyOn", true)]
        [InlineData("/P /AllOn", false)]
        [InlineData("/P /AnyOff", true)]
        [InlineData("/P /AllOff", false)]
        [InlineData("", true)]
        public void AMembershipDictionary_CombinesItsGroupsByPolicy(string policy, bool shown)
        {
            var member = $"<< /Type /OCMD /OCGs [6 0 R 7 0 R] {policy} >>";
            var text = TextOf("BT /F1 10 Tf 10 80 Td /OC /MCM BDC (Layered) Tj EMC ET",
                "/OCProperties << /D << /OFF [7 0 R] >> >>", member);

            Assert.Equal(shown ? "Layered" : "", text);
        }

        [Theory]
        [InlineData("[/And 6 0 R 7 0 R]", false)]
        [InlineData("[/Or 6 0 R 7 0 R]", true)]
        [InlineData("[/Not 7 0 R]", true)]
        [InlineData("[/Not [/Or 6 0 R 7 0 R]]", false)]
        public void AVisibilityExpression_IsEvaluated(string expression, bool shown)
        {
            var member = $"<< /Type /OCMD /OCGs [6 0 R 7 0 R] /VE {expression} >>";
            var text = TextOf("BT /F1 10 Tf 10 80 Td /OC /MCM BDC (Layered) Tj EMC ET", "/OCProperties << /D << /OFF [7 0 R] >> >>", member);

            Assert.Equal(shown ? "Layered" : "", text);
        }

        [Fact]
        public void AFormXObjectOnALayerThatIsOff_IsNotDrawnOrRead()
        {
            var form = TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 200 100] /OC 7 0 R /Resources << /Font << /F1 5 0 R >> >>", "BT /F1 10 Tf 10 20 Td (InForm) Tj ET");
            var text = TextOf("/Fm0 Do BT /F1 10 Tf 10 80 Td (Page) Tj ET", "/OCProperties << /D << /OFF [7 0 R] >> >>", form);

            Assert.Equal("Page", text);
        }

        [Fact]
        public void APathOnALayerThatIsOff_IsNotPainted_AndOneOnALayerThatIsOnIs()
        {
            // Two red squares side by side, the left on an on layer and the right on an off one; the path-clearing "n" must still run inside the hidden span.
            var content = "1 0 0 rg /OC /MCOn BDC 10 10 30 30 re f EMC /OC /MCOff BDC 60 10 30 30 re f EMC";
            var pdf = TestPdf.Build(content, resources: "/Properties << /MCOn 5 0 R /MCOff 6 0 R >>", width: 100, height: 50,
                catalogExtras: "/OCProperties << /D << /OFF [6 0 R] >> >>", extraObjects: ["<< /Type /OCG /Name (On) >>", "<< /Type /OCG /Name (Off) >>"]);
            using var canvas = new PeachDrawing.RasterRenderContext().CreateCanvas(100, 50);

            PdfPageSource.Open(pdf).Paint(0, canvas);

            var pixels = canvas.ToPixelBuffer();
            var rgba = pixels.PremultipliedRgba.Span;
            int At(int x, int y) => (y * pixels.Width + x) * 4;
            Assert.True(rgba[At(25, 25)] > 200 && rgba[At(25, 25) + 1] < 50, "the square on the on layer is red");
            Assert.True(rgba[At(75, 25) + 3] == 0 || rgba[At(75, 25) + 1] > 200, "the square on the off layer is not painted");
        }
    }
}
