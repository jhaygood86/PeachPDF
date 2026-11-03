using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An absolutely positioned box whose offsets on an axis are both <c>auto</c> sits at its static position on that axis, where it would have been
    /// in flow (CSS 2.1 sections 10.3.7 and 10.6.4, CSS Positioned Layout 3 section 3.5): below the previous in-flow sibling and at the parent's content
    /// edge, not at the corner of its containing block. This covers a box in block flow and one among inline content, which sits at its place on the line.
    /// </summary>
    public class AbsoluteStaticPositionTests
    {
        private const string Style = "<style>body{margin:0} #c{position:relative;width:300pt;margin:0;padding:10pt 20pt} #p{margin:0;height:50pt} #a{position:absolute;width:20pt;height:10pt}</style>";

        private static async Task<(CssBox C, CssBox P, CssBox A)> Layout(string container)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Style + container));
            return (LayoutHarness.FindById(root, "c")!, LayoutHarness.FindById(root, "p")!, LayoutHarness.FindById(root, "a")!);
        }

        [Fact]
        public async Task AfterABlockSibling_SitsBelowItAtTheParentsContentEdge()
        {
            var (c, p, a) = await Layout("<div id='c'><div id='p'>para</div><div id='a'>x</div></div>");

            Assert.Equal(p.ActualBottom, a.Location.Y, 0.5);
            Assert.Equal(c.ClientLeft, a.Location.X, 0.5);
        }

        [Fact]
        public async Task AsTheFirstChild_SitsAtTheParentsContentTopLeft_NotItsPaddingCorner()
        {
            var (c, _, a) = await Layout("<div id='c'><div id='a'>x</div><div id='p'>para</div></div>");

            Assert.Equal(c.ClientTop, a.Location.Y, 0.5);
            Assert.Equal(c.ClientLeft, a.Location.X, 0.5);
            Assert.True(a.Location.X > c.Location.X + 15, "the 20pt padding is inside the static position");
        }

        [Fact]
        public async Task TheAbsoluteBoxsOwnMarginAddsToItsStaticPosition()
        {
            var (_, p, a) = await Layout("<div id='c'><div id='p'>para</div><div id='a' style='margin:7pt 0 0 5pt'>x</div></div>");

            Assert.Equal(p.ActualBottom + 7, a.Location.Y, 0.5);
        }

        [Fact]
        public async Task AnAxisWithAnOffsetSet_KeepsThatOffset_AndTheOtherStaysStatic()
        {
            var (c, p, a) = await Layout("<div id='c'><div id='p'>para</div><div id='a' style='left:30pt'>x</div></div>");

            // left is set: measured from the padding edge of the containing block; top is still static.
            Assert.Equal(c.Location.X + 30, a.Location.X, 0.5);
            Assert.Equal(p.ActualBottom, a.Location.Y, 0.5);
        }

        [Fact]
        public async Task BothOffsetsSet_AreUnchanged()
        {
            var (c, _, a) = await Layout("<div id='c'><div id='p'>para</div><div id='a' style='left:5pt;top:3pt'>x</div></div>");

            Assert.Equal(c.Location.X + 5, a.Location.X, 0.5);
            Assert.Equal(c.Location.Y + 3, a.Location.Y, 0.5);
        }

        private static (double Left, double Right, double Top) WordOf(CssBox root, string text)
        {
            var word = LayoutHarness.Descendants(root).SelectMany(b => b.Words).First(w => w.Text?.Trim() == text);
            return (word.Left, word.Right, LayoutHarness.LineTopOf(root, word));
        }

        [Fact]
        public async Task AmongInlineContent_SitsRightAfterThePrecedingWordOnItsLine()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0} #c{margin-left:100pt;line-height:20pt} #a{position:absolute;width:4pt;height:4pt}</style>"
                + "<div id='c'>aaa<span id='a'></span>bbb</div>"));
            var a = LayoutHarness.FindById(root, "a")!;
            var aaa = WordOf(root, "aaa");

            Assert.Equal(aaa.Right, a.Location.X, 0.5);
            Assert.Equal(aaa.Top, a.Location.Y, 0.5);
            Assert.True(a.Location.X > LayoutHarness.FindById(root, "c")!.ClientLeft + 5, "not at the containing block's corner");
        }

        [Fact]
        public async Task AmongInlineContent_FollowsTheLineWhenTextAlignMovesIt()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0} #c{width:300pt;text-align:right;line-height:20pt} #a{position:absolute;width:4pt;height:4pt}</style>"
                + "<div id='c'>aaa<span id='a'></span>bbb</div>"));
            var a = LayoutHarness.FindById(root, "a")!;

            Assert.Equal(WordOf(root, "aaa").Right, a.Location.X, 0.5);
            Assert.True(a.Location.X > 200, $"a right-aligned line puts the place near the right edge, was {a.Location.X}");
        }

        [Fact]
        public async Task AmongInlineContent_OnALaterLine_SitsOnThatLine()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0} #c{width:60pt;line-height:20pt;font-size:12pt} #a{position:absolute;width:4pt;height:4pt}</style>"
                + "<div id='c'>aaaaa bbbbb ccccc<span id='a'></span> ddddd</div>"));
            var a = LayoutHarness.FindById(root, "a")!;
            var ccccc = WordOf(root, "ccccc");

            Assert.Equal(ccccc.Right, a.Location.X, 0.5);
            Assert.Equal(ccccc.Top, a.Location.Y, 0.5);
            Assert.True(ccccc.Top > LayoutHarness.FindById(root, "c")!.ClientTop + 15, "the word is below the first line");
        }

        [Fact]
        public async Task AmongInlineContent_AnOffsetOnOneAxisKeepsThatOffset()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0} #c{position:relative;margin-left:100pt;line-height:20pt} #a{position:absolute;top:30pt;width:4pt;height:4pt}</style>"
                + "<div id='c'>aaa<span id='a'></span>bbb</div>"));
            var c = LayoutHarness.FindById(root, "c")!;
            var a = LayoutHarness.FindById(root, "a")!;

            Assert.Equal(WordOf(root, "aaa").Right, a.Location.X, 0.5);
            Assert.Equal(c.Location.Y + 30, a.Location.Y, 0.5);
        }

        [Fact]
        public async Task InADirectionRtlParent_TheRightMarginEdgeSitsAtTheParentsContentRightEdge()
        {
            var (c, p, a) = await Layout("<div id='c' dir='rtl'><div id='p'>para</div><div id='a' style='margin-right:7pt'>x</div></div>");

            Assert.Equal(c.ClientRight, a.ActualRight + 7, 0.5);
            Assert.Equal(p.ActualBottom, a.Location.Y, 0.5);
        }

        [Fact]
        public async Task InADirectionRtlParent_AnOffsetOnTheAcrossAxisIsKept()
        {
            var (c, _, a) = await Layout("<div id='c' dir='rtl'><div id='p'>para</div><div id='a' style='left:30pt'>x</div></div>");

            Assert.Equal(c.Location.X + 30, a.Location.X, 0.5);
        }

        [Fact]
        public async Task AmongInlineContentInADirectionRtlParent_TheRightEdgeSitsAtThePlaceOnTheLine()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>body{margin:0} #c{width:300pt;line-height:20pt} #a{position:absolute;width:4pt;height:4pt}</style>"
                + "<div id='c' dir='rtl'><span>aaa</span><span id='a'></span><span>bbb</span></div>"));
            var a = LayoutHarness.FindById(root, "a")!;
            var aaa = WordOf(root, "aaa");

            // Latin words form a left-to-right run inside the rtl line; the place is after "aaa" and the box's right edge is there.
            Assert.Equal(aaa.Right, a.ActualRight, 0.5);
        }
    }
}
