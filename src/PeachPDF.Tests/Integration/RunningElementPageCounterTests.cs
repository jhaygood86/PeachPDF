using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-page-3's <c>page</c>/<c>pages</c> counters resolved inside a <c>position: running()</c>
    /// element — the "Page N of M" running footer. They are UA-maintained per-page counters, not
    /// document counters, so <c>CssCounterEngine</c> has no notion of them and answers 1 for both;
    /// <c>CssContentEngine.ApplyContent</c> also runs once at DOM-construction time and bakes its
    /// result into the box's text, which is correct for everything in a running element except these.
    /// <para>
    /// Asserted on the words in each page's <see cref="MarginBoxFragment"/>, since that is what gets
    /// painted, and per this repo's warning against proving a feature from a content-stream substring.
    /// </para>
    /// </summary>
    public class RunningElementPageCounterTests
    {
        private static string Fixture(string footerContent, string extraCss) =>
            $$"""
            <!DOCTYPE html>
            <html><head><style>
            @page { size: a6; margin: 12mm; }
            @page { @bottom-center { content: element(foot); font-size: 8pt; } }
            #foot { position: running(foot); margin: 0; }
            #foot::before { content: {{footerContent}}; }
            {{extraCss}}
            </style></head><body>
            <div id="foot"></div>
            """ +
            string.Concat(Enumerable.Repeat("<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>", 60)) +
            "</body></html>";

        private static async Task<string[]> FooterTextPerPageAsync(string footerContent, string extraCss = "")
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Fixture(footerContent, extraCss), new PdfGenerateConfig { PageSize = PageSize.A6 });

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 2,
                "fixture must run to at least three pages, or 'Page N of M' asserts nothing");

            return container.FragmentTree.Fragmentainers
                .Select(f => string.Concat(
                    Flatten(f.MarginBoxes.Single(m => m.BoxName == "bottom-center").Content)
                        .SelectMany(b => b.Words)
                        .Select(w => w.Word.Text)))
                .ToArray();
        }

        private static System.Collections.Generic.IEnumerable<BoxFragment> Flatten(BoxFragment box)
        {
            yield return box;
            foreach (var child in box.Children.OfType<BoxFragment>())
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }

        [Fact]
        public async Task CounterPage_InARunningFooter_CountsTheActualPage()
        {
            var perPage = await FooterTextPerPageAsync("counter(page)");

            // Every page differs, and each reads its own number — not "1" repeated, which is what a
            // document-counter lookup answers, and not all-identical, which is what baking the string
            // once at DOM-construction time produces.
            Assert.Equal(
                Enumerable.Range(1, perPage.Length).Select(n => n.ToString()).ToArray(),
                perPage);
        }

        [Fact]
        public async Task CounterPages_InARunningFooter_IsTheRealFinalPageCount()
        {
            var perPage = await FooterTextPerPageAsync("counter(pages)");

            // The total is known only once layout has settled, so this also pins that the context is
            // populated after the fragment tree is materialized rather than mid-reflow.
            var total = perPage.Length.ToString();
            Assert.All(perPage, text => Assert.Equal(total, text));
        }

        [Fact]
        public async Task PageOfPages_InARunningFooter_ReadsCorrectlyOnEveryPage()
        {
            // The maintainer's own repro shape, and the one real documents use.
            var perPage = await FooterTextPerPageAsync("counter(page) \" of \" counter(pages)");

            var total = perPage.Length;
            Assert.Equal(
                Enumerable.Range(1, total).Select(n => $"{n}of{total}").ToArray(),
                perPage);
        }

        [Fact]
        public async Task ADocumentCounterInARunningFooter_IsUnaffected()
        {
            // The contrast case: an ordinary document counter must keep the value it resolved at
            // DOM-construction time. Without it, the assertions above also pass if page/pages
            // resolution accidentally captured every counter.
            var perPage = await FooterTextPerPageAsync("counter(chapter)", "body { counter-reset: chapter 7; }");

            Assert.All(perPage, text => Assert.Equal("7", text));
        }

        [Fact]
        public async Task QuotesAroundAPageCounter_ResolveAgainstTheRealDocumentPosition()
        {
            // Re-resolving `content` re-runs the quote-depth walk, which reads ParentBox and previous
            // siblings live — unlike the counter lookup, it has no memoization to short-circuit it.
            // The refresh therefore runs before the running box is reparented onto its scratch
            // container. This fixture pins the observable half: quotes and a page counter in one
            // declaration still resolve per page. It does NOT discriminate the ordering, because a
            // declaration that opens and closes its own quote starts at depth 0 either way — moving
            // the refresh back after the reparent leaves it green.
            var perPage = await FooterTextPerPageAsync("open-quote counter(page) close-quote");

            // The engine's default `quotes` is guillemets. Each page opens and closes at depth 0.
            Assert.Equal(
                Enumerable.Range(1, perPage.Length).Select(n => $"«{n}»").ToArray(),
                perPage);
        }
    }
}
