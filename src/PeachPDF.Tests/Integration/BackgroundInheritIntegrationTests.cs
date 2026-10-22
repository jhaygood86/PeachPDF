using PeachPDF;
using PeachDrawing.PDF.Engine;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>background-image: inherit</c> (and <c>background: inherit</c>) takes the parent's image layers: the child draws its own copy of the image.
    /// </summary>
    public class BackgroundInheritIntegrationTests
    {
        private const string PngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static string Html(string childStyle) =>
            "<!DOCTYPE html><html><head><style>body { margin: 0; }</style></head><body>" +
            $"<div style=\"padding: 10pt; width: 100pt; height: 90pt; background: url('data:image/png;base64,{PngBase64}') no-repeat 5pt 5pt; background-size: 20pt 20pt;\">" +
            $"<div style=\"height: 40pt; margin-top: 40pt; {childStyle}\"></div></div></body></html>";

        private static int ImageDraws(string pdfText) => Regex.Matches(pdfText, @"q 20 0 0 20 ").Count;

        [Fact]
        public async Task TheParentAloneDrawsTheImageOnce() =>
            Assert.Equal(1, ImageDraws(await GetPdfText(Html(""))));

        [Theory]
        [InlineData("background: inherit;")]
        [InlineData("background-image: inherit; background-repeat: inherit; background-position: inherit; background-size: inherit;")]
        public async Task AChildThatInherits_DrawsItsOwnCopyOfTheImage(string childStyle) =>
            Assert.Equal(2, ImageDraws(await GetPdfText(Html(childStyle))));
    }
}
