using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies min-width/max-width/min-height/max-height are enforced across general block boxes,
    /// replaced elements (images), absolutely positioned boxes, flex cross-axis stretch, and table cells.
    /// </summary>
    public class BoxSizingConstraintsIntegrationTests
    {
        // ─── General block ───────────────────────────────────────────────────────

        [Fact]
        public async Task MaxWidth_ClampsAutoWidthBlock()
        {
            var html = Wrap(@"
                <div style='width:400px;'>
                    <div id='item' style='max-width:100px;'>
                        Some longer text content that would otherwise fill the whole container width.
                    </div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingWidth, 96, 104);
        }

        [Fact]
        public async Task MaxHeight_ClampsBlock_ContentOverflowsPastActualBottom()
        {
            var html = Wrap(@"
                <div id='item' style='max-height:50px;'>
                    <div style='height:200px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingHeight, 46, 54);
        }

        [Fact]
        public async Task Height_ClampsBlock_ContentOverflowsPastActualBottom()
        {
            // CSS 2.1 §10.6.3: a definite (non-auto) height is the used height regardless of
            // content - a taller child overflows past it rather than growing the box. Regression:
            // CssLayoutEngine.GetBoxHeight previously only ever applied explicit `height` as a floor
            // (via Math.Max against the content-driven height), so a box with height smaller than
            // its content silently grew to fit the content instead of being constrained to it - the
            // exact same shape max-height already correctly handles above via a separate, unconditional
            // clamp in ApplyHeight.
            var html = Wrap(@"
                <div id='item' style='height:50px;'>
                    <div style='height:200px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingHeight, 46, 54);
        }

        [Fact]
        public async Task MinHeight_WinsOverConflictingHeight()
        {
            // CSS 2.1 §10.7: when min-height and height conflict, min-height wins.
            var html = Wrap(@"<div id='item' style='height:50px; min-height:150px;'></div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingHeight, 148, 152);
        }

        [Fact]
        public async Task MinWidth_WinsOverConflictingMaxWidth()
        {
            var html = Wrap(@"
                <div style='width:300px;'>
                    <div id='item' style='min-width:150px; max-width:100px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingWidth, 148, 152);
        }

        [Fact]
        public async Task MinHeight_WinsOverConflictingMaxHeight()
        {
            var html = Wrap(@"
                <div id='item' style='min-height:150px; max-height:100px;'>
                    <div style='height:10px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingHeight, 148, 152);
        }

        [Fact]
        public async Task PercentageMaxHeight_AgainstIndefiniteAncestor_DoesNotCollapseToZero()
        {
            // Ancestor has auto (indefinite) height on the first layout pass, so max-height:50%
            // must not resolve against a stale/zero containing-block height and collapse the
            // child to ~0 (without the indefinite guard, this collapses to 0).
            var html = Wrap(@"
                <div id='ancestor' style='width:200px;'>
                    <div id='item' style='max-height:50%;'>
                        <div style='height:200px;'></div>
                    </div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.True(item.ActualBoxSizingHeight > 50,
                $"Percentage max-height against an indefinite ancestor must not collapse the box to ~0: {item.ActualBoxSizingHeight}");
        }

        // ─── Replaced elements (images) ─────────────────────────────────────────

        [Fact]
        public async Task Image_MinWidth_GrowsBelowMinimum()
        {
            // Images are always inline-replaced in this engine (CorrectImgBoxes wraps a
            // display:block img in an anonymous block and forces the img itself back to
            // inline), so width is read from the image word's rectangle, not
            // ActualBoxSizingWidth (which only general block-level width resolution populates).
            var html = Wrap(@"<img id='img' style='width:50px; min-width:100px;' />");
            var (root, _) = await BuildAndLayout(html);
            var img = FindById(root, "img")!;
            Assert.InRange(img.Words[0].Width, 96, 104);
        }

        [Fact]
        public async Task Image_MaxHeight_ClampsAboveMaximum()
        {
            var html = Wrap(@"<img id='img' style='width:50px; height:200px; max-height:80px;' />");
            var (root, _) = await BuildAndLayout(html);
            var img = FindById(root, "img")!;
            Assert.InRange(img.ActualBoxSizingHeight, 76, 84);
        }

        [Fact]
        public async Task Image_MinHeight_GrowsBelowMinimum()
        {
            var html = Wrap(@"<img id='img' style='width:50px; height:20px; min-height:60px;' />");
            var (root, _) = await BuildAndLayout(html);
            var img = FindById(root, "img")!;
            Assert.InRange(img.ActualBoxSizingHeight, 56, 64);
        }

        // ─── Absolutely positioned boxes ────────────────────────────────────────

        [Fact]
        public async Task AbsolutePosition_MaxWidth_ClampsAutoWidthBox()
        {
            var html = Wrap(@"
                <div style='position:relative; width:400px; height:100px;'>
                    <div id='item' style='position:absolute; max-width:100px;'>
                        Some fairly long text content that would otherwise take up much more width.
                    </div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingWidth, 96, 104);
        }

        // ─── Flex cross-axis stretch ─────────────────────────────────────────────

        [Fact]
        public async Task FlexCrossAxisStretch_MaxHeight_ClampsItem()
        {
            var html = Wrap(@"
                <div style='display:flex; height:200px; align-items:stretch;'>
                    <div id='item' style='width:50px; max-height:80px;'></div>
                </div>");
            var (root, _) = await BuildAndLayout(html);
            var item = FindById(root, "item")!;
            Assert.InRange(item.ActualBoxSizingHeight, 76, 84);
        }

        // ─── Table cells ─────────────────────────────────────────────────────────

        [Fact]
        public async Task TableCell_MaxWidth_CapsColumnWidth()
        {
            var html = Wrap(@"
                <table style='width:400px;'>
                    <tr><td id='cell' style='max-width:60px;'>
                        This is fairly long text content that would normally need much more than 60 pixels of width to avoid wrapping excessively across many lines.
                    </td></tr>
                </table>");
            var (root, _) = await BuildAndLayout(html);
            var cell = FindById(root, "cell")!;
            Assert.True(cell.ActualBoxSizingWidth < 100,
                $"Cell should be capped near max-width:60px, not fill the 400px table: {cell.ActualBoxSizingWidth}");
        }

        [Fact]
        public async Task TableCell_MinWidth_WidensColumnBeyondContent()
        {
            var html = Wrap(@"
                <table style='width:400px;'>
                    <tr>
                        <td id='a' style='min-width:200px;'>X</td>
                        <td id='b'>Y</td>
                    </tr>
                </table>");
            var (root, _) = await BuildAndLayout(html);
            var a = FindById(root, "a")!;
            Assert.True(a.ActualBoxSizingWidth >= 198,
                $"Cell 'a' should be widened to at least its min-width:200px: {a.ActualBoxSizingWidth}");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

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
