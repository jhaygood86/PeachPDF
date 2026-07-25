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

        /// <summary>
        /// Pins the exact set of materialized pages, and each one's local origin, against the values
        /// the pre-fragment-tree pagination-slot walk produced for the same fixtures. Introducing the
        /// fragment tree was meant to change how pages are discovered (structurally, from layout's own
        /// output) without changing which pages exist.
        /// </summary>
        [Theory]
        // A short document is one page.
        [InlineData("<p id='p'>hello</p>", 842, "0", "0")]
        // A tall but content-empty div only prints where its one word is, so later slots are skipped.
        [InlineData("<div style='height:1000pt'>x</div>", 200, "0", "0")]
        // Forced breaks materialize consecutive pages; each origin is the previous plus one band.
        [InlineData("<p>a</p><p style='page-break-before:always'>b</p><p style='page-break-before:always'>c</p>", 300, "0,1,2", "0,260,520")]
        // A margin large enough to cross a page is truncated, so it never paginates as blank space.
        [InlineData("<div style='margin-top:900pt'>far below</div>", 200, "0", "0")]
        // Real content either side of a tall empty gap: the gap's own slots are never materialized.
        [InlineData("<p>top</p><div style='height:900pt'></div><p>far below</p>", 200, "0,6", "0,960")]
        public async Task MaterializedPages_MatchThePrePagedFragmentTreeBehaviour(
            string body, double pageHeight, string expectedSlots, string expectedOrigins)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), pageHeight: pageHeight);

            var fragmentainers = container.FragmentTree!.Fragmentainers;

            Assert.Equal(expectedSlots, string.Join(",", fragmentainers.Select(f => f.SlotIndex)));
            Assert.Equal(expectedOrigins, string.Join(",", fragmentainers.Select(f => f.LocalOriginY)));
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
        public async Task CanvasBackgroundBox_DoesNotCountAsPrintableContent()
        {
            // A root background is promoted to fill every page's canvas, so it must not count as this
            // box's own content - otherwise "html { background: … }" would look like real content
            // across the root's entire auto height (i.e. the whole document) and no page could ever be
            // skipped. The promotion is resolved during layout precisely so the fragment tree can see
            // it; resolving it afterwards silently reinstated every skipped page.
            var (_, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>html { background: white }</style></head>"
                + "<body style='margin:0'><p>top</p><div style='height:900pt'></div><p>far below</p></body></html>",
                pageHeight: 200);

            Assert.NotNull(container.CanvasBackgroundBox);
            Assert.True(container.CanvasBackgroundBox!.SuppressOwnBackgroundPaint);
            Assert.Equal("0,6", string.Join(",", container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex)));
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

        // ─── Slice geometry (box-decoration-break, css-break-3 §6.2) ───────────────

        [Fact]
        public async Task WrappingInline_CarriesTheUnbrokenBoxAsSeenFromEachLine()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'><span id='s'>Alpha<br>Beta<br>Gamma</span></div>"));

            var span = LayoutHarness.FindById(root, "s")!;
            var lines = LinesOf(container.FragmentTree!, "s");
            Assert.Equal(3, lines.Count);

            // The unbroken box is the span on one infinitely long line, so it is as wide as the three
            // rectangles put together...
            var total = span.Rectangles.Values.Sum(r => r.Width);
            Assert.All(lines, line => Assert.Equal(total, line.Slice.UnbrokenStrip.Width, 3));

            // ...positioned so each line's own slice of it lines up. That is what puts the strip's left edge
            // at the first line's left and its right edge at the last line's right, which is in turn what
            // makes a rounded or gradient-filled decoration come out sliced with no per-corner special case.
            var preceding = 0d;
            for (var i = 0; i < lines.Count; i++)
            {
                Assert.Equal(lines[i].Rect.X - preceding, lines[i].Slice.UnbrokenStrip.X, 3);
                preceding += lines[i].Rect.Width;
            }

            Assert.Equal(lines[0].Rect.Left, lines[0].Slice.UnbrokenStrip.Left, 3);
            Assert.Equal(lines[^1].Rect.Right, lines[^1].Slice.UnbrokenStrip.Right, 3);
        }

        [Fact]
        public async Task WrappingInline_OwnsOnlyItsTrueOuterEdges()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial'><span id='s'>Alpha<br>Beta<br>Gamma</span></div>"));

            var lines = LinesOf(container.FragmentTree!, "s");

            Assert.Equal([true, false, false], lines.Select(l => l.Slice.HasLeftEdge));
            Assert.Equal([false, false, true], lines.Select(l => l.Slice.HasRightEdge));
        }

        [Fact]
        public async Task InlineSpanningAPageBreak_KeepsCountingEdgesAcrossPages()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='width:200pt;font:10pt Arial;line-height:30pt'>" +
                "<span id='s'>Alpha<br>Beta<br>Gamma<br>Delta</span></div>"),
                pageHeight: 80, margin: 0);

            var tree = container.FragmentTree!;
            Assert.True(tree.Fragmentainers.Count >= 2, "fixture must paginate");

            // Three lines land on page 0 and the fourth on page 1. Edges belong to the box, not to a page,
            // so the leading edge is on page 0's first line and the trailing edge on page 1's only line -
            // page 0's last line owns neither, even though it is the last one *there*.
            var page0 = LinesOf(tree, "s", page: 0);
            var page1 = LinesOf(tree, "s", page: 1);

            Assert.Equal([true, false, false], page0.Select(l => l.Slice.HasLeftEdge));
            Assert.Equal([false, false, false], page0.Select(l => l.Slice.HasRightEdge));
            Assert.All(page1, l => Assert.False(l.Slice.HasLeftEdge));
            Assert.True(page1[^1].Slice.HasRightEdge);
        }

        [Fact]
        public async Task DecoratedInlineSpanningAPageBreak_CountsItsLeadingSpacingOnce()
        {
            // The same fixture as above with a decorated span. The unbroken box is the span on one
            // infinitely long line, so its width is the sum of the rectangles' widths - which is only
            // right if each border and padding is counted once. A resumed pass that re-opened the box
            // charged the leading set again on the page it resumed on, overshooting the strip and, with
            // it, a sliced gradient's extent and a rounded inline's radius reduction.
            var (root, container) = await LayoutHarness.LayoutAsync(SpanningInline(leadingSpacing: true),
                pageHeight: 200, margin: 20);
            var (plainRoot, _) = await LayoutHarness.LayoutAsync(SpanningInline(leadingSpacing: false),
                pageHeight: 200, margin: 20);

            var tree = container.FragmentTree!;
            Assert.True(tree.Fragmentainers.Count >= 2, "fixture must paginate");

            // ...and genuinely resume, rather than simply flowing on into a later band: only a pass that
            // re-enters the box can re-open it.
            Assert.True(container.FragmentainerPasses > 1,
                $"fixture must resume, but layout took {container.FragmentainerPasses} pass(es)");

            var span = LayoutHarness.FindById(root, "s")!;
            var lines = LinesOf(tree, "s");

            var total = span.Rectangles.Values.Sum(r => r.Width);
            Assert.All(lines, line => Assert.Equal(total, line.Slice.UnbrokenStrip.Width, 3));

            // The two fixtures hold the same text, so the whole difference between them is the
            // decoration - one leading set, counted once however many pages the box spans.
            var plain = LayoutHarness.FindById(plainRoot, "s")!.Rectangles.Values.Sum(r => r.Width);
            Assert.Equal(12, total - plain, 2);
        }

        [Fact]
        public async Task BlockSpanningAPageBreak_CutsItsFragmentRectToEachBand()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                // The background is what makes the second page carry printable content, so it is materialized
                // at all (CSS Paged Media 3 §3.2 — a content-empty slot never becomes a page).
                LayoutHarness.Wrap("<div id='tall' style='height:300pt;background:#0f0'>x</div>"),
                pageHeight: 200, margin: 0);

            var tall = LayoutHarness.FindById(root, "tall")!;
            var tree = container.FragmentTree!;
            Assert.Equal(2, tree.Fragmentainers.Count);

            foreach (var fragmentainer in tree.Fragmentainers)
            {
                var line = Assert.Single(LinesOf(tree, "tall", fragmentainer.SlotIndex));

                // A block-level box is one rectangle, so nothing slices it in the inline axis...
                Assert.Equal(line.Rect, line.Slice.UnbrokenStrip);
                Assert.True(line.Slice.HasLeftEdge);
                Assert.True(line.Slice.HasRightEdge);

                // ...but it is fragmented in the block axis, and the fragment rect is the part of it that
                // lives in this band - which is what `clone` closes off with its own border.
                Assert.True(line.Slice.FragmentRect.Height < tall.Bounds.Height,
                    $"expected the band-cut rect to be shorter than the whole box ({tall.Bounds.Height})");
                Assert.InRange(line.Slice.FragmentRect.Bottom, 0, fragmentainer.Geometry.BandHeight + container.MarginTop + 1);
            }
        }

        [Fact]
        public async Task BoxOwningItsOwnLines_IsNotTreatedAsAnInlineSlice()
        {
            // A box that lays out its own line boxes gets one rectangle per line - stacked down the block axis,
            // not slices along the inline one. Concatenating their widths would describe a box that does not
            // exist and resolve a gradient or corner radii against it.
            //
            // Only a box that owns its lines qualifies. A `display: inline-block` generated-content box does
            // NOT: PeachPDF flows its text into the parent block's own line boxes, so it really is one inline
            // run broken across lines and the strip is right for it.
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>" +
                "p::before { display: block; width: 100pt; background: #0f0; border-radius: 8pt;" +
                " content: 'a fairly long generated label that wraps onto several lines' }" +
                "</style></head><body style='margin:0'><p id='p' style='width:200pt;font:10pt Arial'>x</p></body></html>");

            var generated = LayoutHarness.Descendants(LayoutHarness.FindById(root, "p")!)
                .First(b => b.IsBeforePseudoElement);

            var lines = LinesOf(container.FragmentTree!, generated);
            Assert.True(lines.Count > 1, $"fixture must produce several rectangles, got {lines.Count}");

            Assert.All(lines, line =>
            {
                Assert.Equal(line.Rect, line.Slice.UnbrokenStrip);
                Assert.True(line.Slice.HasLeftEdge);
                Assert.True(line.Slice.HasRightEdge);
            });
        }

        [Fact]
        public async Task NestedCloningBoxes_EachFragmentStartsInsideItsAncestors()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='outer' style='box-decoration-break:clone;border-top:4pt solid #f00;padding-top:10pt;" +
                "border-bottom:4pt solid #f00;padding-bottom:10pt'>" +
                "<div id='inner' style='box-decoration-break:clone;border-top:4pt solid #00f;padding-top:10pt;" +
                "border-bottom:4pt solid #00f;padding-bottom:10pt;font:10pt Arial;line-height:14pt'>" +
                string.Join(" ", Enumerable.Range(0, 300).Select(i => $"word{i}")) + "</div></div>"),
                pageHeight: 200, margin: 10);

            var tree = container.FragmentTree!;
            Assert.True(tree.Fragmentainers.Count > 2, "fixture must paginate");

            var outer = LayoutHarness.FindById(root, "outer")!;
            var inner = LayoutHarness.FindById(root, "inner")!;

            // Each fragment is wrapped independently, so the inner box's fragment opens inside the outer box's
            // re-opened border and padding rather than on the same line as it — otherwise both borders would be
            // drawn at the band edge, one over the other, with layout's reserved room left blank between them.
            foreach (var fragmentainer in tree.Fragmentainers.Skip(1))
            {
                var page = fragmentainer.SlotIndex;
                var outerRect = Assert.Single(LinesOf(tree, "outer", page)).Slice.FragmentRect;
                var innerRect = Assert.Single(LinesOf(tree, "inner", page)).Slice.FragmentRect;

                Assert.Equal(outerRect.Top + 14, innerRect.Top, 1);
                Assert.Equal(outerRect.Bottom - 14, innerRect.Bottom, 1);
            }

            Assert.NotNull(outer);
            Assert.NotNull(inner);
        }

        [Fact]
        public async Task UnfragmentedBox_IsItsOwnDecorationArea()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='d' style='height:20pt'>x</div>"));

            var line = Assert.Single(LinesOf(container.FragmentTree!, "d"));

            Assert.Equal(line.Rect, line.Slice.UnbrokenStrip);
            Assert.Equal(line.Rect, line.Slice.FragmentRect);
            Assert.True(line.Slice.HasLeftEdge);
            Assert.True(line.Slice.HasRightEdge);
        }

        [Fact]
        public async Task RowspanCell_ShownInEveryRowItSpans_CarriesTheSameSliceGeometry()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table><tr><td id='spanning' rowspan='2'>tall</td><td>a</td></tr><tr><td>b</td></tr></table>"));

            // The same cell box appears once per row it spans, through a CssSpacingBox. Each appearance is a
            // distinct fragment, and none of them is a slice of anything.
            var lines = LinesOf(container.FragmentTree!, "spanning");
            Assert.NotEmpty(lines);

            Assert.All(lines, line =>
            {
                Assert.Equal(line.Rect, line.Slice.UnbrokenStrip);
                Assert.True(line.Slice.HasLeftEdge);
                Assert.True(line.Slice.HasRightEdge);
            });
        }

        // ─── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// A box's decoration rectangles, in the order they were laid out — across every page, or on one
        /// named page.
        /// </summary>
        private static List<LineFragment> LinesOf(FragmentTree tree, string id, int? page = null) =>
        [
            .. FragmentsOf(tree, id)
                .Where(f => page is null || f.FragmentainerIndex == page)
                .SelectMany(f => f.Lines)
                .OrderBy(l => l.Rect.Y).ThenBy(l => l.Rect.X)
        ];

        /// <summary>The same, for a box with no <c>id</c> to find it by — a generated-content box.</summary>
        private static List<LineFragment> LinesOf(FragmentTree tree, CssBox box) =>
        [
            .. tree.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => ReferenceEquals(f.Box, box))
                .SelectMany(f => f.Lines)
                .OrderBy(l => l.Rect.Y).ThenBy(l => l.Rect.X)
        ];

        /// <summary>
        /// A many-line inline, optionally decorated on its leading edge with a 3pt border and 9pt
        /// padding. The 22pt line height deliberately does not divide the fixture's 160pt band, so a
        /// line straddles a boundary and the flow genuinely has to stop and resume — content that merely
        /// flows on into a later band never re-enters the box and would test nothing.
        /// <para>
        /// The lines are also wider than the decoration: <c>FlowBox</c>'s "handle width setting" branch
        /// compares the cursor's advance across the whole box against the box's own width, and with
        /// <c>&lt;br&gt;</c>s resetting the cursor to the line start that advance is only the last line's,
        /// so a short final line makes it pad the box out and register an extra rectangle. That is a
        /// pre-existing defect of its own and not what this test is about.
        /// </para>
        /// </summary>
        private static string SpanningInline(bool leadingSpacing) => LayoutHarness.Wrap(
            "<div style='font:10pt Arial;line-height:22pt'>" +
            "<span id='s'" + (leadingSpacing ? " style='border-left:3pt solid #000;padding-left:9pt'" : "") + ">" +
            string.Join("<br>", Enumerable.Range(0, 20).Select(i => $"Linenumber{i}")) +
            "</span></div>");

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
