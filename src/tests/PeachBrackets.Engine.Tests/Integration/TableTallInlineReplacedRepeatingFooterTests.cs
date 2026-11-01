using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A table whose <c>&lt;tfoot&gt;</c> repeats while its body cell holds inline replaced content taller than
    /// a page (a 732pt <c>&lt;svg&gt;</c> on an A4 page) is a layout that never finished on v0.9.19: the
    /// render spun forever instead of failing or producing pages. It stopped hanging with the change that
    /// sizes a line from the empty inlines on it, incidentally and with no test of its own.
    /// </summary>
    /// <remarks>
    /// The timeout is a hang guard, not a performance bound - see the note above the same attribute in
    /// <c>Sheet.cs</c>: the only thing it asserts is that layout terminates, and the layout takes about a
    /// second, so the value is an order of magnitude of headroom. Layout is CPU-bound, so it runs on the
    /// thread pool: a synchronous spin on the test's own thread could not be timed out at all.
    /// <para>
    /// Where the words end up on pages is deliberately <i>not</i> asserted beyond "drawn": the text of this
    /// same document is still drawn on two pages, which is a separate, open defect (#1325).
    /// </para>
    /// </remarks>
    public class TableTallInlineReplacedRepeatingFooterTests
    {
        private const string Markup =
            "<table><tfoot><tr><td style=\"height: 100pt\">foot</td></tr></tfoot>" +
            "<tbody><tr><td><svg width=\"10pt\" height=\"732pt\"><rect width=\"1\" height=\"1\"/></svg>text</td></tr></tbody></table>" +
            "<p>after</p>";

        [Fact(Timeout = 60000)]
        public async Task TallInlineSvgInABodyCell_WithARepeatingFooter_LaysOutAndDrawsItsText()
        {
            var (_, container) = await Task.Run(() => LayoutHarness.LayoutAsync(Markup), TestContext.Current.CancellationToken);

            var g = new TestRecordingGraphics();
            for (var page = 0; page < container.FragmentTree!.Fragmentainers.Count; page++)
                FragmentPaintHarness.PaintPage(container, g, page);

            var drawn = g.DrawStringCalls.Select(c => c.Text.Trim()).ToList();

            Assert.Contains("foot", drawn);
            Assert.Contains("text", drawn);
            Assert.Contains("after", drawn);
        }

        [Theory(Timeout = 60000)]
        [InlineData(732)]
        [InlineData(750)]
        [InlineData(792)]
        public async Task TheTextAfterATallSvg_IsInTheFragmentTreeOnce(int svgHeight)
        {
            // The row plus the reserved footer is taller than a band, so it is sliced into strips. The line holding the text
            // starts below the first strip's foot (the strip is the band less the footer), but within the band itself, and was
            // claimed by both strips: the fragment tree held it on the first page and on the second.
            var html = Markup.Replace("732pt", svgHeight + "pt");

            var (_, container) = await Task.Run(() => LayoutHarness.LayoutAsync(html), TestContext.Current.CancellationToken);

            var textFragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(b => b.Words)
                .Count(w => w.Word.Text?.Trim() == "text");

            Assert.Equal(1, textFragments);
        }

        private static IEnumerable<PeachBrackets.Html.Fragments.BoxFragment> Flatten(PeachBrackets.Html.Fragments.BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }

        [Fact(Timeout = 60000)]
        public async Task TallInlineSvgInABodyCell_WithoutAFooter_StillLaysOut()
        {
            // The control that says the footer is what the document above needs, not just the tall svg.
            var (_, container) = await Task.Run(() => LayoutHarness.LayoutAsync(
                "<table><tbody><tr><td><svg width=\"10pt\" height=\"732pt\"><rect width=\"1\" height=\"1\"/></svg>text</td></tr></tbody></table><p>after</p>"), TestContext.Current.CancellationToken);

            Assert.True(container.FragmentTree!.Fragmentainers.Count >= 1);
        }
    }
}
