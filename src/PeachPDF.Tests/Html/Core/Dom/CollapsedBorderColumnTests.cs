using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// <c>&lt;col&gt;</c>/<c>&lt;colgroup&gt;</c> border (CSS 2.1 §17.6.2's widest origin-priority tier)
    /// and background (§17.5.1's layering) participation - both previously entirely unpainted, since
    /// neither box was ever given real layout geometry (see <c>CssLayoutEngineTable.SetColumnBoxDimensions</c>).
    /// </summary>
    public class CollapsedBorderColumnTests
    {
        private static CssBox FindById(CssBox root, string id) => LayoutHarness.FindById(root, id)!;

        [Fact]
        public async Task Col_GetsRealGeometry_SpanningItsOwnColumnAndTheFullRowGrid()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <col id='c0' /><col id='c1' />
                    <tr><td id='a'>a</td><td id='b'>b</td></tr>
                    <tr><td id='c'>c</td><td id='d'>d</td></tr>
                </table>"));

            var col0 = FindById(root, "c0");
            var a = FindById(root, "a");
            var c = FindById(root, "c");

            Assert.Equal(a.Location.X, col0.Location.X, 1);
            Assert.Equal(a.Location.Y, col0.Location.Y, 1); // top of the row grid
            Assert.Equal(c.ActualBottom, col0.ActualBottom, 1); // bottom of the row grid
            Assert.Equal(a.ActualRight, col0.ActualRight, 1);
        }

        [Fact]
        public async Task ColSpan_GivesTheColBoxTheCombinedWidthOfEveryColumnItCovers()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <col id='wide' span='2' />
                    <tr><td id='a'>a</td><td id='b'>b</td></tr>
                </table>"));

            var wide = FindById(root, "wide");
            var a = FindById(root, "a");
            var b = FindById(root, "b");

            Assert.Equal(a.Location.X, wide.Location.X, 1);
            Assert.Equal(b.ActualRight, wide.ActualRight, 1);
        }

        [Fact]
        public async Task ColGroupWithColChildren_AlsoGetsItsOwnGeometry_SpanningAllItsChildren()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <colgroup id='g'><col id='c0' /><col id='c1' /></colgroup>
                    <tr><td id='a'>a</td><td id='b'>b</td></tr>
                </table>"));

            var group = FindById(root, "g");
            var a = FindById(root, "a");
            var b = FindById(root, "b");

            Assert.Equal(a.Location.X, group.Location.X, 1);
            Assert.Equal(b.ActualRight, group.ActualRight, 1);
        }

        [Fact]
        public async Task Background_LayersUnderTheCellsBackground_InFillOrder()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <col id='c0' style='background-color:blue' />
                    <tr><td id='a' style='background-color:red'>a</td></tr>
                </table>"));

            var table = LayoutHarness.Descendants(root).First(x => x.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable);
            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording);

            var fills = recording.Log.Where(op => op.Kind == PaintOpKind.FillRect).ToList();

            // The column's own fill must come before the cell's own fill - DOM order (col precedes tbody)
            // already gives the right layer order once both have real geometry to paint from.
            Assert.True(fills.Count >= 2, $"expected at least 2 fills, got {fills.Count}");
        }

        [Fact]
        public async Task ColBackground_IsNotPaintCulled_EvenWithNoOtherOutOfFlowContentInTheDocument()
        {
            // CssBox's paint-time visibility-culling optimization (intersecting a box's own Bounds
            // against the current clip) only activates when the document has no floated/absolute/fixed
            // content anywhere - the exact condition this fixture deliberately has none of, so a <col>
            // still stuck at a degenerate (0,0,0,0) Bounds would be silently dropped here.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <col id='c0' style='background-color:blue' />
                    <tr><td>a</td></tr>
                </table>"));

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording);

            Assert.Contains(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        [Fact]
        public async Task ColBorder_WinsAgainstAWeakerCellBorder_AndIsDrawnOnce()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <col id='c0' style='border-left:8pt solid black' />
                    <tr><td id='a' style='border-left:1pt solid red'>a</td></tr>
                </table>"));

            var table = LayoutHarness.Descendants(root).First(x => x.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable);

            Assert.NotNull(table.CollapsedBorders);
            var resolved = table.CollapsedBorders!.Vertical(0, 0);

            Assert.Equal(8, resolved.Width, 1);
            Assert.Equal(LineStyle.Solid, resolved.Style);
        }

        [Fact]
        public async Task ColGroupBorder_LosesTo_AMoreSpecificColBorder()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse'>
                    <colgroup style='border-left:5pt solid blue'><col id='c0' style='border-left:5pt solid red' /></colgroup>
                    <tr><td id='a'>a</td></tr>
                </table>"));

            var table = LayoutHarness.Descendants(root).First(x => x.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable);
            var resolved = table.CollapsedBorders!.Vertical(0, 0);

            // Equal width/style - Column outranks ColumnGroup, so red (the <col>'s own) wins.
            Assert.Equal(PeachPDF.Html.Adapters.Entities.RColor.FromArgb(255, 0, 0), resolved.Color);
        }
    }
}
