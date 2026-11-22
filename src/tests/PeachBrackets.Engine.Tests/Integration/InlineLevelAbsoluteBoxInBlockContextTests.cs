using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An inline-level absolute box among block-level siblings has the static position of a line of its own (css-position-3 section 3.4): below the block before it, as narrow as the
    /// floats there leave it, and aligned by the block's text-align as an empty line is. These are the css-position inline-level-absolute-in-block-level-context tests.
    /// </summary>
    public class InlineLevelAbsoluteBoxInBlockContextTests
    {
        private static async Task<(CssBox Container, CssBox Box)> LayoutAsync(string containerStyle, string floatStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0} #c{position:relative;width:100pt;height:100pt} #f{width:50pt;height:50pt} #a{display:inline;position:absolute;width:50pt;height:50pt}</style>"
                + $"<div id='c' style='{containerStyle}'><div style='height:50pt'></div><div id='f' style='{floatStyle}'></div><div id='a'></div></div>"));
            return (LayoutHarness.FindById(root, "c")!, LayoutHarness.FindById(root, "a")!);
        }

        [Theory]
        [InlineData("direction:ltr;text-align:left", "float:left", 50)]
        [InlineData("direction:ltr;text-align:center", "float:left", 75)]
        [InlineData("direction:ltr;text-align:right", "float:left", 100)]
        [InlineData("direction:ltr;text-align:left", "float:right", 0)]
        [InlineData("direction:ltr;text-align:center", "float:right", 25)]
        [InlineData("direction:ltr;text-align:right", "float:right", 50)]
        public async Task TheBoxStartsWhereTheAlignedLineBesideTheFloatDoes_InALeftToRightBlock(string container, string floated, double expectedLeft)
        {
            var (c, a) = await LayoutAsync(container, floated);

            Assert.Equal(c.ClientLeft + expectedLeft, a.Location.X, 0.5);
            Assert.Equal(c.ClientTop + 50, a.Location.Y, 0.5);
        }

        [Theory]
        [InlineData("direction:rtl;text-align:left", "float:left", 50)]
        [InlineData("direction:rtl;text-align:left", "float:right", 0)]
        [InlineData("direction:rtl;text-align:right", "float:left", 100)]
        public async Task InARightToLeftBlock_TheBoxsRightEdgeIsWhereTheLineStarts(string container, string floated, double expectedRight)
        {
            var (c, a) = await LayoutAsync(container, floated);

            Assert.Equal(c.ClientLeft + expectedRight, a.ActualRight, 0.5);
        }

        [Fact]
        public async Task ABoxThatWasBlockLevel_KeepsTheBlocksStartEdge()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0} #c{position:relative;width:100pt;height:100pt;text-align:right} #f{float:left;width:50pt;height:50pt} #a{position:absolute;width:50pt;height:50pt}</style>"
                + "<div id='c'><div id='f'></div><div id='a'></div></div>"));

            Assert.Equal(LayoutHarness.FindById(root, "c")!.ClientLeft, LayoutHarness.FindById(root, "a")!.Location.X, 0.5);
        }
    }
}
