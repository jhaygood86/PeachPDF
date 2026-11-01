using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An empty inline element still generates an inline box (CSS 2.1 section 9.4.2) and a line box is as thick as its tallest inline box
    /// (section 10.8.1), in a vertical writing mode as in a horizontal one: <c>text&lt;span style="font-size:40pt"&gt;&lt;/span&gt;more</c> makes a
    /// column as thick as the same span holding a character does.
    /// </summary>
    public class VerticalEmptyInlineLineThicknessTests
    {
        private static async Task<double> ColumnThickness(string mode, string inner)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='writing-mode:{mode};font-size:10pt;line-height:1.2;height:200pt;margin:0;font-family:monospace'>{inner}</div>"));
            var box = LayoutHarness.FindById(root, "d")!;
            return box.ActualRight - box.Location.X;
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task AnEmptyInlineWithALargerFont_ThickensItsColumn(string mode)
        {
            var empty = await ColumnThickness(mode, "text<span style='font-size:40pt'></span>more");
            var withText = await ColumnThickness(mode, "text<span style='font-size:40pt'>x</span>more");
            var plain = await ColumnThickness(mode, "textmore");

            Assert.Equal(withText, empty, 0.5);
            Assert.True(empty > plain + 20, $"empty {empty}, plain {plain}");
        }

        [Fact]
        public async Task AnEmptyInlineAtTheStart_ThickensTheFirstColumn()
        {
            var empty = await ColumnThickness("vertical-rl", "<span style='font-size:40pt'></span>text");
            var plain = await ColumnThickness("vertical-rl", "text");
            Assert.True(empty > plain + 20, $"empty {empty}, plain {plain}");
        }

        [Fact]
        public async Task AnEmptyInlineAtTheEnd_ThickensTheLastColumn()
        {
            var empty = await ColumnThickness("vertical-rl", "text<span style='font-size:40pt'></span>");
            var plain = await ColumnThickness("vertical-rl", "text");
            Assert.True(empty > plain + 20, $"empty {empty}, plain {plain}");
        }

        [Fact]
        public async Task AnEmptyInlineWithTheSameFont_ChangesNothing()
        {
            var empty = await ColumnThickness("vertical-rl", "text<span></span>more");
            var plain = await ColumnThickness("vertical-rl", "textmore");
            Assert.Equal(plain, empty, 0.5);
        }
    }
}
