using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 §10.8.1: <c>vertical-align</c> moves an inline-level box together with its content. A text-less
    /// <c>inline-grid</c>/<c>inline-table</c> holds no baseline to recognise it as a unit by, so each is checked
    /// here against the same markup laid out with <c>vertical-align: baseline</c>.
    /// </summary>
    public class InlineGridTableVerticalAlignIntegrationTests
    {
        private static string Markup(string display, string align) => display switch
        {
            "inline-grid" => LayoutHarness.Wrap(
                "<div style='font-size:30pt'>Xg " +
                $"<span id='c' style='display:inline-grid; position:relative; vertical-align:{align}; width:40pt; height:12pt'>" +
                "<div id='i' style='width:10pt; height:4pt'></div>" +
                "<div id='o' style='position:absolute; top:5pt; left:7pt; width:10pt; height:4pt'></div>" +
                "</span> Xg</div>"),
            _ => LayoutHarness.Wrap(
                "<div style='font-size:30pt'>Xg " +
                $"<span id='c' style='display:inline-table; position:relative; vertical-align:{align}; width:40pt; height:12pt'>" +
                "<span style='display:table-row'><span id='i' style='display:table-cell; width:10pt; height:4pt'></span></span>" +
                "<div id='o' style='position:absolute; top:5pt; left:7pt; width:10pt; height:4pt'></div>" +
                "</span> Xg</div>"),
        };

        [Theory]
        [InlineData("inline-grid", "bottom")]
        [InlineData("inline-grid", "middle")]
        [InlineData("inline-grid", "text-bottom")]
        [InlineData("inline-table", "bottom")]
        [InlineData("inline-table", "middle")]
        [InlineData("inline-table", "text-bottom")]
        public async Task ContentFollowsTheBox_WhenVerticalAlignMovesATextlessBox(string display, string align)
        {
            var (baselineRoot, _) = await LayoutHarness.LayoutAsync(Markup(display, "baseline"));
            var (root, _) = await LayoutHarness.LayoutAsync(Markup(display, align));

            var baseBox = LayoutHarness.FindById(baselineRoot, "c")!;
            var box = LayoutHarness.FindById(root, "c")!;
            var shift = box.Rectangles.Values.Single().Y - baseBox.Rectangles.Values.Single().Y;
            Assert.True(System.Math.Abs(shift) > 1, "the alignment must actually move the box");

            // The box's Location, its in-flow item and its absolute child all move by the box's own shift, once.
            Assert.Equal(box.Rectangles.Values.Single().Y, box.Location.Y, 1.0);
            Assert.Equal(baseBox.Location.Y + shift, box.Location.Y, 1.0);

            var item = LayoutHarness.FindById(root, "i")!;
            var baseItem = LayoutHarness.FindById(baselineRoot, "i")!;
            Assert.Equal(baseItem.Location.Y + shift, item.Location.Y, 1.0);

            var abs = LayoutHarness.FindById(root, "o")!;
            Assert.Equal(box.Location.X + 7, abs.Location.X, 1.0);
            Assert.Equal(box.Location.Y + 5, abs.Location.Y, 1.0);
        }
    }
}
