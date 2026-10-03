using PeachPDF;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;


namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end regression tests for faux-bold/italic synthesis: previously-dead
    /// <c>StyleSimulations</c>/<c>MustSimulateBold</c>/<c>MustSimulateItalic</c> plumbing in the embedded
    /// PDFsharp fork is now actually wired from <c>FontResolver.ResolveTypeface</c>'s nearest-weight/style
    /// matching through to <c>XGraphicsPdfRenderer</c>'s already-existing fill+stroke (bold) / X-offset
    /// shear (italic) rendering. Verified via the PDF content stream's text render-mode operator (<c>Tr</c>)
    /// per PDF spec Table 5.2 - mode 2 is "fill, then stroke text," the exact, unambiguous signal bold
    /// simulation is engaged, not a fuzzy content-stream substring guess.
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

            var expectedSkew = Math.Sin(10.0 * Math.PI / 180.0).ToString("0.####", CultureInfo.InvariantCulture);
            var defaultSkew = Math.Sin(20.0 * Math.PI / 180.0).ToString("0.####", CultureInfo.InvariantCulture);

            Assert.Contains(expectedSkew, pdfText);
            Assert.DoesNotContain(defaultSkew, pdfText);
        }

        [Theory]
        [InlineData("font-synthesis: none")]
        [InlineData("font-synthesis-weight: none")]
        [InlineData("font-synthesis: style")]
        public async Task FontSynthesisWeightNone_RemovesFauxBoldFillAndStroke(string declaration)
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var b64 = Convert.ToBase64String(ttfBytes);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestSynthOff'; src: url('data:font/truetype;base64,{b64}'); }}
body {{ font-family: 'TestSynthOff'; font-size: 14pt; font-weight: bold; {declaration} }}
</style></head>
<body>Bold text with synthesis forbidden</body>
</html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var pdfText = GetPdfText(await generator.GeneratePdf(html, config));

            Assert.DoesNotContain("2 Tr", pdfText);
        }

        [Fact]
        public async Task FontSynthesisStyleNone_RemovesFauxItalicShear_ButKeepsFauxBold()
        {
            var ttfBytes = File.ReadAllBytes(BundledFonts.Ttf);
            var b64 = Convert.ToBase64String(ttfBytes);

            string Html(string declaration) => $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TestSynthStyle'; src: url('data:font/truetype;base64,{b64}'); }}
body {{ font-family: 'TestSynthStyle'; font-size: 14pt; font-weight: bold; font-style: oblique 10deg; {declaration} }}
</style></head>
<body>Oblique and bold</body>
</html>";

            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var skew = Math.Sin(10.0 * Math.PI / 180.0).ToString("0.####", CultureInfo.InvariantCulture);

            var withSynthesis = GetPdfText(await new PdfGenerator().GeneratePdf(Html(""), config));
            var styleOff = GetPdfText(await new PdfGenerator().GeneratePdf(Html("font-synthesis-style: none"), config));

            Assert.Contains(skew, withSynthesis);
            Assert.DoesNotContain(skew, styleOff);
            Assert.Contains("2 Tr", styleOff);
        }

        private static string GetPdfText(PeachPdfDocument doc)
        {
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
