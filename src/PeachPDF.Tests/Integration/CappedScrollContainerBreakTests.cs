using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A scroll container with an auto height and a <c>max-height</c> breaks between its lines like a plain
    /// block while its content is under the cap, and the content past the cap runs on without taking a break
    /// (it is clipped away, and a break among it would end the pass with the siblings after the box unplaced).
    /// </summary>
    public class CappedScrollContainerBreakTests
    {
        // Pages are 300pt wide with a 160pt band at 20pt margins: band k is [20 + 160k, 180 + 160k).
        private const string Page = "@page { size: 300pt 200pt; margin: 20pt } body { margin: 0; font: 10pt/12pt Arial }";

        private static string Document(double filler, string cardStyle, int lines) =>
            $"<!DOCTYPE html><html><head><style>{Page}</style></head><body>"
            + $"<div style='height:{filler.ToString(CultureInfo.InvariantCulture)}pt'>S</div>"
            + $"<div id='card' style='{cardStyle}'>{string.Join("<br>", Enumerable.Range(1, lines).Select(i => $"W{i}"))}</div>"
            + "<div id='after'>A1<br>A2<br>A3</div></body></html>";

        // A box whose content fits under its cap breaks between its lines, for every scroll overflow value.
        [Theory]
        [InlineData("auto")]
        [InlineData("scroll")]
        [InlineData("hidden")]
        public async Task ABoxWhoseContentFitsUnderItsCap_BreaksBetweenItsLines(string overflow)
        {
            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document(130, $"overflow:{overflow};max-height:400pt", 6), new PdfGenerateConfig());

            var card = LayoutHarness.FindById(root, "card")!;
            var lines = card.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak).ToList();

            Assert.Equal([0, 0, 1, 1, 1, 1], lines.Select(w => container.PageIndexOf(w.Top)));
            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);
        }

        // The box reaches its cap where the lines it placed add up to it, not a cap's distance below its top:
        // the unused strip at the foot of the first page is not content.
        [Fact]
        public async Task ACappedBoxThatBreaks_EndsWhereItsConsumedHeightReachesTheCap()
        {
            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document(120, "overflow:hidden;max-height:96pt", 20), new PdfGenerateConfig());

            var card = LayoutHarness.FindById(root, "card")!;
            var after = LayoutHarness.FindById(root, "after")!;

            // 36pt of lines (W1-W3) fill the first page from the box's top at 140; 60pt of the cap are left
            // for the second page, which starts at 180.
            Assert.InRange(card.ActualBottom, container.PageTopOf(1) + 59.5, container.PageTopOf(1) + 61);
            Assert.True(after.Location.Y >= card.ActualBottom - 0.01, "the content after the box starts below its cap");
            Assert.Equal(1, container.PageIndexOf(after.Location.Y + 0.1));
        }

        // Content past the cap is clipped away and must neither lose what follows the box, nor draw a page of
        // its own, nor leave the box short of lines under its cap, wherever the first page ends.
        [Theory]
        [InlineData("overflow:hidden;max-height:96pt")]
        [InlineData("overflow:hidden;max-height:96pt;padding:4pt;border:2pt solid #999;box-sizing:border-box")]
        [InlineData("overflow:hidden;max-height:50%")]
        public async Task ContentPastTheCap_NeverLosesWhatFollowsTheBox(string cardStyle)
        {
            List<string> failures = [];

            for (var filler = 12.0; filler <= 330; filler += 7)
            {
                var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                    Document(filler, cardStyle, 20), new PdfGenerateConfig());

                var card = LayoutHarness.FindById(root, "card")!;
                var after = LayoutHarness.FindById(root, "after")!;
                var shown = SlicedMonolithLinesTests.VisibleFractions(container);

                foreach (var a in new[] { "A1", "A2", "A3" })
                {
                    var fraction = shown.GetValueOrDefault(a);
                    if (fraction is < 0.95 or > 1.05) failures.Add($"{filler}pt: {a} shows {fraction:0.00}");
                }

                if (after.Location.Y < card.ActualBottom - 0.01)
                    failures.Add($"{filler}pt: the content after the box starts at {after.Location.Y:0.0}, above its end {card.ActualBottom:0.0}");

                var lastPage = container.PageIndexOf(after.ActualBottom - 0.1);
                if (container.FragmentTree!.Fragmentainers.Count > lastPage + 1)
                    failures.Add($"{filler}pt: {container.FragmentTree.Fragmentainers.Count} pages, content ends on page {lastPage + 1}");
            }

            Assert.Empty(failures);
        }

        // Every line under the cap is drawn in full exactly once however it falls across the pages, and the
        // lines past it are not shown.
        [Fact]
        public async Task EveryLineUnderTheCapIsShownOnceAndNoneBeyondIt()
        {
            List<string> failures = [];

            for (var filler = 14.0; filler <= 330; filler += 7)
            {
                var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                    Document(filler, "overflow:hidden;max-height:96pt", 20), new PdfGenerateConfig());

                var shown = SlicedMonolithLinesTests.VisibleFractions(container);

                for (var i = 1; i <= 20; i++)
                {
                    var fraction = shown.GetValueOrDefault($"W{i}");

                    if (i <= 8 && fraction is < 0.95 or > 1.05) failures.Add($"{filler}pt: W{i} shows {fraction:0.00}");
                    if (i > 8 && fraction > 0.05) failures.Add($"{filler}pt: W{i} past the cap shows {fraction:0.00}");
                }
            }

            Assert.Empty(failures);
        }

        // A box that crosses whole pages before it reaches its cap counts each page it crosses in full.
        [Fact]
        public async Task ABoxSpanningSeveralPagesBeforeItsCap_ShowsEveryLineUnderTheCapOnce()
        {
            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document(30, "overflow:hidden;max-height:480pt", 80), new PdfGenerateConfig());

            var card = LayoutHarness.FindById(root, "card")!;
            var after = LayoutHarness.FindById(root, "after")!;
            var shown = SlicedMonolithLinesTests.VisibleFractions(container);

            // 480pt of the cap is 40 lines, whichever page a line falls on.
            List<string> failures = [];
            for (var i = 1; i <= 80; i++)
            {
                var fraction = shown.GetValueOrDefault($"W{i}");
                if (i <= 38 && fraction is < 0.95 or > 1.05) failures.Add($"W{i} shows {fraction:0.00}");
                if (i > 42 && fraction > 0.05) failures.Add($"W{i} past the cap shows {fraction:0.00}");
            }

            Assert.Empty(failures);
            Assert.True(after.Location.Y >= card.ActualBottom - 0.01);
            Assert.True(container.PageIndexOf(card.ActualBottom - 0.1) >= 3, "the box must cross whole pages before its cap");
            Assert.InRange(shown.GetValueOrDefault("A1"), 0.95, 1.05);
        }

        // A box holding a table, flex or grid container takes breaks the line hook cannot hold back, so it keeps
        // the whole-box treatment (MonolithicContent) and moves to the next page as one.
        [Fact]
        public async Task ACappedBoxHoldingATable_StaysWhole()
        {
            var html = $"<!DOCTYPE html><html><head><style>{Page}</style></head><body><div style='height:140pt'>S</div>"
                + "<div id='card' style='overflow:hidden;max-height:96pt'><table><tr><td>T1<br>T2<br>T3<br>T4<br>T5</td></tr></table></div>"
                + "<div id='after'>A1</div></body></html>";

            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig());
            var card = LayoutHarness.FindById(root, "card")!;

            Assert.True(MonolithicContent.IsMonolithic(card));
            Assert.Equal(container.PageTopOf(1), card.Location.Y, 1);
        }
    }
}
