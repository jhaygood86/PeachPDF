using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>vertical-align</c> actually repositions inline-level content relative to its line
    /// box - <c>top</c>/<c>bottom</c>/<c>middle</c>/<c>text-top</c>/<c>text-bottom</c> previously hit
    /// an empty case in <see cref="CssLayoutEngine"/>'s <c>ApplyVerticalAlignment</c> and were silent
    /// no-ops for ordinary inline content (only <c>baseline</c>/<c>sub</c>/<c>super</c> worked; the
    /// full set only worked in table cells via the separate <c>ApplyCellVerticalAlignment</c>). Uses
    /// real layout via <see cref="HtmlContainerInt"/>/<see cref="PdfSharpAdapter"/> and asserts on the
    /// post-layout <c>Top</c> of the aligned box's actual word (via <see cref="CssBox.FirstWordOccurence"/>,
    /// since a <c>&lt;span&gt;</c> doesn't own its text word directly - the underlying text node is its
    /// own anonymous child box), rather than the span's own <see cref="CssBox.Rectangles"/> entry - the
    /// pre-existing <c>SetBaseLine</c> path (used by <c>sub</c>/<c>super</c>) only conditionally
    /// updates a box's own line rectangle, but always updates its actual words, so reading the word is
    /// the one signal that's reliable across both that path and the new one added here.
    ///
    /// Each test line includes a much taller sibling span (60pt) so the line box's own height is
    /// established by that sibling, leaving the smaller (10pt) target span real room to move within
    /// the line - if the target span were itself the tallest content on the line, its rectangle would
    /// already span the full line height by construction, making top/bottom alignment a trivial (and
    /// falsely-passing) no-op regardless of whether the alignment code actually works.
    /// </summary>
    public class VerticalAlignIntegrationTests
    {
        [Fact]
        public async Task Top_PositionsHigherThanBottom()
        {
            var topY = await GetAlignedTopAsync("top");
            var bottomY = await GetAlignedTopAsync("bottom");

            Assert.True(topY < bottomY, $"expected top-aligned span ({topY}) to sit above bottom-aligned span ({bottomY})");
        }

        [Fact]
        public async Task Middle_PositionsBetweenTopAndBottom()
        {
            var topY = await GetAlignedTopAsync("top");
            var bottomY = await GetAlignedTopAsync("bottom");
            var middleY = await GetAlignedTopAsync("middle");

            Assert.True(middleY > topY && middleY < bottomY);
        }

        [Fact]
        public async Task TextTop_PositionsAboveTextBottom()
        {
            var textTopY = await GetAlignedTopAsync("text-top");
            var textBottomY = await GetAlignedTopAsync("text-bottom");

            Assert.True(textTopY < textBottomY, $"top={textTopY} bottom={textBottomY}");
        }

        [Fact]
        public async Task Sub_PositionsBelowSuper()
        {
            var subY = await GetAlignedTopAsync("sub");
            var superY = await GetAlignedTopAsync("super");

            Assert.True(subY > superY, $"sub={subY} super={superY}");
        }

        // No "top differs from baseline" regression test: FlowBox places every word's initial Top at
        // the same shared Y (coordinates.CurrentY, CssLayoutEngine.cs) regardless of the owning box's
        // font size, before alignment runs - and default/"baseline" alignment (SetBaseLine) re-applies
        // that same shared value. Since "top" also targets that value (the line's top edge), the two
        // legitimately coincide in this engine; bottom/middle below are unaffected and do differ.

        [Fact]
        public async Task Bottom_DiffersFromDefaultBaselineAlignment()
        {
            var bottomY = await GetAlignedTopAsync("bottom");
            var baselineY = await GetAlignedTopAsync("baseline");

            Assert.NotEqual(baselineY, bottomY);
        }

        [Fact]
        public async Task Middle_DiffersFromDefaultBaselineAlignment()
        {
            var middleY = await GetAlignedTopAsync("middle");
            var baselineY = await GetAlignedTopAsync("baseline");

            Assert.NotEqual(baselineY, middleY);
        }

        [Fact]
        public async Task TextTop_ReferencesParentFontAscent_NotJustLineTop()
        {
            // text-top aligns with the top of the *parent's* font (CSS1 §5.6.11), not the line's raw
            // top extent the way plain "top" does - changing only the parent's font-size (the target
            // span stays fixed at 10pt in both builds) must still move the result, proving the
            // parent's font metrics are actually consulted rather than this collapsing to plain "top".
            var htmlSmallParent = Wrap(
                "<p id='p' style='font-size:10pt'><span style='font-size:60pt'>TALL</span> " +
                "<span id='v' style='vertical-align:text-top; font-size:10pt'>small</span></p>");
            var htmlLargeParent = Wrap(
                "<p id='p' style='font-size:40pt'><span style='font-size:60pt'>TALL</span> " +
                "<span id='v' style='vertical-align:text-top; font-size:10pt'>small</span></p>");

            var (rootSmall, _) = await BuildAndLayout(htmlSmallParent);
            var (rootLarge, _) = await BuildAndLayout(htmlLargeParent);

            var pSmall = FindById(rootSmall, "p")!;
            var pLarge = FindById(rootLarge, "p")!;
            var ySmall = CssBox.FirstWordOccurence(FindById(rootSmall, "v")!, pSmall.LineBoxes[0])!.Top;
            var yLarge = CssBox.FirstWordOccurence(FindById(rootLarge, "v")!, pLarge.LineBoxes[0])!.Top;

            Assert.NotEqual(ySmall, yLarge);
        }

        [Fact]
        public async Task Bottom_OnATableCell_PushesShortContentLowerThanTopAligned()
        {
            // CssLayoutEngine.ApplyCellVerticalAlignment's table-specific alignment algorithm (distinct
            // from the inline ApplyVerticalAlignment exercised by the other tests in this file) - a
            // short cell in a taller row must be pushed all the way to the row's bottom under
            // vertical-align:bottom, unlike vertical-align:top where it stays put.
            var htmlTop = Wrap(
                "<table><tr>"
                + "<td style='height:100pt'>Tall</td>"
                + "<td id='v' style='vertical-align:top'>Short</td>"
                + "</tr></table>");
            var htmlBottom = Wrap(
                "<table><tr>"
                + "<td style='height:100pt'>Tall</td>"
                + "<td id='v' style='vertical-align:bottom'>Short</td>"
                + "</tr></table>");

            var (rootTop, _) = await BuildAndLayout(htmlTop);
            var (rootBottom, _) = await BuildAndLayout(htmlBottom);

            var topY = CssBox.FirstWordOccurence(FindById(rootTop, "v")!, FindById(rootTop, "v")!.LineBoxes[0])!.Top;
            var bottomY = CssBox.FirstWordOccurence(FindById(rootBottom, "v")!, FindById(rootBottom, "v")!.LineBoxes[0])!.Top;

            Assert.True(bottomY > topY,
                $"vertical-align:bottom ({bottomY}) should push the cell's content lower than vertical-align:top ({topY})");
        }

        [Fact]
        public async Task Middle_OnATableCellWithExplicitHeight_CentersShortContent()
        {
            // GetMaximumBottom's own-height fallback used to be applied to the cell itself: once the
            // row-layout loop stretches the cell's ActualBottom to the row's shared height, a cell that
            // also carries a CSS `height` had its own explicit height clamp the "content bottom" back to
            // that same row-equalized ActualBottom, making the (cellBottom - bottom) offset always zero -
            // vertical-align:middle/bottom were silent no-ops whenever a `<td>` had an explicit height.
            var htmlTop = Wrap(
                "<table><tr>"
                + "<td style='height:100pt'>Tall</td>"
                + "<td id='v' style='height:100pt; vertical-align:top'>Short</td>"
                + "</tr></table>");
            var htmlMiddle = Wrap(
                "<table><tr>"
                + "<td style='height:100pt'>Tall</td>"
                + "<td id='v' style='height:100pt; vertical-align:middle'>Short</td>"
                + "</tr></table>");

            var (rootTop, _) = await BuildAndLayout(htmlTop);
            var (rootMiddle, _) = await BuildAndLayout(htmlMiddle);

            var topY = CssBox.FirstWordOccurence(FindById(rootTop, "v")!, FindById(rootTop, "v")!.LineBoxes[0])!.Top;
            var middleY = CssBox.FirstWordOccurence(FindById(rootMiddle, "v")!, FindById(rootMiddle, "v")!.LineBoxes[0])!.Top;

            Assert.True(middleY > topY,
                $"vertical-align:middle ({middleY}) should push the cell's content lower than vertical-align:top ({topY}) even with an explicit cell height");
        }

        [Fact]
        public async Task Length_PositiveValue_RaisesTheBoxAboveBaseline()
        {
            // CSS 2.1 §10.8.1: a positive length raises the box by that distance from its own baseline
            // (issue #603).
            var baselineY = await GetAlignedTopAsync("baseline");
            var raisedY = await GetAlignedTopAsync("5pt");

            Assert.True(raisedY < baselineY, $"raised={raisedY} baseline={baselineY}");
        }

        [Fact]
        public async Task Length_NegativeValue_LowersTheBoxBelowBaseline()
        {
            var baselineY = await GetAlignedTopAsync("baseline");
            var loweredY = await GetAlignedTopAsync("-5pt");

            Assert.True(loweredY > baselineY, $"lowered={loweredY} baseline={baselineY}");
        }

        [Fact]
        public async Task Percentage_PositiveValue_RaisesTheBoxAboveBaseline()
        {
            var baselineY = await GetAlignedTopAsync("baseline");
            var raisedY = await GetAlignedTopAsync("50%");

            Assert.True(raisedY < baselineY, $"raised={raisedY} baseline={baselineY}");
        }

        [Fact]
        public async Task Percentage_ResolvesAgainstTheBoxsOwnLineHeight()
        {
            // A percentage is a fraction of the box's own line-height (CSS 2.1 §10.8.1) - doubling the
            // line-height (everything else unchanged) must double the raise relative to that
            // line-height's own baseline, proving the percentage is actually resolved against it rather
            // than treated as some other fixed reference (e.g. font-size).
            var baseline20 = await GetAlignedTopAsync("baseline", lineHeight: "20pt");
            var percent20 = await GetAlignedTopAsync("50%", lineHeight: "20pt");
            var baseline40 = await GetAlignedTopAsync("baseline", lineHeight: "40pt");
            var percent40 = await GetAlignedTopAsync("50%", lineHeight: "40pt");

            var raise20 = baseline20 - percent20;
            var raise40 = baseline40 - percent40;

            Assert.Equal(raise20 * 2, raise40, 1);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<double> GetAlignedTopAsync(string verticalAlign, string? lineHeight = null)
        {
            // "v" is deliberately smaller than its parent's own font (10pt vs 16pt) - text-top/
            // text-bottom align with the *parent's* font box, and if the aligned box were taller than
            // that reference box, "top" and "bottom" alignment can legitimately cross over (the excess
            // height sticks out on both ends), which isn't representative of how text-top/text-bottom
            // are normally used (e.g. a small badge/icon sized close to or smaller than surrounding text).
            var lineHeightDecl = lineHeight is null ? "" : $"; line-height:{lineHeight}";
            var html = Wrap(
                "<p id='p' style='font-size:16pt'><span style='font-size:60pt'>TALL</span> " +
                $"<span id='v' style='vertical-align:{verticalAlign}; font-size:10pt{lineHeightDecl}'>small</span></p>");
            var (root, _) = await BuildAndLayout(html);
            var p = FindById(root, "p")!;
            var v = FindById(root, "v")!;
            // "v" (a <span>) doesn't own its text word directly - the HTML text node becomes its own
            // anonymous child CssBox, which is what actually owns the CssRect word. FirstWordOccurence
            // walks the subtree to find it (the same helper CssLineBox.SetBaseLine itself uses).
            return CssBox.FirstWordOccurence(v, p.LineBoxes[0])!.Top;
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
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
