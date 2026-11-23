using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Which characters collapse as white space (css-text-3 section 4.1.1): the space, tab, segment break and ogham space mark do; the
    /// other Unicode space separators, the ideographic space among them, are ordinary characters that keep their width.
    /// </summary>
    public class CollapsibleWhitespaceTests
    {
        [Theory]
        [InlineData(' ', true)]
        [InlineData('\t', true)]
        [InlineData('\n', true)]
        [InlineData('\r', true)]
        [InlineData(' ', true)]
        [InlineData(' ', false)]
        [InlineData('　', false)]
        [InlineData(' ', false)]
        [InlineData(' ', false)]
        [InlineData('x', false)]
        public void IsCollapsibleWhitespace_FollowsTheCssDefinition(char c, bool expected) =>
            Assert.Equal(expected, HtmlUtils.IsCollapsibleWhitespace(c));

        [Fact]
        public async Task IdeographicSpace_IsNotCollapsedWithTheSpacesAroundIt()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<p id='p'>a　　b</p><p id='q'>a  b</p>"));
            var kept = string.Concat(LayoutHarness.Descendants(LayoutHarness.FindById(root, "p")!).SelectMany(b => b.Words).Select(w => w.Text));
            var collapsed = string.Concat(LayoutHarness.Descendants(LayoutHarness.FindById(root, "q")!).SelectMany(b => b.Words).Select(w => w.Text));

            Assert.Equal("a　　b", kept);
            Assert.DoesNotContain("  ", collapsed);
        }
    }
}
