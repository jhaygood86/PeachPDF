using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A logical margin whose value uses <c>var()</c> is applied after the plain declarations, but it still takes its place in the cascade where it
    /// was written: a later physical declaration (here the more specific rule's <c>margin: 0</c>) outranks it, as it does without the variable
    /// (CSS Logical Properties 1 section 1.1). The page's `p { margin-block: 0 var(--space-4) }` kept 16px under a footer paragraph that
    /// `.site-footer p { margin: 0 }` had set to nothing.
    /// </summary>
    public class DeferredVarLogicalMarginOrderTests
    {
        private static async Task<double> MarginBottomAsync(string css)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>:root { --s: 16pt } body { margin: 0 } " + css + "</style></head>"
                + "<body><div class='f'><p id='p'>text</p></div></body></html>");
            return LayoutHarness.FindById(root, "p")!.ActualMarginBottom;
        }

        [Theory]
        [InlineData("p { margin-block: 0 var(--s) } .f p { margin: 0 }", 0)]
        [InlineData(".f { & p { margin: 0 } } p { margin-block: 0 var(--s) }", 0)]
        [InlineData("p { margin-block: 0 var(--s) }", 16)]
        [InlineData("p { margin: 0 } p { margin-block: 0 var(--s) }", 16)]
        [InlineData("p { margin-block-end: var(--s) } p { margin-bottom: 4pt }", 4)]
        [InlineData("p { margin-bottom: 4pt } p { margin-block-end: var(--s) }", 16)]
        public async Task ADeferredLogicalMargin_KeepsItsPlaceInTheCascade(string css, double expectedBottom)
        {
            Assert.Equal(expectedBottom, await MarginBottomAsync(css), 3);
        }
    }
}
