using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>-peachpdf-barcode()</c> as a CSS image: it resolves wherever an <c>&lt;image&gt;</c> does and reaches the PDF as vector
    /// content (a form XObject of path operators), never an image XObject.
    /// </summary>
    public class BarcodeCssImageTests
    {
        private static async Task<(PeachPdfDocument Document, string Raw)> RenderAsync(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0} .b{width:150px;height:150px;background-repeat:no-repeat;background-size:contain}</style></head><body>" + body + "</body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            config.SetMargins(0);
            var document = await new PdfGenerator().GeneratePdf(html, config);
            using var ms = new MemoryStream();
            document.Save(ms);
            return (document, Encoding.Latin1.GetString(ms.ToArray()));
        }

        private static string FormContent(PeachPdfDocument document)
        {
            var file = document.Inspect();
            var xobjects = file.Pages[0].Dict("/Resources")?.Dict("/XObject");
            Assert.NotNull(xobjects);
            return string.Concat(xobjects.Keys.Select(k => (xobjects[k] as PdfStreamObject)?.DecodedText));
        }

        [Theory]
        [InlineData("qr, \"https://peachpdf.net/\"")]
        [InlineData("data-matrix, \"PeachPDF\"")]
        [InlineData("code128, \"PeachPDF-128\"")]
        [InlineData("gs1-128, \"(01)09501101530003(10)AB\"")]
        [InlineData("ean13, \"590123412345\"")]
        [InlineData("ean8, \"9638507\"")]
        [InlineData("upca, \"03600029145\"")]
        public async Task BackgroundImageRendersEachSymbologyAsVector(string args)
        {
            var (document, raw) = await RenderAsync($"<div class='b' style='background-image: -peachpdf-barcode({args})'></div>");

            Assert.DoesNotContain("/Subtype /Image", raw);
            Assert.True(Regex.Matches(FormContent(document), @"\bl\b").Count > 30);
        }

        [Fact]
        public async Task ColorsAreHonored()
        {
            var (document, raw) = await RenderAsync(
                "<div class='b' style=\"background-image: -peachpdf-barcode(qr, 'hi', rgb(204, 0, 0), white)\"></div>");

            Assert.DoesNotContain("/Subtype /Image", raw);
            Assert.Contains("0.8 0 0 rg", FormContent(document));
        }

        [Fact]
        public async Task TheContentPropertyTakesABarcodeToo()
        {
            var (document, raw) = await RenderAsync(
                "<style>.q::before { content: -peachpdf-barcode(code128, 'AB'); display: inline-block; width: 150px; height: 50px; }</style><p class='q'>x</p>");

            Assert.DoesNotContain("/Subtype /Image", raw);
            Assert.True(Regex.Matches(FormContent(document), @"\bl\b").Count > 5);
        }

        [Fact]
        public async Task CssEscapesInTheStringAreDecoded()
        {
            // \30 followed by a space is "0": the string spells the EAN-8 digits 9638507.
            var (document, raw) = await RenderAsync("<div class='b' style=\"background-image: -peachpdf-barcode(ean8, '96385\\30 7')\"></div>");

            Assert.DoesNotContain("/Subtype /Image", raw);
            Assert.True(Regex.Matches(FormContent(document), @"\bl\b").Count > 10);
        }

        [Fact]
        public async Task CurrentcolorTakesTheElementsColorInABackground()
        {
            var (document, _) = await RenderAsync(
                "<div class='b' style=\"color: rgb(204, 0, 0); background-image: -peachpdf-barcode(qr, 'x', currentcolor)\"></div>" +
                "<div class='b' style=\"color: rgb(0, 0, 204); background-image: -peachpdf-barcode(qr, 'x', currentcolor)\"></div>");

            var content = FormContent(document);
            var colors = string.Join("|", Regex.Matches(content, @"[0-9. ]+ rg").Select(m => m.Value));
            Assert.True(content.Contains("0.8 0 0 rg") && content.Contains("0 0 0.8 rg"), $"{content.Length} chars, fills: {colors}");
        }

        [Fact]
        public async Task ListStyleImageTakesABarcode()
        {
            var (document, raw) = await RenderAsync(
                "<ul style=\"color: rgb(204, 0, 0); list-style-image: -peachpdf-barcode(qr, 'x', currentcolor)\"><li>item</li></ul>");

            Assert.DoesNotContain("/Subtype /Image", raw);
            Assert.Contains("0.8 0 0 rg", FormContent(document));
        }

        [Fact]
        public async Task CurrentcolorTakesTheElementsColorInGeneratedContent()
        {
            var (document, _) = await RenderAsync(
                "<style>.q::before { content: -peachpdf-barcode(code128, 'AB', currentcolor); display: inline-block; width: 150px; height: 50px; }</style>" +
                "<p class='q' style='color: rgb(204, 0, 0)'>x</p>");

            Assert.Contains("0.8 0 0 rg", FormContent(document));
        }

        [Fact]
        public async Task AnOversizedQrTextPaintsNothingInsteadOfFailingTheRender()
        {
            var text = new string('x', 10000);
            var (document, _) = await RenderAsync($"<div class='b' style='background-image: -peachpdf-barcode(qr, \"{text}\")'></div>");

            Assert.Null(document.Inspect().Pages[0].Dict("/Resources")?.Dict("/XObject"));
        }

        [Theory]
        [InlineData("unknown, \"x\"")]
        [InlineData("qr")]
        [InlineData("ean13, \"12\"")]
        [InlineData("qr, \"\"")]
        public async Task InvalidBarcodesPaintNothing(string args)
        {
            var (document, raw) = await RenderAsync($"<div class='b' style='background-image: -peachpdf-barcode({args})'></div>");

            Assert.DoesNotContain("/Subtype /Image", raw);
            Assert.Null(document.Inspect().Pages[0].Dict("/Resources")?.Dict("/XObject"));
        }
    }
}
