using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>mask-image</c> in a written PDF: the masked element is a form drawn through an soft mask, never plain page content. The
    /// pixels are checked by the bitmap canvas tests in PeachBrackets.Tests and by rasterizing the showcase with PDFium and MuPDF.
    /// </summary>
    public class MaskImageIntegrationTests
    {
        private static async Task<string> PdfText(string style)
        {
            var html = $"<!DOCTYPE html><html><body style='margin:0'><div style='width:200px;height:50px;background:#00f;{style}'></div></body></html>";
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task AnAlphaMask_IsWrittenAsAnAlphaSoftMask()
        {
            var text = await PdfText("mask-image: linear-gradient(to right, #000, transparent)");

            Assert.Matches(new Regex(@"/SMask\s*<<[^>]*?/S\s*/Alpha|/S\s*/Alpha", RegexOptions.Singleline), text);
            Assert.Contains("/SMask", text);
        }

        [Fact]
        public async Task ALuminanceMask_IsWrittenAsALuminositySoftMask()
        {
            var text = await PdfText("mask-image: linear-gradient(to right, #fff, #000); mask-mode: luminance");

            Assert.Matches(new Regex(@"/S\s*/Luminosity"), text);
        }

        [Fact]
        public async Task WithoutAMask_NoSoftMaskIsWritten()
        {
            var text = await PdfText("mask-image: none");

            Assert.DoesNotContain("/SMask", text);
        }
    }
}
