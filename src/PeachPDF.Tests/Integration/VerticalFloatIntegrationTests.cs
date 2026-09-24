using PeachPDF.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>float</c> and <c>clear</c> in a vertical writing mode (issue #796). <c>left</c>/<c>right</c> are
    /// line-relative: they resolve against the containing block's writing mode, and in <c>vertical-rl</c> and
    /// <c>vertical-lr</c> line-left is the physical <b>top</b> and line-right the physical <b>bottom</b>, whatever the
    /// <c>direction</c> (CSS Writing Modes 4 sections 6.4 and 7.5). A float sits at the current block-axis position of
    /// its container and slides along the inline axis; CSS 2.1 section 9.5.1 applies with the axes swapped.
    /// </summary>
    public class VerticalFloatIntegrationTests
    {
        // 300pt wide, 200pt tall content box: the harness puts the content edge at 20pt.
        private const string Wrapper = "width: 300pt; height: 200pt";

        private static async Task<(CssBox Wrapper, Dictionary<string, CssBox> Boxes)> LayoutAsync(string html, params string[] ids)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<style>html { font-family: 'Vertical Float Fixture' }</style>" + LayoutHarness.Wrap(html),
                configureAdapter: RegisterFont);

            var wrapper = LayoutHarness.FindById(root, "wrapper")!;
            var boxes = ids.ToDictionary(id => id, id => LayoutHarness.FindById(root, id)!);
            return (wrapper, boxes);
        }

        private static Task RegisterFont(PdfSharpAdapter adapter) =>
            BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, "Vertical Float Fixture");

        [Theory]
        [InlineData("vertical-rl", "ltr", "left", true)]
        [InlineData("vertical-rl", "ltr", "right", false)]
        [InlineData("vertical-rl", "rtl", "left", true)]
        [InlineData("vertical-rl", "rtl", "right", false)]
        [InlineData("vertical-lr", "ltr", "left", true)]
        [InlineData("vertical-lr", "ltr", "right", false)]
        [InlineData("vertical-lr", "rtl", "left", true)]
        [InlineData("vertical-lr", "rtl", "right", false)]
        public async Task FloatLeftIsTheTopAndFloatRightTheBottom_WhateverTheDirection(
            string mode, string direction, string side, bool atTop)
        {
            var (wrapper, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:{mode}; direction:{direction}; {Wrapper}'>" +
                $"<div id='f' style='float:{side}; width:30pt; height:60pt'></div></div>", "f");
            var f = b["f"];

            // Block-start edge of the container: the right for vertical-rl, the left for vertical-lr.
            var blockStartEdge = mode == "vertical-rl" ? f.ActualRight : f.Location.X;
            Assert.Equal(mode == "vertical-rl" ? wrapper.ClientRight : wrapper.ClientLeft, blockStartEdge, 2);
            Assert.Equal(30, f.ActualRight - f.Location.X, 2);
            Assert.Equal(60, f.ActualBottom - f.Location.Y, 2);

            if (atTop) Assert.Equal(wrapper.ClientTop, f.Location.Y, 2);
            else Assert.Equal(wrapper.ClientBottom, f.ActualBottom, 2);
        }

        [Fact]
        public async Task Float_TakesNoBlockAxisSpace_SoTheFollowingBlockStartsWhereItWouldWithoutIt()
        {
            var withFloat = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><div id='f' style='float:left; width:30pt; height:60pt'></div>" +
                "<div id='next' style='width:40pt; height:50pt'></div></div>", "next");
            var without = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><div id='next' style='width:40pt; height:50pt'></div></div>", "next");

            Assert.Equal(without.Boxes["next"].Location.X, withFloat.Boxes["next"].Location.X, 2);
            Assert.Equal(without.Boxes["next"].Location.Y, withFloat.Boxes["next"].Location.Y, 2);
        }

        [Fact]
        public async Task Float_SitsAtTheBlockAxisPositionItsSourceOrderReaches()
        {
            var (wrapper, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><div id='before' style='width:40pt; height:50pt'></div>" +
                "<div id='f' style='float:left; width:30pt; height:60pt'></div></div>", "f", "before");

            // Beside the block that precedes it in the flow, not at the container's block-start edge.
            Assert.Equal(b["before"].Location.X, b["f"].ActualRight, 2);
            Assert.Equal(wrapper.ClientTop, b["f"].Location.Y, 2);
        }

        [Fact]
        public async Task FloatsOnOppositeSides_ShareTheBlockAxisPosition()
        {
            var (wrapper, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><div id='top' style='float:left; width:30pt; height:60pt'></div>" +
                "<div id='bottom' style='float:right; width:30pt; height:60pt'></div></div>", "top", "bottom");

            Assert.Equal(b["top"].ActualRight, b["bottom"].ActualRight, 2);
            Assert.Equal(wrapper.ClientTop, b["top"].Location.Y, 2);
            Assert.Equal(wrapper.ClientBottom, b["bottom"].ActualBottom, 2);
        }

        [Fact]
        public async Task SameSideFloats_StackAlongTheInlineAxisWhileTheyFit_ThenDropTowardBlockEnd()
        {
            // Three 90pt floats in a 200pt-tall container: the second fits beside the first (180pt), the third
            // does not (270pt) and drops to the block-end edge of the first to end - the flow of CSS 2.1 9.5.1
            // rules 2 and 5 with the axes swapped.
            var (wrapper, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'>" +
                "<div id='a' style='float:left; width:30pt; height:90pt'></div>" +
                "<div id='b' style='float:left; width:30pt; height:90pt'></div>" +
                "<div id='c' style='float:left; width:30pt; height:90pt'></div></div>", "a", "b", "c");

            Assert.Equal(wrapper.ClientTop, b["a"].Location.Y, 2);
            Assert.Equal(b["a"].ActualBottom, b["b"].Location.Y, 2);
            Assert.Equal(b["a"].ActualRight, b["b"].ActualRight, 2);

            // Dropped: back at the top, one float's block size (30pt) toward block-end (leftward in vertical-rl).
            Assert.Equal(wrapper.ClientTop, b["c"].Location.Y, 2);
            Assert.Equal(b["a"].Location.X - 30, b["c"].Location.X, 2);
        }

        [Fact]
        public async Task AFloatWithClear_StartsBeyondTheBlockEndEdgeOfTheFloatItClears()
        {
            var (wrapper, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><div id='a' style='float:left; width:30pt; height:60pt'></div>" +
                "<div id='b' style='float:left; clear:left; width:30pt; height:60pt'></div></div>", "a", "b");

            Assert.Equal(b["a"].Location.X, b["b"].ActualRight, 2);
            Assert.Equal(wrapper.ClientTop, b["b"].Location.Y, 2);
        }

        [Fact]
        public async Task ClearLeft_MovesABlockBeyondTheBlockEndEdgeOfALeftFloat()
        {
            var (_, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><div id='f' style='float:left; width:30pt; height:60pt'></div>" +
                "<div id='cleared' style='clear:left; width:40pt; height:50pt'></div>" +
                "<div id='free' style='width:40pt; height:50pt'></div></div>", "f", "cleared", "free");

            // The cleared block's block-start edge is the float's block-end edge; the next block follows it.
            Assert.Equal(b["f"].Location.X, b["cleared"].ActualRight, 2);
            Assert.Equal(b["cleared"].Location.X, b["free"].ActualRight, 2);
        }

        [Fact]
        public async Task ClearRight_DoesNotClearALeftFloat()
        {
            var (wrapper, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><div id='f' style='float:left; width:30pt; height:60pt'></div>" +
                "<div id='other' style='clear:right; width:40pt; height:50pt'></div></div>", "other");

            Assert.Equal(wrapper.ClientRight, b["other"].ActualRight, 2);
        }

        [Fact]
        public async Task ClearBoth_ClearsFloatsOnEitherSide()
        {
            var (_, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><div id='f' style='float:right; width:30pt; height:60pt'></div>" +
                "<div id='cleared' style='clear:both; width:40pt; height:50pt'></div></div>", "f", "cleared");

            Assert.Equal(b["f"].Location.X, b["cleared"].ActualRight, 2);
        }

        [Theory]
        [InlineData("vertical-rl", "ltr", "inline-start", true)]
        [InlineData("vertical-rl", "ltr", "inline-end", false)]
        [InlineData("vertical-rl", "rtl", "inline-start", false)]
        [InlineData("vertical-lr", "rtl", "inline-end", true)]
        public async Task InlineStartAndInlineEnd_FollowTheContainersDirection(string mode, string direction, string keyword, bool atTop)
        {
            var (wrapper, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:{mode}; direction:{direction}; {Wrapper}'>" +
                $"<div id='f' style='float:{keyword}; width:30pt; height:60pt'></div></div>", "f");

            if (atTop) Assert.Equal(wrapper.ClientTop, b["f"].Location.Y, 2);
            else Assert.Equal(wrapper.ClientBottom, b["f"].ActualBottom, 2);
        }

        [Fact]
        public async Task FloatRight_InAnAutoHeightContainer_ReachesTheFinalBottomEdge()
        {
            // The bottom edge is not known until the container's height is: the float is moved there once it is.
            var (wrapper, b) = await LayoutAsync(
                "<div id='wrapper' style='writing-mode:vertical-rl; width:300pt'>" +
                "<div id='f' style='float:right; width:30pt; height:60pt'></div>" +
                "<p id='p' style='width:100pt; height:150pt; margin:0'>Alpha Beta</p></div>", "f");

            Assert.Equal(wrapper.ClientBottom, b["f"].ActualBottom, 2);
        }

        // ─── Text beside a float ───

        private static List<CssRect> WordsOf(CssBox box) =>
            box.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();

        private const string Alpha =
            "Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa Lambda Mu Nu Xi Omicron Pi Rho Sigma Tau Upsilon";

        [Theory]
        [InlineData("vertical-rl", "ltr")]
        [InlineData("vertical-rl", "rtl")]
        [InlineData("vertical-lr", "ltr")]
        public async Task TextBesideATopFloat_StartsBelowIt(string mode, string direction)
        {
            var (_, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:{mode}; direction:{direction}; {Wrapper}'>" +
                "<div id='f' style='float:left; width:50pt; height:60pt'></div>" +
                $"<p id='p' style='width:200pt; height:200pt; margin:0'>{Alpha}</p></div>", "f", "p");
            var f = b["f"];

            var beside = WordsOf(b["p"]).Where(w => w.Left < f.ActualRight + f.ActualMarginRight && w.Right > f.Location.X - f.ActualMarginLeft).ToList();

            Assert.NotEmpty(beside);
            Assert.All(beside, w => Assert.True(w.Top >= f.ActualBottom + f.ActualMarginBottom - 0.5,
                $"'{w.Text}' at Y {w.Top} overlaps the float that ends at {f.ActualBottom}"));

            // Columns beyond the float's block-axis range are not beside it at all, so they keep the whole
            // column: the text does not all sit under the float.
            Assert.Contains(WordsOf(b["p"]), w => !(w.Left < f.ActualRight + f.ActualMarginRight && w.Right > f.Location.X - f.ActualMarginLeft));
        }

        [Theory]
        [InlineData("vertical-rl", "ltr")]
        [InlineData("vertical-rl", "rtl")]
        [InlineData("vertical-lr", "ltr")]
        public async Task TextBesideABottomFloat_EndsAboveIt(string mode, string direction)
        {
            var (_, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:{mode}; direction:{direction}; {Wrapper}'>" +
                "<div id='f' style='float:right; width:50pt; height:60pt'></div>" +
                $"<p id='p' style='width:200pt; height:200pt; margin:0'>{Alpha}</p></div>", "f", "p");
            var f = b["f"];

            var beside = WordsOf(b["p"]).Where(w => w.Left < f.ActualRight + f.ActualMarginRight && w.Right > f.Location.X - f.ActualMarginLeft).ToList();

            Assert.NotEmpty(beside);
            Assert.All(beside, w => Assert.True(w.Top + w.Height <= f.Location.Y - f.ActualMarginTop + 0.5,
                $"'{w.Text}' ending at {w.Top + w.Height} overlaps the float that starts at {f.Location.Y}"));
        }

        [Fact]
        public async Task FloatAmongBareText_WrapsTheColumnsBesideItBelowIt()
        {
            // A box holding only inline content and a float takes the inline path, not the block one: the float
            // is placed at the block-axis position of the column the word stream has reached.
            var (_, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'>" +
                $"<span id='f' style='float:left; width:50pt; height:60pt'></span>{Alpha}</div>", "f", "wrapper");
            var f = b["f"];

            Assert.Equal(b["wrapper"].ClientTop, f.Location.Y, 2);

            var beside = WordsOf(b["wrapper"]).Where(w => w.Left < f.ActualRight + f.ActualMarginRight && w.Right > f.Location.X - f.ActualMarginLeft).ToList();
            Assert.NotEmpty(beside);
            Assert.All(beside, w => Assert.True(w.Top >= f.ActualBottom - 0.5,
                $"'{w.Text}' at Y {w.Top} overlaps the float that ends at {f.ActualBottom}"));
        }

        [Fact]
        public async Task FloatReachedMidText_SitsBesideTheNextColumnsNotTheOnesAlreadyPlaced()
        {
            var (_, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'>{Alpha} " +
                $"<span id='f' style='float:left; width:30pt; height:60pt'></span> {Alpha}</div>", "f", "wrapper");
            var f = b["f"];

            // The words before it in source order are on columns at the block-start side of the float.
            var earlier = WordsOf(b["wrapper"]).Take(5).ToList();
            Assert.All(earlier, w => Assert.True(w.Left >= f.ActualRight - 0.5 || w.Right <= f.Location.X + 0.5 || w.Top >= f.ActualBottom - 0.5 || w.Top + w.Height <= f.Location.Y + 0.5,
                $"'{w.Text}' at ({w.Left}, {w.Top}) overlaps the float"));

            // ...and the columns after it that reach its block-axis range start below it.
            var beside = WordsOf(b["wrapper"]).Where(w => w.Left < f.ActualRight && w.Right > f.Location.X).ToList();
            Assert.All(beside, w => Assert.True(w.Top >= f.ActualBottom - 0.5, $"'{w.Text}' at Y {w.Top} overlaps the float"));
        }

        [Fact]
        public async Task FloatAfterTheLastWord_IsStillPlaced()
        {
            var (wrapper, b) = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'>Alpha Beta <span id='f' style='float:right; width:30pt; height:60pt'></span></div>", "f");

            Assert.Equal(wrapper.ClientBottom, b["f"].ActualBottom, 2);
        }

        [Fact]
        public async Task TextBeforeAFloatInSourceOrder_IsUnaffectedByIt()
        {
            var withFloat = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><p id='p' style='width:100pt; height:200pt; margin:0'>{Alpha}</p>" +
                "<div style='float:left; width:30pt; height:60pt'></div></div>", "p");
            var without = await LayoutAsync(
                $"<div id='wrapper' style='writing-mode:vertical-rl; {Wrapper}'><p id='p' style='width:100pt; height:200pt; margin:0'>{Alpha}</p></div>", "p");

            var a = WordsOf(withFloat.Boxes["p"]);
            var c = WordsOf(without.Boxes["p"]);

            Assert.Equal(c.Count, a.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.Equal(c[i].Top, a[i].Top, 1);
                Assert.Equal(c[i].Left, a[i].Left, 1);
            }
        }
    }
}
