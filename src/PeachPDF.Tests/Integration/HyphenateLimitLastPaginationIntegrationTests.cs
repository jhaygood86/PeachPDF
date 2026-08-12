using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end pagination coverage for <c>hyphenate-limit-last</c> (CSS Text 4 §6.3.5, issue #723),
    /// complementing <c>HyphenateLimitLastEnforcementTests</c>'s direct exercise of
    /// <c>CssLayoutEngine.EnforceHyphenateLimitLastBeforeBreak</c>: real <c>hyphens:auto</c> content laid
    /// out across a real page break, confirming the property actually changes where the hyphen lands
    /// rather than just that the rewind's own merge logic is correct in isolation. Widths/heights below
    /// were confirmed empirically (dumping <see cref="CssBox.LineBoxes"/> per page), not hand-derived -
    /// this repo's convention for pixel-exact fragmentation fixtures (see OrphansWidowsIntegrationTests).
    /// </summary>
    public class HyphenateLimitLastPaginationIntegrationTests
    {
        private const string LongWord = "antidisestablishmentarianism";

        // "x " ahead of the long word means the word's hyphenation point sits mid-line rather than always
        // opening a fresh one - so undoing it and moving the whole word into the next fragmentainer
        // actually gains it more room, which is what proves the rewind's effect rather than its no-op
        // decline (see the sole-word-on-line regression test below instead).
        private static string ContentWithLeadingFiller(int repeats) =>
            string.Join(' ', Enumerable.Repeat($"x {LongWord}", repeats));

        private const string Style = "width:90px;font-size:10px;line-height:20pt;margin:0";

        [Fact]
        public async Task Default_None_AllowsTheLastLineOfAPageToEndInAHyphen()
        {
            var (box, container) = await LayoutAcrossAPageBreak(hyphenateLimitLast: null);

            Assert.EndsWith("-", LastLineOfPage(box, container, page: 0)[^1].Text);
        }

        [Fact]
        public async Task Always_ForbidsTheLastLineOfAPageFromEndingInAHyphen()
        {
            var (box, container) = await LayoutAcrossAPageBreak(hyphenateLimitLast: "always");

            Assert.DoesNotContain(LastLineOfPage(box, container, page: 0), w => w.Text!.EndsWith('-'));
        }

        [Fact]
        public async Task Always_MovesTheWholeWordForwardRatherThanLosingOrDuplicatingIt()
        {
            var (noneBox, _) = await LayoutAcrossAPageBreak(hyphenateLimitLast: null);
            var (alwaysBox, _) = await LayoutAcrossAPageBreak(hyphenateLimitLast: "always");

            // Concatenated with no separator (rather than joined per word) so which split points either
            // run happened to choose - "an-"+"tidisestablishmentarianism" vs the whole word kept as one
            // token - doesn't itself change the comparison; only genuinely lost or duplicated characters
            // would. The rewind only ever relocates a split back into one whole word, never drops or
            // duplicates content.
            static string Concatenated(CssBox box) =>
                string.Concat(box.LineBoxes.SelectMany(l => l.Words).Select(w => w.Text!.TrimEnd('-')));

            Assert.Equal(Concatenated(noneBox), Concatenated(alwaysBox));
        }

        // Regression coverage for a livelock this feature measurably produced before a guard was added:
        // when the hyphenated word is its line's *only* word, that line was already a fresh, full-width
        // one - the same width the next fragmentainer's opening line offers - so undoing the split gains
        // nothing, and the identical word hyphenates again there, whose prefix is again its line's only
        // word, forever. Reproduced with 100,000+ empty pages before the fragmentainer-pass cap forced a
        // monolithic fallback; EnforceHyphenateLimitLastBeforeBreak now declines the undo in exactly this
        // shape (`keptLine.Words.Count <= 1`) and leaves the hyphen in place instead.
        [Fact]
        public async Task Always_SoleWordOnItsLine_DeclinesTheUndoInsteadOfLivelocking()
        {
            const string style = "width:40px;font-size:10px;line-height:20pt;margin:0";
            var html = $"<!DOCTYPE html><html lang=\"en\"><head></head><body style='margin:0'>" +
                       $"<p id='p' style='{style};hyphens:auto;hyphenate-limit-last:always'>" +
                       $"{string.Join(' ', Enumerable.Repeat(LongWord, 12))}</p></body></html>";

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 400, pageHeight: 20, margin: 0);

            // The unconstrained baseline for this same fixture takes 24 passes (one line per page, 24
            // lines); a working guard costs nothing extra, while the livelock this pins burned through the
            // entire fragmentainer-pass budget for a single stuck word.
            Assert.True(container.FragmentainerPasses <= 30,
                $"expected a bounded pass count, got {container.FragmentainerPasses}");
        }

        // ─── helpers ────────────────────────────────────────────────────────────

        private static async Task<(CssBox box, HtmlContainerInt container)> LayoutAcrossAPageBreak(
            string? hyphenateLimitLast)
        {
            var declaration = hyphenateLimitLast is null ? "" : $";hyphenate-limit-last:{hyphenateLimitLast}";
            var html = $"<!DOCTYPE html><html lang=\"en\"><head></head><body style='margin:0'>" +
                       $"<p id='p' style='{Style};hyphens:auto{declaration}'>{ContentWithLeadingFiller(8)}</p>" +
                       "</body></html>";

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 400, pageHeight: 40, margin: 0);
            var box = LayoutHarness.FindById(root, "p")!;

            // The scenario must actually reach a second page - otherwise there is no break for the
            // property to act on and the assertions above would pass vacuously.
            Assert.True(box.LineBoxes.Select(l => container.PageIndexOf(l.Words[0].Top)).Max() > 0,
                "expected the content to actually cross a page boundary");

            return (box, container);
        }

        /// <summary>The words of whichever line box is the last one landing on <paramref name="page"/>.</summary>
        private static List<CssRect> LastLineOfPage(CssBox box, HtmlContainerInt container, int page) =>
            box.LineBoxes.Last(l => container.PageIndexOf(l.Words[0].Top) == page).Words;
    }
}
