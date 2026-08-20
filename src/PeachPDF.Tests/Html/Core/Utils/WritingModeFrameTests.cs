using System.Threading.Tasks;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// <see cref="WritingModeFrame"/>'s logical-to-physical geometry conversion, exercised directly via
    /// <see cref="WritingModeFrame.ForContentBox"/> against a fixed content box
    /// (left=10, top=20, right=110, bottom=220 - a 100x200 box) for every writing-mode x direction
    /// combination in scope (horizontal-tb, vertical-rl, vertical-lr; sideways-* fall back to
    /// horizontal-tb's identity behavior, matching <see cref="WritingModeFrame.IsVertical"/>).
    /// </summary>
    public class WritingModeFrameTests
    {
        // int parameters, not the internal WritingMode/DirectionMode enums directly: a public [Theory]
        // method cannot declare an internal enum parameter (CS0051).
        private const double Left = 10;
        private const double Top = 20;
        private const double Right = 110;
        private const double Bottom = 220;

        private static WritingModeFrame Frame(WritingMode writingMode, DirectionMode direction = DirectionMode.Ltr) =>
            WritingModeFrame.ForContentBox(Left, Top, Right, Bottom, writingMode, direction);

        [Theory]
        [InlineData((int)WritingMode.HorizontalTb, false)]
        [InlineData((int)WritingMode.VerticalRl, true)]
        [InlineData((int)WritingMode.VerticalLr, true)]
        [InlineData((int)WritingMode.SidewaysRl, false)]
        [InlineData((int)WritingMode.SidewaysLr, false)]
        public void IsVertical_TrueOnlyForVerticalRlAndVerticalLr(int writingMode, bool expected)
        {
            Assert.Equal(expected, Frame((WritingMode)writingMode).IsVertical);
        }

        [Fact]
        public void HorizontalTb_LogicalContentExtentsMatchPhysicalWidthHeight()
        {
            var frame = Frame(WritingMode.HorizontalTb);

            Assert.Equal(Right - Left, frame.LogicalContentWidth);
            Assert.Equal(Bottom - Top, frame.LogicalContentHeight);
        }

        [Theory]
        [InlineData((int)WritingMode.VerticalRl)]
        [InlineData((int)WritingMode.VerticalLr)]
        public void Vertical_LogicalContentExtentsAreSwapped(int writingMode)
        {
            var frame = Frame((WritingMode)writingMode);

            Assert.Equal(Bottom - Top, frame.LogicalContentWidth);
            Assert.Equal(Right - Left, frame.LogicalContentHeight);
        }

        [Fact]
        public void HorizontalTb_ToPhysicalRect_IsAPureTranslationByTheContentOrigin()
        {
            var frame = Frame(WritingMode.HorizontalTb);

            var physical = frame.ToPhysical(new RRect(5, 7, 30, 40));

            Assert.Equal(new RRect(Left + 5, Top + 7, 30, 40), physical);
        }

        [Fact]
        public void HorizontalTb_DirectionIsNotConsulted()
        {
            var ltr = Frame(WritingMode.HorizontalTb, DirectionMode.Ltr);
            var rtl = Frame(WritingMode.HorizontalTb, DirectionMode.Rtl);

            Assert.Equal(ltr.ToPhysical(new RRect(5, 7, 30, 40)), rtl.ToPhysical(new RRect(5, 7, 30, 40)));
        }

        [Fact]
        public void VerticalRl_Ltr_BlockAxisGrowsFromRightEdgeLeftward_InlineAxisGrowsDownwardFromTop()
        {
            var frame = Frame(WritingMode.VerticalRl, DirectionMode.Ltr);

            // logical: inline offset 5, block offset 0, inline size 30 (physical height), block size 12 (physical width)
            var physical = frame.ToPhysical(new RRect(5, 0, 30, 12));

            // block-start (offset 0) touches the content box's right edge; block axis runs physical-X.
            Assert.Equal(Right - 12, physical.X);
            Assert.Equal(12, physical.Width);
            // inline-start (ltr) is the top edge; inline axis runs physical-Y, growing downward.
            Assert.Equal(Top + 5, physical.Y);
            Assert.Equal(30, physical.Height);
        }

        [Fact]
        public void VerticalRl_Ltr_SecondBlockLineSitsToTheLeftOfTheFirst()
        {
            var frame = Frame(WritingMode.VerticalRl, DirectionMode.Ltr);

            var firstLine = frame.ToPhysical(new RRect(0, 0, 30, 12));
            var secondLine = frame.ToPhysical(new RRect(0, 12, 30, 12));

            Assert.Equal(firstLine.X - 12, secondLine.X);
        }

        [Fact]
        public void VerticalLr_Ltr_BlockAxisGrowsFromLeftEdgeRightward()
        {
            var frame = Frame(WritingMode.VerticalLr, DirectionMode.Ltr);

            var firstLine = frame.ToPhysical(new RRect(0, 0, 30, 12));
            var secondLine = frame.ToPhysical(new RRect(0, 12, 30, 12));

            Assert.Equal(Left, firstLine.X);
            Assert.Equal(firstLine.X + 12, secondLine.X);
        }

        [Fact]
        public void VerticalRl_Rtl_InlineAxisGrowsUpwardFromBottom()
        {
            var frame = Frame(WritingMode.VerticalRl, DirectionMode.Rtl);

            var physical = frame.ToPhysical(new RRect(0, 0, 30, 12));

            // inline-start (rtl, vertical) is the bottom edge.
            Assert.Equal(Bottom - 30, physical.Y);
            Assert.Equal(30, physical.Height);
        }

        [Theory]
        [InlineData((int)WritingMode.VerticalRl)]
        [InlineData((int)WritingMode.VerticalLr)]
        public void Vertical_ToPhysicalSize_SwapsWidthAndHeight(int writingMode)
        {
            var frame = Frame((WritingMode)writingMode);

            var physical = frame.ToPhysical(new RSize(30, 12));

            Assert.Equal(new RSize(12, 30), physical);
        }

        [Fact]
        public void HorizontalTb_ToPhysicalSize_IsUnchanged()
        {
            var frame = Frame(WritingMode.HorizontalTb);

            var physical = frame.ToPhysical(new RSize(30, 12));

            Assert.Equal(new RSize(30, 12), physical);
        }

        [Fact]
        public void ToPhysicalPoint_MatchesTheZeroSizeRectCase()
        {
            var frame = Frame(WritingMode.VerticalRl, DirectionMode.Ltr);

            var point = frame.ToPhysical(5, 7);
            var rectLocation = frame.ToPhysical(new RRect(5, 7, 0, 0)).Location;

            Assert.Equal(rectLocation, point);
        }

        [Fact]
        public async Task For_BuildsTheSameFrameAsForContentBox_FromARealCssBoxsOwnResolvedGeometry()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; direction: rtl; width: 100px; height: 60px; padding: 5px">x</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el");

            Assert.NotNull(el);

            var viaFor = WritingModeFrame.For(el!);
            var viaContentBox = WritingModeFrame.ForContentBox(
                el!.ClientLeft, el.ClientTop, el.ClientRight, el.ClientBottom, el.WritingMode.Value, el.Direction.Value);

            Assert.True(viaFor.IsVertical);
            Assert.Equal(viaContentBox.LogicalContentWidth, viaFor.LogicalContentWidth);
            Assert.Equal(viaContentBox.LogicalContentHeight, viaFor.LogicalContentHeight);
            Assert.Equal(viaContentBox.ToPhysical(new RRect(1, 2, 3, 4)), viaFor.ToPhysical(new RRect(1, 2, 3, 4)));
        }
    }
}
