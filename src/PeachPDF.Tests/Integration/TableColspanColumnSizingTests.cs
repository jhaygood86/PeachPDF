using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 §17.5.2.2 step 3: a cell spanning more than one column constrains those columns
    /// <i>together</i> — "increase the minimum widths of the columns it spans so that together, they
    /// are at least as wide as the cell". It says nothing about each column individually, so a
    /// spanning cell must not widen a column that the span, as a whole, already fits into.
    /// <para>
    /// Column boundaries are asserted as a cell's laid-out <c>Location.X</c>, measured against a
    /// control table that differs only in the row under test — the number itself depends on font
    /// metrics, but the comparison does not.
    /// </para>
    /// </summary>
    public class TableColspanColumnSizingTests
    {
        [Fact]
        public async Task ASpanThatAlreadyFits_DoesNotWidenTheNarrowColumnItStraddles()
        {
            // The columns the span covers already sum to more than it needs, so per §17.5.2.2 nothing
            // moves. Dividing the cell's width by its span and forcing each column to at least that
            // share instead drags the narrow one up, and the surplus pushes every later boundary out.
            var withSpan = await ColumnStartsAsync("""
                <tr><td id='c0'>N</td><td id='c1'>a very much longer second column indeed</td><td id='c2'>T</td></tr>
                <tr><td colspan='2'>short span</td><td>T</td></tr>
                """);
            var withoutSpan = await ColumnStartsAsync("""
                <tr><td id='c0'>N</td><td id='c1'>a very much longer second column indeed</td><td id='c2'>T</td></tr>
                """);

            Assert.Equal(withoutSpan[1], withSpan[1], 0.5);
            Assert.Equal(withoutSpan[2], withSpan[2], 0.5);
        }

        [Fact]
        public async Task ASpanWiderThanItsColumns_StillWidensThem()
        {
            // The contrast case: the early return must be "already wide enough", not "never widen".
            var withSpan = await ColumnStartsAsync("""
                <tr><td id='c0'>N</td><td id='c1'>M</td><td id='c2'>T</td></tr>
                <tr><td colspan='2'>a considerably longer spanning run of text indeed</td><td>T</td></tr>
                """);
            var withoutSpan = await ColumnStartsAsync("""
                <tr><td id='c0'>N</td><td id='c1'>M</td><td id='c2'>T</td></tr>
                """);

            Assert.True(withSpan[2] > withoutSpan[2] + 50,
                $"a span wider than its columns must widen them: third boundary {withSpan[2]} vs {withoutSpan[2]}");
        }

        [Fact]
        public async Task TheShortfallIsSharedInProportionToWhatTheColumnsAlreadyMeasure()
        {
            // A column carrying more of the table's content takes more of the extra. Asserted as an
            // ordering rather than an exact split, since the split depends on font metrics: the
            // already-wider column must end up wider still, never equalised with the narrow one.
            var starts = await ColumnStartsAsync("""
                <tr><td id='c0'>N</td><td id='c1'>a longer second column</td><td id='c2'>T</td></tr>
                <tr><td colspan='2'>a considerably longer spanning run of text indeed here</td><td>T</td></tr>
                """);

            var col0Width = starts[1] - starts[0];
            var col1Width = starts[2] - starts[1];

            Assert.True(col1Width > col0Width * 2,
                $"the column already carrying more content must take more of the shortfall: {col0Width} vs {col1Width}");
        }

        [Fact]
        public async Task OverlappingSpans_LeaveEveryCellWithEnoughRoom()
        {
            // Spans are applied in document order and each mutates the widths the next one measures
            // against, so the result is order-dependent when they overlap. What must hold regardless
            // is §17.5.2.2's own requirement, as a postcondition: every spanning cell's columns
            // together are at least as wide as that cell. Growth-only updates make that reachable
            // whatever order they run in.
            var (root, _) = await LayoutAsync(Wrap("""
                <table style='border-collapse:collapse; table-layout:auto; font-size:10pt;'>
                  <tr><td id='c0'>N</td><td id='c1'>M</td><td id='c2'>P</td><td id='c3'>Q</td></tr>
                  <tr><td id='wide3' colspan='3'>a long run spanning the first three columns here</td><td>Q</td></tr>
                  <tr><td>N</td><td id='wide2' colspan='2'>a long run spanning the middle two</td><td>Q</td></tr>
                </table>
                """), margin: 36);

            foreach (var id in new[] { "wide3", "wide2" })
            {
                var cell = FindById(root, id)!;
                var widest = Descendants(cell).SelectMany(b => b.Words)
                    .Select(w => w.Right).DefaultIfEmpty(0).Max();

                Assert.True(cell.ActualRight >= widest - 0.5,
                    $"'{id}' spans columns too narrow for its own content: cell right {cell.ActualRight}, content {widest}");
            }
        }

        /// <summary>
        /// The laid-out <c>Location.X</c> of the cells marked <c>c0</c>/<c>c1</c>/<c>c2</c> — the
        /// column boundaries the intrinsic widths decide.
        /// </summary>
        private static async Task<double[]> ColumnStartsAsync(string rows)
        {
            var (root, _) = await LayoutAsync(Wrap($@"
                <table style='border-collapse:collapse; table-layout:auto; font-size:10pt;'>
                  {rows}
                </table>"), margin: 36);

            return new[] { "c0", "c1", "c2" }
                .Select(id => FindById(root, id)!.Location.X)
                .ToArray();
        }
    }
}
