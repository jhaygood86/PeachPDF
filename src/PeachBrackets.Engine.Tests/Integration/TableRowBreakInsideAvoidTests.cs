using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>break-inside: avoid</c> on a table row keeps the row in one band when it fits in a fresh one
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-within">css-break-3 §3.2</see>, applied to rows by
    /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">§4.1</see>): the row is carried onto the next band
    /// whole instead of being fragmented between its cells' lines at the band's foot.
    /// </summary>
    /// <remarks>
    /// Each fixture row's second cell wraps to several lines, so a band boundary can fall between two of its lines; with
    /// <c>break-inside: auto</c> that is a legal break and the row is fragmented there, which
    /// <see cref="RowsThatAllowBreaks_AreFragmentedAtTheFoot"/> pins as the control — without it, a fixture that never
    /// puts a boundary inside a row would let both assertions pass against an engine that ignores the declaration.
    /// </remarks>
    public class TableRowBreakInsideAvoidTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        private static string Words(int row, int count) =>
            string.Join(' ', Enumerable.Range(1, count).Select(i => $"w{row}_{i}"));

        private static string LongTable(string rowStyle, string before = "", string selector = "tr.r") => LayoutHarness.Wrap(
            $"<style>{selector} {{ {rowStyle} }}</style>{before}" +
            "<table style='width:300pt'><thead><tr><th>Id</th><th>Text</th></tr></thead><tbody>" +
            string.Concat(Enumerable.Range(1, 30).Select(r => $"<tr class='r'><td>w{r}_0</td><td>{Words(r, 24)}</td></tr>")) +
            "</tbody></table>");

        private static List<CssBox> BodyRowsOf(CssBox root) =>
            LayoutHarness.Descendants(root)
                .Where(b => b is not CssProxyBox && b.Display.Value == DisplayMode.TableRow && b.HtmlTag?.TryGetAttribute("class") == "r")
                .ToList();

        /// <summary>The words each fragmentainer holds, read off the fragment tree the painter walks.</summary>
        private static List<List<string>> WordsPerPage(HtmlContainerInt container) =>
            container.FragmentTree!.Fragmentainers.Select(f => WordsOf(f.Root).ToList()).ToList();

        private static IEnumerable<string> WordsOf(BoxFragment fragment) =>
            fragment.Words.Select(w => w.Word.Text ?? string.Empty).Concat(fragment.Children.SelectMany(WordsOf));

        private static List<CssBox> RowsCrossingTheirBand(CssBox root, HtmlContainerInt container) =>
            BodyRowsOf(root)
                .Where(row => HtmlContainerInt.FallsPast(row.ActualBottom, container.BandOfSlot(container.SlotStartingAt(row.Location.Y))))
                .ToList();

        [Fact]
        public async Task RowsThatAvoidBreaks_AreNotFragmented()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LongTable("break-inside: avoid"), pageHeight: PageHeight, margin: Margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1, "the fixture has to overflow, or this asserts nothing");
            Assert.Equal(30, BodyRowsOf(root).Count);

            foreach (var row in RowsCrossingTheirBand(root, container))
            {
                Assert.Fail($"a row avoiding breaks at {row.Location.Y:F1}..{row.ActualBottom:F1} crosses out of its band");
            }
        }

        /// <summary>
        /// The reduction of the first report: a heading, a paragraph and a short table above the long one move the band
        /// boundaries relative to the rows, and the legacy <c>page-break-inside</c> alias is declared alongside.
        /// </summary>
        [Fact]
        public async Task RowsThatAvoidBreaks_AfterOtherContent_AreNotFragmented()
        {
            const string before =
                "<h1>w900_1</h1><p>w900_2 w900_3</p>" +
                "<table><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td>w901_1</td><td>w901_2<br/>w901_3</td></tr></tbody></table>" +
                "<h1>w902_1</h1>";
            var (root, container) = await LayoutHarness.LayoutAsync(
                LongTable("break-inside: avoid; page-break-inside: avoid", before), pageHeight: PageHeight, margin: Margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1, "the fixture has to overflow, or this asserts nothing");

            foreach (var row in RowsCrossingTheirBand(root, container))
            {
                Assert.Fail($"a row avoiding breaks at {row.Location.Y:F1}..{row.ActualBottom:F1} crosses out of its band");
            }
        }

        /// <summary>
        /// Declared on the cells rather than the row: a cell is a parallel flow with no break point of its own before it
        /// (§4.1), so avoiding a break inside one can only be honored by keeping its row whole.
        /// </summary>
        [Fact]
        public async Task RowsWhoseCellsAvoidBreaks_AreNotFragmented()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LongTable("break-inside: avoid", selector: "tr.r td"), pageHeight: PageHeight, margin: Margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1, "the fixture has to overflow, or this asserts nothing");

            foreach (var row in RowsCrossingTheirBand(root, container))
            {
                Assert.Fail($"a row whose cells avoid breaks at {row.Location.Y:F1}..{row.ActualBottom:F1} crosses out of its band");
            }
        }

        /// <summary>
        /// A row that avoids breaks but no band holds is still split (§4.4 relaxes the avoid): the move is attempted once, the
        /// row stops again where it lands, and every word is laid out exactly once. Its first band is also the band a repeated
        /// header opens, so this is the row that, moved a second time, would leave that header alone on a page.
        /// </summary>
        [Fact]
        public async Task ARowThatAvoidsBreaksButNoBandHolds_IsSplitWithNothingLostAndNoHeaderLeftAlone()
        {
            var body =
                "<style>tr.r { break-inside: avoid }</style>" +
                "<table style='width:300pt'><thead><tr><th>wh_1</th><th>wh_2</th></tr></thead><tbody>" +
                string.Concat(Enumerable.Range(1, 3).Select(r => $"<tr class='r'><td>w{r}_0</td><td>{Words(r, 24)}</td></tr>")) +
                $"<tr class='r'><td>w4_0</td><td>{Words(4, 400)}</td></tr>" +
                $"<tr class='r'><td>w5_0</td><td>{Words(5, 24)}</td></tr>" +
                "</tbody></table>";
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), pageHeight: PageHeight, margin: Margin);

            var pages = WordsPerPage(container);
            var bodyWords = pages.SelectMany(p => p).Where(w => !w.StartsWith("wh_")).ToList();

            Assert.True(pages.Count > 2, "the tall row has to span bands, or this asserts nothing");
            Assert.Equal(PaintedWords.InMarkup(body).Count(w => !w.StartsWith("wh_")), bodyWords.Count);
            Assert.Equal(bodyWords.Count, bodyWords.Distinct().Count());
            Assert.All(pages.Where(p => p.Contains("wh_1")), p => Assert.Contains(p, w => !w.StartsWith("wh_")));
        }

        /// <summary>
        /// Rows that avoid breaks while starting and ending <c>rowspan</c> cells: a moved row takes back the writes it made to the
        /// spanning cell of the row above as well as its own unfinished cells, so no word is lost and no word of the rows' own
        /// cells is laid out twice. The spanning cells' own words are left out of the repeat check: drawing those once is #1834.
        /// </summary>
        [Fact]
        public async Task RowsThatAvoidBreaksAcrossRowspans_LoseNothingAndRepeatNoWordOfTheirOwnCells()
        {
            var body =
                "<style>tr.r { break-inside: avoid }</style><table style='width:300pt'><tbody>" +
                string.Concat(Enumerable.Range(1, 15).Select(p =>
                    $"<tr class='r'><td rowspan='2'>w{p}_0</td><td>{Words(p, 24)}</td></tr>" +
                    $"<tr class='r'><td>{string.Join(' ', Enumerable.Range(1, 24).Select(i => $"w{p}_{100 + i}"))}</td></tr>")) +
                "</tbody></table>";
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), pageHeight: PageHeight, margin: Margin);

            var laidOut = WordsPerPage(container).SelectMany(p => p).ToList();

            var ownCellWords = laidOut.Where(w => !w.EndsWith("_0")).ToList();

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1, "the fixture has to overflow, or this asserts nothing");
            Assert.Empty(PaintedWords.InMarkup(body).Except(laidOut));
            Assert.Equal(ownCellWords.Count, ownCellWords.Distinct().Count());
        }

        /// <summary>The control: with <c>break-inside: auto</c> the same fixture does put a band boundary inside a row.</summary>
        [Fact]
        public async Task RowsThatAllowBreaks_AreFragmentedAtTheFoot()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LongTable("break-inside: auto"), pageHeight: PageHeight, margin: Margin);

            Assert.NotEmpty(RowsCrossingTheirBand(root, container));
        }
    }
}
