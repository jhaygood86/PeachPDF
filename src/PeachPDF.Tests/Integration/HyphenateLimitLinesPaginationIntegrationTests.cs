using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end pagination coverage for <c>hyphenate-limit-lines</c> (CSS Text 4 §6.3.5, issue #724):
    /// the run of consecutive hyphenated lines the property caps must keep counting across a real page
    /// break rather than restarting at 0 there, complementing
    /// <see cref="HyphenationControlPropertiesIntegrationTests"/>'s single-page coverage of the same
    /// property. Widths/heights below were confirmed empirically (dumping <see cref="CssBox.LineBoxes"/>
    /// per page), not hand-derived - this repo's convention for pixel-exact fragmentation fixtures (see
    /// OrphansWidowsIntegrationTests).
    /// </summary>
    public class HyphenateLimitLinesPaginationIntegrationTests
    {
        private const string LongWord = "antidisestablishmentarianism";

        // One line fits per page (line-height 20pt on a 25pt page band), so the break falls right after
        // the first line - which this fixture's own unconstrained shape (confirmed below) always ends in
        // a hyphen, exercising exactly the boundary the carried count has to cover.
        private const string Style = "width:100px;font-size:10px;line-height:20pt;margin:0;hyphens:auto";

        [Fact]
        public async Task Unconstrained_HyphenatesTheFirstLineOfTheSecondPageToo()
        {
            // Baseline: without a limit, this fixture's second word hyphenates onto the second page's
            // first line exactly as it would if the whole thing were laid out on one unbroken page (see
            // HyphenationControlPropertiesIntegrationTests' single-page coverage of the same two-word
            // shape) - confirming the scenario actually reaches a fresh hyphenation opportunity on page 2,
            // not merely a residual with no candidates left to split further.
            var (box, container) = await LayoutAcrossAPageBreak(hyphenateLimitLines: null);

            Assert.True(FirstLineOfPage(box, container, page: 1)[^1].Text!.EndsWith('-'),
                "expected the fixture's own baseline to hyphenate the second page's first line");
        }

        [Fact]
        public async Task LimitOne_ForbidsTheFirstLineOfTheSecondPageFromHyphenatingToo()
        {
            var (box, container) = await LayoutAcrossAPageBreak(hyphenateLimitLines: 1);

            // Page 1's only line already spent hyphenate-limit-lines:1's whole budget - the carried count
            // (InlineBreakToken.ConsecutiveHyphenatedLines) must forbid page 2's first line from
            // hyphenating too, the same way it would forbid a second consecutive hyphenated line
            // anywhere else in the document. Without the carry (issue #724), this line resets to a fresh
            // budget at the top of the resumed page and hyphenates anyway - the same word split the
            // unconstrained baseline above produces.
            Assert.False(FirstLineOfPage(box, container, page: 1)[^1].Text!.EndsWith('-'),
                "expected the second page's first line not to hyphenate given hyphenate-limit-lines:1");
        }

        // ─── helpers ────────────────────────────────────────────────────────────

        private static async Task<(CssBox box, HtmlContainerInt container)> LayoutAcrossAPageBreak(
            int? hyphenateLimitLines)
        {
            var declaration = hyphenateLimitLines is null ? "" : $";hyphenate-limit-lines:{hyphenateLimitLines}";
            var content = string.Join(' ', Enumerable.Repeat(LongWord, 2));
            var html = "<!DOCTYPE html><html lang=\"en\"><head></head><body style='margin:0'>" +
                       $"<p id='p' style='{Style}{declaration}'>{content}</p></body></html>";

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 200, pageHeight: 25, margin: 0);
            var box = LayoutHarness.FindById(root, "p")!;

            // The scenario must actually reach a second page - otherwise there is no break for the
            // property to act across and the assertions above would pass vacuously.
            Assert.True(box.LineBoxes.Select(l => container.PageIndexOf(l.Words[0].Top)).Max() > 0,
                "expected the content to actually cross a page boundary");

            return (box, container);
        }

        /// <summary>The words of whichever line box is the first one landing on <paramref name="page"/>.</summary>
        private static List<CssRect> FirstLineOfPage(
            CssBox box, HtmlContainerInt container, int page) =>
            box.LineBoxes.First(l => container.PageIndexOf(l.Words[0].Top) == page).Words;
    }
}
