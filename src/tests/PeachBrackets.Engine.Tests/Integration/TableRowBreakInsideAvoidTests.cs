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

        [Theory]
        [InlineData("break-inside: avoid")]
        [InlineData("break-inside: avoid-page")]
        [InlineData("page-break-inside: avoid")]
        public async Task RowsThatAvoidBreaks_AreNotFragmented(string rowStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LongTable(rowStyle), pageHeight: PageHeight, margin: Margin);

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
        [Theory]
        [InlineData("break-inside: avoid")]
        [InlineData("break-inside: avoid-page")]
        public async Task RowsWhoseCellsAvoidBreaks_AreNotFragmented(string cellStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LongTable(cellStyle, selector: "tr.r td"), pageHeight: PageHeight, margin: Margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1, "the fixture has to overflow, or this asserts nothing");

            foreach (var row in RowsCrossingTheirBand(root, container))
            {
                Assert.Fail($"a row whose cells avoid breaks at {row.Location.Y:F1}..{row.ActualBottom:F1} crosses out of its band");
            }
        }

        /// <summary>Rows that avoid breaks and fit where they are stay there: only a row that stopped is moved.</summary>
        [Fact]
        public async Task RowsThatAvoidBreaksAndFit_StayOnTheirPage()
        {
            var body =
                "<style>tr.r { break-inside: avoid }</style><table style='width:300pt'><thead><tr><th>Id</th><th>Text</th></tr></thead><tbody>" +
                string.Concat(Enumerable.Range(1, 3).Select(r => $"<tr class='r'><td>w{r}_0</td><td>{Words(r, 6)}</td></tr>")) +
                "</tbody></table>";
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), pageHeight: PageHeight, margin: Margin);

            Assert.Single(container.FragmentTree!.Fragmentainers);
        }

        private static string RowOfLinesAfterThreeRows(int lines, string movedRowStyle) =>
            "<!doctype html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
            "body { margin: 0; font: 10pt Arial } table { width: 100%; border-collapse: collapse } " +
            "td, th { border: 1px solid #999; padding: 2pt } tr { break-inside: avoid }</style></head><body>" +
            "<table><thead><tr><th>Id</th><th>Text</th></tr></thead><tbody>" +
            string.Concat(Enumerable.Range(1, 3).Select(r => $"<tr><td>w{r}_1</td><td>w{r}_2 w{r}_3</td></tr>")) +
            $"<tr style='{movedRowStyle}'><td>{string.Join("<br>", Enumerable.Range(1, lines).Select(l => $"w4_{l}"))}</td><td>w4_0</td></tr>" +
            string.Concat(Enumerable.Range(5, 4).Select(r => $"<tr><td>w{r}_1</td><td>w{r}_2</td></tr>")) +
            "</tbody></table></body></html>";

        /// <summary>
        /// A moved row starts where a break before it would put it, under the repeated header, and each of its lines is laid out
        /// once. 15 and 16 lines fit the fresh band; 17 and 18 do not, so the row stops again where it lands and is split there.
        /// </summary>
        [Theory]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(18)]
        public async Task ARowMovedToTheNextBand_StartsAtItsTopAndIsLaidOutOnce(int lines)
        {
            var moved = RowOfLinesAfterThreeRows(lines, "");

            var (painted, _) = await PaintedWords.LayOutAndPaintAsync(moved);
            var (lost, doubled) = PaintedWords.Diff(moved, painted);
            var at = (await PaintedWords.PositionsAsync(moved))["w4_1"];
            var broken = (await PaintedWords.PositionsAsync(RowOfLinesAfterThreeRows(lines, "break-before: page")))["w4_1"];

            Assert.True(at.Page == 1, $"the row has to be moved off the first page, or this asserts nothing; it starts on page {at.Page}");
            Assert.Empty(lost);
            Assert.Empty(doubled);
            Assert.Equal(broken.Page, at.Page);
            Assert.Equal(broken.Y, at.Y, 3);
        }

        /// <summary>
        /// A table in a multi-column container: a column is a fragmentainer the page grid does not describe, so a row that
        /// avoids breaks and stops at a column's foot is left to the columns engine, and lays out exactly as a row that allows
        /// breaks does. Moved against the page grid instead, the row's words were laid out in more than one column.
        /// </summary>
        /// <remarks>Fonts are pinned: on host fonts the rows can land where no row stops at a column's foot, and the case is never reached.</remarks>
        [Theory]
        [InlineData(300, 1)]
        [InlineData(300, 3)]
        [InlineData(240, 3)]
        [InlineData(400, 4)]
        public async Task RowsThatAvoidBreaksInAColumn_AreLeftToTheColumns(double pageHeight, int everyNthRowAvoids)
        {
            string InColumns(string rowStyle) => LayoutHarness.Wrap(
                $"<style>body {{ font: 10pt sans-serif }} tr.r {{ {rowStyle} }}</style><div style='columns: 2; column-gap: 10pt'><table style='width:100%'><thead><tr><th>Id</th><th>T</th></tr></thead><tbody>" +
                string.Concat(Enumerable.Range(1, 30).Select(r => $"<tr{(r % everyNthRowAvoids == 0 ? " class='r'" : "")}><td>w{r}_0</td><td>{Words(r, 12 + r % 5 * 4)}</td></tr>")) +
                "</tbody></table></div>");

            var (_, avoiding) = await LayoutHarness.LayoutAsync(InColumns("break-inside: avoid"), pageHeight: pageHeight, margin: Margin,
                configureRenderContext: BundledFonts.PinSansSerifAsync);
            var (_, allowing) = await LayoutHarness.LayoutAsync(InColumns("break-inside: auto"), pageHeight: pageHeight, margin: Margin,
                configureRenderContext: BundledFonts.PinSansSerifAsync);

            Assert.True(allowing.FragmentTree!.Fragmentainers.Count > 1, "the fixture has to overflow, or this asserts nothing");
            Assert.Equal(WordsPerPage(allowing), WordsPerPage(avoiding));
        }

        /// <summary>
        /// A nested table whose rows avoid breaks too (a global <c>tr</c> rule reaches them): the nested row is moved, does
        /// not fit, and stops again. Each of its lines is laid out once, whether the nested table is the cell's own child or sits
        /// further down inside it.
        /// </summary>
        [Theory]
        [InlineData("", "")]
        [InlineData("<div><div>", "</div></div>")]
        public async Task ANestedRowThatAvoidsBreaksAndStopsAgain_IsLaidOutOnce(string open, string close)
        {
            var html =
                "<!doctype html><html><head><meta charset='utf-8'><style>@page{size:300pt 300pt;margin:20pt}body{margin:0;font:10pt Arial}" +
                "table{width:100%;border-collapse:collapse}td,th{border:1px solid #999;padding:2pt}tr{break-inside:avoid}</style></head><body>" +
                "<table><thead><tr><th>HDR0</th><th>HDR1</th></tr></thead><tbody>" +
                string.Concat(Enumerable.Range(1, 3).Select(r => $"<tr><td>w{r}_1</td><td>w{r}_2 w{r}_3</td></tr>")) +
                $"<tr><td>w10_1{open}<table><tr><td>w10_901</td><td>w10_902</td></tr>" +
                $"<tr><td>w10_4</td><td>{string.Join("<br>", Enumerable.Range(5, 22).Select(l => $"w10_{l}"))}</td></tr></table>{close}</td><td>x</td></tr>" +
                string.Concat(Enumerable.Range(21, 4).Select(r => $"<tr><td>w{r}_1</td><td>w{r}_2 w{r}_3</td></tr>")) +
                "</tbody></table></body></html>";

            var (painted, _) = await PaintedWords.LayOutAndPaintAsync(html);
            var (lost, doubled) = PaintedWords.Diff(html, painted);
            var (visible, _) = await PaintedWords.LayOutAndCollectVisibleAsync(html);

            Assert.Empty(lost);
            Assert.Empty(doubled);
            Assert.Empty(PaintedWords.InMarkup(html).Except(visible));
        }

        private static string RowEndingPastTheFreshBand(double overshoot, string movedRowStyle) =>
            "<!doctype html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
            "body { margin: 0; font: 8pt/10pt Arial } table { width: 100%; border-collapse: collapse; border-spacing: 0 } " +
            "th, td { padding: 0; border: 0 } th { height: 20pt } td.c { padding-bottom: 4pt }</style></head><body>" +
            "<table><thead><tr><th>Id</th></tr></thead><tbody>" +
            "<tr><td><div style='height:100pt'>w1_1</div></td></tr>" +
            $"<tr style='{movedRowStyle}'><td class='c' style='padding-top:{overshoot.ToString(System.Globalization.CultureInfo.InvariantCulture)}pt'>{string.Join("<br>", Enumerable.Range(1, 19).Select(l => $"w2_{l}"))}</td></tr>" +
            "<tr><td>w3_1</td></tr></tbody></table></body></html>";

        /// <summary>
        /// A moved row whose last line ends less than the band-overlap tolerance past the fresh band's foot: the line does not
        /// stop the cell, but the cell's bottom padding makes the row one no band holds, so it is sliced and displaced under the
        /// next page's repeated header. The line is laid out once, on the page it is on, whether the row was moved because it
        /// avoids breaks or because a break was forced before it.
        /// </summary>
        [Theory]
        [InlineData(0.3, "break-inside: avoid")]
        [InlineData(0.45, "break-inside: avoid")]
        [InlineData(0.3, "break-before: page")]
        public async Task ARowJustTallerThanAFreshBand_IsLaidOutOnce(double overshoot, string movedRowStyle)
        {
            var html = RowEndingPastTheFreshBand(overshoot, movedRowStyle);

            var (painted, _) = await PaintedWords.LayOutAndPaintAsync(html);
            var (lost, doubled) = PaintedWords.Diff(html, painted);
            var at = await PaintedWords.PositionsAsync(html);

            Assert.True(at["w2_1"].Page == 1, $"the row has to be moved off the first page, or this asserts nothing; it starts on page {at["w2_1"].Page}");
            Assert.Empty(lost);
            Assert.Empty(doubled);
            Assert.Equal(1, at["w2_19"].Page);
        }

        /// <summary>
        /// A row no band holds, sliced under a repeated header, keeps a line shorter than the band-overlap tolerance: the
        /// strip that holds it draws it, wherever in the strip it sits.
        /// </summary>
        [Theory]
        [InlineData(60)]
        [InlineData(150)]
        [InlineData(209.8)]
        public async Task ASlicedRowKeepsALineShorterThanTheTolerance(double above)
        {
            var html =
                "<!doctype html><html><head><meta charset='utf-8'><style>@page { size: 300pt 250pt; margin: 20pt } " +
                "body { margin: 0; font: 8pt/10pt Arial } table { width: 100%; border-collapse: collapse } th, td { padding: 0; border: 0 } " +
                "th { height: 20pt }</style></head><body><table><thead><tr><th>Id</th></tr></thead><tbody>" +
                $"<tr><td><div style='height:{above.ToString(System.Globalization.CultureInfo.InvariantCulture)}pt'></div>" +
                "<div style='font-size:0.3pt; line-height:0.3pt'>w1_1</div><div style='height:300pt'></div></td></tr>" +
                "<tr><td>w2_1</td></tr></tbody></table></body></html>";

            var (painted, _) = await PaintedWords.LayOutAndPaintAsync(html);
            var (lost, doubled) = PaintedWords.Diff(html, painted);

            Assert.Empty(lost);
            Assert.Empty(doubled);
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

        /// <summary>
        /// A forced break inside a cell is taken where the cell put it, once: a row holding one is not moved, whatever it avoids,
        /// even when the lines after the break run out of band and stop the row.
        /// </summary>
        [Fact]
        public async Task ARowThatAvoidsBreaksButHoldsAForcedBreak_TakesItOnce()
        {
            var rows = string.Concat(Enumerable.Range(1, 8).Select(i =>
                i == 4
                    ? "<tr style='break-inside:avoid'><td><div style='height:10pt'>before</div>"
                      + $"<div id='after' style='break-before:page'>{string.Join("<br>", Enumerable.Range(1, 40).Select(l => $"after {l}"))}</div></td></tr>"
                    : $"<tr><td><div style='height:50pt'>row {i}</div></td></tr>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<table style='width:100%;border-collapse:collapse'>{rows}</table>"),
                pageHeight: PageHeight, margin: Margin);

            Assert.Equal(1, container.SlotStartingAt(LayoutHarness.FindById(root, "after")!.Location.Y));
        }

        /// <summary>
        /// The control: with <c>break-inside: auto</c> the same fixture does put a band boundary inside a row, and so does
        /// <c>avoid-column</c>, which says nothing about a page break.
        /// </summary>
        [Theory]
        [InlineData("break-inside: auto")]
        [InlineData("break-inside: avoid-column")]
        public async Task RowsThatAllowBreaks_AreFragmentedAtTheFoot(string rowStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LongTable(rowStyle), pageHeight: PageHeight, margin: Margin);

            Assert.NotEmpty(RowsCrossingTheirBand(root, container));
        }
    }
}
