using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>position: relative</c> on a table's caption, row group, row or cell moves that part, and what it holds, by its offsets (CSS 2.1 section 9.4.3) and
    /// leaves every other part where the table put it.
    /// </summary>
    public class RelativelyPositionedTablePartTests
    {
        private const string Cells = "<td id='a' style='padding: 0'><div id='ad' style='width: 50pt; height: 50pt'></div></td><td id='b' style='padding: 0'><div style='width: 50pt; height: 50pt'></div></td>";

        private static async Task<CssBox> LayoutRootAsync(string table)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(table));
            return root;
        }

        private static CssBox Find(CssBox root, string id) =>
            LayoutHarness.FindById(root, id) ?? throw new Xunit.Sdk.XunitException($"no box #{id}");

        [Theory]
        [InlineData("<table style='border-spacing: 0'><tbody id='g' {0}><tr id='r'>" + Cells + "</tr></tbody></table>", "g")]
        [InlineData("<table style='border-spacing: 0'><tbody><tr id='g' {0}>" + Cells + "</tr></tbody></table>", "g")]
        public async Task ARelativeRowGroupOrRow_MovesWithEveryCellItHolds(string template, string moved)
        {
            var plain = await LayoutRootAsync(string.Format(template, ""));
            var shifted = await LayoutRootAsync(string.Format(template, "style='position: relative; left: 30pt; top: 12pt'"));

            Assert.Equal(Find(plain, moved).Location.X + 30, Find(shifted, moved).Location.X, 3);
            Assert.Equal(Find(plain, moved).Location.Y + 12, Find(shifted, moved).Location.Y, 3);
            foreach (var id in new[] { "a", "b", "ad" })
            {
                Assert.Equal(Find(plain, id).Location.X + 30, Find(shifted, id).Location.X, 3);
                Assert.Equal(Find(plain, id).Location.Y + 12, Find(shifted, id).Location.Y, 3);
            }
        }

        [Fact]
        public async Task ARelativeCell_MovesAloneAndTheOthersStay()
        {
            var plain = await LayoutRootAsync("<table style='border-spacing: 0'><tr>" + Cells + "</tr></table>");
            var shifted = await LayoutRootAsync("<table style='border-spacing: 0'><tr><td id='a' style='padding: 0; position: relative; left: 7pt; top: 3pt'><div id='ad' style='width: 50pt; height: 50pt'></div></td>"
                                                + "<td id='b' style='padding: 0'><div style='width: 50pt; height: 50pt'></div></td></tr></table>");

            Assert.Equal(Find(plain, "a").Location.X + 7, Find(shifted, "a").Location.X, 3);
            Assert.Equal(Find(plain, "ad").Location.Y + 3, Find(shifted, "ad").Location.Y, 3);
            Assert.Equal(Find(plain, "b").Location.X, Find(shifted, "b").Location.X, 3);
            Assert.Equal(Find(plain, "b").Location.Y, Find(shifted, "b").Location.Y, 3);
        }

        [Fact]
        public async Task ARelativeRowAndItsRelativeCell_OffsetsAdd()
        {
            var plain = await LayoutRootAsync("<table style='border-spacing: 0'><tr>" + Cells + "</tr></table>");
            var shifted = await LayoutRootAsync("<table style='border-spacing: 0'><tr style='position: relative; left: 10pt'>"
                                                + "<td id='a' style='padding: 0; position: relative; left: 5pt'><div id='ad' style='width: 50pt; height: 50pt'></div></td>"
                                                + "<td id='b' style='padding: 0'><div style='width: 50pt; height: 50pt'></div></td></tr></table>");

            Assert.Equal(Find(plain, "a").Location.X + 15, Find(shifted, "a").Location.X, 3);
            Assert.Equal(Find(plain, "ad").Location.X + 15, Find(shifted, "ad").Location.X, 3);
            Assert.Equal(Find(plain, "b").Location.X + 10, Find(shifted, "b").Location.X, 3);
        }

        [Fact]
        public async Task APercentageTop_OnARow_ResolvesAgainstTheHeightItsRowGroupDeclares()
        {
            var plain = await LayoutRootAsync("<table style='border-spacing: 0'><tbody style='height: 20pt'><tr id='r'>" + Cells + "</tr></tbody></table>");
            var shifted = await LayoutRootAsync("<table style='border-spacing: 0'><tbody style='height: 20pt'><tr id='r' style='position: relative; top: 50%'>" + Cells + "</tr></tbody></table>");

            Assert.Equal(Find(plain, "r").Location.Y + 10, Find(shifted, "r").Location.Y, 3);
        }

        [Fact]
        public async Task ACaptionThatDeclaresAWidth_WidensTheTableToIt()
        {
            var root = await LayoutRootAsync("<table style='border-spacing: 0'><caption id='cap' style='width: 80pt; height: 10pt'></caption><tr><td id='a' style='padding: 0; width: 20pt'><div style='height: 5pt'></div></td></tr></table>");

            var caption = Find(root, "cap");
            var cell = Find(root, "a");

            Assert.Equal(80, caption.ActualRight - caption.Location.X, 1);
            Assert.True(cell.ActualRight - cell.Location.X >= 79, $"the cell is {cell.ActualRight - cell.Location.X}pt wide");
        }

        [Fact]
        public async Task AnAbsoluteBoxInACell_WhoseContainingBlockIsARelativeRowGroup_IsPlacedAgainstTheGroupWhereItEndsUp()
        {
            var plain = await LayoutRootAsync("<table style='border-spacing: 0'><tbody id='g' style='position: relative'><tr><td style='padding: 0'><div id='x' style='position: absolute; left: 20pt; top: 4pt; width: 5pt; height: 5pt'></div>"
                                              + "<div style='width: 50pt; height: 50pt'></div></td></tr></tbody></table>");
            var shifted = await LayoutRootAsync("<table style='border-spacing: 0'><tbody id='g' style='position: relative; left: 9pt'><tr><td style='padding: 0'><div id='x' style='position: absolute; left: 20pt; top: 4pt; width: 5pt; height: 5pt'></div>"
                                                + "<div style='width: 50pt; height: 50pt'></div></td></tr></tbody></table>");

            // Against the group's own place, not the origin the group sat at while its cells were laid out.
            Assert.Equal(Find(plain, "g").Location.X + 20, Find(plain, "x").Location.X, 3);
            Assert.Equal(Find(plain, "g").Location.Y + 4, Find(plain, "x").Location.Y, 3);
            Assert.Equal(Find(plain, "x").Location.X + 9, Find(shifted, "x").Location.X, 3);
        }
    }
}
