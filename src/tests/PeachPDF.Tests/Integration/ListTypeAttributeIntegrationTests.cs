using PeachPDF;
using PeachDrawing.PDF.Engine;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The <c>type</c> attribute on ol/ul/li is a presentational hint for <c>list-style-type</c>: the PDF it
    /// produces is the same as the one written with the CSS property, differs from the plain list, and author
    /// CSS beats the hint.
    /// </summary>
    public class ListTypeAttributeIntegrationTests
    {
        private static async Task<string> Render(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>body { margin: 0; }</style></head><body>" + body + "</body></html>";
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var file = doc.Inspect();
            return string.Concat(file.Pages.Select(file.ContentText));
        }

        [Theory]
        [InlineData("<ol type='a' start='3'><li>x</li><li>y</li></ol>", "<ol style='list-style-type: lower-alpha' start='3'><li>x</li><li>y</li></ol>")]
        [InlineData("<ol type='A'><li>x</li></ol>", "<ol style='list-style-type: upper-alpha'><li>x</li></ol>")]
        [InlineData("<ol type='i'><li>x</li></ol>", "<ol style='list-style-type: lower-roman'><li>x</li></ol>")]
        [InlineData("<ol type='I'><li>x</li></ol>", "<ol style='list-style-type: upper-roman'><li>x</li></ol>")]
        [InlineData("<ol type='1'><li>x</li></ol>", "<ol style='list-style-type: decimal'><li>x</li></ol>")]
        [InlineData("<ul type='square'><li>x</li></ul>", "<ul style='list-style-type: square'><li>x</li></ul>")]
        [InlineData("<ul type='CIRCLE'><li>x</li></ul>", "<ul style='list-style-type: circle'><li>x</li></ul>")]
        [InlineData("<ul type='disc'><li>x</li></ul>", "<ul style='list-style-type: disc'><li>x</li></ul>")]
        [InlineData("<ol><li type='I'>x</li></ol>", "<ol><li style='list-style-type: upper-roman'>x</li></ol>")]
        [InlineData("<ul><li type='square'>x</li></ul>", "<ul><li style='list-style-type: square'>x</li></ul>")]
        public async Task TypeAttribute_RendersLikeTheCssProperty(string withAttribute, string withCss)
        {
            Assert.Equal(await Render(withCss), await Render(withAttribute));
        }

        [Fact]
        public async Task TypeAttribute_ChangesTheOutput()
        {
            Assert.NotEqual(await Render("<ol><li>x</li></ol>"), await Render("<ol type='a'><li>x</li></ol>"));
            Assert.NotEqual(await Render("<ul><li>x</li></ul>"), await Render("<ul type='square'><li>x</li></ul>"));
        }

        [Theory]
        [InlineData("<ol type='B'><li>x</li></ol>")]
        [InlineData("<ol type='bogus'><li>x</li></ol>")]
        [InlineData("<ul type='1'><li>x</li></ul>")]
        [InlineData("<div type='a'><ol><li>x</li></ol></div>")]
        public async Task UnrecognizedOrMisplacedType_IsIgnored(string html)
        {
            var plain = html.Replace(" type='B'", "").Replace(" type='bogus'", "").Replace(" type='1'", "").Replace(" type='a'", "");
            Assert.Equal(await Render(plain), await Render(html));
        }

        [Fact]
        public async Task AuthorCss_BeatsTheHint()
        {
            Assert.Equal(
                await Render("<ol style='list-style-type: lower-roman'><li>x</li></ol>"),
                await Render("<ol type='a' style='list-style-type: lower-roman'><li>x</li></ol>"));
        }
    }
}
