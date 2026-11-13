using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>margin</c> and <c>padding</c> do not apply to every box (CSS 2.1 sections 8.3 and 8.4): the internal table boxes ignore them, a cell excepted for
    /// padding.
    /// </summary>
    public class MarginPaddingApplicabilityTests
    {
        [Theory]
        [InlineData("table-row-group")]
        [InlineData("table-header-group")]
        [InlineData("table-footer-group")]
        [InlineData("table-row")]
        [InlineData("table-column-group")]
        [InlineData("table-column")]
        [InlineData("table-cell")]
        public async Task MarginDoesNotApplyToAnInternalTableBox(string display)
        {
            var (root, _) = await LayoutAsync(Wrap($"<div style='display:table'><div id='b' style='display:{display};margin:30pt'></div></div>"));
            var b = FindById(root, "b")!;

            Assert.Equal(0, b.ActualMarginTop);
            Assert.Equal(0, b.ActualMarginBottom);
            Assert.Equal(0, b.ActualMarginLeft);
            Assert.Equal(0, b.ActualMarginRight);
        }

        [Theory]
        [InlineData("table-row-group", 0.0)]
        [InlineData("table-row", 0.0)]
        [InlineData("table-column", 0.0)]
        [InlineData("table-cell", 7.0)]
        [InlineData("block", 7.0)]
        public async Task PaddingDoesNotApplyToAnInternalTableBox_ExceptACell(string display, double expected)
        {
            var (root, _) = await LayoutAsync(Wrap($"<div style='display:table'><div id='b' style='display:{display};padding:7pt'></div></div>"));
            var b = FindById(root, "b")!;

            Assert.Equal(expected, b.ActualPaddingTop, 3);
            Assert.Equal(expected, b.ActualPaddingLeft, 3);
        }

        [Fact]
        public async Task MarginStillAppliesToATableAndACaption()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='t' style='display:table;margin:9pt'><div id='c' style='display:table-caption;margin:5pt'>x</div></div>"));

            Assert.Equal(9, FindById(root, "t")!.ActualMarginTop, 3);
            Assert.Equal(5, FindById(root, "c")!.ActualMarginTop, 3);
        }
    }
}
