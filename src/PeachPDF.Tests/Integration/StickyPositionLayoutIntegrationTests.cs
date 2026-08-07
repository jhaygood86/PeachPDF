using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout tests for issue #647: a <c>position: sticky</c> box was never routed through
    /// <c>CssBox.ResolveBlockChildOffset</c>'s block-flow placement arithmetic, so it kept its
    /// unlaid-out default location (in practice <c>(0, 0)</c>) instead of the CSS Position 3 §6.1
    /// fallback - identical to <c>position: relative</c> placement, but with a zero offset, since
    /// PeachPDF has no scrolling to ever cross a sticky threshold against.
    /// </summary>
    public class StickyPositionLayoutIntegrationTests
    {
        [Fact]
        public async Task Sticky_AfterPrecedingSibling_IsPlacedInNormalFlow()
        {
            // Before the fix this stayed at Location.Y == 0, overlapping the preceding sibling,
            // instead of landing below its 20pt height like position:relative would.
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='before' style='height:20pt;'></div>" +
                "<div id='sticky' style='position:sticky;'></div>"));

            var sticky = FindById(root, "sticky")!;
            Assert.Equal(20, sticky.Location.Y, 1.5);
        }

        [Fact]
        public async Task Sticky_MatchesRelativeSiblingPlacement()
        {
            // Two boxes with identical preceding content, one relative and one sticky - CSS Position
            // 3 §6.1 says an un-stuck sticky box behaves exactly like relative (offset zero), so the
            // two must land at the same Y.
            var (relativeRoot, _) = await BuildAndLayout(Wrap(
                "<div style='height:20pt;'></div>" +
                "<div id='t' style='position:relative;'></div>"));
            var (stickyRoot, _) = await BuildAndLayout(Wrap(
                "<div style='height:20pt;'></div>" +
                "<div id='t' style='position:sticky;'></div>"));

            var relative = FindById(relativeRoot, "t")!;
            var sticky = FindById(stickyRoot, "t")!;
            Assert.Equal(relative.Location.Y, sticky.Location.Y, 1.5);
        }

        [Fact]
        public async Task Sticky_TopAndLeftOffsets_DoNotShiftTheBox()
        {
            // Unlike position:relative, sticky's top/left/right/bottom are scroll-threshold
            // parameters, not a static visual shift - PeachPDF never crosses that threshold, so the
            // fallback offset is zero regardless of what top/left are set to.
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='before' style='height:20pt;'></div>" +
                "<div id='sticky' style='position:sticky; top:15pt; left:30pt;'></div>"));

            var sticky = FindById(root, "sticky")!;
            Assert.Equal(20, sticky.Location.Y, 1.5);
            Assert.Equal(0, sticky.Location.X, 1.5);
        }

        [Fact]
        public async Task Sticky_IsNotOutOfFlow()
        {
            var box = await FindByIdAsync(
                "<div id='t' style='position:sticky;'>x</div>", "t");
            Assert.False(box.IsOutOfFlow);
        }

        // ─── Helpers (mirrors AbsolutePositioningIntegrationTests) ────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body style='margin:0;'>{body}</body></html>";

        private static async Task<CssBox> FindByIdAsync(string fragment, string id)
        {
            var (root, _) = await BuildAndLayout(Wrap(fragment));
            return FindById(root, id)!;
        }

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
