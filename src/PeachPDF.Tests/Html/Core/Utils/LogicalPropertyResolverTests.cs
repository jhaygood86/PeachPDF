using PeachPDF.CSS;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// <see cref="LogicalPropertyResolver"/> against CSS Writing Modes Level 4 §7.1's abstract-to-physical
    /// mapping table directly, covering every <c>writing-mode</c> value crossed with both <c>direction</c>
    /// values (10 combinations) - not just the two combinations bidi alone exercises.
    /// </summary>
    public class LogicalPropertyResolverTests
    {
        // int parameters (not the internal WritingMode/DirectionMode/PhysicalSide enums directly): a
        // public [Theory] method cannot declare an internal enum parameter (CS0051).

        [Theory]
        [InlineData((int)WritingMode.HorizontalTb, (int)PhysicalSide.Top)]
        [InlineData((int)WritingMode.VerticalRl, (int)PhysicalSide.Right)]
        [InlineData((int)WritingMode.VerticalLr, (int)PhysicalSide.Left)]
        [InlineData((int)WritingMode.SidewaysRl, (int)PhysicalSide.Right)]
        [InlineData((int)WritingMode.SidewaysLr, (int)PhysicalSide.Left)]
        public void BlockStart_DependsOnlyOnWritingMode(int writingMode, int expected)
        {
            Assert.Equal((PhysicalSide)expected, LogicalPropertyResolver.BlockStart((WritingMode)writingMode));
        }

        [Theory]
        [InlineData((int)WritingMode.HorizontalTb, (int)PhysicalSide.Bottom)]
        [InlineData((int)WritingMode.VerticalRl, (int)PhysicalSide.Left)]
        [InlineData((int)WritingMode.VerticalLr, (int)PhysicalSide.Right)]
        [InlineData((int)WritingMode.SidewaysRl, (int)PhysicalSide.Left)]
        [InlineData((int)WritingMode.SidewaysLr, (int)PhysicalSide.Right)]
        public void BlockEnd_IsTheOppositeOfBlockStart(int writingMode, int expected)
        {
            Assert.Equal((PhysicalSide)expected, LogicalPropertyResolver.BlockEnd((WritingMode)writingMode));
        }

        [Theory]
        [InlineData((int)WritingMode.HorizontalTb, (int)DirectionMode.Ltr, (int)PhysicalSide.Left)]
        [InlineData((int)WritingMode.HorizontalTb, (int)DirectionMode.Rtl, (int)PhysicalSide.Right)]
        [InlineData((int)WritingMode.VerticalRl, (int)DirectionMode.Ltr, (int)PhysicalSide.Top)]
        [InlineData((int)WritingMode.VerticalRl, (int)DirectionMode.Rtl, (int)PhysicalSide.Bottom)]
        [InlineData((int)WritingMode.VerticalLr, (int)DirectionMode.Ltr, (int)PhysicalSide.Top)]
        [InlineData((int)WritingMode.VerticalLr, (int)DirectionMode.Rtl, (int)PhysicalSide.Bottom)]
        [InlineData((int)WritingMode.SidewaysRl, (int)DirectionMode.Ltr, (int)PhysicalSide.Top)]
        [InlineData((int)WritingMode.SidewaysRl, (int)DirectionMode.Rtl, (int)PhysicalSide.Bottom)]
        // sideways-lr is the one writing mode whose inline mapping is reversed relative to
        // vertical-rl/vertical-lr/sideways-rl (CSS Writing Modes 4 §7.1).
        [InlineData((int)WritingMode.SidewaysLr, (int)DirectionMode.Ltr, (int)PhysicalSide.Bottom)]
        [InlineData((int)WritingMode.SidewaysLr, (int)DirectionMode.Rtl, (int)PhysicalSide.Top)]
        public void InlineStart_DependsOnWritingModeAndDirection(int writingMode, int direction, int expected)
        {
            Assert.Equal((PhysicalSide)expected, LogicalPropertyResolver.InlineStart((WritingMode)writingMode, (DirectionMode)direction));
        }

        [Theory]
        [InlineData((int)WritingMode.HorizontalTb, (int)DirectionMode.Ltr, (int)PhysicalSide.Right)]
        [InlineData((int)WritingMode.HorizontalTb, (int)DirectionMode.Rtl, (int)PhysicalSide.Left)]
        [InlineData((int)WritingMode.VerticalRl, (int)DirectionMode.Ltr, (int)PhysicalSide.Bottom)]
        [InlineData((int)WritingMode.VerticalRl, (int)DirectionMode.Rtl, (int)PhysicalSide.Top)]
        [InlineData((int)WritingMode.VerticalLr, (int)DirectionMode.Ltr, (int)PhysicalSide.Bottom)]
        [InlineData((int)WritingMode.VerticalLr, (int)DirectionMode.Rtl, (int)PhysicalSide.Top)]
        [InlineData((int)WritingMode.SidewaysRl, (int)DirectionMode.Ltr, (int)PhysicalSide.Bottom)]
        [InlineData((int)WritingMode.SidewaysRl, (int)DirectionMode.Rtl, (int)PhysicalSide.Top)]
        [InlineData((int)WritingMode.SidewaysLr, (int)DirectionMode.Ltr, (int)PhysicalSide.Top)]
        [InlineData((int)WritingMode.SidewaysLr, (int)DirectionMode.Rtl, (int)PhysicalSide.Bottom)]
        public void InlineEnd_IsTheOppositeOfInlineStart(int writingMode, int direction, int expected)
        {
            Assert.Equal((PhysicalSide)expected, LogicalPropertyResolver.InlineEnd((WritingMode)writingMode, (DirectionMode)direction));
        }
    }
}
