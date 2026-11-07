using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-text-4 <c>text-wrap</c>, <c>text-wrap-mode</c>, <c>text-wrap-style</c> and <c>white-space-collapse</c>,
    /// driven through <see cref="PdfGenerator"/> the way a caller reaches them. The layout details are asserted on
    /// the box tree in <c>PeachBrackets.Engine.Tests</c>; this pins the end-to-end behaviour on the page text.
    /// </summary>
    public class TextWrapRenderingTests
    {
        private const string Heading = "Balanced headings avoid lonely words";

        private static async Task<int> PagesAsync(string body)
        {
            var doc = await new PdfGenerator().GeneratePdf(
                $"<html><body>{body}</body></html>",
                new PdfGenerateConfig { PageSize = PageSize.Letter });
            return doc.Inspect().Pages.Count;
        }

        [Theory]
        [InlineData("text-wrap: balance")]
        [InlineData("text-wrap: nowrap")]
        [InlineData("text-wrap: nowrap pretty")]
        [InlineData("text-wrap-style: pretty")]
        [InlineData("text-wrap-style: stable")]
        [InlineData("text-wrap-mode: nowrap")]
        [InlineData("white-space-collapse: preserve")]
        [InlineData("white-space-collapse: preserve-breaks")]
        [InlineData("white-space-collapse: preserve-spaces")]
        [InlineData("white-space-collapse: break-spaces")]
        [InlineData("white-space: break-spaces nowrap")]
        [InlineData("white-space: pre-wrap")]
        [InlineData("white-space: pre-line")]
        public async Task EveryKeywordRenders(string declaration)
        {
            Assert.Equal(1, await PagesAsync(
                $"<div style=\"width:200pt;font:bold 20pt Arial;{declaration}\">{Heading}\n  and   more\ntext</div>"));
        }

        [Fact]
        public async Task BalanceInAVerticalWritingModeAndNestedNowrapRuns()
        {
            Assert.Equal(1, await PagesAsync(
                "<div style=\"writing-mode:vertical-rl;height:120pt;white-space:nowrap\">one two three four five six</div>" +
                "<div style=\"writing-mode:vertical-rl;height:120pt;white-space:pre-wrap\">one two   three four five six</div>" +
                "<div style=\"writing-mode:vertical-rl;height:120pt;white-space:break-spaces;text-wrap:balance\">one two   three four five six</div>" +
                "<div style=\"width:150pt\">before <span style=\"text-wrap-mode:nowrap\">unbreakable run of words</span> after it</div>" +
                "<div style=\"width:150pt;white-space:pre\">before <span style=\"white-space:pre\">kept   run</span> after</div>" +
                "<table><tr><td nowrap>a long cell that does not wrap at all because of the attribute</td></tr></table>"));
        }

        [Fact]
        public async Task BalancedParagraphThatSpansPagesStillRenders()
        {
            var words = string.Join(' ', Enumerable.Repeat("balance", 4000));
            Assert.True(await PagesAsync($"<p style=\"text-wrap:balance\">{words}</p>") > 1);
        }
    }
}
