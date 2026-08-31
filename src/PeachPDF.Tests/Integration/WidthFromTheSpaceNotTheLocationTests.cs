using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A block-level box's inline size comes from the space it lands in, not from the coordinate its
    /// <see cref="CssBox.Location"/> happened to be holding when the size was resolved
    /// (<see href="https://www.w3.org/TR/css-page-3/#page-model">css-page-3 §5.1</see>: each page's own page
    /// area is the containing block for the layout that occurs between page breaks).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CssBox.PlaceBlockBox</c> used to resolve the size <i>before</i> asking the frame above where the
    /// box goes, which left <c>CssLayoutEngine.GetBoxWidth</c> nothing to read but a <c>Location.Y</c> that
    /// belonged to an earlier layout generation — page 0's measure on the first one. Only
    /// <c>HtmlContainerInt.PerformLayout</c>'s reflow loop could iterate that back to the truth, and it is
    /// capped at three iterations: past the cap, boxes were simply left wrapped for a page they are not on.
    /// The offset is now decided first and the size resolved against it.
    /// </para>
    /// <para>
    /// <b>These fixtures are font-metric sensitive by design</b> (see
    /// <c>.claude/invariants/testing-the-reflow-fixtures-are-platform-sensitive-by-design.md</c>): they
    /// assert an invariant that must hold at <i>whatever</i> page boundaries the platform's metrics produce,
    /// rather than a coordinate, which is what keeps them meaningful on a platform whose text is a different
    /// width.
    /// </para>
    /// </remarks>
    public class WidthFromTheSpaceNotTheLocationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;

        /// <summary>
        /// The mechanism, in isolation: the measure follows the block-start offset the caller names, and the
        /// box's own <see cref="CssBox.Location"/> has no say in it. This is the read that used to be
        /// <c>box.Location.Y</c> outright.
        /// </summary>
        [Fact]
        public async Task TheMeasureFollowsTheOfferedBlockTop_NotTheBoxsOwnLocation()
        {
            var (container, g) = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>page zero paragraph</p>
                </body></html>
                """);

            using (g)
            {
                var p0 = FindById(container.Root!, "p0")!;
                Assert.Equal(0, container.PageIndexOf(p0.Location.Y));

                // Page 0 is the wide one (margin-left: 0 -> a 562pt content box, right edge 612); every later
                // page keeps the base 512pt content box, right edge 562.
                var pageZeroTop = container.PageTopOf(0);
                var pageOneTop = container.PageTopOf(1);
                Assert.NotEqual(container.PageContentRightOf(pageZeroTop), container.PageContentRightOf(pageOneTop));

                // Same box, same Location — two answers, each the measure of the page named by the offset.
                var atItsOwnPage = await CssLayoutEngine.GetBoxWidth(g, p0, pageZeroTop);
                var atTheNextPage = await CssLayoutEngine.GetBoxWidth(g, p0, pageOneTop);

                Assert.Equal(container.PageContentRightOf(pageZeroTop) - container.MarginLeft, atItsOwnPage, 0.5);
                Assert.Equal(container.PageContentRightOf(pageOneTop) - container.MarginLeft, atTheNextPage, 0.5);
                Assert.True(atItsOwnPage > atTheNextPage);

                // Omitting the offset falls back to the box's own coordinate, which is what the engines
                // (whose container has already been placed by the frame above) rely on.
                Assert.Equal(await CssLayoutEngine.GetBoxWidth(g, p0, p0.Location.Y),
                             await CssLayoutEngine.GetBoxWidth(g, p0), 0.5);
            }
        }

        /// <summary>
        /// Alternating left/right measures over a full-bleed first page, five times more content than the
        /// reflow loop's three iterations can settle by feeding each generation's positions into the next.
        /// Measured on <c>origin/main</c>: 23 of the 120 blocks came out wrapped for a page they are not on,
        /// and the loop burned all three iterations getting that far.
        /// </summary>
        [Fact]
        public async Task AlternatingPageMeasures_EveryBlockCarriesTheMeasureOfThePageItIsOn()
        {
            var (container, g) = await BuildLayoutAsync(Blocks(120, """
                @page { margin: 40pt 20pt; }
                @page :left { margin: 40pt 260pt; }
                @page :right { margin: 40pt 20pt; }
                @page :first { margin: 0; }
                """));

            using (g)
            {
                Assert.True(container.UseVariableInlineMeasure, "fixture must exercise per-page reflow");

                var blocks = BlocksOf(container);
                Assert.True(container.PageIndexOf(blocks[^1].Location.Y) >= 4,
                    "fixture must span more pages than the reflow loop has iterations");

                AssertEveryBlockIsWrappedForItsOwnPage(container, blocks);

                // And the loop itself now has nothing left to settle: PerformLayout runs one layout, one
                // reflow iteration that finds the page assignment unchanged, and the final settled layout.
                // Three is the floor for a per-page-width document; this fixture took five before.
                Assert.Equal(3, container.LayoutGeneration);
            }
        }

        /// <summary>
        /// The same claim with a wide first page and a single narrow measure everywhere after it — the
        /// shape a cover page in front of a body gives a real document. Measured on <c>origin/main</c>: 39
        /// of 120 blocks wrapped for the wrong page, and the document ran to nine pages instead of six
        /// because the over-wide blocks were laid out too short and then never re-wrapped.
        /// </summary>
        [Fact]
        public async Task AFullBleedFirstPageThenNarrowOnes_EveryBlockCarriesTheMeasureOfThePageItIsOn()
        {
            var (container, g) = await BuildLayoutAsync(Blocks(120, """
                @page { margin: 20pt 240pt; }
                @page :first { margin: 0; }
                @page :left { margin: 20pt 40pt; }
                """));

            using (g)
            {
                Assert.True(container.UseVariableInlineMeasure, "fixture must exercise per-page reflow");
                AssertEveryBlockIsWrappedForItsOwnPage(container, BlocksOf(container));
            }
        }

        /// <summary>
        /// The one thing committing an offset can still move: <c>clear</c> (and the same scan's float
        /// displacement) pushes the box below a float it may not sit beside, which can carry it onto the
        /// next page. Its measure follows it there — the bounded re-resolution round in
        /// <c>PlaceBlockBox</c>, which is what keeps the first placement self-consistent rather than
        /// leaving it to be corrected by the break that displacement also amounts to.
        /// </summary>
        /// <remarks>
        /// Unlike the two above, this one also holds without that round — a displacement big enough to
        /// change the measure has crossed a page boundary, and the fragmentation machinery places the box
        /// again at the resumed target. It is here to pin the invariant at the seam where the box's measure
        /// and its final position are decided by two different steps, and because a round that fires (it
        /// does, once per layout generation on this fixture) with nothing asserting its result is a round
        /// the next change will delete without noticing what it was for.
        /// </remarks>
        [Fact]
        public async Task ABlockClearedPastAFloatOntoTheNextPage_TakesThatPagesMeasure()
        {
            var (container, g) = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 40pt 200pt; }
                @page :first { margin: 40pt 0; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <div style='height:600pt'></div>
                <div style='float:left;width:100pt;height:200pt'></div>
                <p id='cleared' style='clear:both'>cleared paragraph</p>
                </body></html>
                """);

            using (g)
            {
                var cleared = FindById(container.Root!, "cleared")!;

                // It really was pushed off the page its static position put it on.
                Assert.True(container.PageIndexOf(cleared.Location.Y) >= 1,
                    "the fixture must clear the paragraph onto a later page");
                Assert.False(container.MeasureIsSharedBetween(0, container.PageIndexOf(cleared.Location.Y)),
                    "the two pages must have different measures for the claim to mean anything");

                Assert.Equal(container.PageContentRightOf(cleared.Location.Y), cleared.ActualRight, 0.5);
            }
        }

        /// <summary>
        /// The self-measurement-before-registration bug behind issue #202's underlying mechanism: a box
        /// carrying <c>page: &lt;name&gt;</c> is measured (<c>CssBox.ResolveOwnInlineSize</c>) BEFORE its
        /// own <c>CommitBlockChildOffset</c> call registers that name
        /// (<c>HtmlContainerInt.RegisterNamedPageElement</c>) - so without
        /// <c>CssBox._measureResolvedAgainst</c>/<c>InlineSizeCameFromAnotherPagesMeasure</c> catching it
        /// the same pass it happens, the box would silently keep whatever page-geometry slot was cached
        /// under the PREVIOUS name, and nothing in the outer reflow loop's signature comparison
        /// (page *index* only, not the measure used to get there) would ever notice.
        /// </summary>
        [Fact]
        public async Task ABoxOpeningANamedPage_GetsThatPagesOwnMeasureImmediately()
        {
            var (container, g) = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 40pt 200pt; }
                @page wide { margin: 40pt 20pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p>base-measure paragraph before the named page</p>
                <p id='opens' style='page: wide'>the paragraph that opens the wide named page</p>
                </body></html>
                """);

            using (g)
            {
                var opens = FindById(container.Root!, "opens")!;

                Assert.True(container.PageIndexOf(opens.Location.Y) >= 1,
                    "the named page must force a break onto a fresh page");
                Assert.False(container.MeasureIsSharedBetween(0, container.PageIndexOf(opens.Location.Y)),
                    "the named page's own margins must differ from the base page for the claim to mean anything");

                // The box that OPENS the named page gets that page's own (wide) measure immediately - not
                // the base page's, which is what a stale, pre-registration lookup would have given it.
                Assert.Equal(container.PageContentRightOf(opens.Location.Y), opens.ActualRight, 0.5);
            }
        }

        /// <summary>
        /// The keep-with-next pull (<c>CssBox.PlaceBlockBox</c>'s own run relocation ahead of a break, and
        /// its first-line-retry twin) translates a run's geometry in place (<c>CssBox.OffsetTop</c>) rather
        /// than re-measuring it - correct only when the page it leaves and the page it lands on share one
        /// measure (<c>HtmlContainerInt.MeasureIsSharedBetween</c>), now declined otherwise rather than
        /// silently relocating a run to a page it was never measured against. Interspersing
        /// <c>break-after: avoid</c> headings through a fixture that alternates measures gives many
        /// independent chances for a pull to straddle a measure change; every heading and the paragraph it
        /// is paired with must still end up wrapped for whichever page it actually landed on.
        /// </summary>
        [Fact]
        public async Task KeepWithNextRun_PulledAcrossAMeasureChange_StaysCorrectlyWrapped()
        {
            var headed = string.Concat(Enumerable.Range(1, 60).Select(i =>
                $"<h2 class='b' style='break-after:avoid;margin:0'>Heading {i}</h2>" +
                $"<p class='b' style='margin:0'>Paragraph {i}: lorem ipsum dolor sit amet consectetur " +
                "adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>"));

            var (container, g) = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 40pt 20pt; }
                @page :left { margin: 40pt 260pt; }
                @page :right { margin: 40pt 20pt; }
                @page :first { margin: 0; }
                body { margin: 0; }
                </style></head><body>{{headed}}</body></html>
                """);

            using (g)
            {
                Assert.True(container.UseVariableInlineMeasure, "fixture must exercise per-page reflow");

                var blocks = BlocksOf(container);
                Assert.True(container.PageIndexOf(blocks[^1].Location.Y) >= 4,
                    "fixture must span several pages of alternating measure");

                AssertEveryBlockIsWrappedForItsOwnPage(container, blocks);
            }
        }

        /// <summary>
        /// <c>UseVariableInlineMeasure</c> (formerly <c>UseVariablePageWidth</c>) used to gate on
        /// <c>PageGeometry.HasHorizontalMarginOverrides</c> alone - a document whose per-page rule changes
        /// only the SHEET size (no margin override at all) left every page's own content measure computed
        /// from the document's base sheet width, even though <c>PdfGenerator.AddPdfPages</c> already gives
        /// that page a genuinely different, independent physical <c>/MediaBox</c>. Now gated on
        /// <c>HasHorizontalMarginOverrides OR HasSizeOverrides</c>, and <c>PageContentRightOf</c> reads this
        /// slot's own <c>PageBandGeometry.SheetWidthPt</c> rather than the document's base sheet width.
        /// </summary>
        [Fact]
        public async Task SizeOnlyOverride_NoMarginOverride_StillReflowsToItsOwnMeasure()
        {
            // The named rule declares ONLY `size` - no margin-left/margin-right/margin-top/margin-bottom
            // (nor the `margin` shorthand, which expands to all four) anywhere in its declaration block.
            // PageGeometryTable.HasHorizontalMarginOverrides checks whether any selector-carrying rule
            // declares a margin property AT ALL (not whether the value actually differs from the base
            // rule's) - so redeclaring `margin: 20pt` here, even at the same value as the base rule, would
            // satisfy the OLD (pre-widening) gate on its own and prove nothing about HasSizeOverrides.
            var (container, g) = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { size: 300pt 400pt; margin: 20pt; }
                @page wide { size: 700pt 400pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p>base paragraph</p>
                <p id='opens' style='page: wide'>the paragraph on the wide-sheet named page</p>
                </body></html>
                """);

            using (g)
            {
                Assert.True(container.UseVariableInlineMeasure,
                    "a size-only per-page override (no margin override at all) must still enable per-page reflow");

                var opens = FindById(container.Root!, "opens")!;
                Assert.True(container.PageIndexOf(opens.Location.Y) >= 1);

                // The named page's own sheet (700pt) minus its own 20pt margins each side = 660pt content
                // width, at the base 20pt left origin, so its right edge is 680pt - well past the base
                // page's own (300 - 40 = 260pt content, right edge 280pt).
                Assert.True(opens.ActualRight > 280,
                    "content on the wide-sheet named page must reflow to that page's own (much wider) measure");
                Assert.Equal(container.PageContentRightOf(opens.Location.Y), opens.ActualRight, 0.5);
            }
        }

        private static void AssertEveryBlockIsWrappedForItsOwnPage(HtmlContainerInt container, List<CssBox> blocks)
        {
            Assert.True(blocks.Count > 3, "fixture should span several pages worth of blocks");

            var strays = blocks
                .Where(b => Math.Abs(container.PageContentRightOf(b.Location.Y) - b.ActualRight) > 0.5)
                .Select(b => $"y={b.Location.Y:F1} page={container.PageIndexOf(b.Location.Y)} " +
                             $"right={b.ActualRight:F1} expected={container.PageContentRightOf(b.Location.Y):F1}")
                .ToList();

            Assert.True(strays.Count == 0,
                $"{strays.Count} of {blocks.Count} blocks are wrapped for a page they are not on: " +
                string.Join("; ", strays.Take(5)));

            // The fixture is only evidence if the pages genuinely differ, so at least two distinct measures
            // must actually be in play.
            Assert.True(blocks.Select(b => Math.Round(container.PageContentRightOf(b.Location.Y), 1)).Distinct().Count() > 1,
                "the fixture must put blocks on pages of more than one measure");
        }

        private static string Blocks(int count, string pageCss)
        {
            var paragraphs = string.Concat(Enumerable.Range(1, count).Select(i =>
                $"<p class='b'>Block {i}: lorem ipsum dolor sit amet consectetur adipiscing elit sed " +
                "do eiusmod tempor incididunt ut labore et dolore magna aliqua ut enim ad minim veniam.</p>"));

            return $$"""
                <!DOCTYPE html><html><head><style>
                {{pageCss}}
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>{{paragraphs}}</body></html>
                """;
        }

        private static List<CssBox> BlocksOf(HtmlContainerInt container)
        {
            List<CssBox> blocks = [];
            CollectByClass(container.Root!, "b", blocks);
            return blocks;
        }

        // --- Harness (mirrors PdfGenerator.SetContent's geometry derivation; see
        //     PerPageHorizontalReflowLayoutIntegrationTests) ---

        private static async Task<(HtmlContainerInt Container, RGraphics Graphics)> BuildLayoutAsync(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.PageSize = new RSize(
                SheetW - container.MarginLeft - container.MarginRight,
                SheetH - container.MarginTop - container.MarginBottom);
            container.Location = new RPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new RSize(container.PageSize.Width, 0);

            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container, graphics);
        }

        private static void CollectByClass(CssBox box, string className, List<CssBox> result)
        {
            var classAttr = box.HtmlTag?.TryGetAttribute("class", "");
            if (!string.IsNullOrEmpty(classAttr) &&
                classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className))
            {
                result.Add(box);
            }

            foreach (var child in box.Boxes)
                CollectByClass(child, className, result);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
        }
    }
}
