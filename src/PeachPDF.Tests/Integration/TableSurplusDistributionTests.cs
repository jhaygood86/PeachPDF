using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 §17.5.2.2: "if the table is wider than the columns, the extra width should be
    /// distributed over the columns." A table with a declared width whose columns are all auto has
    /// surplus to distribute once each column has been resolved to its own max-content width.
    /// <para>
    /// Handing every column the same number of POINTS does not keep a widened table looking like the
    /// same table — a 250pt description column and a 30pt quantity column each grow by the same
    /// absolute amount. Browsers scale them instead, and the neighbouring all-columns-specified
    /// clause in <c>DetermineMissingColumnWidths</c> already did.
    /// </para>
    /// </summary>
    public class TableSurplusDistributionTests
    {
        private const string Cells =
            "<tr><td id='c0'>a very much longer description column indeed here</td>"
            + "<td id='c1'>Qty</td><td id='c2'>N</td></tr>";

        [Fact]
        public async Task ExtraWidthIsSharedInProportionToWhatEachColumnMeasures()
        {
            // Compared as each column's SHARE of the table rather than as absolute points, because
            // the two engines disagree about font metrics and so about the natural widths the shares
            // are taken of. The shares are what "distributed proportionally" actually means, and
            // they are what an equal split gets wrong.
            //
            // Chrome 152 on this fixture at 500pt: 451.2 / 32.9 / 15.9 => 90.2% / 6.6% / 3.2%.
            double[] chromeShare = [0.902, 0.066, 0.032];

            var widened = await ColumnWidthsAsync("width:500pt;");
            var total = widened.Sum();

            for (var i = 0; i < chromeShare.Length; i++)
            {
                Assert.Equal(chromeShare[i], widened[i] / total, 1);
            }
        }

        [Fact]
        public async Task EachColumnGrowsByAShareOfItsOwnSizeNotAFlatAmount()
        {
            // The direct statement of the rule, on the engine's own numbers: the bump each column
            // takes is proportional to what it already measured. An equal split gives all three the
            // same bump regardless of size, which is the behaviour this replaces.
            var natural = await ColumnWidthsAsync("");
            var widened = await ColumnWidthsAsync("width:500pt;");

            var bumps = widened.Zip(natural, (w, n) => w - n).ToArray();

            Assert.True(bumps.All(b => b > 0), "every auto column should take some of the surplus");
            Assert.True(bumps[0] > bumps[1] * 5,
                $"the widest column must take much the largest bump: {bumps[0]:F1} vs {bumps[1]:F1}");
            Assert.True(bumps[1] > bumps[2],
                $"bumps must order by column size: {bumps[1]:F1} vs {bumps[2]:F1}");
        }

        [Fact]
        public async Task TheWideColumnKeepsItsShareOfTheTable()
        {
            // The same statement in the terms a reader would notice, and the one an equal share gets
            // most wrong. Chrome 152 on this fixture at 500pt: 451.2 / 32.9 / 15.9. An equal share
            // gives 299.7 / 104.7 / 95.5 — the description column loses 150pt to two columns holding
            // three characters between them.
            var widened = await ColumnWidthsAsync("width:500pt;");

            Assert.True(widened[0] / widened.Sum() > 0.85,
                $"the description column should still hold ~90% of the table, held {widened[0] / widened.Sum():P0}");
        }

        [Fact]
        public async Task AnExplicitMaxWidthStillCapsAColumn()
        {
            // The cap the equal-share version already honoured must survive the change to
            // proportional: a column may not be grown past its own declared max-width.
            var (root, _) = await LayoutAsync(Wrap(
                "<table style='width:500pt; border-collapse:collapse'>"
                + "<tr><td id='c0'>a very much longer description column indeed here</td>"
                + "<td id='c1' style='max-width:40pt'>Qty</td><td id='c2'>N</td></tr></table>"));

            var capped = FindById(root, "c1")!;
            Assert.True(capped.ActualRight - capped.Location.X <= 40.5,
                $"max-width must still cap the column, was {capped.ActualRight - capped.Location.X}");
        }

        private static async Task<double[]> ColumnWidthsAsync(string tableStyle)
        {
            var (root, _) = await LayoutAsync(Wrap(
                $"<table style='{tableStyle} border-collapse:collapse'>{Cells}</table>"));

            return new[] { "c0", "c1", "c2" }
                .Select(id => FindById(root, id)!)
                .Select(b => b.ActualRight - b.Location.X)
                .ToArray();
        }
    }
}
