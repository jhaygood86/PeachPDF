using PeachPDF.Html.Adapters.Entities;
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
    /// Geometry and structure of the fragment tree — layout's immutable output (CSS Fragmentation
    /// Level 3 §2). The parity tests here are the load-bearing ones: the fragment tree must describe
    /// exactly the pages, and exactly the paint-time coordinates, the pre-fragment-tree pipeline
    /// produced, since introducing it is meant to be rendering-identical.
    /// </summary>
    public class FragmentTreeBuilderTests
    {
        // ─── Fragmentainers ────────────────────────────────────────────────────────

        [Fact]
        public async Task ShortDocument_ProducesASingleFragmentainer()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<p id='p'>hello</p>"));

            var tree = container.FragmentTree;

            Assert.NotNull(tree);
            var fragmentainer = Assert.Single(tree!.Fragmentainers);
            Assert.Equal(0, fragmentainer.SlotIndex);
        }

        [Fact]
        public async Task ContentSpanningThreePages_ProducesThreeFragmentainers()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(ThreePageBlocks()), pageHeight: 200, margin: 0);

            Assert.Equal(3, container.FragmentTree!.Fragmentainers.Count);
            Assert.Equal([0, 1, 2], container.FragmentTree.Fragmentainers.Select(f => f.SlotIndex));
        }

        [Fact]
        public async Task Fragmentainer_CarriesItsSlotBandGeometry()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(ThreePageBlocks()), pageHeight: 200);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                var slot = fragmentainer.SlotIndex;

                Assert.Equal(container.PageGeometry.GetPage(slot), fragmentainer.Geometry);
                Assert.Equal(container.PageTopOf(slot) - container.MarginTop, fragmentainer.LocalOriginY, 6);
            }
        }

        // ─── Parity with the pre-fragment-tree pipeline ────────────────────────────

        [Theory]
        [InlineData("<p id='p'>hello</p>", 842)]
        [InlineData("<div style='height:1000pt'>x</div>", 200)]
        [InlineData("<p>a</p><p style='page-break-before:always'>b</p><p style='page-break-before:always'>c</p>", 300)]
        [InlineData("<div style='margin-top:900pt'>far below</div>", 200)]
        public async Task Fragmentainers_MatchTheLegacyPaginationSlots(string body, double pageHeight)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), pageHeight: pageHeight);

            var legacySlots = container.GetPaginationSlots();
            var fragmentainers = container.FragmentTree!.Fragmentainers;

            Assert.Equal(legacySlots.Select(s => s.SlotIndex), fragmentainers.Select(f => f.SlotIndex));

            // SlotTop is the "scroll offset" convention the painter used: PageTopOf(k) - MarginTop.
            // That is exactly LocalOriginY, i.e. what the builder now subtracts up front.
            Assert.Equal(legacySlots.Select(s => s.SlotTop), fragmentainers.Select(f => f.LocalOriginY));
        }

        [Fact]
        public async Task FragmentRects_EqualTheLegacyPaintTimeCoordinates()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(ThreePageBlocks()), pageHeight: 200);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                // The painter's per-page scroll offset, which it added to every document-space rect.
                var scrollOffset = -fragmentainer.LocalOriginY;

                foreach (var fragment in Flatten(fragmentainer.Root))
                {
                    var expectedOffset = fragment.IsFixed ? 0 : scrollOffset;

                    foreach (var line in fragment.Lines)
                    {
                        var source = line.Line is null
                            ? fragment.Box.Bounds
                            : fragment.Box.Rectangles[line.Line];

                        Assert.Equal(source.X, line.Rect.X, 6);
                        Assert.Equal(source.Y + expectedOffset, line.Rect.Y, 6);
                        Assert.Equal(source.Width, line.Rect.Width, 6);
                        Assert.Equal(source.Height, line.Rect.Height, 6);
                    }

                    foreach (var word in fragment.Words)
                    {
                        Assert.Equal(word.Word.Rectangle.X, word.Rect.X, 6);
                        Assert.Equal(word.Word.Rectangle.Y + expectedOffset, word.Rect.Y, 6);
                    }

                    Assert.Equal(fragment.Box.Bounds.Y + expectedOffset, fragment.WholeBoxRect.Y, 6);
                }
            }

            Assert.NotEmpty(Flatten(container.FragmentTree.Fragmentainers[0].Root));
            Assert.NotNull(root);
        }

        // ─── Per-box fragmentation ─────────────────────────────────────────────────

        [Fact]
        public async Task BoxSpanningThreePages_ProducesOneFragmentPerPage()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='tall' style='height:500pt;background:red'>x</div>"),
                pageHeight: 200, margin: 0);

            var fragments = FragmentsOf(container.FragmentTree!, "tall");

            Assert.Equal(3, fragments.Count);
            Assert.Equal([0, 1, 2], fragments.Select(f => f.FragmentainerIndex));
        }

        [Fact]
        public async Task SpanningBox_FlagsOnlyItsFirstAndLastFragment()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='tall' style='height:500pt;background:red'>x</div>"),
                pageHeight: 200, margin: 0);

            var fragments = FragmentsOf(container.FragmentTree!, "tall");

            Assert.Equal([true, false, false], fragments.Select(f => f.IsFirstFragment));
            Assert.Equal([false, false, true], fragments.Select(f => f.IsLastFragment));
        }

        [Fact]
        public async Task UnfragmentedBox_IsBothFirstAndLastFragment()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='d' style='height:20pt;background:red'>x</div>"));

            var fragment = Assert.Single(FragmentsOf(container.FragmentTree!, "d"));

            Assert.True(fragment.IsFirstFragment);
            Assert.True(fragment.IsLastFragment);
        }

        [Fact]
        public async Task EachWord_LandsInExactlyOneFragmentainer()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(ThreePageBlocks()), pageHeight: 200);

            var perWord = new Dictionary<CssRect, List<int>>();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                foreach (var fragment in Flatten(fragmentainer.Root))
                {
                    foreach (var word in fragment.Words)
                    {
                        if (!perWord.TryGetValue(word.Word, out var pages))
                            perWord[word.Word] = pages = [];

                        pages.Add(fragment.FragmentainerIndex);
                    }
                }
            }

            Assert.NotEmpty(perWord);

            // Words are monolithic - CssRect.BreakPage relocates a whole word rather than splitting
            // it - so no word may be claimed by two fragmentainers.
            Assert.All(perWord, entry => Assert.Single(entry.Value.Distinct()));
        }

        [Fact]
        public async Task DisplayNoneSubtree_ProducesNoFragments()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='gone' style='display:none'><span id='child'>x</span></div><p>visible</p>"));

            Assert.Empty(FragmentsOf(container.FragmentTree!, "gone"));
            Assert.Empty(FragmentsOf(container.FragmentTree!, "child"));
        }

        [Fact]
        public async Task FixedBox_GetsAFragmentInEveryFragmentainer_AtIdenticalCoordinates()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='bar' style='position:fixed;top:30pt;left:30pt;width:40pt;height:20pt;background:navy'></div>"
                    + ThreePageBlocks()),
                pageHeight: 200);

            var fragments = FragmentsOf(container.FragmentTree!, "bar");

            Assert.Equal(container.FragmentTree!.Fragmentainers.Count, fragments.Count);
            Assert.True(fragments.All(f => f.IsFixed));

            // A fixed fragment carries raw document coordinates - it does not move with the page.
            var first = fragments[0].WholeBoxRect;
            Assert.All(fragments, f => Assert.Equal(first.Y, f.WholeBoxRect.Y, 6));
        }

        [Fact]
        public async Task FixedBox_DoesNotByItselfMaterializeAPage()
        {
            // The fixed bar repeats on every page, so if it counted as printable content every slot -
            // including the huge decorative margin gap - would look non-empty.
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='position:fixed;top:30pt;left:30pt;width:40pt;height:20pt;background:navy'></div>"
                    + "<div style='margin-top:900pt'>far below</div>"),
                pageHeight: 200);

            var slots = container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex).ToList();

            Assert.DoesNotContain(1, slots);
            Assert.DoesNotContain(2, slots);
        }

        [Fact]
        public async Task ContentEmptySlots_AreNotMaterialized()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<p>top</p><div style='height:900pt'></div><p>far below</p>"),
                pageHeight: 200);

            var slots = container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex).ToList();

            // The first slot carries "top", a later one carries "far below"; the purely-decorative
            // margin gap between them materializes no pages (CSS Paged Media Level 3 §3.2).
            Assert.Equal(2, slots.Count);
            Assert.Equal(0, slots[0]);
            Assert.True(slots[1] > 1, $"expected a skipped gap, got slots [{string.Join(", ", slots)}]");
        }

        [Fact]
        public async Task AncestorOfContentOnALaterPage_GetsAFragmentThere()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='outer'><div style='height:300pt'></div><p id='deep'>later</p></div>"),
                pageHeight: 200);

            var deep = FragmentsOf(container.FragmentTree!, "deep");
            var outer = FragmentsOf(container.FragmentTree!, "outer");

            var deepPage = Assert.Single(deep).FragmentainerIndex;

            // Paint reaches a descendant only through its ancestors, so an ancestor must be present in
            // every fragmentainer any descendant appears in.
            Assert.Contains(outer, f => f.FragmentainerIndex == deepPage);
        }

        [Fact]
        public async Task RepeatingTableHeader_ProducesHeaderFragmentsOnEveryPageItRepeatsOn()
        {
            var rows = string.Concat(Enumerable.Range(0, 40).Select(i => $"<tr><td>row {i}</td></tr>"));
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<table style='width:200pt'><thead><tr><th>Header</th></tr></thead><tbody>{rows}</tbody></table>"),
                pageHeight: 200);

            var tree = container.FragmentTree!;
            Assert.True(tree.Fragmentainers.Count > 1, "fixture must paginate");

            // The repeated header lives in CssProxyBox instances created by the table engine; each
            // proxy is a distinct box, so the header text appears once per page it repeats on.
            var headerPages = tree.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Words.Any(w => w.Word.Text == "Header"))
                .Select(f => f.FragmentainerIndex)
                .Distinct()
                .ToList();

            Assert.Equal(tree.Fragmentainers.Count, headerPages.Count);
        }

        [Fact]
        public async Task PerPageMarginOverride_LocalizesAgainstThatPagesOwnBand()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>@page :first { margin-top: 100pt }</style></head>"
                + $"<body style='margin:0'>{ThreePageBlocks()}</body></html>",
                pageHeight: 300);

            var tree = container.FragmentTree!;
            Assert.True(tree.Fragmentainers.Count > 1, "fixture must paginate");

            foreach (var fragmentainer in tree.Fragmentainers)
            {
                Assert.Equal(
                    container.PageTopOf(fragmentainer.SlotIndex) - container.MarginTop,
                    fragmentainer.LocalOriginY,
                    6);
            }

            // The overridden first page has a shorter content band than the base pages.
            Assert.True(tree.Fragmentainers[0].Geometry.BandHeight < tree.Fragmentainers[1].Geometry.BandHeight);
        }

        // ─── Helpers ───────────────────────────────────────────────────────────────

        private static string ThreePageBlocks() => string.Concat(
            Enumerable.Range(0, 30).Select(i => $"<p style='margin:0;height:20pt'>line {i}</p>"));

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }

        private static List<BoxFragment> FragmentsOf(FragmentTree tree, string id) =>
        [
            .. tree.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == id)
                .OrderBy(f => f.FragmentainerIndex)
        ];
    }
}
