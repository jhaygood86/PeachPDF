using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
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
            Assert.Equal(DisplayMode.Block, box.Display.Value);
        }

        [Fact]
        public async Task Fixed_InlineBlock_BlockifiesToBlock()
        {
            var box = await FindByIdAsync(
                "<div id='t' style='display:inline-block; position:fixed'>x</div>", "t");
            Assert.Equal(DisplayMode.Block, box.Display.Value);
        }

        [Fact]
        public async Task Absolute_InlineFlex_BlockifiesToFlex()
        {
            var box = await FindByIdAsync(
                "<div id='t' style='display:inline-flex; position:absolute'></div>", "t");
            Assert.Equal(DisplayMode.Flex, box.Display.Value);
        }

        [Fact]
        public async Task Absolute_InlineGrid_BlockifiesToGrid()
        {
            var box = await FindByIdAsync(
                "<div id='t' style='display:inline-grid; position:absolute'></div>", "t");
            Assert.Equal(DisplayMode.Grid, box.Display.Value);
        }

        [Fact]
        public async Task Fixed_InlineTable_BlockifiesToTable()
        {
            var box = await FindByIdAsync(
                "<div id='t' style='display:inline-table; position:fixed'></div>", "t");
            Assert.Equal(DisplayMode.Table, box.Display.Value);
        }

        [Fact]
        public async Task Static_InlineBlock_IsNotBlockified()
        {
            // A static (non-positioned) box keeps its inline-level display.
            var box = await FindByIdAsync(
                "<div id='t' style='display:inline-block'>x</div>", "t");
            Assert.Equal(DisplayMode.InlineBlock, box.Display.Value);
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

        // ─── §10.1 containing block for content inside a detached <thead>/<tfoot> (issue #787) ──
        //
        // vertical-align:top on every cell below is deliberate, not incidental: it neutralizes the
        // UA-default vertical-align:middle a <th>/<td> would otherwise apply to its own in-flow content,
        // which is an orthogonal axis of behavior from the containing-block bug these tests target and
        // would otherwise make the expected Y coordinates depend on an unrelated cell-sizing detail.

        [Fact]
        public async Task AbsoluteInTheadCell_NoPositionedAncestor_ResolvesAgainstPageContentOrigin()
        {
            // CssLayoutEngineTable.RemoveHeaderFooterFromTree detaches a <thead> (ParentBox = null)
            // before laying its rows out. Before the fix, GetNearestPositionedAncestor stopped at that
            // detached, not-yet-positioned box (Location still (0,0)) instead of continuing on to the
            // real document root - landing "abs" near (5, 5) instead of the real page-content origin.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table style='border-collapse:collapse; border-spacing:0;'>" +
                "<thead><tr><th style='padding:0; border:0; vertical-align:top;'>" +
                "<div id='abs' style='position:absolute; top:5pt; left:5pt; width:10pt; height:10pt;'></div>" +
                "</th></tr></thead>" +
                "<tbody><tr><td style='padding:0; border:0; vertical-align:top;'>x</td></tr></tbody>" +
                "</table>"), margin: 20);

            var abs = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "abs")!;
            Assert.Equal(25, abs.Location.X, 1.5);
            Assert.Equal(25, abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task AbsoluteInTfootCell_NoPositionedAncestor_ResolvesAgainstPageContentOrigin()
        {
            // Same bug, the <tfoot> branch of RemoveHeaderFooterFromTree/DomParentBox. The footer cell
            // needs some real in-flow content alongside "abs" - a footer whose only content is
            // out-of-flow measures a natural height of 0, which (a separate, pre-existing table quirk,
            // out of scope here) skips creating a footer proxy for it at all.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table style='border-collapse:collapse; border-spacing:0;'>" +
                "<tbody><tr><td style='padding:0; border:0; vertical-align:top;'>x</td></tr></tbody>" +
                "<tfoot><tr><td style='padding:0; border:0; vertical-align:top;'>" +
                "y<div id='abs' style='position:absolute; top:5pt; left:5pt; width:10pt; height:10pt;'></div>" +
                "</td></tr></tfoot>" +
                "</table>"), margin: 20);

            var abs = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "abs")!;
            Assert.Equal(25, abs.Location.X, 1.5);
            Assert.Equal(25, abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task AbsoluteInTheadCell_PositionedAncestorSeveralLevelsAboveTable_ResolvesAgainstThatAncestor()
        {
            // The containing-block walk must not stop early at the detached <thead> (this test's own
            // regression target) NOR skip past a real positioned ancestor further up the tree to land on
            // the document root instead. #pos is pushed away from the page origin on both axes - down by
            // a spacer, right by its own margin - so the two possible (wrong) answers - page origin, or
            // "stopped at the header" - are both numerically distinguishable from the correct one.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='height:50pt;'></div>" +
                "<div id='pos' style='position:relative; margin:0 0 0 30pt; padding:0; border:0;'>" +
                "<div style='margin:0; padding:0; border:0;'>" +
                "<table style='border-collapse:collapse; border-spacing:0;'>" +
                "<thead><tr><th style='padding:0; border:0; vertical-align:top;'>" +
                "<div id='abs' style='position:absolute; top:5pt; left:5pt; width:10pt; height:10pt;'></div>" +
                "</th></tr></thead>" +
                "<tbody><tr><td style='padding:0; border:0; vertical-align:top;'>x</td></tr></tbody>" +
                "</table></div></div>"), margin: 20);

            var pos = LayoutHarness.FindById(root, "pos")!;
            var abs = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "abs")!;

            Assert.Equal(pos.ClientLeft + 5, abs.Location.X, 1.5);
            Assert.Equal(pos.ClientTop + 5, abs.Location.Y, 1.5);
            // Proves the walk didn't stop at the page origin (the bug this test guards against fixing
            // too eagerly) or short at the header itself.
            Assert.NotEqual(25, abs.Location.X, 1.5);
            Assert.NotEqual(25, abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task AbsolutePercentWidthInTheadCell_ResolvesAgainstPositionedAncestor_NotDetachedHeader()
        {
            // CssLayoutEngine.PercentageBase (CSS 2.1 §10.1) calls the same GetNearestPositionedAncestor
            // this fix corrects, so a percentage width on an absolute box inside a <thead> cell benefits
            // automatically - it must resolve against #pos's own 120pt width, not against the detached,
            // not-yet-sized header box's own stale default width.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='pos' style='position:relative; width:120pt; height:80pt; margin:0; padding:0; border:0;'>" +
                "<div style='margin:0; padding:0; border:0;'>" +
                "<table style='border-collapse:collapse; border-spacing:0;'>" +
                "<thead><tr><th style='padding:0; border:0; vertical-align:top;'>" +
                "<div id='abs' style='position:absolute; width:100%; height:10pt;'></div>" +
                "</th></tr></thead>" +
                "<tbody><tr><td style='padding:0; border:0; vertical-align:top;'>x</td></tr></tbody>" +
                "</table></div></div>"), margin: 20);

            var abs = LayoutHarness.FindByIdIncludingHeaderFooterProxies(root, "abs")!;
            Assert.Equal(120, abs.Size.Width, 1.5);
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
