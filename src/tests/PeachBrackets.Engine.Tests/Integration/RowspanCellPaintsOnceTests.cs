using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>A <c>rowspan</c> cell's text is drawn once, however many rows it spans.</summary>
    public class RowspanCellPaintsOnceTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public async Task SpanningCellText_IsDrawnOnce(int span)
        {
            var rows = string.Concat(Enumerable.Range(0, span).Select(i =>
                i == 0 ? $"<tr><td id='s' rowspan='{span}'>SPANNING</td><td>A</td></tr>" : $"<tr><td>B{i}</td></tr>"));
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<table style='border-collapse:collapse'>{rows}</table>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var drawn = g.Log.OfType<TestRecordingGraphics.DrawStringCall>().Count(c => c.Text.Contains("SPANNING"));
            Assert.Equal(1, drawn);
        }
    }
}
