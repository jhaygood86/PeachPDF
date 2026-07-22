using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layer-B tests for <see cref="CssClipPathResolver.TryBuildClipPath"/>: resolves a validated
    /// <c>clip-path</c> value against a known reference box and asserts the produced
    /// <see cref="RGraphicsPath"/> geometry (captured via <see cref="TestGraphicsPath"/>).
    /// </summary>
    public class ClipPathResolverIntegrationTests
    {
        private static async Task<CssBox> BuildBoxAsync()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml("<div></div>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            return container.Root!;
        }

        [Fact]
        public async Task Polygon_ResolvesPercentAndLengthPointsAgainstReferenceBox()
        {
            var box = await BuildBoxAsync();
            var g = new TestRecordingGraphics();
            var reference = new RRect(100, 200, 300, 400);

            var built = CssClipPathResolver.TryBuildClipPath(
                g, "polygon(0% 0%, 100% 0%, 50% 100%)", reference, box, out var path, out var useEvenOdd);

            Assert.True(built);
            Assert.False(useEvenOdd);
            var points = ((TestGraphicsPath)path!).Points;

            // (0%,0%)->(100,200), (100%,0%)->(400,200), (50%,100%)->(250,600)
            Assert.Equal(3, points.Count);
            Assert.Equal(100, points[0].X, 3);
            Assert.Equal(200, points[0].Y, 3);
            Assert.Equal(400, points[1].X, 3);
            Assert.Equal(200, points[1].Y, 3);
            Assert.Equal(250, points[2].X, 3);
            Assert.Equal(600, points[2].Y, 3);
        }

        [Fact]
        public async Task Polygon_Evenodd_SetsUseEvenOdd()
        {
            var box = await BuildBoxAsync();
            var g = new TestRecordingGraphics();
            var reference = new RRect(0, 0, 100, 100);

            var built = CssClipPathResolver.TryBuildClipPath(
                g, "polygon(evenodd, 0 0, 100px 0, 0 100px)", reference, box, out var path, out var useEvenOdd);

            Assert.True(built);
            Assert.True(useEvenOdd);
            Assert.Equal(RFillMode.EvenOdd, path!.FillMode);
        }

        [Fact]
        public async Task Inset_ResolvesRectangleEdges()
        {
            var box = await BuildBoxAsync();
            var g = new TestRecordingGraphics();
            var reference = new RRect(0, 0, 200, 100);

            // top/bottom against height(100), left/right against width(200): inset(10pt 20pt 30pt 40pt).
            // pt units are used so the expected values equal the layout units (points) 1:1.
            var built = CssClipPathResolver.TryBuildClipPath(
                g, "inset(10pt 20pt 30pt 40pt)", reference, box, out var path, out _);

            Assert.True(built);
            var points = ((TestGraphicsPath)path!).Points;

            // rect LTRB = (0+40, 0+10) .. (200-20, 100-30) = (40,10)..(180,70)
            Assert.Equal(4, points.Count);
            Assert.Equal(40, points[0].X, 3);
            Assert.Equal(10, points[0].Y, 3);
            Assert.Equal(180, points[1].X, 3);
            Assert.Equal(10, points[1].Y, 3);
            Assert.Equal(180, points[2].X, 3);
            Assert.Equal(70, points[2].Y, 3);
            Assert.Equal(40, points[3].X, 3);
            Assert.Equal(70, points[3].Y, 3);
        }

        [Fact]
        public async Task Circle_ClosestSide_UsesMinCenterToEdgeDistance()
        {
            var box = await BuildBoxAsync();
            var g = new TestRecordingGraphics();
            var reference = new RRect(0, 0, 200, 100);

            // center default 50% 50% => (100,50). closest-side = min(100,100,50,50) = 50.
            var built = CssClipPathResolver.TryBuildClipPath(
                g, "circle()", reference, box, out var path, out _);

            Assert.True(built);
            var points = ((TestGraphicsPath)path!).Points;

            // AppendEllipse records 5 cardinal points (start + 4 arc endpoints).
            var minX = points.Min(p => p.X);
            var maxX = points.Max(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxY = points.Max(p => p.Y);
            Assert.Equal(50, minX, 3);   // 100 - 50
            Assert.Equal(150, maxX, 3);  // 100 + 50
            Assert.Equal(0, minY, 3);    // 50 - 50
            Assert.Equal(100, maxY, 3);  // 50 + 50
        }

        [Fact]
        public async Task Ellipse_ExplicitRadiiAndCenter()
        {
            var box = await BuildBoxAsync();
            var g = new TestRecordingGraphics();
            var reference = new RRect(0, 0, 200, 100);

            // rx 25% of 200 = 50, ry 40% of 100 = 40, center 50% 50% = (100,50).
            var built = CssClipPathResolver.TryBuildClipPath(
                g, "ellipse(25% 40% at 50% 50%)", reference, box, out var path, out _);

            Assert.True(built);
            var points = ((TestGraphicsPath)path!).Points;

            Assert.Equal(50, points.Min(p => p.X), 3);   // 100 - 50
            Assert.Equal(150, points.Max(p => p.X), 3);  // 100 + 50
            Assert.Equal(10, points.Min(p => p.Y), 3);   // 50 - 40
            Assert.Equal(90, points.Max(p => p.Y), 3);   // 50 + 40
        }

        [Theory]
        [InlineData("none")]
        [InlineData("banana")]
        [InlineData("")]
        public async Task InvalidOrNone_ReturnsFalse(string value)
        {
            var box = await BuildBoxAsync();
            var g = new TestRecordingGraphics();

            var built = CssClipPathResolver.TryBuildClipPath(
                g, value, new RRect(0, 0, 100, 100), box, out var path, out var useEvenOdd);

            Assert.False(built);
            Assert.Null(path);
            Assert.False(useEvenOdd);
        }
    }
}
