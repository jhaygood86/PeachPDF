using PeachPDF.Rendering;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS Borders 4 <c>corner-shape</c>: the longhands, the shorthands that expand to them, the flow-relative corners, and the
    /// shaped paths the fills, clips and borders are built from (asserted on the calls made to the canvas, not on tokens).
    /// </summary>
    public class CornerShapeIntegrationTests
    {
        private static async Task<(CssBox Box, BorderRadii Radii)> RadiiOf(string style)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='box' style='width:100pt;height:100pt;{style}'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;
            return (box, box.ComputeRadii(new Rect(0, 0, 100, 100)));
        }

        [Fact]
        public async Task TheInitialShapeIsRound()
        {
            var (_, radii) = await RadiiOf("border-radius:10pt;");
            Assert.Equal(1d, radii.KTL);
            Assert.Equal(1d, radii.KTR);
            Assert.Equal(1d, radii.KBR);
            Assert.Equal(1d, radii.KBL);
            Assert.False(radii.HasShapes);
        }

        [Theory]
        [InlineData("round", 1d)]
        [InlineData("squircle", 2d)]
        [InlineData("bevel", 0d)]
        [InlineData("scoop", -1d)]
        [InlineData("square", double.PositiveInfinity)]
        [InlineData("notch", double.NegativeInfinity)]
        [InlineData("superellipse(0.5)", 0.5d)]
        [InlineData("superellipse(infinity)", double.PositiveInfinity)]
        [InlineData("superellipse(-infinity)", double.NegativeInfinity)]
        public async Task EachLonghandValueMapsToItsSuperellipseParameter(string value, double k)
        {
            var (_, radii) = await RadiiOf($"border-radius:10pt;corner-top-right-shape:{value};");
            Assert.Equal(k, radii.KTR);
            Assert.Equal(1d, radii.KTL);
        }

        [Fact]
        public async Task AnInvalidShapeIsIgnored()
        {
            var (_, radii) = await RadiiOf("border-radius:10pt;corner-top-left-shape:bevel;corner-top-left-shape:wobbly;");
            Assert.Equal(0d, radii.KTL);
        }

        [Fact]
        public async Task CornerShapeTakesOneToFourValuesInCornerOrder()
        {
            var (_, one) = await RadiiOf("border-radius:10pt;corner-shape:bevel;");
            Assert.Equal([0d, 0d, 0d, 0d], [one.KTL, one.KTR, one.KBR, one.KBL]);

            var (_, two) = await RadiiOf("border-radius:10pt;corner-shape:bevel scoop;");
            Assert.Equal([0d, -1d, 0d, -1d], [two.KTL, two.KTR, two.KBR, two.KBL]);

            var (_, four) = await RadiiOf("border-radius:10pt;corner-shape:bevel scoop squircle notch;");
            Assert.Equal([0d, -1d, 2d, double.NegativeInfinity], [four.KTL, four.KTR, four.KBR, four.KBL]);
        }

        [Fact]
        public async Task SideShapeShorthandsSetTheirTwoCorners()
        {
            var (_, top) = await RadiiOf("border-radius:10pt;corner-top-shape:bevel scoop;");
            Assert.Equal([0d, -1d, 1d, 1d], [top.KTL, top.KTR, top.KBR, top.KBL]);

            var (_, bottom) = await RadiiOf("border-radius:10pt;corner-bottom-shape:bevel scoop;");
            Assert.Equal([1d, 1d, -1d, 0d], [bottom.KTL, bottom.KTR, bottom.KBR, bottom.KBL]);

            var (_, left) = await RadiiOf("border-radius:10pt;corner-left-shape:bevel scoop;");
            Assert.Equal([0d, 1d, 1d, -1d], [left.KTL, left.KTR, left.KBR, left.KBL]);

            var (_, right) = await RadiiOf("border-radius:10pt;corner-right-shape:squircle;");
            Assert.Equal([1d, 2d, 2d, 1d], [right.KTL, right.KTR, right.KBR, right.KBL]);
        }

        [Fact]
        public async Task SideRadiusShorthandsSetTheirTwoCorners()
        {
            var (_, top) = await RadiiOf("border-top-radius:5pt 10pt;");
            Assert.Equal([5d, 10d, 0d, 0d], [top.TLX, top.TRX, top.BRX, top.BLX]);

            var (_, bottom) = await RadiiOf("border-bottom-radius:5pt 10pt / 2pt 4pt;");
            Assert.Equal([0d, 0d, 10d, 5d], [bottom.TLX, bottom.TRX, bottom.BRX, bottom.BLX]);
            Assert.Equal([0d, 0d, 4d, 2d], [bottom.TLY, bottom.TRY, bottom.BRY, bottom.BLY]);

            var (_, left) = await RadiiOf("border-left-radius:8pt;");
            Assert.Equal([8d, 0d, 0d, 8d], [left.TLX, left.TRX, left.BRX, left.BLX]);

            var (_, right) = await RadiiOf("border-right-radius:6pt 3pt;");
            Assert.Equal([0d, 6d, 3d, 0d], [right.TLX, right.TRX, right.BRX, right.BLX]);
        }

        [Fact]
        public async Task CornerShorthandsSetShapeAndRadiusTogether()
        {
            var (_, single) = await RadiiOf("corner-top-left:bevel 12pt;");
            Assert.Equal(12d, single.TLX);
            Assert.Equal(0d, single.KTL);

            var (_, reversed) = await RadiiOf("corner-bottom-right:12pt scoop;");
            Assert.Equal(12d, reversed.BRX);
            Assert.Equal(-1d, reversed.KBR);

            var (_, top) = await RadiiOf("corner-top:scoop 9pt;");
            Assert.Equal([9d, 9d, 0d, 0d], [top.TLX, top.TRX, top.BRX, top.BLX]);
            Assert.Equal([-1d, -1d, 1d, 1d], [top.KTL, top.KTR, top.KBR, top.KBL]);

            var (_, all) = await RadiiOf("corner:notch 7pt;");
            Assert.Equal([7d, 7d, 7d, 7d], [all.TLX, all.TRX, all.BRX, all.BLX]);
            Assert.True(all.KTL < -1e9 && all.KBR < -1e9);
        }

        [Fact]
        public async Task ACornerShorthandWithOnlyAShapeResetsTheRadiusToZero()
        {
            var (_, radii) = await RadiiOf("border-radius:10pt;corner-top-left:bevel;");
            Assert.Equal(0d, radii.TLX);
            Assert.Equal(0d, radii.KTL);
            Assert.Equal(10d, radii.TRX);
        }

        [Fact]
        public async Task FlowRelativeCornersFollowTheWritingModeAndDirection()
        {
            var (_, ltr) = await RadiiOf("border-start-end-radius:9pt;corner-start-end-shape:bevel;");
            Assert.Equal(9d, ltr.TRX);
            Assert.Equal(0d, ltr.KTR);

            var (_, rtl) = await RadiiOf("direction:rtl;border-start-end-radius:9pt;corner-start-end-shape:bevel;");
            Assert.Equal(9d, rtl.TLX);
            Assert.Equal(0d, rtl.KTL);

            var (_, vertical) = await RadiiOf("writing-mode:vertical-rl;border-end-start-radius:9pt;corner-end-start-shape:scoop;");
            // vertical-rl: block-end is left, inline-start is top -> the top-left corner.
            Assert.Equal(9d, vertical.TLX);
            Assert.Equal(-1d, vertical.KTL);
        }

        [Fact]
        public async Task ALogicalCornerYieldsToALaterPhysicalOne()
        {
            var (_, radii) = await RadiiOf("border-start-start-radius:9pt;border-top-left-radius:4pt;");
            Assert.Equal(4d, radii.TLX);

            var (_, logicalLast) = await RadiiOf("border-top-left-radius:4pt;border-start-start-radius:9pt;");
            Assert.Equal(9d, logicalLast.TLX);
        }

        [Fact]
        public async Task LogicalCornerShorthandsExpandToFlowRelativeCorners()
        {
            var (_, blockStart) = await RadiiOf("corner-block-start-shape:bevel scoop;border-block-end-radius:5pt 6pt;");
            Assert.Equal([0d, -1d, 1d, 1d], [blockStart.KTL, blockStart.KTR, blockStart.KBR, blockStart.KBL]);
            Assert.Equal(5d, blockStart.BLX);
            Assert.Equal(6d, blockStart.BRX);

            var (_, inlineEnd) = await RadiiOf("corner-inline-end:square 4pt;");
            Assert.Equal([0d, 4d, 4d, 0d], [inlineEnd.TLX, inlineEnd.TRX, inlineEnd.BRX, inlineEnd.BLX]);
        }

        [Fact]
        public async Task ABevelBackgroundClipIsAStraightCutNotAnArc()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border-radius:20pt;corner-shape:bevel;background-color:steelblue;'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            var path = Assert.Single(recording.DrawnPaths);
            Assert.Empty(path.Arcs);
            Assert.True(path.Closed);
            // Start, then each of the four corners contributes an entry point and an exit point.
            Assert.Equal(9, path.Points.Count);
            // The path spans exactly the 100pt box (whatever the page margin puts its origin at).
            Assert.Equal(100d, path.Points.Max(p => p.X) - path.Points.Min(p => p.X), 2);
            Assert.Equal(100d, path.Points.Max(p => p.Y) - path.Points.Min(p => p.Y), 2);
        }

        [Fact]
        public async Task ASquareCornerKeepsTheBoxCornerAndAScoopCutsInside()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border-radius:20pt;corner-top-left-shape:square;corner-bottom-right-shape:notch;background-color:steelblue;'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            var path = Assert.Single(recording.DrawnPaths);
            var left = path.Points.Min(p => p.X);
            var top = path.Points.Min(p => p.Y);
            // The square top-left corner reaches the box's own corner...
            Assert.Contains(path.Points, p => p.X == left && p.Y == top);
            // ...and the notch at the bottom-right goes to the corner box's inner corner, 20pt in from each edge.
            Assert.Contains(path.Points, p => System.Math.Abs(p.X - (left + 80)) < 0.01 && System.Math.Abs(p.Y - (top + 80)) < 0.01);
        }

        [Fact]
        public async Task ASquircleIsFlattenedIntoManyPointsInsideItsCornerBox()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border-radius:30pt;corner-shape:squircle;background-color:steelblue;'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            var path = Assert.Single(recording.DrawnPaths);
            Assert.Empty(path.Arcs);
            Assert.True(path.Points.Count > 60);
            Assert.Equal(100d, path.Points.Max(p => p.X) - path.Points.Min(p => p.X), 2);
            Assert.Equal(100d, path.Points.Max(p => p.Y) - path.Points.Min(p => p.Y), 2);
        }

        [Fact]
        public async Task AllRoundCornersStillUseTheExactArcs()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border-radius:20pt;corner-shape:round;background-color:steelblue;'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            var path = Assert.Single(recording.DrawnPaths);
            Assert.Equal(4, path.Arcs.Count);
        }

        [Fact]
        public async Task ABeveledBorderIsPaintedAsARingBetweenTwoShapedContours()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border:6pt solid black;border-radius:20pt;corner-shape:bevel;'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            var ring = Assert.Single(recording.DrawnPaths);
            Assert.Empty(ring.Arcs);
            // The outer and the inner contour, each with four bevelled corners.
            Assert.Equal(18, ring.Points.Count);
        }

        [Fact]
        public async Task ABorderWithDifferentSideColoursIsClippedToOneSectorPerSide()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:100pt;border:6pt solid;border-color:red green blue black;border-radius:20pt;corner-shape:bevel;'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.Equal(4, recording.DrawnPaths.Count);
        }

        [Theory]
        [InlineData("solid")]
        [InlineData("dashed")]
        [InlineData("dotted")]
        [InlineData("double")]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("inset")]
        [InlineData("outset")]
        public async Task EveryBorderStyleIsPaintedOnAShapedCornerWithoutArcs(string style)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='box' style='width:100pt;height:60pt;border:8pt {style} #336699;border-top-color:#993333;border-radius:24pt;corner-shape:squircle scoop bevel notch;'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            // Dotted and dashed sides are stroked along the shaped centerline, every other style is filled.
            var painted = recording.DrawnPaths.Concat(recording.StrokedPaths).ToList();
            Assert.NotEmpty(painted);
            Assert.All(painted, path => Assert.Empty(path.Arcs));
            if (style is "dashed" or "dotted") Assert.NotEmpty(recording.StrokedPaths);
        }

        [Fact]
        public async Task ABorderWithOneSideMissingAndUnevenWidthsStillPaints()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:60pt;border:solid #369;border-width:4pt 0 10pt 6pt;border-radius:20pt;corner-shape:bevel scoop;'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.NotEmpty(recording.DrawnPaths);
        }

        [Fact]
        public async Task AShapedOutlineIsPaintedAroundTheShapedBox()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:60pt;border-radius:20pt;corner-shape:bevel;outline:4pt solid orange;outline-offset:2pt;'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.NotEmpty(recording.DrawnPaths);
            Assert.All(recording.DrawnPaths, path => Assert.Empty(path.Arcs));
        }

        [Fact]
        public async Task AnOverflowClipFollowsTheShapedCorner()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:60pt;border-radius:20pt;corner-shape:bevel;overflow:hidden;'><div style='height:200pt;background:red'></div></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);
            // The clip is applied to the descendants when they are painted.
            FragmentPaintHarness.PaintBox(container, box.Boxes[0], recording);

            Assert.Contains(recording.PushedClipPaths, path => path.Arcs.Count == 0 && path.Points.Count >= 9);
        }

        [Fact]
        public async Task TheClipPathBorderBoxFollowsTheShapedCorner()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:60pt;border-radius:20pt;corner-shape:bevel;clip-path:border-box;background:red'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.Contains(recording.PushedClipPaths, path => path.Arcs.Count == 0 && path.Points.Count >= 9);
        }

        [Fact]
        public async Task AShapedBoxShadowIsBuiltFromShapedLayers()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:60pt;border-radius:20pt;corner-shape:bevel;box-shadow:4pt 4pt 6pt black;background:white'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.NotEmpty(recording.DrawnPaths);
            Assert.All(recording.DrawnPaths, path => Assert.Empty(path.Arcs));
        }

        [Fact]
        public async Task TheBackdropFilterShapeFollowsTheShapedCorner()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='box' style='width:100pt;height:60pt;border-radius:20pt;corner-shape:bevel;backdrop-filter:blur(2pt);background:#fff8'></div>"));
            var box = LayoutHarness.FindById(root, "box")!;

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.True(recording.DrawnPaths.Count > 0 || recording.PushedClipPaths.Count > 0);
        }
    }
}
