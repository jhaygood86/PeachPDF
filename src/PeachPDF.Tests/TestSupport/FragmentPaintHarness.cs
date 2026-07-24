using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using System.Threading.Tasks;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Drives paint the way production does: off one fragmentainer of the fragment tree layout
    /// produced. Paint takes fragments, not boxes, so a test that wants to paint a particular box has
    /// to name the page it wants that box's fragment from.
    /// </summary>
    internal static class FragmentPaintHarness
    {
        /// <summary>
        /// Paints a whole page.
        /// </summary>
        internal static ValueTask PaintPage(HtmlContainerInt container, RGraphics g, int page = 0) =>
            container.PerformPaint(g, container.FragmentTree!.Fragmentainers[page]);

        /// <summary>
        /// Paints a single box's fragment on <paramref name="page"/>, without the surrounding page clip
        /// — the box-level equivalent of the old <c>box.Paint(g)</c>.
        /// </summary>
        internal static async ValueTask PaintBox(HtmlContainerInt container, CssBox box, RGraphics g, int page = 0)
        {
            container.ResetPaintClaims();

            var fragment = FragmentOf(container, box, page);
            await fragment.Box.Paint(g, fragment);
        }

        /// <summary>
        /// The fragment of <paramref name="box"/> on <paramref name="page"/>. Fails the test if the box
        /// produced no fragment there — which is itself a meaningful assertion, since a box only has a
        /// fragment on a page it actually appears on.
        /// </summary>
        internal static BoxFragment FragmentOf(HtmlContainerInt container, CssBox box, int page = 0)
        {
            var tree = container.FragmentTree;
            Assert.NotNull(tree);
            Assert.InRange(page, 0, tree!.Fragmentainers.Count - 1);

            var found = Find(tree.Fragmentainers[page].Root, box);
            Assert.True(found is not null, $"box '{box}' has no fragment on page {page}");
            return found!;
        }

        /// <summary>
        /// The first fragment <paramref name="box"/> produces on any page — for tests about
        /// page-independent structure (paint order, stacking flattening), where which page the box
        /// happens to land on is not the point.
        /// </summary>
        internal static BoxFragment FirstFragmentOf(HtmlContainerInt container, CssBox box)
        {
            var tree = container.FragmentTree;
            Assert.NotNull(tree);

            foreach (var fragmentainer in tree!.Fragmentainers)
            {
                var found = Find(fragmentainer.Root, box);
                if (found is not null) return found;
            }

            Assert.Fail($"box '{box}' has no fragment on any page");
            return null!;
        }

        private static BoxFragment? Find(BoxFragment fragment, CssBox box)
        {
            if (ReferenceEquals(fragment.Box, box)) return fragment;

            foreach (var child in fragment.Children)
            {
                var found = Find(child, box);
                if (found is not null) return found;
            }

            return null;
        }
    }
}
