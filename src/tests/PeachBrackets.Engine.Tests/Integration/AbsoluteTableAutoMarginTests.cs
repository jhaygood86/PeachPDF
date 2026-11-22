using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>An absolutely positioned table keeps the height its rows give it between two insets, and auto margins centre it there (CSS 2.1 section 10.6.4).</summary>
    public class AbsoluteTableAutoMarginTests
    {
        [Fact]
        public async Task ATableWithTopAndBottomInsetsAndAutoMargins_IsCentredAtItsContentHeight()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='cb' style='position: relative; width: 100pt; height: 200pt'><div id='t' style='display: table; position: absolute; top: 0; bottom: 0; margin: auto; width: 100pt'>"
                + "<div style='width: 100pt; height: 100pt'></div></div></div>"));
            var cb = LayoutHarness.FindById(root, "cb")!;
            var table = LayoutHarness.FindById(root, "t")!;

            Assert.Equal(100, table.ActualBottom - table.Location.Y, 1);
            Assert.Equal(cb.Location.Y + 50, table.Location.Y, 1);
        }
    }
}
