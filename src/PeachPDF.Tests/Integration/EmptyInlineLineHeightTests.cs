using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An empty inline element still generates an inline box on its line (CSS 2.1 §9.4.2), and the line
    /// box is tall enough for every inline box on it (§10.8.1), so an empty inline with a larger font grows
    /// its line just as an inline holding text does.
    /// </summary>
    /// <remarks>
    /// Every fixture uses a numeric <c>line-height</c> of 1.2, so a 40pt inline's own line box is 48pt and,
    /// with fonts that scale in proportion, it outreaches the 10pt text's on both sides of the baseline.
    /// </remarks>
    public class EmptyInlineLineHeightTests
    {
        private const double TallLine = 48;
        private const double ShortLine = 12;

        // The bottom of the first page's content: an 842pt page with 20pt margins.
        private const double PageContentBottom = 822;

        // Fits a 120pt line alone, but not after a 40pt "aaa ".
        private const string LongWord = "bbbbbbbbbbbbbbbbbbbb";

        [Fact]
        public async Task EmptyPositionedInline_WithALargerFont_GrowsItsLine()
        {
            // The reported shape: a badge wrapper whose only content is the absolutely positioned badge.
            var (root, _) = await LayoutAsync(
                "<p id='p'>text<span id='s' style='position: relative; font-size: 40pt'>" +
                "<span id='a' style='position: absolute; left: 0; top: 0; width: 6pt; height: 6pt'></span></span>more</p>");

            var p = Find(root, "p");
            var line = Assert.Single(p.LineBoxes);

            Assert.Equal(TallLine, p.ActualBottom - p.Location.Y, 3);
            Assert.Equal(TallLine, line.BaselineExtent!.Value.Height, 3);

            // The text sits on the line's baseline, lower down than a 10pt line would put it.
            foreach (var word in line.Words)
                Assert.Equal(line.BaselineY!.Value, word.Top + word.OwnerBox.ActualFont.Ascent, 3);

            Assert.True(line.BaselineY!.Value - p.Location.Y > ShortLine);

            // The badge's zero-width containing block is the 40pt content area, centred in the line by its
            // half-leading (§10.8.1), rather than one font height hanging above a 10pt line.
            var span = Find(root, "s");
            var badge = Find(root, "a");
            Assert.Equal(line.BaselineY!.Value - span.ActualFont.Ascent, badge.Location.Y, 3);
            Assert.Equal(p.Location.Y + (TallLine - span.ActualFont.Height) / 2, badge.Location.Y, 3);
        }

        [Fact]
        public async Task EmptyInline_WithALargerFont_GrowsItsLine()
        {
            var (root, _) = await LayoutAsync(
                "<p id='p'>text<span style='font-size: 40pt'></span>more</p>");

            var p = Find(root, "p");

            Assert.Equal(TallLine, p.ActualBottom - p.Location.Y, 3);
        }

        [Fact]
        public async Task EmptyInline_BeforeTheLinesFirstWord_GrowsItsLine()
        {
            // Met while the line holds no word yet, so it is recorded and folded in by the first word.
            var (root, _) = await LayoutAsync(
                "<p id='p'><span style='font-size: 40pt'></span>text</p>");

            var p = Find(root, "p");

            Assert.Equal(TallLine, p.ActualBottom - p.Location.Y, 3);
        }

        [Fact]
        public async Task EmptyInline_DoesNotGrowTheLinesAfterTheWordThatTookIt()
        {
            var (root, _) = await LayoutAsync(
                $"<p id='p' style='width: 120pt'><span style='font-size: 40pt'></span>aaa {LongWord}</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(TallLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(ShortLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task EmptyInline_OnALineWithNoContent_LeavesItZeroHeight()
        {
            // §9.4.2: a line box holding no text and no in-flow content is treated as zero-height.
            var (root, _) = await LayoutAsync(
                "<p id='p'><span style='font-size: 40pt'></span></p>");

            var p = Find(root, "p");

            Assert.Equal(0, p.ActualBottom - p.Location.Y, 3);
        }

        [Fact]
        public async Task EmptyInline_GrowsOnlyTheLineItSitsOn()
        {
            var (root, _) = await LayoutAsync(
                "<p id='p'>first<span style='font-size: 40pt'></span><br>second</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(TallLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(ShortLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
            Assert.Equal(TallLine + ShortLine, p.ActualBottom - p.Location.Y, 3);
        }

        [Fact]
        public async Task EmptyInline_AfterAWrapOpportunity_GoesToTheNextLineWithTheWordAfterIt()
        {
            // The line breaks at the space, and the empty inline comes after that break opportunity.
            var (root, _) = await LayoutAsync(
                $"<p id='p' style='width: 120pt'>aaa <span style='font-size: 40pt'></span>{LongWord}</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(ShortLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(TallLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task EmptyInline_BeforeAWrapOpportunity_StaysOnTheLineOfTheWordBeforeIt()
        {
            // The break opportunity is the space after the empty inline, so it stays with "aaa".
            var (root, _) = await LayoutAsync(
                $"<p id='p' style='width: 120pt'>aaa<span style='font-size: 40pt'></span> {LongWord}</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(TallLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(ShortLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task InlineHoldingOnlyAnAtomicInline_IsNotEmpty()
        {
            // The inline-flex places no word, but it is content on the first line, so the span around it is
            // not an empty inline to be carried to the line of the next word.
            var (root, _) = await LayoutAsync(
                "<p id='p' style='width: 120pt'>aaa <span style='font-size: 40pt'>" +
                $"<span style='display: inline-flex; width: 10pt; height: 5pt'></span></span> {LongWord}</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(ShortLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Theory]
        [InlineData("text<span style='font-size: 40pt'></span>")]
        [InlineData("text <span style='font-size: 40pt'></span>")]
        public async Task LineGrownByAnEmptyInline_PastThePageEnd_MovesToTheNextPage(string content)
        {
            // Placed alone, "text" fits at the foot of the first page. The empty inline then grows its line
            // past the page end, with no later word to ask whether the line still fits, so the line moves to
            // the next page, where the same line with a word after the empty inline goes.
            var (root, _) = await LayoutAsync(
                $"<div style='height: 770pt'></div><p id='p'>{content}</p>");

            var line = Find(root, "p").LineBoxes[^1];
            var (controlRoot, _) = await LayoutAsync(
                "<div style='height: 770pt'></div><p id='p'>text<span style='font-size: 40pt'></span>x</p>");
            var controlLine = Find(controlRoot, "p").LineBoxes[^1];

            Assert.Equal(TallLine, line.BaselineExtent!.Value.Height, 3);
            Assert.Equal(controlLine.Words[0].Top, line.Words[0].Top, 3);
        }

        [Theory]
        [InlineData("<span style='font-size: 1000pt'></span>text")]
        [InlineData("text<span style='font-size: 1000pt'></span>")]
        public async Task LinesAfterALineTooDeepForAnyPage_FlowOnFromItsBottom(string head)
        {
            // The deep line overflows the first page, so the flow after it has reached a later page, and every
            // line after it is drawn on the page it falls on (#435's step-over, asked of the line: "text" sits
            // at its baseline, wholly on the second page, so no word on it straddles anything).
            var lines = string.Join("<br>", Enumerable.Range(1, 60).Select(i => $"l{i}"));
            var (root, container) = await LayoutAsync($"<p id='p' style='orphans: 1; widows: 1'>{head}<br>{lines}</p>");

            var p = Find(root, "p");
            var deep = p.LineBoxes[0];

            Assert.Equal(deep.FlowTop!.Value + deep.BaselineExtent!.Value.Height, p.LineBoxes[1].FlowTop!.Value, 3);
            Assert.All(container.FragmentTree!.Fragmentainers, page =>
                Assert.All(Flatten(page.Root).SelectMany(f => f.Words).Where(w => w.Word.Text?.StartsWith('l') == true),
                    w => Assert.Equal(page.SlotIndex, container.SlotStartingAt(w.Word.Line!.FlowTop!.Value))));
        }

        [Fact]
        public async Task EmptyInline_TakenByAnAtomicInlineBeforeAPageBreak_IsNotPlacedAgainAfterIt()
        {
            // The 40pt span comes after a space, so it is held, and the inline-flex takes it onto the first
            // line. Neither places a word, so the span sits at the ordinal of "bbbb…", whose line starts the
            // next page.
            var (root, _) = await LayoutAsync(
                "<div style='height: 739pt'></div><p id='p' style='width: 120pt; orphans: 1; widows: 1'>aaa " +
                $"<span style='font-size: 40pt'></span><span style='display: inline-flex'>A</span> <b>{LongWord}</b></p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(TallLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(PageContentBottom, p.LineBoxes[1].FlowTop!.Value, 3);
            Assert.Equal(ShortLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task TopAlignedTextOnALineGrownByAnEmptyInline_StaysWhereItFits()
        {
            // The text stays at the line's top, still inside the page, so nothing drawn on the line crosses it.
            var (root, _) = await LayoutAsync(
                "<div style='height: 770pt'></div><p id='p'><span style='vertical-align: top'>text</span>" +
                "<span style='font-size: 40pt'></span></p>");

            var line = Assert.Single(Find(root, "p").LineBoxes);

            Assert.True(line.FlowTop!.Value < PageContentBottom);
            Assert.Equal(TallLine, line.BaselineExtent!.Value.Height, 3);
        }

        [Theory]
        [InlineData("inline-flex")]
        [InlineData("inline-table")]
        public async Task EmptyInline_HeldBeforeAnAtomicInline_GoesToTheAtomicInlinesLine(string display)
        {
            // The atomic inline places no word, but it is on the first line, and so is the empty inline before
            // it; the first word after it wraps to the second line.
            var (root, _) = await LayoutAsync(
                "<p id='p' style='width: 120pt'>foo <span style='font-size: 40pt'></span>" +
                $"<span style='display: {display}'>x</span> {LongWord}</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(TallLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(ShortLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Theory]
        [InlineData("text<span style='font-size: 1000pt'></span>")]
        [InlineData("<span style='font-size: 1000pt'></span>text")]
        public async Task LineGrownByAnEmptyInline_TallerThanAnyPage_OverflowsInsteadOfBreakingForever(string content)
        {
            // Moving the line would only repeat the question on the next page, so it stays and overflows
            // (css-break-3 §2), and its text is still laid out. The empty inline grows the line after "text"
            // is placed in the first case, and before it, through the word's own placement, in the second.
            var (_, container) = await LayoutAsync(
                $"<div style='height: 770pt'></div><p>{content}</p><p>after</p>");

            var pages = container.FragmentTree!.Fragmentainers;
            var placed = pages.SelectMany(f => Flatten(f.Root)).SelectMany(f => f.Words).Select(w => w.Word.Text).ToList();

            Assert.True(pages.Count <= 3, $"{pages.Count} pages");
            Assert.Contains("text", placed);
            Assert.Contains("after", placed);
        }

        [Fact]
        public async Task EmptyInline_GivenToTheLineBeforeAPageBreak_IsNotPlacedAgainAfterIt()
        {
            // The empty inline has no space before it, so it stays on "aaa"'s line, which fits at the foot
            // of the first page. The next line starts the second page, and the pass laying it out resumes at
            // the empty inline's own ordinal, since it places no word.
            var (root, _) = await LayoutAsync(
                "<div style='height: 735pt'></div><p id='p' style='width: 120pt; orphans: 1; widows: 1'>" +
                $"aaa<span style='font-size: 40pt'></span> {LongWord}</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.True(p.LineBoxes[0].FlowTop!.Value + TallLine <= PageContentBottom);
            Assert.Equal(PageContentBottom, p.LineBoxes[1].FlowTop!.Value, 3);
            Assert.Equal(TallLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(ShortLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task EmptyInline_HeldForTheLineAPageBreakDiscards_IsPlacedOnTheNextPage()
        {
            // Both empty inlines sit at "LongWord"'s ordinal. The first has no space before it and stays on
            // the first page's line; the 40pt one comes after the space and goes with "LongWord", whose line
            // crosses the page end and is laid out again on the next page.
            var (root, _) = await LayoutAsync(
                "<div style='height: 760pt'></div><p id='p' style='width: 120pt; orphans: 1; widows: 1'>" +
                $"aaaaaaaaaaaa<span></span> <b><span style='font-size: 40pt'></span></b> <b>{LongWord}</b></p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(ShortLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(PageContentBottom, p.LineBoxes[1].FlowTop!.Value, 3);
            Assert.Equal(TallLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task EmptyInline_OnTheLineAPageBreakDiscards_IsPlacedAgainWithIt()
        {
            // The inline-block wraps to open the second line and places no word, so the empty inline after it
            // is at the ordinal of "bbb", where the discarded line resumes, although it was given to that
            // line and not to the one before.
            var (root, _) = await LayoutAsync(
                "<div style='height: 760pt'></div><p id='p' style='width: 120pt; orphans: 1; widows: 1'>aaaaaaaaaaaa " +
                "<span style='display: inline-block; width: 100pt; height: 5pt'></span><span style='font-size: 40pt'></span>bbb</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(PageContentBottom, p.LineBoxes[1].FlowTop!.Value, 3);
            Assert.Equal(TallLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task LineOfAnImageGrownByAnEmptyInline_PastThePageEnd_MovesToTheNextPage()
        {
            // The image is the only word, and it rests on the baseline the empty inline moves down.
            const string image = "<svg id='i' width='8pt' height='8pt'><rect width='8' height='8'/></svg>";
            var (root, _) = await LayoutAsync(
                $"<div style='height: 770pt'></div><p id='p'>{image}<span style='font-size: 40pt'></span></p>");
            var (controlRoot, _) = await LayoutAsync(
                $"<div style='height: 770pt'></div><p id='p'>{image}<span style='font-size: 40pt'></span>x</p>");

            var word = Assert.Single(Find(root, "p").LineBoxes[^1].Words);

            Assert.True(word.Top > PageContentBottom);
            Assert.Equal(Find(controlRoot, "p").LineBoxes[^1].Words[0].Top, word.Top, 3);
        }

        [Theory]
        [InlineData("bottom")]
        [InlineData("middle")]
        public async Task LineOfAnEdgeAlignedImageGrownByAnEmptyInline_PastThePageEnd_MovesToTheNextPage(string align)
        {
            // Aligned against the line box, which the empty inline makes reach past the page end.
            var (root, _) = await LayoutAsync(
                $"<div style='height: 770pt'></div><p id='p'><svg width='8pt' height='8pt' style='vertical-align: {align}'>" +
                "<rect width='8' height='8'/></svg><span style='font-size: 40pt'></span></p>");

            var line = Find(root, "p").LineBoxes[^1];

            Assert.Equal(PageContentBottom, line.FlowTop!.Value, 3);
        }

        [Fact]
        public async Task LineOfATopAlignedImageGrownByAnEmptyInline_StaysWhereTheImageFits()
        {
            // The image stays at the line's top, still inside the page, so nothing on the line crosses it.
            var (root, _) = await LayoutAsync(
                "<div style='height: 770pt'></div><p id='p'><svg width='8pt' height='8pt' style='vertical-align: top'>" +
                "<rect width='8' height='8'/></svg><span style='font-size: 40pt'></span></p>");

            var line = Find(root, "p").LineBoxes[^1];

            Assert.True(line.FlowTop!.Value < PageContentBottom);
            Assert.Equal(TallLine, line.BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task EmptyInline_OnALaterLineThanItsParentsText_CountsItsParent()
        {
            // The 10pt empty span goes to the second line with the word after it, and it is nested in the
            // 40pt span, so that span has a fragment on the second line too, though its text is on the first.
            var (root, _) = await LayoutAsync(
                "<p id='p' style='width: 120pt'><span style='font-size: 40pt'>aaa <span style='font-size: 10pt'></span></span>" +
                $"{LongWord}</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(TallLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task EmptyInline_BeforeAForcedBreak_StaysOnTheLineTheBreakEnds()
        {
            var (root, _) = await LayoutAsync(
                "<p id='p'>first <span style='font-size: 40pt'></span><br>second</p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(TallLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(ShortLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
        }

        [Fact]
        public async Task EmptyInline_AfterTheLastWord_GrowsTheLastLine()
        {
            var (root, _) = await LayoutAsync(
                "<p id='p'>first<br>second <span style='font-size: 40pt'></span></p>");

            var p = Find(root, "p");

            Assert.Equal(2, p.LineBoxes.Count);
            Assert.Equal(ShortLine, p.LineBoxes[0].BaselineExtent!.Value.Height, 3);
            Assert.Equal(TallLine, p.LineBoxes[1].BaselineExtent!.Value.Height, 3);
            Assert.Equal(ShortLine + TallLine, p.ActualBottom - p.Location.Y, 3);
        }

        [Fact]
        public async Task EmptyInline_WithASmallerFont_LeavesTheLineAlone()
        {
            var (root, _) = await LayoutAsync(
                "<p id='p'>text<span style='font-size: 5pt'></span>more</p>");

            var p = Find(root, "p");

            Assert.Equal(ShortLine, p.ActualBottom - p.Location.Y, 3);
        }

        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string body) =>
            LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style='font-size: 10pt; line-height: 1.2'>{body}</div>"));

        private static CssBox Find(CssBox root, string id) =>
            LayoutHarness.FindById(root, id) ?? throw new Xunit.Sdk.XunitException($"no box #{id}");

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment) =>
            fragment.Children.SelectMany(Flatten).Prepend(fragment);
    }
}
