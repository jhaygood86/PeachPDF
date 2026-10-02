using PeachDrawing.Core;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class DecorationPixelSnappingTests
    {
        private const double Px = 0.75;

        private sealed class Surface : TestRecordingGraphics
        {
            public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;
            public bool Tile { get; set; }
            public override Matrix3x2 CurrentTransform => Transform;
            public override bool IsOffscreenTile => Tile;
        }

        [Fact]
        public void Snap_RoundsEveryEdgeToTheNearestWholeCssPixel()
        {
            // 70.87pt is 94.49px and rounds to 94px; 193.9pt is 258.53px and rounds to 259px.
            var snapped = DecorationPixelSnapping.Snap(new Surface(), Rect.FromLTRB(70.87, 10.2, 193.9, 40.5));

            Assert.Equal(94 * Px, snapped.Left, 6);
            Assert.Equal(259 * Px, snapped.Right, 6);
            Assert.Equal(14 * Px, snapped.Top, 6);
            Assert.Equal(54 * Px, snapped.Bottom, 6);
        }

        [Fact]
        public void Snap_LeavesAnAlreadySnappedRectAlone()
        {
            var rect = Rect.FromLTRB(12 * Px, 8 * Px, 100 * Px, 60 * Px);

            Assert.Equal(rect, DecorationPixelSnapping.Snap(new Surface(), rect));
        }

        [Fact]
        public void Snap_ScalesTheGridWithPixelsPerPoint()
        {
            // At 2 layout units per point a CSS pixel is 1.5 units wide.
            var snapped = DecorationPixelSnapping.Snap(
                new Surface { PixelsPerPointOverride = 2 }, Rect.FromLTRB(1.4, 1.4, 10.4, 10.4));

            Assert.Equal(1.5, snapped.Left, 6);
            Assert.Equal(10.5, snapped.Right, 6);
        }

        [Fact]
        public void Snap_IsSkippedUnderATransform()
        {
            var rect = Rect.FromLTRB(70.87, 10.2, 193.9, 40.5);
            var g = new Surface { Transform = Matrix3x2.CreateRotation(0.1f) };

            Assert.Equal(rect, DecorationPixelSnapping.Snap(g, rect));
        }

        [Fact]
        public void Snap_AppliesInAnOffscreenCanvasThatPaintsInPageCoordinates()
        {
            // An opacity or blend-mode layer and a raster region paint in the page's own coordinates, so a
            // box inside one has to land where the same box on the page does.
            var rect = Rect.FromLTRB(70.87, 10.2, 193.9, 40.5);

            Assert.Equal(
                DecorationPixelSnapping.Snap(new Surface(), rect),
                DecorationPixelSnapping.Snap(new Surface { Tile = true }, rect));
            Assert.NotEqual(rect, DecorationPixelSnapping.Snap(new Surface { Tile = true }, rect));
        }

        [Fact]
        public void Snap_KeepsASubPixelAxisVisible()
        {
            // Both x edges round to the same line (and both y edges to another): the original is kept.
            var rect = Rect.FromLTRB(9.9, 5.0, 10.0, 5.1);

            Assert.Equal(rect, DecorationPixelSnapping.Snap(new Surface(), rect));
        }

        [Fact]
        public void Snap_IsSkippedWhenTheGridIsDegenerate()
        {
            var rect = Rect.FromLTRB(70.87, 10.2, 193.9, 40.5);

            Assert.Equal(rect, DecorationPixelSnapping.Snap(new Surface { PixelsPerPointOverride = 0 }, rect));
        }

        private const string FractionalBox =
            "<div id='b' style='margin-left:10.3pt; margin-top:5.2pt; width:100.2pt; height:30.1pt; border:1px solid rgb(10,20,30)'>x</div>";

        private static async Task<(double MinX, double MinY, double MaxX, double MaxY)> BorderExtentAsync(bool snap)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(FractionalBox));
            container.SnapBoxDecorationsToCssPixels = snap;
            var box = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, box, g);

            var points = g.Log
                .SelectMany(entry => entry switch
                {
                    TestRecordingGraphics.DrawPathCall path => path.Points,
                    TestRecordingGraphics.DrawPolygonCall polygon => polygon.Points,
                    _ => [],
                })
                .ToList();
            Assert.NotEmpty(points);

            return (points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        }

        [Fact]
        public async Task Border_IsPaintedOnItsFractionalEdges_ByDefault()
        {
            var (minX, minY, maxX, maxY) = await BorderExtentAsync(snap: false);

            // margin 20pt (page) + 10.3pt / 5.2pt; none of these is a whole CSS pixel.
            Assert.Equal(30.3, minX, 3);
            Assert.Equal(25.2, minY, 3);
            Assert.NotEqual(0, (minX / Px) % 1, 3);
            Assert.NotEqual(0, (maxX / Px) % 1, 3);
        }

        [Fact]
        public async Task Border_SnapsEveryEdgeToAWholeCssPixel_WhenOptedIn()
        {
            var plain = await BorderExtentAsync(snap: false);
            var snapped = await BorderExtentAsync(snap: true);

            foreach (var edge in new[] { snapped.MinX, snapped.MinY, snapped.MaxX, snapped.MaxY })
            {
                Assert.Equal(0, edge / Px - System.Math.Round(edge / Px), 6);
            }

            // Each edge moves by at most half a CSS pixel.
            Assert.InRange(System.Math.Abs(snapped.MinX - plain.MinX), 0, Px / 2 + 1e-6);
            Assert.InRange(System.Math.Abs(snapped.MinY - plain.MinY), 0, Px / 2 + 1e-6);
            Assert.InRange(System.Math.Abs(snapped.MaxX - plain.MaxX), 0, Px / 2 + 1e-6);
            Assert.InRange(System.Math.Abs(snapped.MaxY - plain.MaxY), 0, Px / 2 + 1e-6);
            Assert.NotEqual(plain.MinX, snapped.MinX);
        }

        private static async Task<TestRecordingGraphics> PaintBackgroundBoxAsync(bool snap)
        {
            const string box =
                "<div id='b' style='margin-left:10.3pt; margin-top:5.2pt; width:100.2pt; height:30.1pt; " +
                "background:rgb(200,220,250); border:1px solid rgb(10,20,30); outline:1px solid rgb(250,0,0)'>x</div>";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(box));
            container.SnapBoxDecorationsToCssPixels = snap;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "b")!, g);
            return g;
        }

        private static TestRecordingGraphics.DrawRectCall BackgroundRect(TestRecordingGraphics g) =>
            Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                call => call.PaintColor == PaintColor.FromArgb(200, 220, 250));

        [Fact]
        public async Task Background_IsPaintedOnItsFractionalEdges_ByDefault()
        {
            var rect = BackgroundRect(await PaintBackgroundBoxAsync(snap: false));

            Assert.Equal(30.3, rect.X, 3);
            Assert.Equal(25.2, rect.Y, 3);
        }

        [Fact]
        public async Task Background_SharesTheSnappedBorderEdges_WhenOptedIn()
        {
            var g = await PaintBackgroundBoxAsync(snap: true);
            var rect = BackgroundRect(g);

            foreach (var edge in new[] { rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height })
            {
                Assert.Equal(0, edge / Px - System.Math.Round(edge / Px), 6);
            }

            // The border's outer edge is the background's: nothing of the background shows past it.
            var borderPoints = g.Log
                .SelectMany(entry => entry switch
                {
                    TestRecordingGraphics.DrawPathCall path when path.PaintColor == PaintColor.FromArgb(10, 20, 30) => path.Points,
                    TestRecordingGraphics.DrawPolygonCall polygon when polygon.PaintColor == PaintColor.FromArgb(10, 20, 30) => polygon.Points,
                    _ => [],
                })
                .ToList();
            Assert.NotEmpty(borderPoints);
            Assert.Equal(rect.X, borderPoints.Min(p => p.X), 6);
            Assert.Equal(rect.Y, borderPoints.Min(p => p.Y), 6);
            Assert.Equal(rect.X + rect.Width, borderPoints.Max(p => p.X), 6);
            Assert.Equal(rect.Y + rect.Height, borderPoints.Max(p => p.Y), 6);
        }

        [Fact]
        public void SnapGeometry_MovesTheDecorationAndClipRectsTogether()
        {
            var rect = Rect.FromLTRB(70.87, 10.2, 193.9, 40.5);
            var snapped = DecorationPixelSnapping.Snap(new Surface(), BoxDecorationGeometry.Unbroken(rect));

            Assert.Equal(DecorationPixelSnapping.Snap(new Surface(), rect), snapped.DecorationRect);
            Assert.Equal(snapped.DecorationRect, snapped.ClipRect);
            Assert.False(snapped.NeedsClip);
        }

        [Fact]
        public void SnapGeometry_SnapsASlicedBoxsStripAndTheEdgesItsSliceOwns()
        {
            // The slice is the left part of the strip: its left, top and bottom are the box's own, its right
            // edge is a cut (a page or line break) and stays where it is.
            var strip = Rect.FromLTRB(70.87, 10.2, 193.9, 40.5);
            var slice = Rect.FromLTRB(70.87, 10.2, 120.1, 40.5);
            var geometry = new BoxDecorationGeometry(strip, slice,
                HasLeftEdge: true, HasRightEdge: false, HasTopEdge: true, HasBottomEdge: true, NeedsClip: true);

            var snapped = DecorationPixelSnapping.Snap(new Surface(), geometry);

            Assert.Equal(DecorationPixelSnapping.Snap(new Surface(), strip), snapped.DecorationRect);
            Assert.True(snapped.NeedsClip);
            Assert.Equal(snapped.DecorationRect.Left, snapped.ClipRect.Left, 9);
            Assert.Equal(snapped.DecorationRect.Top, snapped.ClipRect.Top, 9);
            Assert.Equal(snapped.DecorationRect.Bottom, snapped.ClipRect.Bottom, 9);
            Assert.Equal(120.1, snapped.ClipRect.Right, 9);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AdjacentBoxes_NeverOverlap_AndTouchingOnesStayTouching(bool snap)
        {
            // Three equal columns of 66.67pt (88.9px) with no gap: every edge is fractional, and each
            // box's right edge is the next box's left edge in layout.
            const string row =
                "<div style='display:flex; margin-left:10.3pt; width:200pt'>" +
                "<div id='a' style='flex:1; height:30pt; background:rgb(200,220,250); border:1px solid rgb(10,20,30)'></div>" +
                "<div id='b' style='flex:1; height:30pt; background:rgb(200,220,250); border:1px solid rgb(10,20,30)'></div>" +
                "<div id='c' style='flex:1; height:30pt; background:rgb(200,220,250); border:1px solid rgb(10,20,30)'></div></div>";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(row));
            container.SnapBoxDecorationsToCssPixels = snap;

            var rects = new System.Collections.Generic.List<TestRecordingGraphics.DrawRectCall>();
            foreach (var id in new[] { "a", "b", "c" })
            {
                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, id)!, g);
                rects.Add(Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                    call => call.PaintColor == PaintColor.FromArgb(200, 220, 250)));
            }

            for (var i = 0; i < 2; i++)
            {
                var right = rects[i].X + rects[i].Width;
                // Rounding is monotonic, so edges that met in layout still meet: no overlap, no gap.
                Assert.Equal(rects[i + 1].X, right, snap ? 6 : 3);
            }
        }

        [Fact]
        public void Snap_LeavesAnEdgeThatIsNotARealBoxEdge()
        {
            // The top and bottom here are cuts across a page, not edges of the box, so they stay put.
            var rect = Rect.FromLTRB(70.87, 10.2, 193.9, 40.5);
            var snapped = DecorationPixelSnapping.Snap(new Surface(), rect, top: false, bottom: false);

            Assert.Equal(94 * Px, snapped.Left, 6);
            Assert.Equal(259 * Px, snapped.Right, 6);
            Assert.Equal(10.2, snapped.Top, 6);
            Assert.Equal(40.5, snapped.Bottom, 6);
        }

        [Fact]
        public void SnapGeometry_OnlySnapsTheEdgesTheBoxOwns()
        {
            var rect = Rect.FromLTRB(70.87, 10.2, 193.9, 40.5);
            var geometry = new BoxDecorationGeometry(rect, rect,
                HasLeftEdge: true, HasRightEdge: true, HasTopEdge: false, HasBottomEdge: true, NeedsClip: false);

            var snapped = DecorationPixelSnapping.Snap(new Surface(), geometry);

            Assert.Equal(10.2, snapped.DecorationRect.Top, 6);
            Assert.Equal(54 * Px, snapped.DecorationRect.Bottom, 6);
            Assert.Equal(snapped.DecorationRect, snapped.ClipRect);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OverflowClip_FollowsTheSnappedBox(bool snap)
        {
            const string box =
                "<div id='b' style='margin-left:10.3pt; margin-top:5.2pt; width:100.2pt; height:30.1pt; overflow:hidden; " +
                "border:1px solid rgb(10,20,30)'><div style='height:60pt; background:rgb(200,220,250)'></div></div>";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(box));
            container.SnapBoxDecorationsToCssPixels = snap;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "b")!, g);

            var clip = g.Log.OfType<TestRecordingGraphics.PushClipCall>().First(c => c.Rect.Width < 1000).Rect;
            var onGrid = new[] { clip.Left, clip.Top, clip.Right, clip.Bottom }
                .All(edge => System.Math.Abs(edge / Px - System.Math.Round(edge / Px)) < 1e-6);

            // The padding box: an unsnapped one sits on a fractional edge, a snapped one on the grid, so
            // the child it clips never covers part of the snapped border.
            Assert.Equal(snap, onGrid);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TableBorder_AtAPageBreak_SharesTheSnappedBottomWithTheBackground(bool snap)
        {
            var rows = string.Concat(Enumerable.Range(0, 8).Select(i => $"<tr><td style='height:37.3pt'>{i}</td></tr>"));
            var table =
                "<table id='t' style='border-collapse:separate; border-spacing:0; margin-left:10.3pt; width:200.2pt; " +
                $"background:rgb(200,220,250); border:1px solid rgb(10,20,30)'>{rows}</table>";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(table), pageHeight: 150);
            container.SnapBoxDecorationsToCssPixels = snap;
            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "t")!, g);

            var background = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                call => call.PaintColor == PaintColor.FromArgb(200, 220, 250));
            var borderBottom = g.Log
                .SelectMany(entry => entry switch
                {
                    TestRecordingGraphics.DrawPathCall path when path.PaintColor == PaintColor.FromArgb(10, 20, 30) => path.Points,
                    TestRecordingGraphics.DrawPolygonCall polygon when polygon.PaintColor == PaintColor.FromArgb(10, 20, 30) => polygon.Points,
                    _ => [],
                })
                .Max(p => p.Y);

            // The page-break Y the border is cut at is a layout coordinate; snapped like the other edges,
            // it lands on the grid.
            var onGrid = System.Math.Abs(borderBottom / Px - System.Math.Round(borderBottom / Px)) < 1e-6;
            Assert.Equal(snap, onGrid);
        }

        [Fact]
        public void Snap_TreatsTwoComputationsOfTheSameHalfPixelEdgeAlike()
        {
            // 100.5px is exactly on a rounding boundary: one box's right edge (left + width) and the next
            // box's left edge (a running sum) differ by float noise, and must still land on the same line.
            var halfway = 100.5 * Px;
            var a = DecorationPixelSnapping.Snap(new Surface(), Rect.FromLTRB(0, 0, halfway - 1e-12, 10));
            var b = DecorationPixelSnapping.Snap(new Surface(), Rect.FromLTRB(halfway + 1e-12, 0, 200, 10));

            Assert.Equal(a.Right, b.Left, 9);
        }

        private static bool OnGrid(double edge) =>
            System.Math.Abs(edge / Px - System.Math.Round(edge / Px)) < 1e-6;

        private static async Task<System.Collections.Generic.List<Rect>> OverflowClipsAsync(string html, bool snap)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            container.SnapBoxDecorationsToCssPixels = snap;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, root, g);

            return g.Log.OfType<TestRecordingGraphics.PushClipCall>()
                .Select(call => call.Rect)
                .Where(rect => rect.Width < 1000 && rect.Height < 1000)
                .ToList();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RoundedOverflowClip_IsSnappedLikeARectangularOne(bool snap)
        {
            var clips = await OverflowClipsAsync(
                "<div style='margin-left:10.3pt; margin-top:5.2pt; width:100.2pt; height:30.1pt; overflow:hidden; " +
                "border:1px solid rgb(10,20,30); border-radius:6pt'><div style='height:60pt; background:rgb(200,220,250)'></div></div>",
                snap);

            Assert.NotEmpty(clips);
            Assert.Equal(snap, clips.All(clip => new[] { clip.Left, clip.Top, clip.Right, clip.Bottom }.All(OnGrid)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task HoistedDescendant_IsClippedWhereAnOrdinaryChildWouldBe(bool snap)
        {
            // The z-indexed child is hoisted to the stacking context above its overflow:hidden parent, which
            // re-applies that parent's clip on its own path.
            var clips = await OverflowClipsAsync(
                "<div style='margin-left:10.3pt; margin-top:5.2pt; width:100.2pt; height:30.1pt; overflow:hidden; " +
                "border:1px solid rgb(10,20,30)'><div style='position:relative; z-index:1; height:60pt; " +
                "background:rgb(200,220,250)'></div></div>",
                snap);

            Assert.NotEmpty(clips);
            Assert.Equal(snap, clips.All(clip => new[] { clip.Left, clip.Top, clip.Right, clip.Bottom }.All(OnGrid)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CollapsedTableBorders_AreSnappedWithTheCellBackgrounds(bool snap)
        {
            const string table =
                "<table id='t' style='border-collapse:collapse; margin-left:10.3pt; margin-top:5.2pt; width:200.2pt'>" +
                "<tr><td style='height:21.3pt; background:rgb(200,220,250); border:1px solid rgb(10,20,30)'>a</td>" +
                "<td style='background:rgb(200,220,250); border:1px solid rgb(10,20,30)'>b</td></tr></table>";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(table));
            container.SnapBoxDecorationsToCssPixels = snap;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "t")!, g);

            var points = g.Log
                .SelectMany(entry => entry switch
                {
                    TestRecordingGraphics.DrawRectCall rect when rect.PaintColor == PaintColor.FromArgb(10, 20, 30) =>
                        new[] { rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height },
                    TestRecordingGraphics.DrawPathCall path when path.PaintColor == PaintColor.FromArgb(10, 20, 30) =>
                        path.Points.SelectMany(point => new[] { point.X, point.Y }),
                    TestRecordingGraphics.DrawPolygonCall polygon when polygon.PaintColor == PaintColor.FromArgb(10, 20, 30) =>
                        polygon.Points.SelectMany(point => new[] { point.X, point.Y }),
                    _ => [],
                })
                .ToList();
            Assert.NotEmpty(points);
            Assert.Equal(snap, points.All(OnGrid));
        }

        private static string Normalized(PeachPdfDocument document)
        {
            using var stream = new System.IO.MemoryStream();
            document.Save(stream);

            // The writer stamps a date and the save time into header comments, gives each font subset a
            // random tag and the trailer a random ID; blank those so what is left can only differ by what
            // was painted.
            return System.Text.RegularExpressions.Regex.Replace(
                System.Text.Encoding.Latin1.GetString(stream.ToArray()),
                @"(?m)^%.*$|[A-Z]{6}\+|/ID \[[^\]]*\]", "");
        }

        private static PdfGenerateConfig ConfigFor(bool? snap)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                CompressContentStreams = false,
                Metadata = new PdfDocumentMetadata { CreationDate = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc) },
            };
            if (snap is { } value) config.SnapBoxDecorationsToCssPixels = value;
            return config;
        }

        private static async Task AssertOptionReachesThePaintAsync(
            System.Func<PdfGenerateConfig, Task<PeachPdfDocument>> render)
        {
            var byDefault = Normalized(await render(ConfigFor(null)));
            var off = Normalized(await render(ConfigFor(false)));
            var on = Normalized(await render(ConfigFor(true)));

            Assert.False(new PdfGenerateConfig().SnapBoxDecorationsToCssPixels);

            // Two renders that differ only in the option must differ, and two that do not must not: the
            // second is what makes the first mean the flag rather than document IDs or timestamps.
            Assert.Equal(byDefault, off);
            Assert.NotEqual(off, on);
        }

        [Theory]
        [InlineData("opacity:.5", false)]
        [InlineData("opacity:.5", true)]
        [InlineData("mix-blend-mode:multiply", false)]
        public Task BoxInAnOffscreenLayer_IsSnappedLikeOneOnThePage(string style, bool flatten) =>
            AssertOptionReachesThePaintAsync(config =>
            {
                if (flatten) config.TransparencyPolicy = TransparencyPolicy.Flatten;
                return new PdfGenerator().GeneratePdf(
                    LayoutHarness.Wrap(FractionalBox.Replace("style='", $"style='{style}; ")), config);
            });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task EllipsisBoundary_FollowsTheClipThatCutsTheText(bool snap)
        {
            // A glyph is several points wide, so the ellipsis only lands in the sliver between the raw and the
            // snapped padding edge for some widths: sweep the box width so that some of them do.
            for (var width = 100.5; width < 112; width += 0.25)
            {
                var box =
                    $"<div id='b' style='margin-left:10.3pt; width:{width.ToString(System.Globalization.CultureInfo.InvariantCulture)}pt; " +
                    "overflow:hidden; white-space:nowrap; text-overflow:ellipsis; border:1px solid rgb(10,20,30); font:10pt Arial'>" +
                    "WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW</div>";
                var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(box));
                container.SnapBoxDecorationsToCssPixels = snap;

                var g = new TestRecordingGraphics();
                FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "b")!, g);

                var ellipsis = g.Log.OfType<TestRecordingGraphics.DrawStringCall>()
                    .Where(call => call.Text.Contains('…') || call.Text.Contains("..."))
                    .ToList();
                Assert.NotEmpty(ellipsis);

                // The padding box's right edge: page margin and left margin, a 1px border, the width.
                var padRight = 20 + 10.3 + Px + width;
                var visibleRight = snap ? System.Math.Floor(padRight / Px + 0.5) * Px : padRight;
                var right = ellipsis.Max(call => call.PaintPoint.X + call.Size.Width);
                Assert.True(right <= visibleRight + 1e-6, $"width {width}: ellipsis ends at {right}, clip at {visibleRight}");
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OverflowClip_IsTheSnappedBorderBoxInsetByTheBordersOwnWidth(bool hoisted)
        {
            // 2pt is 2.67px: a clip snapped on its own would land on the grid, but the border is drawn at
            // its true width inside the snapped outer edge, so its inner edge is the grid plus 2pt.
            var child = hoisted
                ? "<div style='position:relative; z-index:1; height:60pt; background:rgb(200,220,250)'></div>"
                : "<div style='height:60pt; background:rgb(200,220,250)'></div>";
            var clips = await OverflowClipsAsync(
                "<div style='margin-left:10.3pt; margin-top:5.2pt; width:100.2pt; height:30.1pt; overflow:hidden; " +
                $"border:2pt solid rgb(10,20,30)'>{child}</div>",
                snap: true);

            Assert.NotEmpty(clips);
            foreach (var clip in clips)
            {
                Assert.True(OnGrid(clip.Left - 2) && OnGrid(clip.Top - 2) && OnGrid(clip.Right + 2) && OnGrid(clip.Bottom + 2),
                    $"clip {clip.Left},{clip.Top},{clip.Right},{clip.Bottom} is not the snapped border box inset by 2pt");
            }
        }

        [Fact]
        public async Task OverflowClip_KeepsAFragmentainerBandUnsnapped()
        {
            // A displaced fragment's clip is the ancestor's padding box intersected with a page or column band.
            // The band is a cut, not an edge of the ancestor, so only the ancestor's edges may move.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='o' style='margin-left:10.3pt; margin-top:5.2pt; width:100.2pt; height:60.1pt; overflow:hidden; " +
                "border:1px solid rgb(10,20,30)'><div id='c' style='height:60pt'>x</div></div>"));
            container.SnapBoxDecorationsToCssPixels = true;

            var child = FragmentPaintHarness.FragmentOf(container, LayoutHarness.FindById(root, "c")!);
            var basis = Assert.IsType<PeachPDF.Html.Core.Fragments.OverflowClipBasis>(child.OverflowClipBasis);

            var band = Rect.FromLTRB(0, 0, 1000, 40.1);
            var confined = child with { OverflowClipBasis = basis with { Band = band } };

            var (clip, _) = new FragmentPainter(container).OverflowClipOf(new Surface(), confined);

            Assert.Equal(40.1, clip!.Value.Bottom, 9);
            Assert.True(OnGrid(clip.Value.Left) && OnGrid(clip.Value.Top) && OnGrid(clip.Value.Right));
        }

        [Fact]
        public Task PdfGenerator_PassesTheConfigOptionThrough() =>
            AssertOptionReachesThePaintAsync(
                config => new PdfGenerator().GeneratePdf(LayoutHarness.Wrap(FractionalBox), config));

        [Fact]
        public Task DeclarativeDocumentBuilder_PassesTheConfigOptionThrough() =>
            AssertOptionReachesThePaintAsync(
                config => new PdfGenerator().CreateDocument(
                    doc => doc.Page(page => page.Content(content => content.Html(FractionalBox))), config));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReplacedElement_IsClippedLikeItsSiblingsInsideAnOverflowBox(bool snap)
        {
            var clips = await OverflowClipsAsync(
                "<div style='margin-left:10.3pt; margin-top:5.2pt; width:100.2pt; height:30.1pt; overflow:hidden; " +
                "border:1px solid rgb(10,20,30)'><svg width='40' height='20'><rect width='40' height='20' fill='red'/></svg></div>",
                snap);

            // The box's own padding-box clips (about 100pt wide), not the SVG's own viewport clip, whose
            // content is a replaced element's and is not snapped.
            var paddingBoxClips = clips.Where(clip => clip.Width > 90).ToList();
            Assert.NotEmpty(paddingBoxClips);
            Assert.Equal(snap, paddingBoxClips.All(clip => new[] { clip.Left, clip.Top, clip.Right, clip.Bottom }.All(OnGrid)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CollapsedDashedBorder_KeepsItsStrokeWidthInStepWithTheSnappedRect(bool snap)
        {
            const string table =
                "<table id='t' style='border-collapse:collapse; margin-left:10.3pt; margin-top:5.2pt; width:200.2pt'>" +
                "<tr><td style='height:21.3pt; border:1.5px dashed rgb(10,20,30)'>a</td></tr></table>";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(table));
            container.SnapBoxDecorationsToCssPixels = snap;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "t")!, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .Where(call => call.PaintColor == PaintColor.FromArgb(10, 20, 30))
                .ToList();
            Assert.NotEmpty(lines);

            // A 1.5px stroke is 2px (or 1px) once its rectangle is snapped: its width must be a whole
            // number of CSS pixels, and so must the position of its centre line's two edges.
            Assert.Equal(snap, lines.All(line => OnGrid(line.Width)
                && OnGrid(line.Y1 == line.Y2 ? line.Y1 - line.Width / 2 : line.X1 - line.Width / 2)));
        }
    }
}
