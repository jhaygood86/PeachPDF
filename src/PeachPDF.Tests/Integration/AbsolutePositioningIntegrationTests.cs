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

        // ─── §10.6.4 auto block-axis margins ─────────────────────────────────────

        [Fact]
        public async Task AbsoluteBothInsets_SingleAutoBlockMargin_AbsorbsTheLeftoverSpace()
        {
            // top/height/bottom all non-auto and one auto margin: the equation is solved for that margin,
            // so it takes all 60pt of leftover space and the box lands against the bottom inset.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='cb' style='position:relative; height:100pt; margin:0; padding:0; border:0;'>" +
                "<div id='abs' style='position:absolute; top:0; bottom:0; height:40pt;" +
                " margin-top:auto; margin-bottom:0;'></div></div>"), margin: 20);

            var cb = LayoutHarness.FindById(root, "cb")!;
            var abs = LayoutHarness.FindById(root, "abs")!;

            Assert.Equal(cb.ClientTop + 60, abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task AbsoluteBothInsets_BothAutoBlockMargins_CentreTheBox()
        {
            // Both margins auto: the leftover space is split evenly - the classic vertical centring of an
            // absolutely positioned box.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='cb' style='position:relative; height:100pt; margin:0; padding:0; border:0;'>" +
                "<div id='abs' style='position:absolute; top:0; bottom:0; height:40pt;" +
                " margin-top:auto; margin-bottom:auto;'></div></div>"), margin: 20);

            var cb = LayoutHarness.FindById(root, "cb")!;
            var abs = LayoutHarness.FindById(root, "abs")!;

            Assert.Equal(cb.ClientTop + 30, abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task AbsoluteBothInsets_AutoStartMarginWithNegativeEndMargin_PlacesBoxBelowContainingBlock()
        {
            // The Charts.css column/area/line label: `inset: 0`, a fixed height, `margin-block-start: auto`
            // and a negative end margin that pulls the box out below its containing block entirely -
            // margin-top = 100 - 0 - 40 - 0 - (-40) = 100, so the label's top edge sits on the container's
            // bottom edge rather than across the top of the chart.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='cb' style='position:relative; height:100pt; margin:0; padding:0; border:0;'>" +
                "<div id='abs' style='position:absolute; inset:0; height:40pt;" +
                " margin-block-start:auto; margin-block-end:-40pt;'></div></div>"), margin: 20);

            var cb = LayoutHarness.FindById(root, "cb")!;
            var abs = LayoutHarness.FindById(root, "abs")!;

            Assert.Equal(cb.ClientTop + 100, abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task AbsoluteSingleInset_AutoBlockMargin_StaysAtTheTopInset()
        {
            // §10.6.4 only solves for an auto margin when top, height and bottom are all non-auto. With
            // `bottom: auto` there is no leftover to absorb, so the auto margin is 0 and the box stays put.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='cb' style='position:relative; height:100pt; margin:0; padding:0; border:0;'>" +
                "<div id='abs' style='position:absolute; top:10pt; height:40pt;" +
                " margin-top:auto;'></div></div>"), margin: 20);

            var cb = LayoutHarness.FindById(root, "cb")!;
            var abs = LayoutHarness.FindById(root, "abs")!;

            Assert.Equal(cb.ClientTop + 10, abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task AbsoluteAutoMargins_UseTheFinalHeightOfAnAutoHeightContainingBlock()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='cb' style='position:relative; margin:0; padding:0; border:0;'>" +
                "<div style='height:100pt;'></div>" +
                "<div id='abs' style='position:absolute; inset:0; height:40pt; margin:auto 0;'></div>" +
                "</div>"), margin: 20);

            var cb = LayoutHarness.FindById(root, "cb")!;
            var abs = LayoutHarness.FindById(root, "abs")!;

            Assert.Equal(100, cb.ActualHeight, 0.5);
            Assert.Equal(cb.ClientTop + 30, abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task NestedAbsoluteAutoMargins_UseTheFinalPercentageHeightOfTheirContainingBlock()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='cb' style='position:relative; margin:0; padding:0; border:0;'>" +
                "<div style='height:100pt;'></div>" +
                "<div id='outer-abs' style='position:absolute; top:0; height:50%;'>" +
                "<div id='inner-abs' style='position:absolute; top:0; bottom:0; height:10pt;" +
                " margin-top:auto; margin-bottom:0;'></div></div></div>"), margin: 20);

            var outer = LayoutHarness.FindById(root, "outer-abs")!;
            var inner = LayoutHarness.FindById(root, "inner-abs")!;

            Assert.Equal(50, outer.ActualHeight, 1.5);
            Assert.Equal(outer.ClientTop + 40, inner.Location.Y, 1.5);
        }

        [Fact]
        public async Task FixedZeroInsets_AnchorAtThePageAreasCorner_NotTheSheets()
        {
            // CSS 2.1 §10.1: a fixed box's containing block is the page area in paged media, so `top: 0;
            // left: 0` is the content corner - inside the @page margins - which is where a browser
            // printing the same document puts it. Anchoring at the sheet corner instead also disagreed
            // with the sizes, which have always resolved against the page area.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='fixed' style='position:fixed; top:0; left:0; width:30pt; height:10pt;'></div>"),
                margin: 20);

            var fixedBox = LayoutHarness.FindById(root, "fixed")!;

            Assert.Equal(container.MarginLeft, fixedBox.Location.X, 0.5);
            Assert.Equal(container.MarginTop, fixedBox.Location.Y, 0.5);
        }

        [Fact]
        public async Task FixedLeftAndRightZero_SpanTheFullMeasure()
        {
            // The half that was already right - the fill between two insets resolves against the page
            // area - now agrees with the origin: the box starts at the content edge AND ends at the
            // opposite one, rather than being given the measure but drawn from the sheet edge, stopping
            // one margin short.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='fixed' style='position:fixed; top:0; left:0; right:0; height:2pt;'></div>"),
                margin: 20);

            var fixedBox = LayoutHarness.FindById(root, "fixed")!;

            Assert.Equal(container.MarginLeft, fixedBox.Location.X, 0.5);
            Assert.Equal(container.MarginLeft + container.PageSize.Width, fixedBox.ActualRight, 0.5);
        }

        [Fact]
        public async Task FixedBothInsets_BothAutoBlockMargins_CentreTheBoxInThePageArea()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='fixed' style='position:fixed; inset:0; height:40pt; margin:auto 0;'></div>"),
                margin: 20);

            var fixedBox = LayoutHarness.FindById(root, "fixed")!;
            // Centred within the page AREA, which is where the box's containing block starts (CSS 2.1
            // §10.1) - so the page's own top margin is part of the coordinate, not something the
            // centring replaces.
            var expectedTop = container.MarginTop + (container.PageBandHeightOf(0) - fixedBox.ActualHeight) / 2;

            Assert.Equal(expectedTop, fixedBox.Location.Y, 1.5);
        }

        [Fact]
        public async Task AbsoluteBothInsets_AutoHeight_FillsTheContainingBlock_RatherThanSolvingTheMargin()
        {
            // §10.6.4 solves for an `auto` margin only when top, height AND bottom are all non-auto. With an
            // auto height the rule is the opposite: the auto margins are treated as 0 and the height is what
            // the equation solves for. Running the margin path here solved the same equation twice - against
            // the box's content height, before the fill was applied - and pushed the box its containing
            // block's whole height past the bottom of it.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='cb' style='position:relative; width:200pt; height:100pt;" +
                " margin:0; padding:0; border:0;'>" +
                "<div id='abs' style='position:absolute; top:0; bottom:0;" +
                " margin-top:auto; margin-bottom:0;'>x</div></div>"), margin: 20);

            var cb = LayoutHarness.FindById(root, "cb")!;
            var abs = LayoutHarness.FindById(root, "abs")!;

            Assert.Equal(cb.ClientTop, abs.Location.Y, 1.5);
            Assert.Equal(100, abs.ActualBottom - abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task AbsoluteBothInsets_PercentageHeightAgainstAutoHeightContainingBlock_IsDefinite()
        {
            // CSS Sizing 3 makes an absolute box's containing-block size definite with respect to that box,
            // even when the positioned ancestor's own height is content-driven. The 50pt height leaves 50pt
            // for the single auto margin, so the box starts halfway down the 100pt containing block.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='outer' style='width:200pt;'>" +
                "<div id='cb' style='position:relative; margin:0; padding:0; border:0;'>" +
                "<div style='height:100pt;'></div>" +
                "<div id='abs' style='position:absolute; top:0; bottom:0; height:50%;" +
                " margin-top:auto; margin-bottom:0;'>x</div></div></div>"), margin: 20);

            var cb = LayoutHarness.FindById(root, "cb")!;
            var abs = LayoutHarness.FindById(root, "abs")!;

            Assert.Equal(50, abs.ActualBottom - abs.Location.Y, 1.5);
            Assert.Equal(cb.ClientTop + 50, abs.Location.Y, 1.5);
        }

        [Fact]
        public async Task FixedBothInsets_PercentageHeight_ResolvesAgainstThePageAndSolvesTheMargin()
        {
            // A fixed box's percentage height resolves against the page area, which always has a definite
            // height - so this one IS the margin-solving case, unlike the absolute box above whose
            // containing block is auto-height.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='fixed' style='position:fixed; top:0; bottom:0; height:50%;" +
                " margin-top:auto; margin-bottom:auto;'>x</div>"), margin: 20);

            var fixedBox = LayoutHarness.FindById(root, "fixed")!;
            var band = container.PageBandHeightOf(0);

            // Half the band, centred by the two auto margins: a quarter of the band above it, measured
            // from the page area's own top edge (the containing block, CSS 2.1 §10.1).
            Assert.Equal(band / 2, fixedBox.ActualBottom - fixedBox.Location.Y, 1.5);
            Assert.Equal(container.MarginTop + band / 4, fixedBox.Location.Y, 1.5);
        }

        // ─── §10.1 the containing block is the PADDING box, not the content box (issue #1160) ──
        //
        // Every other fixture in this file zeroes the positioned ancestor's padding, which is
        // exactly why this went unnoticed: with `padding: 0` the padding edge and the content
        // edge are the same point, and the two agree whichever one the code reads.
        //
        // #cb below is a 400×140pt border box at the page origin (300×60 content + 30/40pt
        // padding + a 10pt border), so its padding box is the rectangle (10,10)-(390,130) and
        // its content box is (50,40)-(350,100). Every expected value here is one of those
        // literals rather than anything read back off the tree.

        [Fact]
        public async Task AbsoluteZeroInsets_AnchorAtTheAncestorsPaddingEdge_NotItsContentEdge()
        {
            var (root, _) = await BuildAndLayout(Wrap(PaddedContainingBlock(
                "<div id='abs' style='position:absolute; top:0; left:0; width:10pt; height:10pt;'></div>")));
            var abs = FindById(root, "abs")!;

            // The padding box's top-left, just inside the 10pt border. The content box's would
            // be (50, 40) - one padding further in on each axis.
            Assert.Equal(10, abs.Location.X, 0.5);
            Assert.Equal(10, abs.Location.Y, 0.5);
        }

        [Fact]
        public async Task AbsoluteFarInsets_AnchorAtTheAncestorsPaddingEdge_AndAlwaysDid()
        {
            // The contrast case, and the reason the defect was self-evident once measured: `right`
            // and `bottom` were already resolved against the padding box, so a single box
            // disagreed with itself depending on which pair of offsets placed it. This must not
            // move.
            var (root, _) = await BuildAndLayout(Wrap(PaddedContainingBlock(
                "<div id='abs' style='position:absolute; bottom:0; right:0; width:10pt; height:10pt;'></div>")));
            var abs = FindById(root, "abs")!;

            Assert.Equal(380, abs.Location.X, 0.5);
            Assert.Equal(120, abs.Location.Y, 0.5);
        }

        [Fact]
        public async Task AbsoluteZeroInsets_AncestorWithoutPadding_IsUnaffected()
        {
            // The same fixture with the padding removed: the padding edge and the content edge
            // coincide, so this lands on the border's inner corner either way. It passes with the
            // defect present, which is what makes the padded case above the one that proves
            // anything.
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>html,body{margin:0;padding:0}</style>" +
                "<div id='cb' style='position:relative; width:300pt; height:60pt;" +
                " padding:0; border:10pt solid black;'>" +
                "<div id='abs' style='position:absolute; top:0; left:0; width:10pt; height:10pt;'></div>" +
                "</div>"));
            var abs = FindById(root, "abs")!;

            Assert.Equal(10, abs.Location.X, 0.5);
            Assert.Equal(10, abs.Location.Y, 0.5);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static string PaddedContainingBlock(string child) =>
            "<style>html,body{margin:0;padding:0}</style>" +
            "<div id='cb' style='position:relative; width:300pt; height:60pt;" +
            " padding:30pt 40pt; border:10pt solid black;'>" + child + "</div>";


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
