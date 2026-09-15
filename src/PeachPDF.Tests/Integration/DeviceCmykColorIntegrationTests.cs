using PeachPDF.Tests.TestSupport;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for issue #1083/#1084: a CSS <c>device-cmyk()</c> color reaches the PDF as a
    /// real <c>k</c>/<c>K</c> CMYK fill/stroke operator - not approximated or silently dropped - while an
    /// ordinary RGB-authored color in the same document still emits <c>rg</c>/<c>RG</c> unchanged (PDF's
    /// mixed-color-space document support, <see cref="PeachPDF.PdfSharpCore.Pdf.enums.PdfColorMode.Undefined"/>
    /// - see <c>PdfGenerator.RenderPagesCore</c>). This is a structural/value check on the actual emitted
    /// operator and its numbers (not just token presence), per this repo's own content-stream-substring
    /// pitfall warning.
    /// </summary>
    public class DeviceCmykColorIntegrationTests
    {
        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            // CompressContentStreams: false - the assertions below read operators directly out of the
            // page content stream, which is Flate-compressed (unreadable as plain text) by default.
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task TextColor_DeviceCmyk_EmitsRealCmykFillOperator()
        {
            var html = "<html><body style=\"margin:0\">" +
                "<p style=\"color: device-cmyk(0 1 1 0); font-size: 40pt\">CMYK</p>" +
                "</body></html>";

            var pdfText = await GetPdfText(html);

            // C=0 M=1 Y=1 K=0 (pure "print red") as a fill ('k') operator - not 'rg'.
            Assert.Matches(new Regex(@"0(\.0+)?\s+1(\.0+)?\s+1(\.0+)?\s+0(\.0+)?\s+k\b"), pdfText);
        }

        [Fact]
        public async Task BackgroundColor_DeviceCmyk_EmitsRealCmykFillOperator()
        {
            var html = "<html><body style=\"margin:0\">" +
                "<div style=\"background-color: device-cmyk(0.2 0.4 0.6 0.8); width: 100pt; height: 100pt\"></div>" +
                "</body></html>";

            var pdfText = await GetPdfText(html);

            Assert.Matches(new Regex(@"0\.2\d*\s+0\.4\d*\s+0\.6\d*\s+0\.8\d*\s+k\b"), pdfText);
        }

        [Fact]
        public async Task BorderColor_DeviceCmyk_EmitsRealCmykStrokeOperator()
        {
            var html = "<html><body style=\"margin:0\">" +
                "<div style=\"border: 4pt solid device-cmyk(1 0 0 0); width: 100pt; height: 100pt\"></div>" +
                "</body></html>";

            var pdfText = await GetPdfText(html);

            // Border painting may fill rectangles rather than stroke a path, so accept either the
            // fill ('k') or stroke ('K') CMYK operator for cyan (C=1 M=0 Y=0 K=0).
            Assert.Matches(new Regex(@"1(\.0+)?\s+0(\.0+)?\s+0(\.0+)?\s+0(\.0+)?\s+[kK]\b"), pdfText);
        }

        [Fact]
        public async Task MixedRgbAndCmyk_BothEmitTheirOwnOperators_NoForcedConversion()
        {
            // A document mixing an RGB-authored color and a device-cmyk()-authored color must write
            // both natively (PDF's mixed-color-space support) rather than forcing either through a
            // lossy conversion - see PdfDocumentOptions.ColorMode = Undefined in PdfGenerator.
            var html = "<html><body style=\"margin:0\">" +
                "<p style=\"color: rgb(0, 255, 0)\">RGB</p>" +
                "<p style=\"color: device-cmyk(0 0 0 1)\">CMYK</p>" +
                "</body></html>";

            var pdfText = await GetPdfText(html);

            Assert.Matches(new Regex(@"0(\.0+)?\s+1(\.0+)?\s+0(\.0+)?\s+rg\b"), pdfText);
            Assert.Matches(new Regex(@"0(\.0+)?\s+0(\.0+)?\s+0(\.0+)?\s+1(\.0+)?\s+k\b"), pdfText);
        }

        [Fact]
        public async Task AllRgbDocument_StillEmitsDeviceRgb_UndefinedModeIsNoOp()
        {
            // Regression guard for the PdfDocumentOptions.ColorMode default change (Rgb -> Undefined):
            // a document with no device-cmyk() anywhere must never emit a CMYK fill/stroke operator (4
            // space-separated numbers then 'k'/'K') - a bare "\bk\b" search would false-positive on
            // compressed/binary content-stream bytes incidentally decoding to the letter 'k'.
            var html = "<html><body style=\"margin:0\">" +
                "<p style=\"color: #336699; background-color: #eeeeee; border: 2pt solid #000\">plain</p>" +
                "</body></html>";

            var pdfText = await GetPdfText(html);
            var cmykOperator = new Regex(@"[\d.]+\s+[\d.]+\s+[\d.]+\s+[\d.]+\s+[kK]\b");

            Assert.DoesNotMatch(cmykOperator, pdfText);
        }
    }
}
