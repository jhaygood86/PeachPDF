using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout/cascade tests for absolute/fixed positioning fixes:
    ///  • blockification of an absolutely/fixed-positioned box (CSS 2.1 §9.7 / CSS Display 3 §2.7),
    ///  • out-of-flow children of a flex/table container getting laid out (the engines skip them),
    ///  • percentage width/height on an absolute box resolving against the nearest positioned ancestor
    ///    (CSS 2.1 §10.1), and
    ///  • auto width/height filling the space between opposite insets (CSS 2.1 §10.3.7 / §10.6.4).
    /// pt fixtures so expected values read 1:1.
    /// </summary>
    public class AbsolutePositioningIntegrationTests
    {
        // ─── Blockification (fix 7) ──────────────────────────────────────────────

        [Fact]
        public async Task Absolute_InlineSpan_BlockifiesToBlock()
        {
            var box = await FindByIdAsync(
                "<span id='t' style='position:absolute'>x</span>", "t");
            Assert.Equal("block", box.Display);
        }

        [Fact]
        public async Task Fixed_InlineBlock_BlockifiesToBlock()
        {
            var box = await FindByIdAsync(
                "<div id='t' style='display:inline-block; position:fixed'>x</div>", "t");
            Assert.Equal("block", box.Display);
        }

        [Fact]
        public async Task Absolute_InlineFlex_BlockifiesToFlex()
        {
            var box = await FindByIdAsync(
                "<div id='t' style='display:inline-flex; position:absolute'></div>", "t");
            Assert.Equal("flex", box.Display);
        }

        [Fact]
        public async Task Static_InlineBlock_IsNotBlockified()
        {
            // A static (non-positioned) box keeps its inline-level display.
            var box = await FindByIdAsync(
                "<div id='t' style='display:inline-block'>x</div>", "t");
            Assert.Equal("inline-block", box.Display);
        }

        // ─── Out-of-flow child of a flex container (fix 8a) ──────────────────────

        [Fact]
        public async Task AbsoluteChildOfRelativeFlex_FillsContainerViaFullPercentAndInset()
        {
            // The flex engine skips out-of-flow children, so before the fix this child was never laid out
            // and stayed 0×0. It should now resolve width/height:100% + inset:0 against its 150×100 container.
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='flex' style='display:flex; position:relative; width:150pt; height:100pt;'>" +
                "<div id='abs' style='position:absolute; width:100%; height:100%; top:0; left:0;'></div>" +
                "</div>"));
            var abs = FindById(root, "abs")!;
            Assert.True(abs.IsOutOfFlow);
            Assert.Equal(150, abs.Size.Width, 1.5);
            Assert.Equal(100, abs.ActualHeight, 1.5);
        }

        // ─── §10.1 percentage base = nearest positioned ancestor (fix 8b) ────────

        [Fact]
        public async Task AbsolutePercent_ResolvesAgainstPositionedAncestor_NotStaticContainingBlock()
        {
            // The absolute box's parent chain passes through a position:static middle div, so its
            // ContainingBlock (nearest in-flow block) differs from its nearest positioned ancestor. The
            // percentages must resolve against the positioned ancestor (120×80), not the static middle div.
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='pos' style='position:relative; width:120pt; height:80pt;'>" +
                "<div style='height:auto;'>" +
                "<div id='abs' style='position:absolute; width:100%; height:100%;'></div>" +
                "</div></div>"));
            var abs = FindById(root, "abs")!;
            Assert.Equal(120, abs.Size.Width, 1.5);
            Assert.Equal(80, abs.ActualHeight, 1.5);
        }

        // ─── §10.3.7 / §10.6.4 auto size fills between opposite insets (fix 8c) ──

        [Fact]
        public async Task AbsoluteAutoWidth_LeftAndRightSet_FillsContainingBlockWidth()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='pos' style='position:relative; width:120pt; height:80pt;'>" +
                "<div id='abs' style='position:absolute; left:0; right:0;'></div>" +
                "</div>"));
            var abs = FindById(root, "abs")!;
            Assert.Equal(120, abs.Size.Width, 1.5);
        }

        [Fact]
        public async Task AbsoluteAutoHeight_TopAndBottomSet_FillsContainingBlockHeight()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='pos' style='position:relative; width:120pt; height:80pt;'>" +
                "<div id='abs' style='position:absolute; top:0; bottom:0; width:20pt;'></div>" +
                "</div>"));
            var abs = FindById(root, "abs")!;
            Assert.Equal(80, abs.ActualHeight, 1.5);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

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
