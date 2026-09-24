using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The universal selector, <c>:not()</c> and <c>:lang()</c> match elements only (Selectors 4 §5.2, §4.3,
    /// §7.2); a bare text node "cannot be targeted by selectors" and gets every value by inheritance
    /// (Cascade 4 §1.1, §7.2). Matching the anonymous text box directly made a <c>* { color }</c> rule beat
    /// any more specific rule for the parent element, whatever its specificity or source order.
    /// </summary>
    public class UniversalSelectorTextNodeIntegrationTests
    {
        private static async Task<(CssBox Element, CssBox Text)> LayoutSingleAsync(string style, string body)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<style>{style}</style>{body}"));
            var element = LayoutHarness.FindById(root, "e")!;
            var text = Assert.Single(element.Boxes, b => b.HtmlTag is null && !b.IsPseudoElement);
            Assert.Single(text.Words);
            return (element, text);
        }

        [Fact]
        public async Task Universal_DoesNotOverrideMoreSpecificRuleOnText()
        {
            var (h1, text) = await LayoutSingleAsync(
                "* { color: red; font-size: 30pt } h1 { color: blue; font-size: 10pt }",
                "<h1 id='e'>H</h1>");

            Assert.Equal("rgb(0, 0, 255)", h1.Color);
            Assert.Equal("rgb(0, 0, 255)", text.Color);
            Assert.Equal(h1.FontSize, text.FontSize);
            Assert.Equal(10d, text.ActualFont.Size, 3);
        }

        [Fact]
        public async Task Universal_DoesNotOverrideMoreSpecificFontFamilyDeclaredEarlier()
        {
            // Source order must not help either: the h1 rule comes first, "*" last.
            var (h1, text) = await LayoutSingleAsync(
                "h1 { font-family: \"Segoe UI\", sans-serif } * { font-family: arial }",
                "<h1 id='e'>H</h1>");

            Assert.Contains("Segoe UI", h1.FontFamilyList);
            Assert.Equal(h1.FontFamilyList, text.FontFamilyList);
        }

        [Fact]
        public async Task DescendantUniversal_DoesNotMatchBareText()
        {
            var (div, text) = await LayoutSingleAsync("div * { color: red }", "<div id='e'>plain</div>");

            Assert.Equal(div.Color, text.Color);
            Assert.NotEqual("rgb(255, 0, 0)", text.Color);
        }

        [Fact]
        public async Task DescendantUniversal_StillMatchesChildElement()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>div * { color: red }</style><div><span id='e'>x</span></div>"));

            var span = LayoutHarness.FindById(root, "e")!;
            Assert.Equal("rgb(255, 0, 0)", span.Color);
            Assert.Equal("rgb(255, 0, 0)", Assert.Single(span.Boxes).Color);
        }

        [Fact]
        public async Task Universal_StillStylesElementAndTextInheritsIt()
        {
            var (span, text) = await LayoutSingleAsync("* { color: red }", "<span id='e'>x</span>");

            Assert.Equal("rgb(255, 0, 0)", span.Color);
            Assert.Equal("rgb(255, 0, 0)", text.Color);
        }

        [Fact]
        public async Task Universal_NonInheritedResetStillAppliesToElement()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>* { margin-left: 7pt }</style><div id='e'>x</div>"));

            var div = LayoutHarness.FindById(root, "e")!;
            Assert.Equal(7d, div.ActualMarginLeft, 3);
        }

        [Fact]
        public async Task Not_DoesNotMatchBareText()
        {
            var (div, text) = await LayoutSingleAsync(
                "div { color: blue } :not(.nope) { color: red } div { color: blue !important }",
                "<div id='e'>plain</div>");

            // The !important rule is only there to keep the element's own colour fixed; what matters is
            // that the text follows the element instead of being hit by :not() directly.
            Assert.Equal("rgb(0, 0, 255)", div.Color);
            Assert.Equal("rgb(0, 0, 255)", text.Color);
        }

        [Fact]
        public async Task Lang_DoesNotMatchBareText()
        {
            var (div, text) = await LayoutSingleAsync(
                "div { color: blue } :lang(en) { color: red } div { color: blue !important }",
                "<div id='e' lang='en'>plain</div>");

            Assert.Equal("rgb(0, 0, 255)", div.Color);
            Assert.Equal("rgb(0, 0, 255)", text.Color);
        }

        [Fact]
        public async Task Not_StillMatchesElement()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>div:not(.nope) { color: red }</style><div id='e' class='x'>t</div>"));

            Assert.Equal("rgb(255, 0, 0)", LayoutHarness.FindById(root, "e")!.Color);
        }

        [Fact]
        public async Task Lang_StillMatchesElement()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>div:lang(en) { color: red }</style><div id='e' lang='en-US'>t</div>"));

            Assert.Equal("rgb(255, 0, 0)", LayoutHarness.FindById(root, "e")!.Color);
        }
    }
}
