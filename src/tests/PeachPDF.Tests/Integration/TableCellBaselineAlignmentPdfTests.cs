using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 §17.5.3 / css-tables-3 §7.8: a <c>vertical-align: baseline</c> table cell aligns its own
    /// baseline with the row's, not its top. These read the fill rectangles a saved PDF draws for small,
    /// atomic markers placed inside each cell, so the assertion is about where the content actually ends
    /// up rather than about font metrics - the same style <see cref="TableDefaultPaddingPdfTests"/> uses
    /// for width/padding, applied here to the row-axis shift this wave adds.
    /// </summary>
    public class TableCellBaselineAlignmentPdfTests
    {
        private static async Task<Dictionary<string, (double X, double Y, double Width, double Height)>> FillsAsync(string body)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0 }}</style></head><body>{body}</body></html>";
            return ReadFills((await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect());
        }

        private static Dictionary<string, (double X, double Y, double Width, double Height)> ReadFills(PdfFile file)
        {
            var fills = new Dictionary<string, (double, double, double, double)>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);
            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
            {
                double Number(string group) => double.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
                fills.TryAdd(m.Groups["c"].Value, (Number("x"), Number("y"), Math.Abs(Number("w")), Math.Abs(Number("h"))));
            }

            return fills;
        }

        private const string MarkerRed = "1 0 0";
        private const string MarkerBlue = "0 0 1";
        private const string CellYellow = "1 1 0";

        private static string Marker(string color, int size) =>
            $"<div style='display:inline-block;width:{size}px;height:{size}px;background:{color}'></div>";

        [Fact]
        public async Task CellsWithDifferentTopPaddingLineUpTheirBaselinesNotTheirTops()
        {
            // Same marker height (10px) in both cells, so aligning their baselines (the bottom margin
            // edge of an atomic inline-block, CSS 2.1 §10.8.1) also aligns their tops - a direct read of
            // whether the shift happened at all, with no font metrics involved.
            var fills = await FillsAsync(
                "<table style='border-spacing:0'><tr>"
                + $"<td style='padding-top:40px;vertical-align:baseline'>{Marker("red", 10)}</td>"
                + $"<td style='padding-top:0;vertical-align:baseline'>{Marker("blue", 10)}</td>"
                + "</tr></table>");

            Assert.Equal(fills[MarkerRed].Y, fills[MarkerBlue].Y, 0.05);
        }

        [Fact]
        public async Task CellsWithDifferentTopPaddingDoNotLineUpUnderTopAlignment()
        {
            // Control for the test above: the same markup with `vertical-align: top` (the keyword
            // `baseline` used to be treated as, before this wave) must NOT line the markers up - proving
            // the previous assertion is actually exercising baseline's own code path, not a coincidence
            // of equal padding or rounding.
            var fills = await FillsAsync(
                "<table style='border-spacing:0'><tr>"
                + $"<td style='padding-top:40px;vertical-align:top'>{Marker("red", 10)}</td>"
                + $"<td style='padding-top:0;vertical-align:top'>{Marker("blue", 10)}</td>"
                + "</tr></table>");

            Assert.NotEqual(fills[MarkerRed].Y, fills[MarkerBlue].Y, 0.05);
        }

        [Fact]
        public async Task ABaselineShiftThatOutgrowsACellsNaturalHeightGrowsTheRow()
        {
            // The tall cell (a 120px atomic marker - ascent is its own whole height, descent zero) sets
            // the row's baseline far below the short cell's own natural top-to-baseline distance (four
            // short lines of text). Shifting the short cell down by that difference pushes its own
            // bottom past its natural height, which the row has to grow to still hold (css-tables-3
            // §7.8) - measured here as the first cell's own background (its full border-box, stretched
            // to the row's final height) growing well past the marker's own 90pt, which only a row-wide
            // grow driven by the *other* cell's shift can cause: the first cell's own content never
            // moves, since it already carries the row's largest ascent.
            var fills = await FillsAsync(
                "<table style='border-spacing:0'><tr>"
                + $"<td style='padding:0;vertical-align:baseline;background:yellow'>{Marker("red", 120)}</td>"
                + "<td style='padding:0;vertical-align:baseline;font-size:16px;line-height:16px'>a<br/>a<br/>a<br/>a</td>"
                + "</tr></table>");

            Assert.True(fills[CellYellow].Height > 110,
                $"the tall cell's own background is {fills[CellYellow].Height:F1}pt tall; the row should have grown well past the marker's own 90pt");
        }

        [Fact]
        public async Task AnAnonymousCellDefaultsToBaselineWhileARealTdDefaultsToMiddle()
        {
            // The UA sheet's `td, th { vertical-align: inherit }` and `tfoot, tr { vertical-align: middle
            // }` are keyed on the real HTML tag names - a `display: table-cell` <div> matches neither, so
            // its computed vertical-align stays at the CSS initial value (baseline), while a real <td>
            // inherits `middle` from its `tr`. Same differing-padding markup as the baseline test above,
            // through the two different element sets.
            var anonymous = await FillsAsync(
                "<div style='display:table;border-spacing:0'><div style='display:table-row'>"
                + $"<div style='display:table-cell;padding-top:40px'>{Marker("red", 10)}</div>"
                + $"<div style='display:table-cell;padding-top:0'>{Marker("blue", 10)}</div>"
                + "</div></div>");

            var realTable = await FillsAsync(
                "<table style='border-spacing:0'><tr>"
                + $"<td style='padding-top:40px'>{Marker("red", 10)}</td>"
                + $"<td style='padding-top:0'>{Marker("blue", 10)}</td>"
                + "</tr></table>");

            // Anonymous table-cell <div>s: no UA rule applies, so vertical-align's initial value
            // (baseline) lines the markers up, same as the explicit-baseline test above.
            Assert.Equal(anonymous[MarkerRed].Y, anonymous[MarkerBlue].Y, 0.05);

            // Real <td>s: `middle` centers each marker independently in the row's own height, so they do
            // not end up at the same Y as the shorter-padding cell's marker is centered lower.
            Assert.NotEqual(realTable[MarkerRed].Y, realTable[MarkerBlue].Y, 0.05);
        }
    }
}
