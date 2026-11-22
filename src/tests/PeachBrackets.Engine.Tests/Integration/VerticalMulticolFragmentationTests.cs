using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>The columns of a multi-column container in a vertical writing mode are strips stacked down the inline axis, and a block with only a size continues from one into the next.</summary>
    public class VerticalMulticolFragmentationTests
    {
        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string body) =>
            LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0;writing-mode:vertical-lr} .m{column-count:2;column-fill:auto;column-gap:0;block-size:100pt;inline-size:160pt} .c{background:green;inline-size:80pt;block-size:100pt}</style>" + body));

        [Fact]
        public async Task SiblingsOfOneClass_WithLogicalSizes_AllGetTheirPhysicalSize()
        {
            // Siblings with the same tag and attributes share a style, and the logical size is resolved to a physical one on the box after the cascade: the second and
            // third used to keep `auto` for both.
            var (root, _) = await LayoutAsync("<div class='c'>X</div><div class='c'>X</div><div class='c'>X</div>");
            var divs = LayoutHarness.Descendants(root).Where(b => b.HtmlTag?.Name == "div").ToList();

            Assert.Equal(3, divs.Count);
            Assert.All(divs, d => Assert.Equal("100pt", d.Width));
            Assert.All(divs, d => Assert.Equal("80pt", d.Height));
        }

        [Fact]
        public async Task ABlockWithOnlyASize_TallerThanAColumn_ContinuesIntoTheColumnsBelow()
        {
            // Six columns' worth of block size in a container of two: the box reports its last piece, the sixth strip down, and the container's strips are as wide as the box.
            var (root, _) = await LayoutAsync("<div id='m' class='m'><div id='t' style='inline-size:80pt;block-size:600pt'></div></div>");
            var m = LayoutHarness.FindById(root, "m")!;
            var t = LayoutHarness.FindById(root, "t")!;

            Assert.Equal(m.Location.Y + 5 * 80, t.Location.Y, 1);
            Assert.Equal(100, t.ActualRight - t.Location.X, 1);
        }

        [Fact]
        public async Task EachContainerOfAColumnRun_HasItsStripAsWideAsItsBox_NotOnlyAsItsText()
        {
            var (root, _) = await LayoutAsync("<div id='m' class='m'><div id='a' class='c'>X</div><div id='b' class='c'>X</div><div id='c' class='c'>X</div></div>");
            var m = LayoutHarness.FindById(root, "m")!;

            foreach (var (id, strip) in new[] { ("a", 0), ("b", 1), ("c", 2) })
            {
                var box = LayoutHarness.FindById(root, id)!;
                Assert.Equal(m.Location.Y + strip * 80, box.Location.Y, 1);
                Assert.Equal(m.Location.X, box.Location.X, 1);
                Assert.Equal(100, box.ActualRight - box.Location.X, 1);
            }
        }
    }
}
