using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The block margins the HTML Standard's rendering section gives paragraphs, headings, lists and the like. Every figure is what headless Chrome
    /// measures for the same markup: the gap, in CSS px, between a 5px block with no margin and the element, with the font size set to 14px.
    /// </summary>
    public class DefaultBlockMarginTests
    {
        private const double PointsPerPx = 0.75;

        private static async Task<(double Above, double Below)> GapsAsync(string element, string content, string elementStyle = "")
        {
            var body = $"<div style='font-size:14px'><div id='a' style='height:5px'></div><{element} id='x' style='{elementStyle}'>{content}</{element}><div id='b' style='height:5px'></div></div>";
            var (root, _) = await LayoutAsync(Wrap(body));
            var before = FindById(root, "a")!;
            var box = FindById(root, "x")!;
            var after = FindById(root, "b")!;
            return ((box.Location.Y - before.ActualBottom) / PointsPerPx, (after.Location.Y - box.ActualBottom) / PointsPerPx);
        }

        [Theory]
        [InlineData("p", "t", 14.0)]
        [InlineData("blockquote", "t", 14.0)]
        [InlineData("ul", "<li>t</li>", 14.0)]
        [InlineData("ol", "<li>t</li>", 14.0)]
        [InlineData("dl", "<dt>t</dt>", 14.0)]
        [InlineData("menu", "<li>t</li>", 14.0)]
        [InlineData("figure", "t", 14.0)]
        [InlineData("h1", "t", 18.76)]
        [InlineData("h2", "t", 17.43)]
        [InlineData("h3", "t", 16.38)]
        [InlineData("h4", "t", 18.62)]
        [InlineData("h5", "t", 19.41)]
        [InlineData("h6", "t", 21.85)]
        [InlineData("fieldset", "t", 0.0)]
        [InlineData("form", "t", 0.0)]
        public async Task AnElementsBlockMarginsAreTheStandardsAbove(string element, string content, double expectedPx)
        {
            var (above, below) = await GapsAsync(element, content);

            Assert.Equal(expectedPx, above, 0.1);
            Assert.Equal(expectedPx, below, 0.1);
        }

        [Fact]
        public async Task APreHasTheSameMarginAboveAndBelow()
        {
            // The sheet gives <pre> a 10pt font of its own (not in the standard); its margin is still 1em of whatever font size it has.
            var (above, below) = await GapsAsync("pre", "t", "font-size:14px");

            Assert.Equal(14.0, above, 0.1);
            Assert.Equal(14.0, below, 0.1);
        }

        [Theory]
        [InlineData("dl", "<dt id='first' style='height:5px'></dt><dd><ul id='n'><li style='height:5px'>x</li></ul></dd>")]
        [InlineData("menu", "<li id='first' style='height:5px'></li><li><dl id='n'><dt style='height:5px'></dt></dl></li>")]
        [InlineData("ul", "<li id='first' style='height:5px'></li><li><menu id='n'><li style='height:5px'>x</li></menu></li>")]
        public async Task AListNestedInAListOrADefinitionListHasNoBlockMargin(string outer, string content)
        {
            // The standard's rule is `:is(dir, dl, menu, ol, ul) :is(dir, dl, menu, ol, ul)`, so a dl or a menu is nested too, not only ol and ul.
            // Chrome: the nested list starts where the item before it ends.
            var (root, _) = await LayoutAsync(Wrap($"<div style='font-size:14px'><{outer}>{content}</{outer}></div>"));
            var first = FindById(root, "first")!;
            var nested = FindById(root, "n")!;

            Assert.Equal(first.ActualBottom, nested.Location.Y, 0.05);
        }
    }
}
