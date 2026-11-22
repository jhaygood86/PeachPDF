using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An inline-block whose content has a forced line break is an atomic inline with lines of its own (css-display-3 section 2.3): the break ends its line, not the line of the
    /// block it sits in. These guard against its words being flowed into that block's lines, where a <c>&lt;br&gt;</c> broke the outer line.
    /// </summary>
    public class InlineBlockForcedBreakTests
    {
        [Theory]
        [InlineData("aa<br>bb")]
        [InlineData("aa<br>bb<br>cc")]
        [InlineData("<span>aa<br>bb</span>")]
        public async Task ABreakInsideAnInlineBlock_DoesNotEndTheOuterLine(string content)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0} #c{display:inline-block;width:100pt;border:2pt solid} #x{font:12pt/12pt sans-serif}</style>"
                + $"<div id='x'>before <div id='c'>{content}</div> after</div>"));

            var outer = LayoutHarness.FindById(root, "x")!;
            var inner = LayoutHarness.FindById(root, "c")!;

            Assert.Single(outer.LineBoxes);
            Assert.True(inner.LineBoxes.Count >= 2, $"the inline-block holds its own lines, had {inner.LineBoxes.Count}");
        }

        [Fact]
        public async Task ABreakInsideAnInlineBlock_KeepsTheBlocksBorderAroundAllItsLines()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0} #c{display:inline-block;width:100pt;border:2pt solid;font:12pt/12pt sans-serif}</style><div><div id='c'>aa<br>bb<br>cc</div></div>"));

            var inner = LayoutHarness.FindById(root, "c")!;

            Assert.True(inner.ActualBottom - inner.Location.Y >= 36, "the box is at least three lines tall");
            Assert.Equal(inner.ClientLeft - inner.ActualBorderLeftWidth, inner.Location.X, 0.5);
        }
    }
}
