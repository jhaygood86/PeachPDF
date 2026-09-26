using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Whether a line box's recorded top (<c>CssLineBox.FlowTop</c>) moves with its words when a mover
    /// translates them, since the fragment emitter reads it to decide which page a line is on.
    /// </summary>
    public class LineTopFollowsItsWordsTests
    {
        private const string Style =
            "<style>@page{size:300pt 200pt;margin:20pt} body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>";

        // A bottom-aligned cell with its text directly in it owns the line that text is on, but the
        // alignment moves the cell's children, not the cell. The line's recorded top has to move with its
        // words, since the fragment emitter reads it to decide which page the line is on.
        [Theory]
        [InlineData("bottom")]
        [InlineData("middle")]
        public async Task AnAlignedCellsOwnLine_KeepsItsLineTopWithItsWords(string align)
        {
            var html = $"<!DOCTYPE html><html><head>{Style}</head><body><table><tr>"
                       + $"<td id='cell' style='vertical-align:{align};height:100pt'>text</td></tr></table></body></html>";

            var (root, _) = await LayoutHarness.LayoutAsync(html, 300, 200);
            var cell = LayoutHarness.FindById(root, "cell")!;
            var line = Assert.Single(cell.LineBoxes);
            var word = Assert.Single(line.Words);

            Assert.True(word.Top > cell.Location.Y + 30, "the fixture must actually move the text down");
            Assert.NotNull(line.FlowTop);
            Assert.InRange(word.Top - line.FlowTop!.Value, -2, 2);
        }
    }
}
