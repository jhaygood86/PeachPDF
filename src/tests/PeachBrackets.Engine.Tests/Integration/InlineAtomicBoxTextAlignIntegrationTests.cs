using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS Text 3 §6.1: <c>text-align</c> aligns a line's inline-level content, an atomic
    /// <c>inline-flex</c>/<c>inline-grid</c>/<c>inline-table</c> box included at its margin-box width.
    /// The container is a 300pt block at x=20 (the body margin), so a 120pt box alone on its line sits at 20 (left), 110
    /// (center) or 200 (right).
    /// </summary>
    public class InlineAtomicBoxTextAlignIntegrationTests
    {
        private static string Box(string display, string style = "") => display switch
        {
            "inline-table" =>
                $"<span id='b' style='display:inline-table; position:relative; width:120pt; height:10pt; {style}'>" +
                "<span style='display:table-row'><span id='i' style='display:table-cell; width:120pt; height:10pt'></span></span>" +
                "<div id='o' style='position:absolute; top:2pt; left:7pt; width:5pt; height:3pt'></div></span>",
            _ =>
                $"<span id='b' style='display:{display}; position:relative; width:120pt; height:10pt; {style}'>" +
                "<div id='i' style='width:10pt; height:4pt'></div>" +
                "<div id='o' style='position:absolute; top:2pt; left:7pt; width:5pt; height:3pt'></div></span>",
        };

        private static string Html(string align, string content, string dir = "") => LayoutHarness.Wrap(
            $"<div id='p' {dir} style='margin:0; width:300pt; font:10pt monospace; text-align:{align}'>{content}</div>");

        [Theory]
        [InlineData("inline-flex", "center", 110)]
        [InlineData("inline-grid", "center", 110)]
        [InlineData("inline-table", "center", 110)]
        [InlineData("inline-flex", "right", 200)]
        [InlineData("inline-grid", "right", 200)]
        [InlineData("inline-table", "right", 200)]
        [InlineData("inline-grid", "left", 20)]
        [InlineData("inline-grid", "justify", 20)]
        public async Task BoxAloneOnItsLine_IsAlignedAtItsOwnWidth(string display, string align, double x)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Html(align, Box(display)));

            var box = LayoutHarness.FindById(root, "b")!;
            var item = LayoutHarness.FindById(root, "i")!;
            var abs = LayoutHarness.FindById(root, "o")!;

            Assert.Equal(x, box.Location.X, 1.0);
            Assert.Equal(120, box.ActualBoxSizingWidth, 1.0);
            // The in-flow item and the absolute child travel with the box.
            Assert.Equal(box.Location.X, item.Location.X, 1.0);
            Assert.Equal(box.Location.X + 7, abs.Location.X, 1.0);
        }

        [Theory]
        [InlineData("inline-flex")]
        [InlineData("inline-grid")]
        [InlineData("inline-table")]
        public async Task MarginsCountTowardsTheAlignedWidth(string display)
        {
            // 120 + 10 + 30 = 160 wide margin box: centered at 20 + (300-160)/2 = 90, so the border box at 100.
            var (root, _) = await LayoutHarness.LayoutAsync(
                Html("center", Box(display, "margin-left:10pt; margin-right:30pt")));

            Assert.Equal(100, LayoutHarness.FindById(root, "b")!.Location.X, 1.0);
        }

        [Theory]
        [InlineData("inline-flex")]
        [InlineData("inline-grid")]
        [InlineData("inline-table")]
        public async Task BoxAfterText_IsAlignedTogetherWithTheText(string display)
        {
            // "ab " is 3 monospace characters; the line is that text plus the 120pt box.
            var (textRoot, _) = await LayoutHarness.LayoutAsync(Html("left", "ab " + Box(display)));
            var (root, _) = await LayoutHarness.LayoutAsync(Html("right", "ab " + Box(display)));

            var leftBox = LayoutHarness.FindById(textRoot, "b")!;
            var box = LayoutHarness.FindById(root, "b")!;

            // Flush right: the box ends at the container's right edge (20 + 300).
            Assert.Equal(320, box.Location.X + box.ActualBoxSizingWidth, 1.0);
            // ...and sits the same distance after the text it did when left aligned.
            var shift = box.Location.X - leftBox.Location.X;
            Assert.True(shift > 0);
            Assert.Equal(leftBox.Location.X + shift, box.Location.X, 1.0);
        }

        [Fact]
        public async Task JustifiedLine_DoesNotMoveOrResizeTheBox_AndWordsDoNotRunIntoIt()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                Html("justify", "aa bb cc " + Box("inline-grid") + "<br>x"));

            var box = LayoutHarness.FindById(root, "b")!;
            Assert.Equal(120, box.ActualBoxSizingWidth, 1.0);
            // Natural position: after "aa bb cc ".
            Assert.True(box.Location.X < 20 + 300 - 120 + 1.0);
        }

        [Fact]
        public async Task RtlBoxAlone_FlushesToTheStartEdge()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Html("start", Box("inline-grid"), "dir='rtl'"));

            var box = LayoutHarness.FindById(root, "b")!;
            Assert.Equal(200, box.Location.X, 1.0);
        }
    }
}
