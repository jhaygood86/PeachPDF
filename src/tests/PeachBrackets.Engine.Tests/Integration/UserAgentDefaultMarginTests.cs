using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The user-agent default margins are the ones the HTML Standard's rendering section (and every browser) uses, not the CSS 2.1 sample sheet's.
    /// </summary>
    public class UserAgentDefaultMarginTests
    {
        [Theory]
        [InlineData("p", 12.0)]
        [InlineData("ul", 12.0)]
        [InlineData("blockquote", 12.0)]
        [InlineData("form", 0.0)]
        public async Task TheUserAgentMarginOfAParagraphAndListIsOneEm_AndAFormHasNone(string tag, double expectedPoints)
        {
            // Font size 16px = 12pt, so 1em is 12pt.
            var (root, _) = await LayoutAsync(Wrap($"<div style='font-size:16px'><{tag} id='e'>x</{tag}></div>"));

            Assert.Equal(expectedPoints, FindById(root, "e")!.ActualMarginTop, 2);
        }

        [Theory]
        [InlineData("h2", 0.83 * 1.5)]
        [InlineData("h3", 1.17)]
        [InlineData("h4", 1.33)]
        [InlineData("h5", 1.67 * 0.83)]
        [InlineData("h6", 2.33 * 0.67)]
        public async Task TheUserAgentHeadingMarginsAreTheHtmlStandardsOnes(string tag, double ems)
        {
            // The margin is in em of the heading's own font size; the expected value is that margin in points at a 12pt body.
            var (root, _) = await LayoutAsync(Wrap($"<div style='font-size:16px'><{tag} id='e'>x</{tag}></div>"));

            Assert.Equal(ems * 12, FindById(root, "e")!.ActualMarginTop, 1);
        }
    }
}
