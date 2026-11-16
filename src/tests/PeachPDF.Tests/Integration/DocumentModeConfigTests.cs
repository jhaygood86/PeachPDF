using PeachDrawing.PDF.Engine;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="PdfGenerateConfig.DocumentMode"/>: a document with no DOCTYPE is laid out in quirks mode, as a browser would, unless the caller says
    /// otherwise. The first heading of a body has no top margin in quirks mode, so where an anchor on it lands tells the two modes apart.
    /// </summary>
    public class DocumentModeConfigTests
    {
        private const string Body = "<html><body><h1 id='t'>Title</h1><p><a href='#t'>go</a></p></body></html>";

        private static async Task<string> DestinationAsync(string html, PdfDocumentMode mode)
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, DocumentMode = mode };
            var result = await new PdfGenerator().GeneratePdf(html, config);

            return Assert.IsType<PdfArr>(result.Inspect().NamedDestination("t")).Render();
        }

        [Fact]
        public async Task ADocumentWithNoDoctypeIsLaidOutInQuirksModeByDefault()
        {
            var quirks = await DestinationAsync(Body, PdfDocumentMode.Auto);

            Assert.Equal(quirks, await DestinationAsync(Body, PdfDocumentMode.Quirks));
            Assert.NotEqual(quirks, await DestinationAsync(Body, PdfDocumentMode.NoQuirks));
        }

        [Fact]
        public async Task NoQuirksLaysADoctypelessDocumentOutLikeOneWithADoctype()
        {
            var standards = await DestinationAsync("<!DOCTYPE html>" + Body, PdfDocumentMode.Auto);

            Assert.Equal(standards, await DestinationAsync(Body, PdfDocumentMode.NoQuirks));
        }

        [Fact]
        public async Task QuirksPutsADocumentWithADoctypeInQuirksMode()
        {
            var quirks = await DestinationAsync(Body, PdfDocumentMode.Auto);

            Assert.Equal(quirks, await DestinationAsync("<!DOCTYPE html>" + Body, PdfDocumentMode.Quirks));
        }

        [Fact]
        public async Task LimitedQuirksKeepsTheStandardsMarginsOfTheBody()
        {
            var standards = await DestinationAsync("<!DOCTYPE html>" + Body, PdfDocumentMode.Auto);

            Assert.Equal(standards, await DestinationAsync(Body, PdfDocumentMode.LimitedQuirks));
        }

        [Theory]
        [InlineData("<html><body style='background:red'>short</body></html>")]
        [InlineData("<html><body><h1>a</h1><p>short</p></body></html>")]
        [InlineData("<html><head><style>@page { size: A4; margin: 30mm }</style></head><body>short</body></html>")]
        public async Task AShortDocumentInQuirksModeStillMakesOnePage(string html)
        {
            // html and body are as tall as the first page's area, and must not spill a blank second page.
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.Equal(1, result.Inspect().PageCount);
        }

        [Fact]
        public async Task ALeadingByteOrderMarkBeforeADoctypeDoesNotMakeTheDocumentQuirks()
        {
            var standards = await DestinationAsync("<!DOCTYPE html>" + Body, PdfDocumentMode.Auto);

            Assert.Equal(standards, await DestinationAsync("\uFEFF<!DOCTYPE html>" + Body, PdfDocumentMode.Auto));
        }
    }
}
