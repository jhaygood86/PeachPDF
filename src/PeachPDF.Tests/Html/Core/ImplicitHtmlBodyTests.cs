using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// A document that omits its <c>&lt;html&gt;</c>/<c>&lt;body&gt;</c> start tags still gets both
    /// elements (HTML parsing "before html"/"after head" insertion modes), so rules selecting them apply.
    /// </summary>
    public class ImplicitHtmlBodyTests
    {
        private static async Task<HtmlContainerInt> LoadAsync(string html)
        {
            var container = new HtmlContainerInt(new PdfSharpAdapter
            {
                PixelsPerPoint = 1.0,
                NetworkLoader = new InMemoryNetworkLoader(new RUri("https://t.test/"), string.Empty)
            });
            await container.SetHtml(html, null);
            return container;
        }

        [Theory]
        [InlineData("<style>body{color:red}</style><p>a</p>")]
        [InlineData("<style>body{color:red}</style><body><p>a</p></body>")]
        [InlineData("<html><style>body{color:red}</style><p>a</p></html>")]
        public async Task BodyRuleAppliesWhetherOrNotTagsAreWritten(string html)
        {
            var c = await LoadAsync(html);
            var body = DomUtils.GetBoxByTagName(c.Root, "body");
            Assert.NotNull(body);
            Assert.Equal("rgb(255, 0, 0)", body!.Color);
            var p = DomUtils.GetBoxByTagName(c.Root, "p");
            Assert.Equal("rgb(255, 0, 0)", p!.Color);
        }

        [Fact]
        public async Task HtmlRuleAppliesToImplicitHtml()
        {
            var c = await LoadAsync("<style>html{color:red}</style><p>a</p>");
            Assert.Equal("rgb(255, 0, 0)", DomUtils.GetBoxByTagName(c.Root, "p")!.Color);
        }

        [Fact]
        public async Task ExplicitBodyIsNotWrappedAgain()
        {
            var c = await LoadAsync("<html><body><p>a</p></body></html>");
            var body = DomUtils.GetBoxByTagName(c.Root, "body")!;
            Assert.Null(DomUtils.GetBoxByTagName(body, "body") is { } inner && inner != body ? inner : null);
        }
    }
}
