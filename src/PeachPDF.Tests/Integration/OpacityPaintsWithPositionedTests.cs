using PeachPDF.Adapters;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Color 3 §3.2: a non-positioned element with <c>opacity</c> below 1 paints "on the same layer, within its
    /// parent stacking context, as positioned elements with stack level 0" - Appendix E step 8, in tree order with
    /// them. It used to paint in the block or inline step instead, before every positioned element, so the
    /// background of a positioned box that precedes it in the tree covered it.
    /// </summary>
    public class OpacityPaintsWithPositionedTests
    {
        private static async Task<int[]> FillOrderAsync(string body, params double[] fillHeights)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            var recording = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, recording);

            var fills = recording.Log.Where(op => op.Kind == PaintOpKind.FillRect).ToList();

            // The position in the paint log of the fill that is exactly this tall, per requested height.
            return fillHeights
                .Select(height => fills.FindIndex(op => System.Math.Abs(op.Bounds.Height - height) < 0.01))
                .ToArray();
        }

        [Theory]
        [InlineData("inline-block")]
        [InlineData("block")]
        public async Task StaticElementWithOpacity_PaintsOverThePositionedBoxItSitsIn(string display)
        {
            // Heights 40 / 30 / 10 identify the three fills; the opacity box is the last in tree order.
            var order = await FillOrderAsync($@"
                <div style='position:relative; background-color:red; height:40pt'></div>
                <div style='position:relative; background-color:green; height:30pt'>
                    <span style='display:{display}; opacity:.5; background-color:blue; width:20pt; height:10pt'></span>
                </div>", 40, 30, 10);

            Assert.All(order, index => Assert.True(index >= 0, "a fill was not painted"));
            Assert.True(order[0] < order[1], "tree order between the two positioned boxes");
            Assert.True(order[1] < order[2], "the half-transparent box must paint after the positioned box it sits in");
        }

        [Fact]
        public async Task StaticElementWithOpacity_PaintsAfterEarlierPositionedSiblings()
        {
            var order = await FillOrderAsync(@"
                <div style='position:relative; background-color:red; height:40pt'></div>
                <div style='opacity:.5; background-color:blue; height:10pt'></div>", 40, 10);

            Assert.True(order[0] >= 0 && order[1] > order[0]);
        }

        [Fact]
        public async Task ElementWithoutOpacity_StillPaintsInTheNormalFlowStep()
        {
            // Not a stacking context, so it keeps painting before positioned boxes (Appendix E step 4 before step 8).
            var order = await FillOrderAsync(@"
                <div style='position:relative; background-color:red; height:40pt'></div>
                <div style='background-color:blue; height:10pt'></div>", 40, 10);

            Assert.True(order[1] >= 0 && order[1] < order[0]);
        }
    }
}
