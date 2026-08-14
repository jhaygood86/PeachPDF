using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Paint
{
    /// <summary>
    /// The paint-side half of CSS 2.1 §17.6.2's border-conflict resolution - resolved borders paint once,
    /// after every table-internal background, instead of each participant painting its own border
    /// independently (which is what let a later row's background erase an earlier row's shared border -
    /// <see href="https://github.com/jhaygood86/PeachPDF/issues/735">issue #735</see>).
    /// </summary>
    public class CollapsedBorderPaintTests
    {
        private static CssBox FindTable(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable);

        [Fact]
        public async Task Issue735Repro_BorderDrawsAfterBothAdjacentRowsOwnBackgroundFills()
        {
            // The issue's own shape: two adjacent rows, each cell colored AND carrying the shared
            // border-bottom. Before the fix, each row/cell paints its own border independently in DOM
            // order, so row 2's background fill lands after (and erases) row 1's border. This is the
            // regression gate - it must fail on unfixed code and pass once resolved borders paint last.
            var td = "style='background-color:#C00000;border-bottom:solid #000 1pt'";
            var html = LayoutHarness.Wrap($@"
                <table style='border-collapse:collapse;width:100%'>
                    <tr style='border-bottom:solid #000 1pt'><td {td}>!</td><td style='border-bottom:solid #000 1pt'>Missing Emergency Contact</td></tr>
                    <tr style='border-bottom:solid #000 1pt'><td {td}>!</td><td style='border-bottom:solid #000 1pt'>Missing Emergency Contact</td></tr>
                </table>");

            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var table = FindTable(root);

            var adapter = new PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording);

            // The shared edge between row 1 and row 2 - both rows' own ActualBottom/Location.Y agree on
            // it (that's the whole point of the border-collapse overlap), so either row's geometry names
            // the same Y.
            var firstRow = table.Boxes.First(b => b.DerivedStyle.ActualDisplay == Keywords.TableRow);
            var sharedEdgeY = firstRow.ActualBottom;

            var backgroundFillIndices = recording.Log
                .Select((op, index) => (op, index))
                .Where(t => t.op.Kind == PaintOpKind.FillRect && Overlaps(t.op.Bounds, sharedEdgeY))
                .Select(t => t.index)
                .ToList();

            var borderDrawIndices = recording.Log
                .Select((op, index) => (op, index))
                .Where(t => t.op.Kind is PaintOpKind.Polygon or PaintOpKind.Line && Overlaps(t.op.Bounds, sharedEdgeY))
                .Select(t => t.index)
                .ToList();

            Assert.NotEmpty(backgroundFillIndices);
            Assert.NotEmpty(borderDrawIndices);
            Assert.True(
                borderDrawIndices.Max() > backgroundFillIndices.Max(),
                $"border draw (last at log index {borderDrawIndices.Max()}) must paint after every " +
                $"background fill at the shared edge (last at {backgroundFillIndices.Max()}), or a " +
                "later row's background erases the earlier row's border.");
        }

        private static bool Overlaps(PeachPDF.Html.Adapters.Entities.RRect bounds, double y) =>
            y >= bounds.Y - 1 && y <= bounds.Y + bounds.Height + 1;
    }
}
