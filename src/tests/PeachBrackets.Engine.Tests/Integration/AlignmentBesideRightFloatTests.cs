using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A float shortens the line boxes beside it (CSS 2.1 section 9.5), so <c>text-align</c> aligns to the shortened edge (CSS Text 3 section 6.1):
    /// a justified, centered or end-aligned line stops at a right float instead of running under it.
    /// </summary>
    public class AlignmentBesideRightFloatTests
    {
        private const string Text = "aaa bbb ccc ddd eee fff ggg hhh iii jjj kkk lll mmm nnn ooo ppp";

        private static async Task<(double FloatLeft, double MaxWordRight, double MinWordLeft)> Measure(string align)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style='width:200pt;font:10pt monospace;text-align:{align}'><p id='p' style='margin:0'>" +
                "<span id='f' style='float:right;width:30pt;height:12pt;margin:0 0 4pt 6pt'></span>" + Text + "</p></div>"));

            var p = LayoutHarness.FindById(root, "p")!;
            var floated = LayoutHarness.FindById(root, "f")!;
            var firstLine = p.LineBoxes[0];
            var words = firstLine.Words;
            return (floated.Location.X - floated.ActualMarginLeft, words.Max(w => w.Right), words.Min(w => w.Left));
        }

        [Fact]
        public async Task Justify_EndsAtTheFloat_NotUnderIt()
        {
            var (floatLeft, maxRight, _) = await Measure("justify");
            Assert.True(maxRight <= floatLeft + 0.5, $"line ends at {maxRight}, float starts at {floatLeft}");
            Assert.True(maxRight >= floatLeft - 8, $"line should be justified out to the float ({maxRight} vs {floatLeft})");
        }

        [Fact]
        public async Task Right_FlushesToTheFloat()
        {
            var (floatLeft, maxRight, _) = await Measure("right");
            Assert.Equal(floatLeft, maxRight, 1.0);
        }

        [Fact]
        public async Task Center_StaysClearOfTheFloat()
        {
            var (floatLeft, maxRight, _) = await Measure("center");
            Assert.True(maxRight <= floatLeft + 0.5);
        }
    }
}
