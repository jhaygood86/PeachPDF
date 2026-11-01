using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A <see cref="PdfGenerateConfig"/> that names no page size renders on US Letter instead of a page with no content area; an explicit
    /// <see cref="PdfGenerateConfig.PageSize"/>, a manual size or an <c>@page { size }</c> rule still wins.
    /// </summary>
    public class DefaultPageSizeTests
    {
        private const string Html = "<html><body><p>Hello world</p></body></html>";

        private static async Task<PeachPdfDocument> Render(string html, PdfGenerateConfig config) => await new PdfGenerator().GeneratePdf(html, config);

        [Fact]
        public async Task ABareConfig_IsLetterAndKeepsItsText()
        {
            var doc = await Render(Html, new PdfGenerateConfig { CompressContentStreams = false });
            var page = doc.Page(0);

            Assert.Equal(612, page.Width, 3);
            Assert.Equal(792, page.Height, 3);
            Assert.Contains("Tj", string.Concat(page.Contents.Select(c => c.DecodedText)));
        }

        [Fact]
        public async Task LandscapeOnABareConfig_SwapsLetter()
        {
            var doc = await Render(Html, new PdfGenerateConfig { PageOrientation = PageOrientation.Landscape });
            Assert.Equal(792, doc.Page(0).Width, 3);
            Assert.Equal(612, doc.Page(0).Height, 3);
        }

        [Fact]
        public async Task AnExplicitPageSize_Overrides()
        {
            var doc = await Render(Html, new PdfGenerateConfig { PageSize = PageSize.A4 });
            Assert.Equal(595, doc.Page(0).Width, 0);
        }

        [Fact]
        public async Task ABothDimensionManualSize_Overrides()
        {
            var doc = await Render(Html, new PdfGenerateConfig { ManualPageWidth = 300, ManualPageHeight = 400 });
            Assert.Equal(300, doc.Page(0).Width, 3);
            Assert.Equal(400, doc.Page(0).Height, 3);
        }

        [Fact]
        public async Task AManualSizeWithOneDimension_FallsBackToLetter()
        {
            var doc = await Render(Html, new PdfGenerateConfig { ManualPageWidth = 300 });
            Assert.Equal(612, doc.Page(0).Width, 3);
        }

        [Fact]
        public async Task AnAtPageSizeRule_StillWinsOverTheDefault()
        {
            var doc = await Render("<html><head><style>@page { size: 200pt 300pt }</style></head><body>x</body></html>", new PdfGenerateConfig());
            Assert.Equal(200, doc.Page(0).Width, 3);
            Assert.Equal(300, doc.Page(0).Height, 3);
        }
    }
}
