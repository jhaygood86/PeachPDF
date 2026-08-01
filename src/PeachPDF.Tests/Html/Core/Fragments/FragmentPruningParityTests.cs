using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Fragments
{
    /// <summary>
    /// <c>FragmentEmitter</c> skips re-descending into a subtree it has already walked and observed to
    /// produce nothing, so that emitting a page costs something like the page rather than something like
    /// the document. That is only sound if it is <i>invisible</i>: it may decline to walk a subtree, but
    /// never change what comes out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each test here renders with <see cref="HtmlContainerInt.VerifyFragmentPruningOverride"/> on, which
    /// makes every slot build its draft tree twice — once pruned, once with pruning forced off — and
    /// throw unless the two are indistinguishable across all of a draft's members, the printable-content
    /// flag, and the set of boxes holding fragments. So the real assertion in every one of these is that
    /// laying out at all did not throw; the explicit assertions afterwards are there to prove the fixture
    /// still exercises what it claims to (a fixture that quietly stopped paginating would verify nothing).
    /// </para>
    /// <para>
    /// The fixtures are chosen for the cases where "this subtree is empty here" is hardest to conclude:
    /// content still ahead of the layout frontier, nested fragmentainers that visit one box once per
    /// column, a source subtree repeated through a proxy on every page, a mover that relocates content
    /// out of an already-frozen fragmentainer, and a descendant positioned nowhere near its ancestor.
    /// Setting the whole suite's <c>PEACHPDF_VERIFY_FRAGMENT_PRUNING=1</c> covers far more ground than
    /// this file does; these are the shapes worth pinning so a regression names itself.
    /// </para>
    /// </remarks>
    public class FragmentPruningParityTests
    {
        [Fact]
        public async Task ManyPagesOfPlainBlocks_PrunesWithoutChangingTheTree()
        {
            var blocks = string.Concat(Enumerable.Range(0, 60)
                .Select(i => $"<p id='p{i}' style='margin:0;line-height:20pt'>Paragraph {i}</p>"));

            var (_, container) = await Verified(LayoutHarness.Wrap(blocks), pageHeight: 120, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 5,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
            Assert.Equal(60, DistinctBoxesWithWords(container).Count);
        }

        [Fact]
        public async Task ManyPagesOfMultiColumn_PrunesWithoutChangingTheTree()
        {
            // The container's children are visited once per column of the same slot, so none of them can
            // be observed individually - but the container itself can, and on a document that is mostly
            // multi-column that is the whole point.
            var items = string.Concat(Enumerable.Range(0, 80)
                .Select(i => $"<div id='i{i}' style='line-height:20pt'>Entry {i}</div>"));

            var (_, container) = await Verified(
                LayoutHarness.Wrap($"<div id='mc' style='columns:2;column-gap:0;width:200px'>{items}</div>"),
                pageHeight: 140, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 3,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
        }

        [Fact]
        public async Task ARepeatingTableHeaderAcrossPages_PrunesWithoutChangingTheTree()
        {
            // One source subtree stands in for a header on every page, reached through a different proxy
            // each time - so the same box legitimately has several unrelated runs of fragments, and an
            // observation about it would be about the wrong one.
            var rows = string.Concat(Enumerable.Range(0, 40)
                .Select(i => $"<tr><td style='line-height:20pt'>Row {i}</td></tr>"));

            var (_, container) = await Verified(
                LayoutHarness.Wrap($"<table><thead><tr><th>Heading</th></tr></thead><tbody>{rows}</tbody></table>"),
                pageHeight: 140, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 2,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
        }

        [Fact]
        public async Task ContentRelocatedOutOfAFrozenFragmentainer_PrunesWithoutChangingTheTree()
        {
            // break-inside: avoid moves a box the emitter may already have frozen a page for, which is
            // what re-opens an emitted fragmentainer - and every observation drawn before that describes
            // an arrangement that no longer exists.
            var filler = string.Concat(Enumerable.Range(0, 12)
                .Select(i => $"<p style='margin:0;line-height:20pt'>Filler {i}</p>"));

            var (_, container) = await Verified(
                LayoutHarness.Wrap(
                    filler
                    + "<div id='card' style='break-inside:avoid;line-height:20pt'>"
                    + "<p style='margin:0'>Card one</p><p style='margin:0'>Card two</p>"
                    + "<p style='margin:0'>Card three</p></div>"
                    + filler),
                pageHeight: 120, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 3,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
        }

        [Fact]
        public async Task AnAbsolutelyPositionedDescendantPagesAway_IsNeverPrunedAway()
        {
            // The case the contiguity rule exists for: #host's own content is on page 1, but it contains
            // a descendant positioned far enough down to land pages later. Observing #host empty on an
            // intervening page and skipping it thereafter would drop that descendant entirely.
            var (_, container) = await Verified(
                LayoutHarness.Wrap(
                    "<div id='host' style='position:relative;line-height:20pt'>Host"
                    + "<div id='far' style='position:absolute;top:600pt;line-height:20pt'>Far away</div>"
                    + "</div>"),
                pageHeight: 120, margin: 0);

            var far = LayoutHarness.FindById(container.Root!, "far")!;

            Assert.Contains(AllBoxesInTheTree(container), b => ReferenceEquals(b, far));
        }

        [Fact]
        public async Task ForcedBreaksSteppingOverPages_PruneWithoutChangingTheTree()
        {
            // A directional break reserves slots nothing was ever flowed into, and those are emitted by a
            // path that runs after every pass is over - where an empty walk may not be concluded from.
            var (_, container) = await Verified(
                LayoutHarness.Wrap(
                    "<p style='margin:0;line-height:20pt'>One</p>"
                    + "<p style='margin:0;line-height:20pt;break-before:left'>Two</p>"
                    + "<p style='margin:0;line-height:20pt;break-before:left'>Three</p>"),
                pageHeight: 120, margin: 0);

            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 3,
                $"fixture must paginate to exercise pruning, got {container.FragmentTree.Fragmentainers.Count} pages");
        }

        /// <summary>
        /// Lays <paramref name="html"/> out with the pruned and unpruned walks compared at every slot.
        /// </summary>
        private static Task<(CssBox Root, HtmlContainerInt Container)> Verified(
            string html, double pageHeight, double margin) =>
            LayoutHarness.LayoutAsync(
                html,
                pageHeight: pageHeight,
                margin: margin,
                prepare: root => root.HtmlContainer!.VerifyFragmentPruningOverride = true);

        private static List<CssBox> AllBoxesInTheTree(HtmlContainerInt container)
        {
            var boxes = new List<CssBox>();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                Collect(fragmentainer.Root, boxes);
            }

            return boxes;

            static void Collect(BoxFragment fragment, List<CssBox> into)
            {
                into.Add(fragment.Box);

                foreach (var child in fragment.Children)
                {
                    Collect(child, into);
                }
            }
        }

        private static HashSet<CssBox> DistinctBoxesWithWords(HtmlContainerInt container)
        {
            var boxes = new HashSet<CssBox>(ReferenceEqualityComparer.Instance as IEqualityComparer<CssBox>);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                Collect(fragmentainer.Root, boxes);
            }

            return boxes;

            static void Collect(BoxFragment fragment, HashSet<CssBox> into)
            {
                if (fragment.Words.Count > 0) into.Add(fragment.Box.ParentBox ?? fragment.Box);

                foreach (var child in fragment.Children)
                {
                    Collect(child, into);
                }
            }
        }
    }
}
