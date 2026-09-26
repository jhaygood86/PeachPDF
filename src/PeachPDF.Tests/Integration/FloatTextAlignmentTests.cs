using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    public class FloatTextAlignmentTests
    {
        [Theory]
        [InlineData("text-align:justify")]
        [InlineData("direction:rtl")]
        public async Task LinesBesideARightFloatAlignWithinTheirAvailableWidth(string alignment)
        {
            var (root, _) = await LayoutAsync(Wrap(
                $"<div id='block' style='width:200px;font:10px monospace;{alignment}'>"
                + "<p style='margin:0'><span id='float' style='float:right;width:30px;height:20px;margin:0 0 4px 6px'></span>"
                + "aaa bbb ccc ddd eee fff ggg hhh iii jjj kkk lll mmm nnn ooo ppp</p></div>"));

            var block = FindById(root, "block")!;
            var floatBox = FindById(root, "float")!;
            var lines = Descendants(block).SelectMany(box => box.LineBoxes)
                .Where(line => line.Words.Count > 0).ToList();
            var besideFloat = lines.Where(line => line.Words[0].Top < floatBox.ActualBottom).ToList();

            Assert.NotEmpty(besideFloat);
            Assert.True(besideFloat.Any(line => line != lines[^1]),
                "the fixture must include a non-final line beside the float");
            Assert.All(besideFloat, line =>
            {
                var rightmost = line.Words.Max(word => word.Right);
                var availableRight = floatBox.Location.X - floatBox.ActualMarginLeft;
                Assert.True(rightmost <= availableRight + 0.1,
                    $"{alignment}: word ends at {rightmost:F1} beyond available edge {availableRight:F1}");

                // justify leaves the paragraph's last line ragged; RTL start alignment is flush right
                // on every line, including the last one. Font metrics decide how many lines there are.
                if (alignment == "direction:rtl" || line != lines[^1])
                    Assert.Equal(availableRight, rightmost, 1);
            });
        }
    }
}
