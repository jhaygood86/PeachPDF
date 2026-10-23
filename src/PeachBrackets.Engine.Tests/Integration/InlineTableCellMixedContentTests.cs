namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A cell of an inline-table holding loose text beside a block child drew only the block: the walk that wraps each run
    /// of inline content in an anonymous block (CSS 2.1 section 9.2.1.1) opened an inline-block but never an inline-table, so
    /// the cell's text boxes were not measured. A block-level table was never affected.
    /// </summary>
    public class InlineTableCellMixedContentTests
    {
        private const string Head = "<!DOCTYPE html><html><head><meta charset=utf-8><style>@page{size:A4;margin:20px}body{margin:0;font:14px/1.3 Arial}</style></head><body>";

        private static async Task AssertEveryWordDrawnOnceAsync(string html)
        {
            var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);
            var (lost, doubled) = PaintedWords.Diff(html, visible);

            Assert.True(lost.Count == 0, $"lost: {string.Join(", ", lost)}");
            Assert.True(doubled.Count == 0, $"doubled: {string.Join(", ", doubled)}");
        }

        [Theory]
        [InlineData("w1_1<div>w1_2</div>w1_3")]
        [InlineData("w2_1<p>w2_2</p>w2_3")]
        [InlineData("<div>w3_1</div>w3_2")]
        [InlineData("w4_1<div>w4_2</div>")]
        [InlineData("w5_1<table><tr><td>w5_2</td></tr></table>w5_3")]
        [InlineData("w6_1<div>w6_2</div>w6_3<div>w6_4</div>w6_5")]
        public async Task ExplicitInlineTableCell_DrawsTheLooseTextAroundABlock(string cell)
        {
            await AssertEveryWordDrawnOnceAsync(
                Head + "A[<span style='display:inline-table'><span style='display:table-row'><span style='display:table-cell'>"
                + cell + "</span></span></span>]B</body></html>");
        }

        [Theory]
        [InlineData("w1_1<div>w1_2</div>w1_3")]
        [InlineData("w2_1<p>w2_2</p>w2_3")]
        public async Task InlineTableElement_DrawsTheLooseTextAroundABlock(string cell)
        {
            await AssertEveryWordDrawnOnceAsync(
                Head + "A[<table style='display:inline-table'><tr><td>" + cell + "</td></tr></table>]B</body></html>");
        }

        [Fact]
        public async Task AnInlineBlockAroundTheInlineTable_StillDrawsEverything()
        {
            await AssertEveryWordDrawnOnceAsync(
                Head + "<span style='display:inline-block'>w1_1<div>w1_2</div>w1_3"
                + "<span style='display:inline-table'><span style='display:table-row'><span style='display:table-cell'>w1_4<div>w1_5</div>w1_6</span></span></span></span></body></html>");
        }
    }
}
