using PeachDrawing.PDF.Engine;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <c>rem</c> length in <c>letter-spacing</c>, <c>word-spacing</c> or <c>text-indent</c> is read as a rem length (its trailing "em"
    /// used to be taken for the unit), so the document renders and the value moves the text.
    /// </summary>
    public class RemTextSpacingIntegrationTests
    {
        private static async Task<string> GetPdfText(string style)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(
                $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font: 14px Arial; }}</style></head><body><p style=\"{style}\">hello world</p></body></html>", config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Theory]
        [InlineData("letter-spacing: 1.5rem")]
        [InlineData("word-spacing: 3rem")]
        [InlineData("text-indent: 2rem")]
        [InlineData("text-indent: 2rem hanging")]
        public async Task ARemValue_RendersAndChangesTheOutput(string style)
        {
            var baseline = await GetPdfText("");
            var withRem = await GetPdfText(style);

            Assert.NotEqual(baseline, withRem);
        }
    }
}
