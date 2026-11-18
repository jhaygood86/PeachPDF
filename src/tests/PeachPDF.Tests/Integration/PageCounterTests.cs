using PeachDrawing.PDF;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-page-3 section 6.1: the <c>page</c> counter as a real counter (<c>counter-reset</c>, <c>counter-set</c>, <c>counter-increment</c> in
    /// <c>@page</c>) and <c>counter(page, &lt;style&gt;)</c> in margin boxes, read back from the text the saved PDF draws in the margin.
    /// </summary>
    public class PageCounterTests
    {
        /// <summary>One 100pt-high block per page of a 200pt-high, margin-free page, so each <c>div</c> is its own page.</summary>
        private static string Pages(string pageCss, int pages = 4, string topLevelCss = "")
        {
            var blocks = string.Concat(Enumerable.Range(1, pages).Select(i => $"<div style='height:100pt;{(i > 1 ? "break-before:page;" : "")}'>p{i}</div>"));
            return $"<!DOCTYPE html><html><head><style>@page {{ size: 200pt 200pt; margin: 40pt 10pt; {pageCss} }} {topLevelCss} body {{ margin: 0; }}</style></head><body>{blocks}</body></html>";
        }

        private static async Task<string[]> FootersAsync(string html, PdfGenerateConfig? config = null)
        {
            var document = await new PdfGenerator().GeneratePdf(html, config ?? new PdfGenerateConfig { PageSize = PageSize.A4 });
            using var stream = new MemoryStream();
            document.Save(stream);
            var source = PdfPageSource.Open(stream.ToArray());
            return [.. Enumerable.Range(0, source.PageCount).Select(i => source.GetText(i).Text.Replace("\n", " ").Replace("p" + (i + 1), "").Trim())];
        }

        private const string Footer = "@bottom-center { content: \"[\" counter(page) \"]\"; }";

        [Fact]
        public async Task WithNoCounterRules_PagesCountFromOne()
        {
            Assert.Equal(["[1]", "[2]", "[3]"], await FootersAsync(Pages(Footer, 3)));
        }

        [Fact]
        public async Task CounterResetOnAPage_RestartsTheNumberingThere()
        {
            // A named page that holds the body's first page: front matter is numbered 1, 2 and the body restarts at 1. (The rule matches every page of that name, so only one page uses it.)
            var html = "<!DOCTYPE html><html><head><style>@page { size: 200pt 200pt; margin: 40pt 10pt; " + Footer + " } @page body { counter-reset: page 1; } " +
                       ".body { page: body; } div { height: 100pt; } body { margin: 0; }</style></head><body>" +
                       "<div>p1</div><div style='break-before:page'>p2</div><div class='body' style='break-before:page'>p3</div><div style='break-before:page; page: auto'>p4</div></body></html>";
            Assert.Equal("[1] [2] [1] [2]", string.Join(" ", await FootersAsync(html)));
        }

        [Fact]
        public async Task FirstPageCounterReset_SetsTheFirstNumber()
        {
            Assert.Equal(["[5]", "[6]", "[7]"], await FootersAsync(Pages(Footer, 3, "@page :first { counter-reset: page 5 }")));
        }

        [Fact]
        public async Task CounterSet_IsAppliedAfterTheImplicitIncrement()
        {
            var html = Pages(Footer, 3, "@page :first { counter-set: page 10 }");
            Assert.Equal(["[10]", "[11]", "[12]"], await FootersAsync(html));
        }

        [Fact]
        public async Task CounterIncrementOnPage_ReplacesTheImplicitOneMore()
        {
            Assert.Equal(["[2]", "[4]", "[6]"], await FootersAsync(Pages("counter-increment: page 2; " + Footer, 3)));
        }

        [Fact]
        public async Task TheCounterStyle_IsHonoredInMarginBoxes()
        {
            var footers = await FootersAsync(Pages("@bottom-center { content: counter(page, lower-roman) \" / \" counter(pages, upper-alpha); }", 3));

            Assert.Equal(["i / C", "ii / C", "iii / C"], footers);
        }

        [Fact]
        public async Task ARestartedCounterWithAStyle_PrintsTheRestartedNumberInThatStyle()
        {
            var html = Pages("@bottom-center { content: counter(page, lower-roman); }", 3, "@page :first { counter-reset: page 4 }");

            Assert.Equal(["iv", "v", "vi"], await FootersAsync(html));
        }

        [Fact]
        public async Task LeftAndRightPages_StayThePhysicalPositionWhateverTheCounterSays()
        {
            // css-page-3 section 4.2.1 classifies left and right by page progression, so the page counter's value does not change which page is which.
            var html = Pages("", 3, "@page :right { @bottom-center { content: \"R\" counter(page); } } @page :left { @bottom-center { content: \"L\" counter(page); } } @page :first { counter-reset: page 2 }");

            Assert.Equal(["R2", "L3", "R4"], await FootersAsync(html));
        }

        [Fact]
        public async Task ARunningElementsCounter_FollowsTheSameSequence()
        {
            var html = "<!DOCTYPE html><html><head><style>@page { size: 200pt 200pt; margin: 40pt 10pt; @bottom-center { content: element(foot); } } @page :first { counter-reset: page 7 } " +
                       ".foot { position: running(foot); } .foot::after { content: counter(page, upper-roman); } div { height: 100pt; } body { margin: 0; }</style></head><body>" +
                       "<div class='foot'>pg </div><div>p1</div><div style='break-before:page'>p2</div></body></html>";

            var footers = await FootersAsync(html);

            Assert.Contains("VII", footers[0]);
            Assert.Contains("VIII", footers[1]);
        }
    }
}
