using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// What a structurally-cloned box's <c>break-*</c> values actually do to pagination, now that the clone
    /// carries them (<see cref="BreakValueCascadeTests"/> asserts the storage).
    /// <para>
    /// <b>Characterization, not desired output.</b> Carrying the values is a box-model correctness fix; on
    /// its own it changes no output today, and these tests pin why, so a later change in this area can tell
    /// a deliberate boundary from a regression:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>Repeated table header.</b> <see cref="CssLayoutEngineTable"/> positions its own rows rather than
    /// routing them through <c>CssBox.PerformLayoutImp</c>'s sibling-relative break machinery, and
    /// <c>CssProxyBox.PerformLayoutImp</c> is fully overridden and never calls the base. So a proxy's break
    /// values are stored and read by nothing.
    /// </item>
    /// <item>
    /// <b>Block-in-inline split.</b> Only <i>inline</i> boxes are split, and
    /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see>/<see
    /// href="https://www.w3.org/TR/css-break-3/#break-within">§3.2</see> apply the break properties to
    /// block-level boxes, flex items and grid items — not to inlines. The values are therefore inert on a
    /// split fragment <i>by specification</i>. Independently, <c>DomParser</c> wraps each fragment in an
    /// anonymous block, so a sibling walk from the following box sees the wrapper rather than the fragment.
    /// </item>
    /// </list>
    /// </summary>
    public class StructuralCloneBreakValueBehaviourTests
    {
        private const double PageHeight = 300;

        // A repeating header must never be the last thing on a page. This holds structurally today (the
        // table engine never places a header without following it with a row), independently of the
        // header's break values - which is exactly what makes it the right invariant to pin before the
        // engine starts reading them.
        [Theory]
        [InlineData("")]
        [InlineData("break-after: avoid")]
        [InlineData("break-inside: avoid")]
        public async Task RepeatedHeader_IsNeverStrandedWithoutARowBeneathIt(string headerCss)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(TableDocument(headerCss), pageHeight: PageHeight);

            var tree = container.FragmentTree!;
            Assert.True(tree.Fragmentainers.Count > 1, "fixture must paginate");

            foreach (var fragmentainer in tree.Fragmentainers)
            {
                var boxes = Flatten(fragmentainer.Root).ToList();

                var header = boxes.FirstOrDefault(f => f.Box is CssProxyBox);
                if (header is null) continue;

                var rowBelow = boxes.Any(f => f.Words.Any(t => t.Word.Text?.StartsWith("Row") == true)
                                              && f.Rect.Top >= header.Rect.Top);

                Assert.True(rowBelow,
                    $"page {fragmentainer.SlotIndex} repeats the header with no body row beneath it");
            }
        }

        // The shape to worry about when a proxy started carrying the edge values: a forced break-after taken
        // once per repetition would paginate without bound. It is inert in both directions - it adds no
        // page, including the single one §3.1 says the element's own trailing edge should produce.
        [Fact]
        public async Task RepeatedHeaderWithForcedBreakAfter_IsCurrentlyInert()
        {
            var (_, plain) = await LayoutHarness.LayoutAsync(TableDocument(""), pageHeight: PageHeight);
            var (_, forced) = await LayoutHarness.LayoutAsync(
                TableDocument("break-after: page"), pageHeight: PageHeight);

            var plainPages = plain.FragmentTree!.Fragmentainers.Count;
            var forcedPages = forced.FragmentTree!.Fragmentainers.Count;

            Assert.True(plainPages > 1, "fixture must paginate");
            Assert.Equal(plainPages, forcedPages);
        }

        // Both halves of the split-fragment story at once: every fragment now carries the span's own
        // break-after (the storage fix), and an anonymous block still stands between the fragments and the
        // sibling that would read it - so DomUtils.GetPrecedingKeepWithNextRun sees "auto" regardless. That
        // second half is not a defect to fix here: the break properties do not apply to inline boxes, and
        // an inline box is the only thing this correction ever splits.
        [Fact]
        public async Task SplitFragmentsCarryBreakAfter_ButAnAnonymousWrapperSeparatesThemFromTheirSibling()
        {
            var html = LayoutHarness.Wrap(
                "<div style='height:120pt'>filler</div>" +
                "<span style='break-after: avoid'>lead<div>split</div>tail</span>" +
                "<div id='kept' style='height:100pt;break-inside:avoid'>kept</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 200);

            var spans = LayoutHarness.Descendants(root).Where(b => b.HtmlTag?.Name == "span").ToList();
            Assert.True(spans.Count > 1, $"expected the span to be split, found {spans.Count} box(es)");
            Assert.All(spans, s => Assert.Equal(CssConstants.Avoid, s.BreakAfter));

            var kept = LayoutHarness.FindById(root, "kept");
            Assert.NotNull(kept);

            var predecessor = DomUtils.GetPreviousSibling(kept!, false);
            Assert.NotNull(predecessor);

            // An anonymous block generated by the correction, not the span itself.
            Assert.Null(predecessor!.HtmlTag);
            Assert.Equal(CssConstants.Auto, predecessor.BreakAfter);
            Assert.Empty(DomUtils.GetPrecedingKeepWithNextRun(kept!));
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string TableDocument(string headerCss)
        {
            var rows = string.Join("", Enumerable.Range(1, 30)
                .Select(i => $"<tr><td>Row {i} Cell 1</td><td>Row {i} Cell 2</td></tr>"));

            return $$"""
                <!DOCTYPE html><html><head><style>
                  body { margin: 0 }
                  table { width: 100%; border-collapse: collapse; }
                  thead { display: table-header-group; {{headerCss}}; }
                  th, td { border: 1px solid black; padding: 4px; }
                </style></head><body>
                <table>
                  <thead><tr><th>Header 1</th><th>Header 2</th></tr></thead>
                  <tbody>{{rows}}</tbody>
                </table>
                </body></html>
                """;
        }

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
    }
}
