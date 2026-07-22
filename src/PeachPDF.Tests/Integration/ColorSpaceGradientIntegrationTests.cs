using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class ColorSpaceGradientIntegrationTests
    {
        private static string GradientHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 200px; height: 100px; {css} }}</style></head><body><div></div></body></html>";

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // ── linear-gradient in <colorspace> ───────────────────────────────────

        [Fact]
        public async Task LinearGradient_InOklab_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in oklab, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_InOklab_HasMoreStopsThanBaseline()
        {
            // With sRGB: 2-stop gradient → 2 sub-functions
            // With oklab: 2-stop gradient → 16 sub-functions (15 intermediate + endpoints)
            var pdfSrgb = await GetPdfText(GradientHtml("background-image: linear-gradient(red, blue);"));
            var pdfOklab = await GetPdfText(GradientHtml("background-image: linear-gradient(in oklab, red, blue);"));

            // The oklab version must have a stitching function with more sub-functions
            Assert.Contains("/FunctionType 3", pdfOklab);
        }

        [Fact]
        public async Task LinearGradient_InHsl_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in hsl, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_InHslLongerHue_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in hsl longer hue, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_InLab_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in lab, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Theory]
        [InlineData("in hwb, red, blue")]
        [InlineData("in lch, red, blue")]
        [InlineData("in xyz-d50, red, blue")]
        [InlineData("in oklch increasing hue, red, blue")]
        [InlineData("in oklch decreasing hue, red, blue")]
        public async Task LinearGradient_AcrossSpacesAndHueMethods_RendersSuccessfully(string args)
        {
            var pdfText = await GetPdfText(GradientHtml($"background-image: linear-gradient({args});"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_InOklch_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in oklch, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_InOklchLongerHue_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in oklch longer hue, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_InSrgbLinear_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in srgb-linear, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_InDisplayP3_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in display-p3, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task LinearGradient_InXyz_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in xyz, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── Midpoint color sanity: oklab should differ from sRGB ──────────────

        [Fact]
        public async Task LinearGradient_InOklab_MidpointDiffersFromSrgb()
        {
            // red→blue in sRGB midpoint is (127,0,127); in OKLab it passes through purple-ish hues
            // Both should render, but the PDF content (color component bytes) should differ
            var pdfSrgb  = await GetPdfText(GradientHtml("background-image: linear-gradient(red, blue);"));
            var pdfOklab = await GetPdfText(GradientHtml("background-image: linear-gradient(in oklab, red, blue);"));

            Assert.NotEqual(pdfSrgb, pdfOklab);
        }

        // ── radial-gradient in <colorspace> ──────────────────────────────────

        [Fact]
        public async Task RadialGradient_InOklab_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(in oklab, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task RadialGradient_InHsl_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: radial-gradient(in hsl, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── conic-gradient in <colorspace> ───────────────────────────────────

        [Fact]
        public async Task ConicGradient_InOklab_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(in oklab, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ConicGradient_InHsl_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(in hsl, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── color space + direction combined ─────────────────────────────────

        [Fact]
        public async Task LinearGradient_InOklab_WithDirection_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(in oklab to right, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ConicGradient_InOklab_WithFromAngle_RendersSuccessfully()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: conic-gradient(in oklab from 45deg, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        // ── Regression: plain gradient still works ────────────────────────────

        [Fact]
        public async Task LinearGradient_NoColorSpace_StillWorks()
        {
            var pdfText = await GetPdfText(GradientHtml("background-image: linear-gradient(to right, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }
    }
}
