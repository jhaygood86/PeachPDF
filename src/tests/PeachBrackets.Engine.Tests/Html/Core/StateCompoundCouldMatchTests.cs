using PeachBrackets.Engine.Tests.CSS;
using PeachBrackets.Html;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    public class StateCompoundCouldMatchTests
    {
        private const string Body = "<a id=link class=k href='#'>x</a><b id=other>y</b><svg id=s width='10' height='10'></svg>";

        // Whether a change of the state of the element with the id could make the rule's selector match differently.
        private static async Task<bool> CouldAsync(string selector, string id)
        {
            var sheet = CssConstructionFunctions.ParseStyleSheet($"{selector} {{ fill: red }}");
            var rule = sheet.StyleRules.Single();
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Body));
            return CssData.StateCompoundCouldMatch(rule.Selector, LayoutHarness.FindById(root, id)!);
        }

        [Theory]
        [InlineData("a:hover + svg .x", "link", true)]
        [InlineData("a:hover + svg .x", "other", false)]
        [InlineData("a.k:hover ~ svg .x", "link", true)]
        [InlineData("a.nope:hover ~ svg .x", "link", false)]
        [InlineData("#link:focus .x", "link", true)]
        [InlineData(":hover .x", "other", true)]
        [InlineData("a:hover, b:active svg", "other", true)]
        [InlineData("a:hover, b:active svg", "s", true)]
        [InlineData(".x ~ svg", "link", false)]
        [InlineData(":not(:hover) .x", "other", true)]
        [InlineData(".k:has(:hover) + svg .x", "other", true)]
        [InlineData(":is(a, b):hover + svg .x", "other", true)]
        [InlineData("a:hover::before", "other", false)]
        [InlineData(":not(:active) .x", "other", true)]
        [InlineData(":is(:focus-within) .x", "other", true)]
        [InlineData(":not(.k) .x", "other", false)]
        public async Task TestsTheStateCompoundAgainstTheChangedElement(string selector, string id, bool expected) =>
            Assert.Equal(expected, await CouldAsync(selector, id));
    }
}
