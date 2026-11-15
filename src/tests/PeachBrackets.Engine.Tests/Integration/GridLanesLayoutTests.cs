using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>display: grid-lanes</c> (CSS Grid Layout Module Level 3, formerly masonry): items are dropped into the lane
    /// whose running end is shortest, within <c>fit-tolerance</c>. The container is built on the same harness as the grid
    /// tests; every assertion reads a box's laid-out geometry relative to its container.
    /// </summary>
    public class GridLanesLayoutTests
    {
        private const string ThreeLanes = "display:grid-lanes; width:300pt; grid-template-columns:100pt 100pt 100pt; fit-tolerance:0;";

        [Fact]
        public async Task DisplayGridLanes_IsParsed_AndReadsAsAGridToTheEngine()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='a' style='display:grid-lanes'></div><div id='b' style='display:inline-grid-lanes'></div>"));
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(DisplayMode.GridLanes, a.Display.Value);
            Assert.Equal(DisplayMode.InlineGridLanes, b.Display.Value);
            Assert.Equal("grid", a.DerivedStyle.ActualDisplay);
            Assert.True(b.IsInline);
        }

        [Fact]
        public async Task FlowTolerance_IsTheEarlierNameOfFitTolerance()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='a' style='display:grid-lanes; flow-tolerance: 12pt'></div><div id='b' style='display:grid-lanes; fit-tolerance: infinite'></div>"));

            Assert.Equal("12pt", FindById(root, "a")!.FitTolerance);
            Assert.Equal("infinite", FindById(root, "b")!.FitTolerance);
        }

        [Fact]
        public async Task Items_GoToTheShortestLane_AndTheContainerIsAsTallAsTheLongest()
        {
            var (root, _) = await BuildAndLayout(Wrap($@"
                <div id='c' style='{ThreeLanes}'>
                    <div id='a' style='height:50pt'></div>
                    <div id='b' style='height:30pt'></div>
                    <div id='d' style='height:40pt'></div>
                    <div id='e' style='height:20pt'></div>
                    <div id='f' style='height:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            // a, b, d fill the three lanes; e takes the shortest (b's, 30), then f the shortest again (d's, 40).
            AssertAt(c, FindById(root, "a")!, 0, 0);
            AssertAt(c, FindById(root, "b")!, 100, 0);
            AssertAt(c, FindById(root, "d")!, 200, 0);
            AssertAt(c, FindById(root, "e")!, 100, 30);
            AssertAt(c, FindById(root, "f")!, 200, 40);

            Assert.Equal(100, FindById(root, "a")!.ActualBoxSizingWidth, 0.5);
            Assert.Equal(50, c.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task TheStackingAxisGap_SeparatesItemsInALane()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; width:220pt; grid-template-columns:100pt 100pt; fit-tolerance:0; row-gap:10pt; column-gap:20pt'>
                    <div id='a' style='height:30pt'></div>
                    <div id='b' style='height:50pt'></div>
                    <div id='d' style='height:20pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "a")!, 0, 0);
            AssertAt(c, FindById(root, "b")!, 120, 0);
            AssertAt(c, FindById(root, "d")!, 0, 40);
            Assert.Equal(60, c.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task FitTolerance_LetsAnEarlierLaneWinWhileTheDifferenceIsWithinIt()
        {
            string Page(string tolerance) => Wrap($@"
                <div id='c' style='display:grid-lanes; width:300pt; grid-template-columns:100pt 100pt 100pt; fit-tolerance:{tolerance};'>
                    <div style='height:50pt'></div>
                    <div style='height:30pt'></div>
                    <div style='height:40pt'></div>
                    <div id='d' style='height:20pt'></div>
                </div>");

            // Lanes end at 50, 30, 40 and the cursor wrapped to the first lane. With no tolerance the shortest (30) wins;
            // with 25pt the first lane (50 <= 30 + 25) is still close enough and comes first.
            var (strict, _) = await BuildAndLayout(Page("0pt"));
            AssertAt(FindById(strict, "c")!, FindById(strict, "d")!, 100, 30);

            var (loose, _) = await BuildAndLayout(Page("25pt"));
            AssertAt(FindById(loose, "c")!, FindById(loose, "d")!, 0, 50);
        }

        [Fact]
        public async Task InfiniteTolerance_DealsItemsOutRoundRobin()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; width:200pt; grid-template-columns:100pt 100pt; fit-tolerance:infinite;'>
                    <div id='a' style='height:90pt'></div>
                    <div id='b' style='height:10pt'></div>
                    <div id='d' style='height:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "a")!, 0, 0);
            AssertAt(c, FindById(root, "b")!, 100, 0);
            AssertAt(c, FindById(root, "d")!, 0, 90);
        }

        [Fact]
        public async Task ASpanningItem_StartsBelowTheLongerOfTheLanesItCovers()
        {
            var (root, _) = await BuildAndLayout(Wrap($@"
                <div id='c' style='{ThreeLanes}'>
                    <div style='height:50pt'></div>
                    <div style='height:30pt'></div>
                    <div id='s' style='grid-column: span 2; height:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;
            var spanner = FindById(root, "s")!;

            // Lanes 2-3 end at 30 and 0 (start 30); lanes 1-2 at 50 and 30 (start 50). The first of the two spans is not chosen - 30 is the lower start.
            AssertAt(c, spanner, 100, 30);
            Assert.Equal(200, spanner.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task ADefiniteItem_TakesItsOwnLane_WhateverTheOthersHold()
        {
            var (root, _) = await BuildAndLayout(Wrap($@"
                <div id='c' style='{ThreeLanes}'>
                    <div id='a' style='height:50pt'></div>
                    <div id='b' style='grid-column: 1; height:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "a")!, 0, 0);
            AssertAt(c, FindById(root, "b")!, 0, 50);
        }

        [Fact]
        public async Task Order_ChangesTheSequenceItemsAreDealt()
        {
            var (root, _) = await BuildAndLayout(Wrap($@"
                <div id='c' style='{ThreeLanes}'>
                    <div id='a' style='order:2; height:10pt'></div>
                    <div id='b' style='order:1; height:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "b")!, 0, 0);
            AssertAt(c, FindById(root, "a")!, 100, 0);
        }

        [Fact]
        public async Task AutoLanes_AreSizedFromTheItemsThatCanLandInThem()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; width:400pt; grid-template-columns:auto auto; fit-tolerance:0;'>
                    <div id='a' style='height:10pt; width:60pt'></div>
                    <div id='b' style='height:10pt; width:30pt'></div>
                </div>"));

            // Either item may land in either lane, so both lanes are at least as wide as the wider one.
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            Assert.Equal(60, a.ActualBoxSizingWidth, 0.5);
            Assert.True(b.Location.X - a.Location.X >= 59.5, $"lane two starts at least 60pt after lane one: {b.Location.X - a.Location.X}");
        }

        [Fact]
        public async Task RowsAreTheLanes_WhenGridAutoFlowIsRow_AndItemsStackAlongTheInlineAxis()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; grid-auto-flow:row; width:300pt; grid-template-rows:40pt 40pt; fit-tolerance:0;'>
                    <div id='a' style='width:100pt'></div>
                    <div id='b' style='width:50pt'></div>
                    <div id='d' style='width:30pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "a")!, 0, 0);
            AssertAt(c, FindById(root, "b")!, 0, 40);
            AssertAt(c, FindById(root, "d")!, 50, 40);
            Assert.Equal(40, FindById(root, "a")!.ActualBoxSizingHeight, 0.5);
            Assert.Equal(80, c.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task NormalFlow_UsesRowsAsLanes_WhenOnlyARowTemplateIsGiven()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; width:300pt; grid-template-rows:30pt 30pt; fit-tolerance:0;'>
                    <div id='a' style='width:80pt'></div>
                    <div id='b' style='width:20pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "a")!, 0, 0);
            AssertAt(c, FindById(root, "b")!, 0, 30);
        }

        [Fact]
        public async Task ANormalGrid_IsUnaffectedByGridAutoFlowNormal()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid; grid-auto-flow:normal; width:200pt; grid-template-columns:100pt 100pt;'>
                    <div id='a' style='height:10pt'></div>
                    <div id='b' style='height:10pt'></div>
                    <div id='d' style='height:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "b")!, 100, 0);
            AssertAt(c, FindById(root, "d")!, 0, 10);
        }

        [Fact]
        public async Task TrackReverse_RunsTheLanesFromTheEndOfTheGridAxis()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; grid-lanes-direction:column track-reverse; width:300pt; grid-template-columns:100pt 100pt 100pt; fit-tolerance:0;'>
                    <div id='a' style='height:10pt'></div>
                    <div id='b' style='height:10pt'></div>
                    <div id='d' style='height:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "a")!, 200, 0);
            AssertAt(c, FindById(root, "b")!, 100, 0);
            AssertAt(c, FindById(root, "d")!, 0, 0);
        }

        [Fact]
        public async Task FillReverse_StacksFromTheEndOfTheStackingAxis()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; grid-lanes-direction:column fill-reverse; width:200pt; height:100pt; grid-template-columns:100pt 100pt; fit-tolerance:0;'>
                    <div id='a' style='height:30pt'></div>
                    <div id='b' style='height:50pt'></div>
                    <div id='d' style='height:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "a")!, 0, 70);
            AssertAt(c, FindById(root, "b")!, 100, 50);
            AssertAt(c, FindById(root, "d")!, 0, 60);
        }

        [Fact]
        public async Task GridLanesDirectionRow_MakesTheRowsTheLanes_AndItsGrammarIsChecked()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='ok' style='display:grid-lanes; grid-lanes-direction:row fill-reverse track-reverse; grid-lanes-pack: dense'></div>
                <div id='bad' style='display:grid-lanes; grid-lanes-direction:row-reverse; grid-lanes-pack: sparse'></div>"));

            Assert.Equal("row fill-reverse track-reverse", FindById(root, "ok")!.GridLanesDirection);
            Assert.Equal("dense", FindById(root, "ok")!.GridLanesPack);
            Assert.Equal("normal", FindById(root, "bad")!.GridLanesDirection);
            Assert.Equal("normal", FindById(root, "bad")!.GridLanesPack);
        }

        [Fact]
        public async Task DensePacking_BackfillsTheRoomASpanningItemSkipped()
        {
            // lane 1: 40, lane 2: 10; the spanner covers both and starts at 40, leaving 10..40 of lane 2 empty; a later one-lane item fits there.
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; grid-auto-flow:dense; width:200pt; grid-template-columns:100pt 100pt; fit-tolerance:0;'>
                    <div style='height:40pt'></div>
                    <div style='height:10pt'></div>
                    <div style='grid-column: span 2; height:10pt'></div>
                    <div id='fill' style='height:20pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "fill")!, 100, 10);

            // The item that moved up does not leave the container as tall as the place it was first laid: the spanner ends the stack at 50.
            Assert.Equal(50, c.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task StretchedItems_FillTheRoomBeforeTheNextItemInTheirLane()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; width:200pt; height:200pt; grid-template-columns:100pt 100pt; fit-tolerance:0; row-gap:10pt;'>
                    <div id='a' style='align-self:stretch'></div>
                    <div id='b' style='height:80pt'></div>
                    <div id='s' style='grid-column: span 2; height:20pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            // a is auto-height and stretches: it fills lane one from its start to where the spanner starts (80) less the gap.
            AssertAt(c, FindById(root, "s")!, 0, 90);
            Assert.Equal(80, FindById(root, "a")!.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task ItemsWithNormalAlignment_KeepTheirContentHeightInTheStackingAxis()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; width:200pt; height:200pt; grid-template-columns:100pt 100pt; fit-tolerance:0; row-gap:10pt;'>
                    <div id='a'></div>
                    <div id='b' style='height:80pt'></div>
                    <div id='s' style='grid-column: span 2; height:20pt'></div>
                </div>"));

            Assert.Equal(0, FindById(root, "a")!.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task TheTwoKeywordDisplaySyntax_IsTheLegacyKeyword()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"<div id='a' style='display:inline grid-lanes'></div><div id='b' style='display:grid-lanes block'></div><div id='c' style='display:inline flex'></div><div id='d' style='display:inline flow-root'></div>"));

            Assert.Equal(DisplayMode.InlineGridLanes, FindById(root, "a")!.Display.Value);
            Assert.Equal(DisplayMode.GridLanes, FindById(root, "b")!.Display.Value);
            Assert.Equal(DisplayMode.InlineFlex, FindById(root, "c")!.Display.Value);
            Assert.Equal(DisplayMode.InlineBlock, FindById(root, "d")!.Display.Value);
        }

        [Fact]
        public async Task TheGridLanesShorthand_SetsTheTemplateAndTheDirection()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='a' style='display:grid-lanes; grid-lanes: 10pt 20pt'></div>
                <div id='b' style='display:grid-lanes; grid-lanes: row fill-reverse 30pt 40pt'></div>
                <div id='c' style='display:grid-lanes; grid-lanes: ""x y"" 50pt 50pt track-reverse'></div>"));
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;

            Assert.Equal(2, a.GridTemplateColumns.Value!.Tracks.Count);
            Assert.Equal("normal", a.GridLanesDirection);
            Assert.Equal(2, b.GridTemplateRows.Value!.Tracks.Count);
            Assert.Equal("row fill-reverse", b.GridLanesDirection);
            Assert.Equal("column track-reverse", c.GridLanesDirection);
            Assert.Contains("x y", c.GridTemplateAreas);
        }

        [Fact]
        public async Task DirectionRtl_MirrorsTheLanes()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; direction:rtl; width:300pt; grid-template-columns:100pt 100pt 100pt; fit-tolerance:0;'>
                    <div id='a' style='height:10pt'></div>
                    <div id='b' style='height:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "a")!, 200, 0);
            AssertAt(c, FindById(root, "b")!, 100, 0);
        }

        [Fact]
        public async Task AlignContent_PositionsTheStackingRange()
        {
            string Page(string value) => Wrap($@"
                <div id='c' style='display:grid-lanes; width:200pt; height:100pt; align-content:{value}; grid-template-columns:100pt 100pt; fit-tolerance:0;'>
                    <div id='a' style='height:30pt'></div>
                    <div id='b' style='height:20pt'></div>
                </div>");

            var (center, _) = await BuildAndLayout(Page("center"));
            AssertAt(FindById(center, "c")!, FindById(center, "a")!, 0, 35);
            var (end, _) = await BuildAndLayout(Page("end"));
            AssertAt(FindById(end, "c")!, FindById(end, "a")!, 0, 70);
            var (start, _) = await BuildAndLayout(Page("start"));
            AssertAt(FindById(start, "c")!, FindById(start, "a")!, 0, 0);
        }

        [Fact]
        public async Task JustifyContent_PositionsTheStackingRangeOfRowLanes()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; grid-lanes-direction:row; justify-content:end; width:200pt; grid-template-rows:20pt 20pt; fit-tolerance:0;'>
                    <div id='a' style='width:50pt'></div>
                </div>"));

            AssertAt(FindById(root, "c")!, FindById(root, "a")!, 150, 0);
        }

        [Fact]
        public async Task AVerticalWritingMode_RunsTheLanesDownThePageAndStacksAcrossIt()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; writing-mode:vertical-lr; height:300pt; grid-template-columns:100pt 100pt 100pt; fit-tolerance:0;'>
                    <div id='a' style='width:40pt'></div>
                    <div id='b' style='width:20pt'></div>
                    <div id='d' style='width:30pt'></div>
                    <div id='e' style='width:10pt'></div>
                </div>"));
            var c = FindById(root, "c")!;

            AssertAt(c, FindById(root, "a")!, 0, 0);
            AssertAt(c, FindById(root, "b")!, 0, 100);
            AssertAt(c, FindById(root, "d")!, 0, 200);
            // e takes the shortest lane (b's, 20 along), so it stacks after b across the page.
            AssertAt(c, FindById(root, "e")!, 20, 100);
        }

        [Fact]
        public async Task AGridSubgrid_AdoptsTheLanesItSpans()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; width:300pt; grid-template-columns:100pt 100pt 100pt; fit-tolerance:0;'>
                    <div id='h' style='height:10pt'></div>
                    <div id='s' style='grid-column:2 / span 2; display:grid; grid-template-columns:subgrid'>
                        <div id='x' style='height:10pt'></div><div id='y' style='height:10pt'></div>
                    </div>
                </div>"));
            var c = FindById(root, "c")!;

            // h takes lane one; the two-lane subgrid sits over lanes two and three, and its items are on those lanes' lines.
            AssertAt(c, FindById(root, "x")!, 100, 0);
            AssertAt(c, FindById(root, "y")!, 200, 0);
        }

        [Fact]
        public async Task ARowLanesBaselineGroup_SharesABaseline()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; grid-lanes-direction:row; align-items:baseline; width:300pt; grid-template-rows:auto; fit-tolerance:0; font:10pt Arial'>
                    <div id='a' style='width:40pt; font-size:10pt'>A</div>
                    <div id='b' style='width:40pt; font-size:30pt'>B</div>
                </div>"));

            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            // The small item is moved down so its text sits on the large one's baseline.
            Assert.True(a.Location.Y > b.Location.Y + 1, $"a {a.Location.Y} b {b.Location.Y}");
        }

        [Fact]
        public async Task ASubgridItemsOwnItems_SizeTheAutoLanesItSpans()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; width:400pt; grid-template-columns:auto auto; fit-tolerance:0;'>
                    <div style='grid-column: span 2; display:grid; grid-template-columns:subgrid'>
                        <div id='x' style='width:120pt; height:10pt'></div><div id='y' style='width:30pt; height:10pt'></div>
                    </div>
                </div>"));

            // Without the subgrid's contributions the lanes would be 200pt each; the 120pt item makes the first lane 120 and the free space is shared.
            var lane = FindById(root, "y")!.Location.X - FindById(root, "x")!.Location.X;
            Assert.True(lane > 220, $"first lane {lane}");
        }

        [Fact]
        public async Task AVerticalRtlContainerWithAnAutoHeight_StacksRowLanesFromTheBottom()
        {
            var (root, _) = await BuildAndLayout(Wrap(@"
                <div id='c' style='display:grid-lanes; writing-mode:vertical-lr; direction:rtl; grid-lanes-direction:row; width:100pt; grid-template-rows:50pt; fit-tolerance:0;'>
                    <div id='a' style='height:40pt'></div>
                    <div id='b' style='height:60pt'></div>
                </div>"));
            var c = FindById(root, "c")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            // The stack is 100pt long; under rtl it starts at the bottom, so a is the lower one and the two do not overlap.
            Assert.Equal(100, c.ActualBoxSizingHeight, 0.5);
            Assert.True(a.Location.Y > b.Location.Y, $"a {a.Location.Y} b {b.Location.Y}");
            Assert.True(b.Location.Y + b.ActualBoxSizingHeight <= a.Location.Y + 0.5, "no overlap");
        }

        private static void AssertAt(CssBox container, CssBox item, double x, double y)
        {
            Assert.Equal(x, item.Location.X - container.Location.X, 0.5);
            Assert.Equal(y, item.Location.Y - container.Location.Y, 0.5);
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin:0 }}</style></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
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
