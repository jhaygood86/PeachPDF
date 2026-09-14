using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
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
        private static string Doc(string style) =>
            "<!DOCTYPE html><html><head></head><body style=\"margin:0\">" +
            $"<div id='b' style=\"width:100pt;height:60pt;border:10pt solid black;{style}\"></div>" +
            "</body></html>";

        private static async Task<(CssBox Box, TestRecordingGraphics Graphics)> PaintAsync(string style, TestRecordingGraphics? graphics = null)
        {
            var (root, container) = await BuildAndLayout(Doc(style));
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
            // A solid border paints a mitered quad (BordersDrawHandler.SetInOutsetRectanglePoints) via
            // DrawPolygon, one per edge.
            Assert.Equal(4, g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>().Count());
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
            Assert.NotEmpty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
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
            Assert.NotEmpty(g.Log.OfType<TestRecordingGraphics.DrawPolygonCall>());
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
