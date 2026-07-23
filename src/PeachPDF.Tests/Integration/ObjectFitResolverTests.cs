using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Pure-geometry unit tests for <see cref="ObjectFitResolver.Compute"/> (the shared engine behind
    /// object-fit/object-position for every replaced box). A 100×100pt content box at (10, 20) holds a
    /// 2:1 (200×100pt) image; sizes are asserted directly.
    /// </summary>
    public class ObjectFitResolverTests
    {
        private static readonly RRect Box = new(10, 20, 100, 100);
        private const double IntrinsicWidth = 200, IntrinsicHeight = 100; // 2:1, in points

        [Fact]
        public async Task Fill_ReturnsContentBoxUnchanged()
        {
            var (dest, clip) = ObjectFitResolver.Compute(Box, IntrinsicWidth, IntrinsicHeight, "fill", "50% 50%", await AnyBox());
            Assert.Equal(Box.X, dest.X); Assert.Equal(Box.Y, dest.Y);
            Assert.Equal(100, dest.Width); Assert.Equal(100, dest.Height);
            Assert.False(clip);
        }

        [Fact]
        public async Task UnknownValue_FallsBackToFill()
        {
            var (dest, clip) = ObjectFitResolver.Compute(Box, IntrinsicWidth, IntrinsicHeight, "banana", "50% 50%", await AnyBox());
            Assert.Equal(100, dest.Width); Assert.Equal(100, dest.Height);
            Assert.False(clip);
        }

        [Fact]
        public async Task ZeroIntrinsic_ReturnsContentBoxUnchanged()
        {
            var (dest, clip) = ObjectFitResolver.Compute(Box, 0, 0, "contain", "50% 50%", await AnyBox());
            Assert.Equal(100, dest.Width); Assert.Equal(100, dest.Height);
            Assert.False(clip);
        }

        [Fact]
        public async Task Contain_FitsInsidePreservingAspect_Centered()
        {
            var (dest, clip) = ObjectFitResolver.Compute(Box, IntrinsicWidth, IntrinsicHeight, "contain", "50% 50%", await AnyBox());
            Assert.Equal(100, dest.Width); Assert.Equal(50, dest.Height);
            Assert.Equal(Box.X, dest.X); Assert.Equal(Box.Y + 25, dest.Y); // 50pt of vertical slack, centered
            Assert.False(clip);
        }

        [Fact]
        public async Task Cover_FillsAndOverflows_ClipRequested()
        {
            var (dest, clip) = ObjectFitResolver.Compute(Box, IntrinsicWidth, IntrinsicHeight, "cover", "50% 50%", await AnyBox());
            Assert.Equal(200, dest.Width); Assert.Equal(100, dest.Height);
            Assert.Equal(Box.X - 50, dest.X); // -100pt horizontal slack, centered
            Assert.True(clip);
        }

        [Fact]
        public async Task None_UsesIntrinsic_ClipsWhenLarger()
        {
            var (dest, clip) = ObjectFitResolver.Compute(Box, IntrinsicWidth, IntrinsicHeight, "none", "50% 50%", await AnyBox());
            Assert.Equal(200, dest.Width); Assert.Equal(100, dest.Height);
            Assert.True(clip);
        }

        [Fact]
        public async Task ScaleDown_UsesContainWhenIntrinsicLargerThanBox()
        {
            var (dest, clip) = ObjectFitResolver.Compute(Box, IntrinsicWidth, IntrinsicHeight, "scale-down", "50% 50%", await AnyBox());
            Assert.Equal(100, dest.Width); Assert.Equal(50, dest.Height); // == contain
            Assert.False(clip);
        }

        [Fact]
        public async Task ScaleDown_UsesNoneWhenIntrinsicFits()
        {
            var (dest, clip) = ObjectFitResolver.Compute(Box, 40, 20, "scale-down", "50% 50%", await AnyBox());
            Assert.Equal(40, dest.Width); Assert.Equal(20, dest.Height); // == none (intrinsic fits)
            Assert.False(clip);
        }

        [Fact]
        public async Task ObjectPosition_TopLeft_PlacesAtOrigin()
        {
            var (dest, _) = ObjectFitResolver.Compute(Box, IntrinsicWidth, IntrinsicHeight, "contain", "left top", await AnyBox());
            Assert.Equal(Box.X, dest.X); Assert.Equal(Box.Y, dest.Y);
        }

        private static async Task<CssBox> AnyBox()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml("<!DOCTYPE html><html><body></body></html>", null);
            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);
            return container.Root!;
        }
    }
}
