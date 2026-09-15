using PeachPDF;
using PeachPDF.PdfSharpCore;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for the "same-space (all-<c>device-cmyk()</c>-stop) gradients" gap closure:
    /// a gradient whose stops are all <c>device-cmyk()</c> now interpolates directly in C/M/Y/K space and
    /// writes a real <c>/DeviceCMYK</c> shading, rather than being rejected outright. A gradient mixing
    /// <c>device-cmyk()</c> and RGB-authored stops still has no defined conversion and keeps throwing - see
    /// <c>PdfSharpAdapterCmykGradientTests</c> for the adapter-level guard behavior and
    /// <c>.claude/recent-fixes/</c> for why this doesn't extend to mixed-space gradients. These are
    /// structural assertions on the actual shading dictionary (component count, color space name), not
    /// just token presence - a passing content-stream-substring check alone would not catch a component
    /// -count mismatch between <c>/ColorSpace</c> and <c>/C0</c>/<c>/C1</c>.
    /// </summary>
    public class DeviceCmykGradientIntegrationTests
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

        [Fact]
        public async Task LinearGradient_TwoCmykStops_UsesDeviceCmykWithFourComponents()
        {
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: linear-gradient(device-cmyk(0 1 1 0), device-cmyk(1 0 0 0));"));

            Assert.Contains("/DeviceCMYK", pdfText);
            Assert.DoesNotContain("/DeviceRGB", pdfText);
            // C0 = [0 1 1 0] (print red), 4 numeric components, not 3.
            Assert.Matches(new Regex(@"/C0\s*\[\s*0(\.0+)?\s+1(\.0+)?\s+1(\.0+)?\s+0(\.0+)?\s*\]"), pdfText);
        }

        [Fact]
        public async Task LinearGradient_ThreeCmykStops_UsesStitchingFunctionInCmyk()
        {
            // Three stops force PdfShading.BuildStitchingFunction's Type 3 path rather than the plain
            // 2-color Type 2 shortcut - both paths need the same colorMode-per-shading fix.
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: linear-gradient(device-cmyk(0 1 1 0), device-cmyk(1 0 0 0), device-cmyk(0 0 1 0));"));

            Assert.Contains("/DeviceCMYK", pdfText);
            Assert.Contains("/FunctionType 3", pdfText);
        }

        [Fact]
        public async Task RadialGradient_TwoCmykStops_UsesDeviceCmyk()
        {
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: radial-gradient(device-cmyk(0 0 0 1), device-cmyk(0 0 0 0));"));

            Assert.Contains("/DeviceCMYK", pdfText);
            Assert.DoesNotContain("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task ConicGradient_TwoCmykStops_UsesDeviceCmykAndFourComponentDecode()
        {
            var pdfText = await GetPdfText(GradientHtml(
                "background-image: conic-gradient(device-cmyk(0 1 1 0), device-cmyk(1 0 0 0));"));

            Assert.Contains("/DeviceCMYK", pdfText);
            Assert.DoesNotContain("/DeviceRGB", pdfText);
            // /Decode has 4 coordinate numbers (x/y range) + 4 "0 1" component pairs = 12 numbers for
            // CMYK, vs 10 for RGB - assert the CMYK-shaped tail specifically (4 trailing "0 1" pairs).
            Assert.Matches(new Regex(@"/Decode\s*\[[^\]]*0\s+1\s+0\s+1\s+0\s+1\s+0\s+1\s*\]"), pdfText);
        }

        [Fact]
        public async Task LinearGradient_MixedRgbAndCmykStops_ThrowsAtGenerationTime()
        {
            var html = GradientHtml("background-image: linear-gradient(red, device-cmyk(1 0 0 0));");

            // Painting wraps the guard's NotSupportedException in an HtmlRenderException (see
            // RenderErrorReportingTests) - unwrap to the real cause.
            var thrown = await Assert.ThrowsAsync<HtmlRenderException>(async () =>
                await new PdfGenerator().GeneratePdf(html, PageSize.A4));
            Exception? cause = thrown;
            while (cause.InnerException is { } inner) cause = inner;
            Assert.IsType<NotSupportedException>(cause);
        }
    }
}
