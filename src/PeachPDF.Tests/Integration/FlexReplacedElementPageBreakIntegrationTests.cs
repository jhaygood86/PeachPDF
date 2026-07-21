using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for a bug report ("SVG images under clipPath aren't in the rectangle
    /// they are supposed to be in"): a replaced element (an inline <c>&lt;svg&gt;</c>, same for
    /// <c>&lt;img&gt;</c>) laid out as the sole child of a <c>display:flex</c> container, inside a
    /// table cell whose row gets relocated to the next page by
    /// <see cref="CssLayoutEngineTable"/>'s whole-table page-break post-check, painted its content
    /// well below its own box border.
    ///
    /// Root cause: <see cref="CssLayoutEngineFlex"/>'s <c>MeasureItem</c>/<c>ResizeItem</c>/stretch
    /// cross-size re-layout run a real layout pass for a flex item at a temporary, not-yet-final
    /// <c>Location</c> (<c>(_flexBox.ClientLeft, _flexBox.ClientTop)</c>) purely to measure natural
    /// content size - <c>AssignLocations</c> always translates the item to its real final position
    /// afterward via <c>CssBox.OffsetTop</c>. But that temporary position can itself straddle a page
    /// boundary, and <see cref="CssLayoutEngine.FlowBox"/>'s per-word page-break-avoidance check
    /// (<c>CssRect.BreakPage</c>) would fire against it, permanently jumping the item's phantom word
    /// to the next page - independently of the item's own <c>Location</c>, which stays put. The later
    /// translation only shifts <c>Location</c> by a delta computed from itself, so the word's spurious
    /// jump survives untouched, permanently desyncing painted content from its own box.
    ///
    /// Fixed by <see cref="HtmlContainerInt.SuppressWordPageBreaks"/>, set for the duration of every
    /// throwaway/measurement-only layout pass in <see cref="CssLayoutEngineFlex"/>.
    ///
    /// Accepted trade-off: an earlier version of this fix tried re-running the per-word check
    /// once, unsuppressed, at each item's <c>AssignLocations</c>-computed "final" position - but
    /// that position is not always truly final. <c>CssLayoutEngineTable</c>'s whole-table
    /// page-break post-check can retroactively bulk-relocate an entire already-laid-out cell
    /// subtree (via the same <c>CssBox.OffsetTop</c> this fix relies on) *after*
    /// <c>CssLayoutEngineFlex.Layout</c> has already returned for a flex container inside that
    /// cell - so a re-check at that point could itself fire against a not-yet-relocated position
    /// and reintroduce the exact word/Location desync bug one level later (confirmed by
    /// re-generating and visually inspecting the SVG showcase after adding it). Word-level
    /// page-break avoidance (<c>CssRect.BreakPage</c>) is therefore simply unavailable for
    /// content inside any <c>display:flex</c> container, in exchange for correctness.
    /// </summary>
    public class FlexReplacedElementPageBreakIntegrationTests
    {
        // Matches PageBreakTableKeepWithNextIntegrationTests' harness convention: A4-width, no page
        // margins, 1:1 point scale.
        private const double PageHeight = 842.0;

        [Fact]
        public async Task InlineSvg_SoleFlexItem_InTableCellRelocatedAcrossPageBreak_StaysInsideItsOwnBox()
        {
            // 800pt of filler leaves a 42pt gap to the page bottom - too little for the flex
            // box's real 90pt height, but comfortably more than CssLayoutEngineTable's
            // EstimateRowHeight one-line-of-text estimate (~15pt), so the pre-check's estimate
            // says "fits" and only the *post*-check (driven by real, laid-out geometry) can
            // catch the crossing and relocate the table - exactly the path the reported bug
            // went through.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                table { border-collapse: collapse; width: 100%; margin: 0; }
                td { padding: 0; }
                .sbox { display: flex; align-items: center; justify-content: center; height: 90pt; }
                </style></head><body>
                <div style='height: 800pt'></div>
                <table>
                  <tbody><tr><td>
                    <div class='sbox'>
                      <svg id='target' viewBox='0 0 100 100' width='80' height='80'>
                        <circle cx='50' cy='50' r='40' fill='red'/>
                      </svg>
                    </div>
                  </td></tr></tbody>
                </table>
                </body></html>
                """;

            var (svg, table) = await GetTargetSvgAndTable(html);

            Assert.NotNull(svg);
            Assert.NotNull(table);

            // Confirms the repro actually exercises the post-check relocation path (otherwise
            // this test would pass vacuously without the fix in place).
            Assert.True(table!.Location.Y >= PageHeight,
                $"Table should be relocated to page 2 by the post-check (Y >= {PageHeight}) but Y={table.Location.Y:F1}");

            var sbox = svg!.ParentBox!;
            // Nested cell content can land a hair (epsilon) short of the table's own relocated
            // Location due to the page-boundary "flush counts as the earlier slot" convention
            // used throughout this codebase's pagination math - the meaningful check is that this
            // is nowhere near the un-relocated natural position (~800pt, matching the filler
            // height above), not an exact match to PageHeight.
            Assert.True(sbox.Location.Y >= PageHeight - 2,
                $"The svg's flex container should also have moved to page 2 but Y={sbox.Location.Y:F1}");

            // The bug: PaintImpCore paints content from Rectangles (mirroring the box's own
            // phantom word position), not Location - before the fix these could diverge by
            // dozens of points once the containing table got relocated after the flex item's
            // own throwaway measurement layout had already run.
            Assert.True(svg.Rectangles.Count > 0, "The svg box should have a line-box rectangle after layout");
            var contentY = svg.Rectangles.Values.First().Y;

            Assert.True(Math.Abs(svg.Location.Y - contentY) < 1.0,
                $"Painted content position (Rectangles.Y={contentY:F1}) must match the box's own Location.Y={svg.Location.Y:F1}");

            // And, concretely, the content must actually land inside its own flex container's
            // box - not below it.
            Assert.InRange(contentY, sbox.Location.Y - 1.0, sbox.ActualBottom + 1.0);
        }

        [Fact]
        public async Task ImgReplacedElement_SoleFlexItem_InTableCellRelocatedAcrossPageBreak_StaysInsideItsOwnBox()
        {
            // Same repro as the <svg> case above, but for CssBoxImage - confirms the fix (and
            // this assertion) isn't accidentally SVG-specific, since PaintImpCore's
            // Rectangles-vs-Location split is structurally identical between the two.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                table { border-collapse: collapse; width: 100%; margin: 0; }
                td { padding: 0; }
                .sbox { display: flex; align-items: center; justify-content: center; height: 90pt; }
                </style></head><body>
                <div style='height: 800pt'></div>
                <table>
                  <tbody><tr><td>
                    <div class='sbox'>
                      <img id='target' width='80' height='80' src='data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 viewBox=%220 0 100 100%22%3E%3Ccircle cx=%2250%22 cy=%2250%22 r=%2240%22 fill=%22red%22/%3E%3C/svg%3E'/>
                    </div>
                  </td></tr></tbody>
                </table>
                </body></html>
                """;

            var root = await BuildAndLayoutAsync(html);
            var table = FindFirst(root, b => b.Display == "table");
            var img = FindById(root, "target");

            Assert.NotNull(img);
            Assert.NotNull(table);
            Assert.True(table!.Location.Y >= PageHeight,
                $"Table should be relocated to page 2 by the post-check (Y >= {PageHeight}) but Y={table.Location.Y:F1}");

            Assert.True(img!.Rectangles.Count > 0, "The img box should have a line-box rectangle after layout");
            var contentY = img.Rectangles.Values.First().Y;

            Assert.True(Math.Abs(img.Location.Y - contentY) < 1.0,
                $"Painted content position (Rectangles.Y={contentY:F1}) must match the box's own Location.Y={img.Location.Y:F1}");
        }

        [Fact]
        public async Task ItemAtDefaultAlignment_TextStraddlingPageBoundary_PaintsConsistentlyWithItsOwnBox()
        {
            // Negative-space companion to the two tests above, for the *without* a relocating
            // ancestor case: with default alignment (flex-start main/cross, no growth), a single
            // flex item's final position exactly equals the provisional (ClientLeft, ClientTop)
            // position MeasureItem used to measure it, so OffsetLeft/OffsetTop never fire
            // (dx == dy == 0) - meaning this item's word position was set entirely by the
            // (suppressed) measurement pass and never touched again. Confirms that even though
            // page-break avoidance itself is an accepted, documented gap for flex content (see
            // this class's own doc comment), the word still stays perfectly consistent with its
            // own box - it just straddles the boundary rather than jumping across it.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                </style></head><body>
                <div style='height: 830pt'></div>
                <div style='display:flex;'>
                  <div id='item' style='font-size:16pt; line-height:16pt;'>Hello World</div>
                </div>
                </body></html>
                """;

            var root = await BuildAndLayoutAsync(html);
            var item = FindById(root, "item")!;
            Assert.NotNull(item);

            var word = FindFirstWord(item);
            Assert.NotNull(word);
            Assert.True(Math.Abs(word!.Top - item.Location.Y) < 1.0,
                $"Word position (Top={word.Top:F1}) must match the item's own Location.Y={item.Location.Y:F1}, page-break avoidance notwithstanding");
        }

        // --- Helpers ---

        private static async Task<(CssBox? svg, CssBox? table)> GetTargetSvgAndTable(string html)
        {
            var root = await BuildAndLayoutAsync(html);
            var table = FindFirst(root, b => b.Display == "table");
            var svg = FindById(root, "target");
            return (svg, table);
        }

        private static async Task<CssBox> BuildAndLayoutAsync(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, PageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindFirst(CssBox box, Func<CssBox, bool> predicate)
        {
            if (predicate(box)) return box;
            foreach (var child in box.Boxes)
            {
                var found = FindFirst(child, predicate);
                if (found != null) return found;
            }
            return null;
        }

        private static CssRect? FindFirstWord(CssBox box)
        {
            if (box.Words.Count > 0) return box.Words[0];
            foreach (var child in box.Boxes)
            {
                var found = FindFirstWord(child);
                if (found != null) return found;
            }
            return null;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, StringComparison.OrdinalIgnoreCase))
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
