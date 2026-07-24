using PeachPDF;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end regression tests for faux-bold/italic synthesis: previously-dead
    /// <c>StyleSimulations</c>/<c>MustSimulateBold</c>/<c>MustSimulateItalic</c> plumbing in the embedded
    /// PDFsharp fork is now actually wired from <c>FontResolver.ResolveTypeface</c>'s nearest-weight/style
    /// matching through to <c>XGraphicsPdfRenderer</c>'s already-existing fill+stroke (bold) / X-offset
    /// shear (italic) rendering. Verified via the PDF content stream's text render-mode operator (<c>Tr</c>)
    /// per PDF spec Table 5.2 - mode 2 is "fill, then stroke text," the exact, unambiguous signal bold
    /// simulation is engaged (comment in <c>PdfGraphicsState.RealizeFont</c> confirms 0/2 are the only two
    /// modes this renderer ever emits), not a fuzzy content-stream substring guess.
    /// </summary>
    public class FontSynthesisIntegrationTests
    {
        [Fact]
        public async Task BoldRequest_RegularOnlyFamily_EngagesFillAndStrokeRenderMode()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var b64 = Convert.ToBase64String(ttfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestSynthBold'; src: url('data:font/truetype;base64,{b64}'); }}
body {{ font-family: 'TestSynthBold'; font-size: 14pt; font-weight: bold; }}
</style></head>
<body>Bold text with no real bold face</body>
</html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var pdfText = GetPdfText(doc);

            Assert.Contains("2 Tr", pdfText);
        }

        [Fact]
        public async Task NormalWeightRequest_RegularOnlyFamily_UsesFillOnlyRenderMode()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var b64 = Convert.ToBase64String(ttfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestSynthNormal'; src: url('data:font/truetype;base64,{b64}'); }}
body {{ font-family: 'TestSynthNormal'; font-size: 14pt; font-weight: normal; }}
</style></head>
<body>Normal text, no synthesis expected</body>
</html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var pdfText = GetPdfText(doc);

            Assert.DoesNotContain("2 Tr", pdfText);
        }

        [Fact]
        public async Task ObliqueWithExplicitAngle_RegularOnlyFamily_UsesDeclaredAngleNotFixedDefault()
        {
            // CSS Fonts Level 4 "oblique <angle>" (e.g. oblique 10deg) must drive the exact faux-italic
            // shear amount when synthesis is needed, instead of the renderer's fixed sin(20deg)
            // approximation - see XFont.ObliqueSkewSinus / FontObliqueAngleResolver.
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var b64 = Convert.ToBase64String(ttfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestSynthOblique'; src: url('data:font/truetype;base64,{b64}'); }}
body {{ font-family: 'TestSynthOblique'; font-size: 14pt; font-style: oblique 10deg; }}
</style></head>
<body>Oblique text with an explicit angle, no real oblique face</body>
</html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var pdfText = GetPdfText(doc);

            var expectedSkew = Math.Sin(10.0 * Math.PI / 180.0).ToString("0.####");
            var defaultSkew = Math.Sin(20.0 * Math.PI / 180.0).ToString("0.####");

            Assert.Contains(expectedSkew, pdfText);
            Assert.DoesNotContain(defaultSkew, pdfText);
        }

        private static string GetPdfText(PeachPdfDocument doc)
        {
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
