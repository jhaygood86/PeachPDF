using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using Xunit;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A multi-column container of definite height fills columns of exactly that height (css-multicol-1 §7.1), and content
    /// that does not fit a column starts the next one rather than hanging below the container's foot.
    /// </summary>
    public class MulticolColumnFillAutoTests
    {
        private static string Blocks(int count, string height) =>
            string.Concat(System.Linq.Enumerable.Range(0, count).Select(i => $"<div id='b{i}' style='height:{height}'></div>"));

        private static async Task<CssBox> BoxAsync(string container, string content, string id)
        {
            var (root, _) = await LayoutAsync(Wrap($"<div style='{container}'>{content}</div>"));
            return FindById(root, id)!;
        }

        [Fact]
        public async Task ABlockThatDoesNotFitTheFirstColumn_StartsTheSecond()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='columns:2;column-gap:10pt;width:300pt;height:300pt;column-fill:auto'>" + Blocks(4, "100pt") + "</div>"));

            var a = FindById(root, "b0")!;
            var c = FindById(root, "b2")!;
            var d = FindById(root, "b3")!;

            Assert.Equal(a.Location.X, c.Location.X, 0.01);
            Assert.Equal(a.Location.Y + 200, c.Location.Y, 0.01);
            Assert.Equal(a.Location.Y, d.Location.Y, 0.01);
            Assert.Equal(a.Location.X + 155, d.Location.X, 0.01);
        }

        [Fact]
        public async Task ABlockAfterContentThatEndsExactlyAtTheColumnFoot_StartsTheSecond()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='columns:2;column-gap:10pt;width:300pt;height:300pt;column-fill:auto'>" + Blocks(3, "100pt") + "<div id='n' style='height:10pt'></div></div>"));

            var a = FindById(root, "b0")!;
            var n = FindById(root, "n")!;

            Assert.Equal(a.Location.Y, n.Location.Y, 0.01);
            Assert.Equal(a.Location.X + 155, n.Location.X, 0.01);
        }

        [Fact]
        public async Task ABlockTallerThanTheColumn_IsFollowedInTheNextColumn()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='columns:2;column-gap:10pt;width:300pt;height:100pt;column-fill:auto'>" +
                "<div id='t' style='height:105pt'></div><div id='n' style='height:10pt'></div></div>"));

            var t = FindById(root, "t")!;
            var n = FindById(root, "n")!;

            Assert.Equal(t.Location.X + 155, n.Location.X, 0.01);
        }

        [Fact]
        public async Task ABalancedContainerOfDefiniteHeight_StillBalances()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='columns:2;column-gap:10pt;width:300pt;height:300pt'>" + Blocks(4, "50pt") + "</div>"));

            var a = FindById(root, "b0")!;
            var c = FindById(root, "b2")!;

            Assert.Equal(a.Location.Y, c.Location.Y, 0.01);
            Assert.Equal(a.Location.X + 155, c.Location.X, 0.01);
        }
    }
}
