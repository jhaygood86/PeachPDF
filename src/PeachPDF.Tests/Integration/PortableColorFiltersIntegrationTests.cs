using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="PdfGenerateConfig.PortableColorFilters"/> end to end: CSS <c>brightness()</c>,
    /// <c>contrast()</c> and <c>invert()</c> reach the PDF as blend-mode layers behind an alpha soft mask instead of
    /// an ExtGState <c>/TR</c>, which MuPDF-based readers ignore. The colour the layers produce is held to the CSS
    /// formulas by <c>PdfColorMatrixBlendLayersTests</c>; the rendered result was checked by rasterizing the
    /// <c>css_filter_portable</c> showcase in both PDFium and MuPDF.
    /// </summary>
    public class PortableColorFiltersIntegrationTests
    {
        private static readonly Regex GsThenDoOnSameCmLine =
            new(@"cm\s+/GS\d+\s+gs\s+/Fm\d+\s+Do", RegexOptions.Compiled);

        [Fact]
        public void PortableColorFilters_IsOffByDefault()
        {
            Assert.False(new PdfGenerateConfig().PortableColorFilters);
        }

        [Theory]
        [InlineData("brightness(1.5)", "ColorDodge")]
        [InlineData("brightness(0.5)", "Multiply")]
        [InlineData("contrast(2)", "ColorBurn")]
        [InlineData("contrast(0.5)", "Screen")]
        [InlineData("invert(1)", "Difference")]
        public async Task WithTheOptionOn_AFilterFunctionIsBlendLayers_NotATransferFunction(string filter, string blendMode)
        {
            var pdfText = await GetPdfText(Html(filter), portable: true);

            Assert.DoesNotContain("/TR", pdfText);
            Assert.Contains($"/BM /{blendMode}", pdfText);
            Assert.Contains("/S /Alpha", pdfText);
            Assert.Matches(GsThenDoOnSameCmLine, pdfText);
        }

        [Theory]
        [InlineData("brightness(1.5)")]
        [InlineData("contrast(0.5)")]
        [InlineData("invert(1)")]
        public async Task WithTheOptionOff_AFilterFunctionIsStillATransferFunction(string filter)
        {
            var pdfText = await GetPdfText(Html(filter), portable: false);

            Assert.Contains("/TR", pdfText);
            Assert.DoesNotContain("/BM /ColorDodge", pdfText);
            Assert.DoesNotContain("/BM /Difference", pdfText);
        }

        [Fact]
        public async Task WithTheOptionOn_AChainOfFunctionsIsOneStackOfLayers()
        {
            var pdfText = await GetPdfText(Html("brightness(1.3) contrast(1.4)"), portable: true);

            Assert.DoesNotContain("/TR", pdfText);
            Assert.Contains("/BM /ColorDodge", pdfText);
            Assert.Contains("/BM /ColorBurn", pdfText);
        }

        [Fact]
        public async Task WithTheOptionOn_AnUnfilteredBoxAddsNothing()
        {
            var pdfText = await GetPdfText(Html("none"), portable: true);

            Assert.DoesNotContain("/BM /", pdfText);
            Assert.DoesNotContain("/SMask", pdfText);
        }

        private static string Html(string filter) => $$"""
            <!DOCTYPE html><html><body>
            <div style="width: 60pt; height: 60pt; background:#2a9d8f; filter: {{filter}};"></div>
            </body></html>
            """;

        private static async Task<string> GetPdfText(string html, bool portable)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false, PortableColorFilters = portable };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
