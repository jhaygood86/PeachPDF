using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end paint coverage for CSS <c>border-image</c> (CSS Backgrounds and Borders 3 §13): the
    /// 9-slice algorithm itself (<c>BorderImageDrawHandler</c>), <c>border-image-slice</c>'s <c>fill</c>
    /// keyword, <c>-outset</c>, <c>-repeat</c>, and its precedence over the ordinary <c>border-style</c>
    /// stroke. Per this repo's testing convention, painting changes need the actual sequence/geometry of
    /// <c>RGraphics</c> calls asserted, not just that painting completes - a real 4x4 raster image is
    /// decoded by the production adapter (so its intrinsic size is real) and painted through a recording
    /// graphics, and the recorded destination rectangles are asserted against the expected 9-slice geometry.
    /// </summary>
    public class BorderImagePaintIntegrationTests
    {
        // A real, solid 4x4 PNG - intrinsic size is all that matters here (border-image-source doesn't
        // care about its pixel content), so a tiny single-color square keeps the fixture minimal.
        private const string Png4X4 =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAIAAAAmkwkpAAAAE0lEQVR4nGM8YWTEAANMcBZeDgA8MgE0GRiVCQAAAABJRU5ErkJggg==";

        // pt throughout (not px, which resolves at 0.75pt per CSS px - CLAUDE.md's own guidance on
        // fixtures whose numbers should read literally), so the geometry asserted below matches these
        // declared numbers exactly rather than needing a 0.75 conversion factor worked in by hand.
        private static string Doc(string style, double width = 100, double height = 60) =>
            "<!DOCTYPE html><html><head></head><body style=\"margin:0\">" +
            $"<div id='b' style=\"width:{width}pt;height:{height}pt;border:10pt solid black;{style}\"></div>" +
            "</body></html>";

        private static async Task<(CssBox Box, TestRecordingGraphics Graphics)> PaintAsync(
            string style, TestRecordingGraphics? graphics = null, double width = 100, double height = 60)
        {
            var (root, container) = await BuildAndLayout(Doc(style, width, height));
            var box = FindById(root, "b")!;
            var g = graphics ?? new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, box, g);
            return (box, g);
        }

        /// <summary>
        /// <see cref="TestRecordingGraphics"/>'s own <see cref="RGraphics.CreateTile"/> always returns
        /// null (no real page/document context) - correct for every other consumer's own tests, but a
        /// gradient/SVG <c>border-image-source</c> has no natural size of its own and is rendered into
        /// exactly that kind of tile (<c>BorderImageDrawHandler.ResolveSourceImage</c>), so exercising
        /// that path needs a subclass that hands back a real, usable one.
        /// </summary>
        private sealed class TileCapableGraphics : TestRecordingGraphics
        {
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) =>
                (new TestRecordingGraphics(), new TestImage(width, height));
        }

        [Fact]
        public async Task NoBorderImageSource_PaintsOrdinaryBorderInstead()
        {
            var (_, g) = await PaintAsync("");

            Assert.Empty(g.DrawImageCalls);
            // A uniform solid border paints as one closed ring (BoxEdgesDrawHandler's uniform fast path);
            // only a border whose edges differ needs four separately mitred quads.
            Assert.Single(g.FilledShapes);
        }

        [Fact]
        public async Task ValidSource_SuppressesTheOrdinaryBorderStroke()
        {
            var (_, g) = await PaintAsync($"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt");

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
            Assert.NotEmpty(g.DrawImageCalls);
        }

        [Fact]
        public async Task StretchWithNoFill_PaintsExactlyEightRegions_NoMiddle()
        {
            // slice:1 (1px on all sides of a 4x4 source -> a 1px corner/edge band, 2px middle),
            // width:10pt (matches the declared 10pt border on all sides) - the common, simplest case.
            var (box, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt");

            // 4 corners + 4 edges, stretch (one DrawImage call each) - no fill, so no middle.
            Assert.Equal(8, g.DrawImageCalls.Count);

            var border = box.ActualBorderTopWidth; // 10 for all four sides in this fixture
            Assert.Equal(10, border, 1);

            // The border box: content 100x60 + 10pt border all round = 120x80.
            var borderBox = new RRect(0, 0, 120, 80);

            var topLeftCorner = g.DrawImageCalls[0].DestRect;
            Assert.Equal(borderBox.Left, topLeftCorner.X, 1);
            Assert.Equal(borderBox.Top, topLeftCorner.Y, 1);
            Assert.Equal(border, topLeftCorner.Width, 1);
            Assert.Equal(border, topLeftCorner.Height, 1);

            var bottomRightCorner = g.DrawImageCalls[3].DestRect;
            Assert.Equal(borderBox.Right - border, bottomRightCorner.X, 1);
            Assert.Equal(borderBox.Bottom - border, bottomRightCorner.Y, 1);
            Assert.Equal(border, bottomRightCorner.Width, 1);
            Assert.Equal(border, bottomRightCorner.Height, 1);

            // The top edge spans between the two top corners, at the border's own thickness.
            var topEdge = g.DrawImageCalls[4].DestRect;
            Assert.Equal(borderBox.Left + border, topEdge.X, 1);
            Assert.Equal(borderBox.Top, topEdge.Y, 1);
            Assert.Equal(borderBox.Width - 2 * border, topEdge.Width, 1);
            Assert.Equal(border, topEdge.Height, 1);
        }

        [Fact]
        public async Task StretchWithNoFill_TakesEachRegionFromItsOwnNinthOfTheSource()
        {
            // The half of a 9-slice a destination-only assertion cannot see: WHICH part of the source each
            // region draws. PDF has no sub-rectangle image operator, and the backend used to drop srcRect
            // silently - every region drew the whole 4x4 texture squashed into its own band, which looked
            // like a far denser, differently-coloured frame than the author wrote and passed every
            // destination-geometry test here.
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt");

            static void AssertSrc(RRect? src, double x, double y, double w, double h)
            {
                Assert.NotNull(src);
                Assert.Equal(x, src!.Value.X, 3);
                Assert.Equal(y, src.Value.Y, 3);
                Assert.Equal(w, src.Value.Width, 3);
                Assert.Equal(h, src.Value.Height, 3);
            }

            // Corners: the 1x1 pixel in each corner of the 4x4 source.
            AssertSrc(g.DrawImageCalls[0].SrcRect, 0, 0, 1, 1);
            AssertSrc(g.DrawImageCalls[1].SrcRect, 3, 0, 1, 1);
            AssertSrc(g.DrawImageCalls[2].SrcRect, 0, 3, 1, 1);
            AssertSrc(g.DrawImageCalls[3].SrcRect, 3, 3, 1, 1);

            // Edges: the 2px band between the corners, along each side.
            AssertSrc(g.DrawImageCalls[4].SrcRect, 1, 0, 2, 1);
            AssertSrc(g.DrawImageCalls[5].SrcRect, 1, 3, 2, 1);
            AssertSrc(g.DrawImageCalls[6].SrcRect, 0, 1, 1, 2);
            AssertSrc(g.DrawImageCalls[7].SrcRect, 3, 1, 1, 2);
        }

        [Fact]
        public async Task NineSlice_PaintsWithoutInterpolation()
        {
            // Each region is cut out with a clip, so a smoothing renderer samples across that clip edge and
            // bleeds the neighbouring slice half a source pixel into this one - at the 6x-and-up scales a
            // slice-to-border stretch reaches, a visible fraction of the whole border.
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt");

            Assert.All(g.DrawImageCalls, call => Assert.False(call.Interpolate));
        }

        [Fact]
        public async Task Fill_AddsAOneMoreRegionForTheMiddle()
        {
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1 fill;border-image-width:10pt");

            Assert.Equal(9, g.DrawImageCalls.Count);

            // The middle (content box: 100x60, since the 10pt border-image-width matches the declared
            // border) is the last region painted (corners, then edges, then center).
            var middle = g.DrawImageCalls[8].DestRect;
            Assert.Equal(10, middle.X, 1);
            Assert.Equal(10, middle.Y, 1);
            Assert.Equal(100, middle.Width, 1);
            Assert.Equal(60, middle.Height, 1);
        }

        [Fact]
        public async Task Outset_ExpandsThePaintedAreaOutwardFromTheBorderBox()
        {
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt;border-image-outset:5pt");

            // The border-image area is the border box (120x80 at 0,0) extended 5pt on every side:
            // (-5,-5) to (125,85). The top-left corner's own dest rect starts there.
            var topLeftCorner = g.DrawImageCalls[0].DestRect;
            Assert.Equal(-5, topLeftCorner.X, 1);
            Assert.Equal(-5, topLeftCorner.Y, 1);
        }

        [Fact]
        public async Task Repeat_TilesTheEdgeInsteadOfStretchingItOnce()
        {
            var (stretchBox, stretchG) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt;border-image-repeat:stretch");
            var (_, repeatG) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt;border-image-repeat:repeat");

            // Under stretch, the whole 8-region count (4 corners + 4 edges, single call each) - 8 total.
            Assert.Equal(8, stretchG.DrawImageCalls.Count);

            // Under repeat, every edge tiles (more than one DrawImage call for at least one edge), so
            // the total call count must exceed the stretch case's.
            Assert.True(repeatG.DrawImageCalls.Count > stretchG.DrawImageCalls.Count,
                $"expected repeat to emit more draw calls than stretch (stretch={stretchG.DrawImageCalls.Count}, repeat={repeatG.DrawImageCalls.Count})");

            _ = stretchBox;
        }

        [Fact]
        public async Task MixedRepeat_TilesOnlyTheAxisDeclaredRepeat()
        {
            // Horizontally repeated, vertically stretched fill - exercises DrawMiddle's "only one axis
            // tiles" branch (both-stretch and both-tiled are already covered by the fill/repeat tests
            // above).
            var (_, stretchBothG) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1 fill;border-image-width:10pt;border-image-repeat:stretch");
            var (_, mixedG) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1 fill;border-image-width:10pt;border-image-repeat:repeat stretch");

            Assert.True(mixedG.DrawImageCalls.Count > stretchBothG.DrawImageCalls.Count,
                $"expected mixed repeat/stretch to tile the middle horizontally (stretch-both={stretchBothG.DrawImageCalls.Count}, mixed={mixedG.DrawImageCalls.Count})");
        }

        // ─── border-image-repeat: round/space (CSS Backgrounds and Borders 3 §13.5) ───────────────────
        //
        // slice:1 on the 4x4 source (Png4X4) always gives an edge band 2pt-natural-wide (scaled to the
        // edge's own fixed cross-axis thickness) and a 2x2pt middle patch (src rect (1,1,2,2)) - both
        // independent of the fixture's content width/height, which each test below picks specifically to
        // land its axis on an uneven (round) or gap-producing (space) fit against that natural size.

        [Fact]
        public async Task Round_ResizesEdgeTilesToFitAnIntegerCountEvenly()
        {
            // content width 90pt -> border box 110pt -> the top/bottom edge's free-axis extent
            // (areaRect.Width - 2*border) is 90pt. The edge band's natural tile size, scaled to the
            // edge's 10pt cross-axis thickness, is 2 * (10/1) = 20pt. 90/20 = 4.5, which round() takes to
            // 5 tiles of 90/5 = 18pt each - not the natural 20pt a repeat-shaped clip-the-remainder
            // implementation would keep.
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt;border-image-repeat:round",
                width: 90);

            // 4 corners, then the top edge's own tiles (the bottom edge's identical 5 follow it).
            var topEdgeTiles = g.DrawImageCalls.Skip(4).Take(5).ToList();
            Assert.All(topEdgeTiles, call => Assert.Equal(18, call.DestRect.Width, 3));
            Assert.All(topEdgeTiles, call => Assert.Equal(10, call.DestRect.Height, 3));

            // The top edge spans x=10..100 (areaRect 0..110, border-image-width 10pt each side); the
            // resized tiles must fit that exactly, with the last one ending flush at the far side.
            Assert.Equal(10, topEdgeTiles[0].DestRect.X, 3);
            Assert.Equal(100, topEdgeTiles[4].DestRect.X + topEdgeTiles[4].DestRect.Width, 3);
        }

        [Fact]
        public async Task Round_ResizesMiddleTilesToFitAnIntegerCountEvenlyOnBothAxes()
        {
            // content 9x13 -> the fill middle's destWidth/destHeight equal the content box exactly (the
            // 10pt border-image-width matches the declared border). The middle's own natural tile is the
            // 2x2pt source patch. 9/2=4.5 rounds to 5 tiles of 9/5=1.8pt; 13/2=6.5 rounds to 7 tiles of
            // 13/7pt - neither axis's natural size divides its destination evenly.
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1 fill;border-image-width:10pt;border-image-repeat:round",
                width: 9, height: 13);

            var middleSrc = new RRect(1, 1, 2, 2);
            var middleTiles = g.DrawImageCalls.Where(call => call.SrcRect == middleSrc).ToList();

            Assert.Equal(5 * 7, middleTiles.Count);
            Assert.All(middleTiles, call => Assert.Equal(9.0 / 5, call.DestRect.Width, 3));
            Assert.All(middleTiles, call => Assert.Equal(13.0 / 7, call.DestRect.Height, 3));
        }

        [Fact]
        public async Task Space_InsertsEqualGapsBetweenEdgeTiles()
        {
            // content width 110pt -> top/bottom edge destWidth 110pt; natural tile 20pt (as above).
            // floor(110/20) = 5 tiles kept at their natural 20pt size, with the leftover 10pt split into
            // 4 equal 2.5pt gaps between them (first/last tile still touch the edge's own ends).
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt;border-image-repeat:space",
                width: 110);

            var topEdgeTiles = g.DrawImageCalls.Skip(4).Take(5).ToList();
            Assert.All(topEdgeTiles, call => Assert.Equal(20, call.DestRect.Width, 3));

            const double gap = 2.5;
            for (var i = 0; i < topEdgeTiles.Count; i++)
                Assert.Equal(10 + i * (20 + gap), topEdgeTiles[i].DestRect.X, 3);

            // The first tile touches the edge's own left end and the last touches its right end.
            Assert.Equal(10, topEdgeTiles[0].DestRect.X, 3);
            Assert.Equal(120, topEdgeTiles[4].DestRect.X + topEdgeTiles[4].DestRect.Width, 3);
        }

        [Fact]
        public async Task Space_InsertsEqualGapsBetweenMiddleTilesOnBothAxes()
        {
            // content 7x9 -> middle destWidth 7pt, destHeight 9pt; natural tile 2x2pt.
            // floor(7/2)=3 tiles, leftover 1pt split into 2 gaps of 0.5pt; floor(9/2)=4 tiles, leftover
            // 1pt split into 3 gaps of 1/3pt.
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1 fill;border-image-width:10pt;border-image-repeat:space",
                width: 7, height: 9);

            var middleSrc = new RRect(1, 1, 2, 2);
            var middleTiles = g.DrawImageCalls.Where(call => call.SrcRect == middleSrc).ToList();

            const int countX = 3;
            const int countY = 4;
            var gapX = (7 - countX * 2.0) / (countX - 1);
            var gapY = (9 - countY * 2.0) / (countY - 1);

            Assert.Equal(countX * countY, middleTiles.Count);
            Assert.All(middleTiles, call => Assert.Equal(2, call.DestRect.Width, 3));
            Assert.All(middleTiles, call => Assert.Equal(2, call.DestRect.Height, 3));

            var firstRow = middleTiles.Take(countX).ToList();
            for (var i = 0; i < countX; i++)
                Assert.Equal(10 + i * (2 + gapX), firstRow[i].DestRect.X, 3);

            var firstColumn = middleTiles.Where(call => Math.Abs(call.DestRect.X - 10) < 0.01)
                .OrderBy(call => call.DestRect.Y).ToList();
            for (var j = 0; j < countY; j++)
                Assert.Equal(10 + j * (2 + gapY), firstColumn[j].DestRect.Y, 3);
        }

        [Fact]
        public async Task Space_FallsBackToASingleUnclippedTile_OnAnEdge_WhenLessThanOneWholeTileFits()
        {
            // content width 15pt -> top/bottom edge destWidth 15pt, smaller than the 20pt natural tile
            // size - floor(15/20) = 0, so space falls back to exactly one tile at its natural (unshrunk)
            // 20pt size rather than resizing or clipping it to fit.
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt;border-image-repeat:space",
                width: 15);

            // Exactly one tile for the top edge (index 4, right after the 4 corners) - the bottom edge's
            // own single tile follows immediately at index 5, confirming no extra tiles were inserted.
            var topEdgeTile = g.DrawImageCalls[4];
            Assert.Equal(20, topEdgeTile.DestRect.Width, 3);
            Assert.Equal(10, topEdgeTile.DestRect.Height, 3);
            Assert.Equal(10, topEdgeTile.DestRect.X, 3);

            var bottomEdgeTile = g.DrawImageCalls[5];
            Assert.Equal(20, bottomEdgeTile.DestRect.Width, 3);
        }

        [Fact]
        public async Task Space_FallsBackToASingleUnclippedTile_InTheMiddle_WhenLessThanOneWholeTileFits()
        {
            // content 1x1pt -> middle destWidth/destHeight 1pt, smaller than the 2pt natural tile size on
            // either axis - floor(1/2) = 0 on both, so space falls back to one 2x2pt tile rather than a
            // clipped or resized fit.
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1 fill;border-image-width:10pt;border-image-repeat:space",
                width: 1, height: 1);

            var middleSrc = new RRect(1, 1, 2, 2);
            var middleTiles = g.DrawImageCalls.Where(call => call.SrcRect == middleSrc).ToList();

            var middleTile = Assert.Single(middleTiles);
            Assert.Equal(2, middleTile.DestRect.Width, 3);
            Assert.Equal(2, middleTile.DestRect.Height, 3);
        }

        [Fact]
        public async Task RoundedBorderImage_ClipsToTheOuterCurve()
        {
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{Png4X4}');border-image-slice:1;border-image-width:10pt;border-radius:15pt");

            Assert.NotEmpty(g.ClipPaths);
        }

        [Fact]
        public async Task GradientSource_RendersIntoATileAndPaintsIt()
        {
            // A linear-gradient border-image-source has no natural size of its own - resolved via
            // RGraphics.CreateTile instead of a raster image's own Width/Height (see
            // BorderImageDrawHandler.ResolveSourceImage's own remarks).
            var (_, g) = await PaintAsync(
                "border-image-source:linear-gradient(red,blue);border-image-slice:1;border-image-width:10pt",
                new TileCapableGraphics());

            Assert.Equal(8, g.DrawImageCalls.Count);
        }

        [Fact]
        public async Task GradientSource_WithNoRealTileContext_FallsBackToTheOrdinaryBorder()
        {
            // The plain TestRecordingGraphics's own CreateTile returns null (no real page/document
            // context, e.g. a measure-only pass) - ResolveSourceImage must degrade to "no border-image"
            // rather than throwing, the same as CssImagePainter's own gradient/SVG layers already do.
            var (_, g) = await PaintAsync(
                "border-image-source:linear-gradient(red,blue);border-image-slice:1;border-image-width:10pt");

            Assert.Empty(g.DrawImageCalls);
            Assert.NotEmpty(g.FilledShapes);
        }

        [Fact]
        public async Task RadialGradientSource_RendersIntoATileAndPaintsIt()
        {
            var (_, g) = await PaintAsync(
                "border-image-source:radial-gradient(red,blue);border-image-slice:1;border-image-width:10pt",
                new TileCapableGraphics());

            Assert.Equal(8, g.DrawImageCalls.Count);
        }

        [Fact]
        public async Task ConicGradientSource_RendersIntoATileAndPaintsIt()
        {
            var (_, g) = await PaintAsync(
                "border-image-source:conic-gradient(red,blue);border-image-slice:1;border-image-width:10pt",
                new TileCapableGraphics());

            Assert.Equal(8, g.DrawImageCalls.Count);
        }

        [Fact]
        public async Task SvgUrlSource_RendersIntoATileAndPaintsIt()
        {
            const string svg = "data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22%3E%3C/svg%3E";
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{svg}');border-image-slice:1;border-image-width:10pt",
                new TileCapableGraphics());

            Assert.Equal(8, g.DrawImageCalls.Count);
        }

        [Fact]
        public async Task SvgSourceWithAnIntrinsicSize_IsSlicedAtThatSize_NotStretchedOverTheBorderBox()
        {
            // CSS Images 3's default sizing algorithm: an SVG carrying its own width/height has an
            // intrinsic size, so that - not the border-image area - is what the slices are cut from.
            // Sizing it to the area first stretched the artwork over the box before slicing it, which
            // distorted the motif and gave every edge tile the wrong aspect ratio.
            const string svg = "data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%2240%22 height=%2240%22 viewBox=%220 0 40 40%22%3E%3Crect width=%2240%22 height=%2240%22 fill=%22red%22/%3E%3C/svg%3E";
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{svg}');border-image-slice:50%;border-image-width:10pt",
                new TileCapableGraphics());

            // 40 CSS px of SVG is 30pt of layout, and the tile is built at exactly that - not at the
            // fixture's own 120x80pt border box.
            var image = g.DrawImageCalls[0].Image;
            Assert.Equal(30, image.Width, 3);
            Assert.Equal(30, image.Height, 3);

            // ...so a 50% slice is 15pt of it, and the top-left corner takes that much.
            var corner = g.DrawImageCalls[0].SrcRect;
            Assert.NotNull(corner);
            Assert.Equal(new RRect(0, 0, 15, 15), corner!.Value);
        }

        [Fact]
        public async Task SvgSourceWithNoIntrinsicSize_FallsBackToTheBorderImageArea()
        {
            // The other half of the default sizing algorithm: with nothing to take a size from, the
            // default object size - the border-image area itself - is used, as for a gradient.
            const string svg = "data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22%3E%3C/svg%3E";
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{svg}');border-image-slice:50%;border-image-width:10pt",
                new TileCapableGraphics());

            var image = g.DrawImageCalls[0].Image;
            Assert.Equal(120, image.Width, 3);
            Assert.Equal(80, image.Height, 3);
        }

        [Fact]
        public async Task SvgSourceSliceNumbers_AreCssPixels_NotLayoutPoints()
        {
            // A <number> slice is "vector coordinates" for a vector source (CSS Backgrounds 3 §13.4),
            // i.e. CSS pixels - while the tile it is cut from is measured in layout points, 0.75 of one.
            const string svg = "data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%2240%22 height=%2240%22 viewBox=%220 0 40 40%22%3E%3Crect width=%2240%22 height=%2240%22 fill=%22red%22/%3E%3C/svg%3E";
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{svg}');border-image-slice:20;border-image-width:10pt",
                new TileCapableGraphics());

            var corner = g.DrawImageCalls[0].SrcRect;
            Assert.NotNull(corner);
            Assert.Equal(15, corner!.Value.Width, 3);   // 20 CSS px of the source, in points
            Assert.Equal(15, corner.Value.Height, 3);
        }

        [Fact]
        public async Task UrlSourceThatFailsToLoad_FallsBackToTheOrdinaryBorder()
        {
            // A url() that parses fine as a CssImage.Url but decodes to neither a raster image nor an
            // SVG document (garbage bytes) - ResolveSourceImage's default arm - must degrade the same
            // way an unresolved gradient tile does, not throw or paint a blank image.
            const string garbage = "data:image/png;base64,bm90LWEtcG5n";
            var (_, g) = await PaintAsync(
                $"border-image-source:url('{garbage}');border-image-slice:1;border-image-width:10pt",
                new TileCapableGraphics());

            Assert.Empty(g.DrawImageCalls);
            Assert.NotEmpty(g.FilledShapes);
        }

        [Fact]
        public async Task DefaultValues_ReachCssBoxAsTheirInitialStrings()
        {
            var (root, _) = await BuildAndLayout(Doc(""));
            var box = FindById(root, "b")!;

            Assert.Null(box.BorderImageSource);
            Assert.Equal("100%", box.BorderImageSlice);
            Assert.Equal("1", box.BorderImageWidth);
            Assert.Equal("0", box.BorderImageOutset);
            Assert.Equal("stretch", box.BorderImageRepeat);
        }

        [Fact]
        public async Task DoesNotInherit()
        {
            // border-image-* is declared "inherited": false in css-properties.json, and BorderArea (the
            // area it's grouped into) is otherwise 100% non-inherited too, so it is never whole-adopted
            // by CssBox.InheritStyle in the first place - unlike line-clamp's own TextArea, no explicit
            // restore-list entry is needed here (see that invariant's own note on when one is).
            var html = "<!DOCTYPE html><html><head></head><body>" +
                $"<div id='parent' style=\"border-image-source:url('{Png4X4}')\"><div id='child'></div></div>" +
                "</body></html>";
            var (root, _) = await BuildAndLayout(html);
            var child = FindById(root, "child")!;

            Assert.Null(child.BorderImageSource);
        }

        // ─── helpers (mirrors ObjectFitPaintIntegrationTests) ──────────────────────

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
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
            if (box.HtmlTag?.TryGetAttribute("id") == id) return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}
