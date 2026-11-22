using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An absolute box among the inline content of a vertical box sits at its static position: along the column at the place it was passed at, across it at the
    /// block-start edge of that column; one that was block-level starts the next column. The positions are what Chrome gives (measured on the css-position
    /// static-position tests, Ahem at 16px: <c>XXX XX[box]XXXXX</c> in a 400px column has the box's top edge 80px down, as the words XXX XX end).
    /// </summary>
    public class VerticalAbsoluteStaticPositionTests
    {
        private static CssRect Word(CssBox root, string text) =>
            LayoutHarness.Descendants(root).SelectMany(b => b.Words).First(w => w.Text?.Trim() == text);

        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string style, string body) =>
            LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<style>body{{margin:0}} #c{{{style};height:300pt;line-height:20pt}} #a{{position:absolute;width:4pt;height:4pt}}</style><div id='c'>{body}</div>"));

        [Theory]
        [InlineData("writing-mode: vertical-lr")]
        [InlineData("writing-mode: vertical-rl")]
        public async Task AnInlineLevelBox_HasItsTopEdgeWhereTheWordBeforeItEnds_AndSitsOnThatColumn(string style)
        {
            var (root, _) = await LayoutAsync(style, "<span>aaa</span><span id='a'></span><span>bbb</span>");
            var a = LayoutHarness.FindById(root, "a")!;
            var aaa = Word(root, "aaa");

            Assert.Equal(aaa.Bottom, a.Location.Y, 0.5);
            Assert.Equal(style.EndsWith("rl") ? aaa.Right : aaa.Left, style.EndsWith("rl") ? a.ActualRight : a.Location.X, 1.5);
        }

        [Fact]
        public async Task InADirectionRtlColumn_TheLeftToRightWordsAreTopToBottom_FlushToTheBottomEdge_AndTheBoxAfterTheRunIsAboveIt()
        {
            // Chrome (vertical-lr, direction: rtl, 300px): AAA [188,236], BBB [252,300]; and `aaa[box]bbb` has the box at aaa's bottom edge.
            var (root, _) = await LayoutAsync("writing-mode: vertical-lr; direction: rtl", "<span>aaa</span> <span>bbb</span><span id='a'></span>");
            var c = LayoutHarness.FindById(root, "c")!;
            var aaa = Word(root, "aaa");
            var bbb = Word(root, "bbb");

            Assert.True(aaa.Top < bbb.Top, $"aaa is at {aaa.Top}, bbb at {bbb.Top}");
            // Flush to the bottom edge, up to the word spacing the natural placement leaves after a run (Chrome is exactly flush).
            Assert.InRange(bbb.Bottom, c.ClientBottom - 5, c.ClientBottom);
            // The place after the run is a run of its own on the far side of it in the direction the column reads (up), so the box ends at the top of aaa.
            Assert.Equal(aaa.Top, LayoutHarness.FindById(root, "a")!.ActualBottom, 0.5);
        }

        [Fact]
        public async Task ABlockLevelBox_StartsTheNextColumn_AtTheStartOfTheInlineAxis()
        {
            var (root, _) = await LayoutAsync("writing-mode: vertical-lr", "aaa<div id='a'></div><br>bbb");
            var c = LayoutHarness.FindById(root, "c")!;
            var a = LayoutHarness.FindById(root, "a")!;

            Assert.Equal(c.ClientTop, a.Location.Y, 0.5);
            Assert.Equal(Word(root, "bbb").Left, a.Location.X, 1.5);
        }

        [Fact]
        public async Task ABlockLevelBox_InADirectionRtlColumn_HasItsBottomEdgeOnTheBottomOfTheContent()
        {
            var (root, _) = await LayoutAsync("writing-mode: vertical-lr; direction: rtl", "aaa<div id='a'></div><br>bbb");

            Assert.Equal(LayoutHarness.FindById(root, "c")!.ClientBottom, LayoutHarness.FindById(root, "a")!.ActualBottom, 0.5);
        }
    }
}
