using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A table relocated whole by <c>break-inside: avoid</c> draws its repeating
    /// <c>&lt;thead&gt;</c> or <c>&lt;tfoot&gt;</c> once per page it ends up on, and on no page it has
    /// left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table that does not fit in what is left of a band is laid out again from the start on the next
    /// one (<see href="https://www.w3.org/TR/css-break-3/#break-within">css-break-3 §4.2</see>). The
    /// abandoned run's proxies were already dropped from the table's children, but a proxy is not what
    /// paint reads: each one also recorded a repeating-group instance with the fragment emitter, and
    /// those survived. A three-row table pushed onto the next page therefore drew its header three
    /// times — once stranded on the page the table had left, at the position the abandoned run gave it,
    /// and twice on top of itself where the abandoned run's page break had been.
    /// </para>
    /// <para>
    /// The counts here are the whole assertion, so they are taken from the fragment tree rather than
    /// from the table's box list: the box list was already right while the output was wrong, which is
    /// how this survived <see cref="RepeatingTableRelayoutTests"/>.
    /// </para>
    /// <para>
    /// Every assertion is stated against the pages the table's own <i>rows</i> reach rather than
    /// against page numbers, so it says the same thing whatever the platform's font metrics do to
    /// where the table lands. A first version named page 1 and page 2 outright and failed on Windows,
    /// where the same fixture put nothing at all of the table on the first page.
    /// </para>
    /// </remarks>
    public class RelocatedTableHeaderInstanceTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        /// <summary>
        /// <paramref name="fillerHeight"/> leaves too little of the first page for the table, which
        /// carries <c>break-inside: avoid</c>, so the whole table moves to the next one and the run
        /// that placed it on the first is abandoned.
        /// </summary>
        private static string PushedTable(double fillerHeight, string group) =>
            LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + "<table style='width:100pt;break-inside:avoid;font-size:10pt'>"
                + $"<{group}><tr><th>HEADWORD</th></tr></{group}>"
                + "<tbody><tr><td>ROWWORD</td></tr><tr><td>ROWWORD</td></tr><tr><td>ROWWORD</td></tr>"
                + "</tbody></table>");

        [Theory]
        [InlineData(120, "thead")]
        [InlineData(130, "thead")]
        [InlineData(140, "thead")]
        [InlineData(120, "tfoot")]
        [InlineData(130, "tfoot")]
        [InlineData(140, "tfoot")]
        public async Task ATableRelocatedWhole_DrawsItsRepeatingGroupOnceOnThePageItLandsOn(
            double fillerHeight, string group)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                PushedTable(fillerHeight, group), pageHeight: PageHeight, margin: Margin);

            var headers = CountPerPage(container, "HEADWORD");
            var rows = CountPerPage(container, "ROWWORD");

            Assert.True(headers.Count > 1, "fixture does not paginate, so it asserts nothing");

            // The relocation itself: no part of the table is left on the page it was pushed off.
            Assert.Equal(0, rows[0]);

            AssertOneGroupPerPageTheRowsReach(headers, rows);
        }

        /// <summary>
        /// The control: the same table without <c>break-inside: avoid</c> splits across the boundary
        /// and repeats its header per
        /// <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">css-tables-3 §6.2</see>. It
        /// shares every ingredient with the fixture above except the relocation, so a fix that
        /// suppressed repetition outright rather than dropping stale records would fail here. Enough
        /// rows that the table has to span two pages whatever a row measures.
        /// </summary>
        [Fact]
        public async Task ATableAllowedToSplit_StillRepeatsItsHeaderOnEveryPageItSpans()
        {
            var rowMarkup = string.Concat(Enumerable.Repeat("<tr><td>ROWWORD</td></tr>", 30));

            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:100pt;font-size:10pt'>"
                    + "<thead><tr><th>HEADWORD</th></tr></thead>"
                    + $"<tbody>{rowMarkup}</tbody></table>"),
                pageHeight: PageHeight, margin: Margin);

            var headers = CountPerPage(container, "HEADWORD");
            var rows = CountPerPage(container, "ROWWORD");

            Assert.True(rows.Count(n => n > 0) > 1, "the table does not split, so this asserts nothing");

            AssertOneGroupPerPageTheRowsReach(headers, rows);
        }

        /// <summary>
        /// css-tables-3 §6.2 repeats the group on each page the table spans — so exactly one wherever
        /// a row of it lands, and none anywhere else. A stranded copy fails the second half; a copy
        /// drawn on top of itself fails the first.
        /// </summary>
        private static void AssertOneGroupPerPageTheRowsReach(
            IReadOnlyList<int> headers, IReadOnlyList<int> rows)
        {
            Assert.Equal(rows.Select(n => n > 0 ? 1 : 0), headers);
        }

        private static List<int> CountPerPage(HtmlContainerInt container, string word) =>
            container.FragmentTree!.Fragmentainers
                .OrderBy(f => f.SlotIndex)
                .Select(f => Flatten(f.Root)
                    .SelectMany(b => b.Words)
                    .Count(w => w.Word.Text == word))
                .ToList();

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }
    }
}
