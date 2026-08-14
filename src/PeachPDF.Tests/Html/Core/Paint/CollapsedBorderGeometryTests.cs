using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Paint
{
    /// <summary>
    /// <see cref="CssBox.CollapsedBorderSegments"/>' exact geometry at an interior grid line - distinct
    /// from <see cref="CollapsedBorderPaintTests"/>, which only checks paint <i>order</i>. An interior
    /// line's two neighbors deliberately overlap by the whole resolved border width (not just meet at a
    /// point - see <see href="https://github.com/jhaygood86/PeachPDF/issues/735">issue #735</see>'s own
    /// notes), so the border segment painted over that overlap must cover the <i>whole</i> band, not just
    /// the half nearer one side - a segment centered on the wrong point still "draws a border" and still
    /// passes a paint-order check, but leaves a sliver of the overlap uncovered where the later-painted
    /// neighbor's background shows through past the border line
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/744">issue #744</see>).
    /// </summary>
    public class CollapsedBorderGeometryTests
    {
        private static CssBox FindTable(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable);

        [Fact]
        public async Task Issue744Repro_InteriorHorizontalBorderCoversFullOverlapBand()
        {
            var html = LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse;width:100%'>
                    <tr style='border-bottom:solid #000 1pt'><td style='border-bottom:solid #000 1pt;background-color:red'>!</td><td style='border-bottom:solid #000 1pt'>Missing Emergency Contact</td></tr>
                    <tr style='border-bottom:solid #000 1pt'><td style='border-bottom:solid #000 1pt;background-color:#ffff00'>!</td><td style='border-bottom:solid #000 1pt'>Missing Emergency Contact</td></tr>
                </table>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var table = FindTable(root);

            var rows = table.Boxes.Where(b => b.DerivedStyle.ActualDisplay == Keywords.TableRow).ToList();
            Assert.Equal(2, rows.Count);
            var rowAbove = rows[0];
            var rowBelow = rows[1];

            Assert.NotNull(table.CollapsedBorderSegments);

            // The interior line sits between the two rows' own edges - find it by proximity to their
            // midpoint rather than assuming index order, since the table's own outer top/bottom edges
            // also emit horizontal segments.
            var midpoint = (rowAbove.ActualBottom + rowBelow.Location.Y) / 2;
            var interior = table.CollapsedBorderSegments!
                .Where(s => s.IsHorizontal)
                .OrderBy(s => Math.Abs(s.Rect.Y + s.Rect.Height / 2 - midpoint))
                .First();

            // Ground truth, independent of the segment itself: the overlap band is exactly
            // [rowBelow.Location.Y, rowAbove.ActualBottom] (row-below's top to row-above's bottom) - the
            // segment must cover that whole band, not just the half nearer one row.
            Assert.Equal(rowBelow.Location.Y, interior.Rect.Y, 3);
            Assert.Equal(rowAbove.ActualBottom, interior.Rect.Y + interior.Rect.Height, 3);
        }

        [Fact]
        public async Task Issue744Repro_InteriorVerticalBorderCoversFullOverlapBand()
        {
            var html = LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse;width:100%'>
                    <tr><td style='border-right:solid #000 1pt;background-color:red'>A</td><td style='border-left:solid #000 1pt;background-color:#ffff00'>B</td></tr>
                    <tr><td style='border-right:solid #000 1pt'>C</td><td style='border-left:solid #000 1pt'>D</td></tr>
                </table>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var table = FindTable(root);

            var firstRowCells = table.Boxes
                .First(b => b.DerivedStyle.ActualDisplay == Keywords.TableRow).Boxes
                .Where(b => b.DerivedStyle.ActualDisplay == Keywords.TableCell)
                .ToList();
            Assert.Equal(2, firstRowCells.Count);
            var leftCell = firstRowCells[0];
            var rightCell = firstRowCells[1];

            Assert.NotNull(table.CollapsedBorderSegments);

            var midpoint = (leftCell.ActualRight + rightCell.Location.X) / 2;
            var interior = table.CollapsedBorderSegments!
                .Where(s => !s.IsHorizontal)
                .OrderBy(s => Math.Abs(s.Rect.X + s.Rect.Width / 2 - midpoint))
                .First();

            // Ground truth: the overlap band is exactly [rightCell.Location.X, leftCell.ActualRight].
            Assert.Equal(rightCell.Location.X, interior.Rect.X, 3);
            Assert.Equal(leftCell.ActualRight, interior.Rect.X + interior.Rect.Width, 3);
        }

        [Fact]
        public async Task Issue744Repro_RepeatedTheadBoundaryCoversFullOverlapBand()
        {
            // A <thead> always goes through a CssProxyBox, even when it appears (and repeats) exactly
            // once - deliberately kept to a single page here (a handful of rows, default page size) so
            // there is exactly one proxy and one boundary segment, with no cross-page ambiguity about
            // which of the source box's accumulated CollapsedBorderSegments belongs to which page.
            var html = LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse;width:100%'>
                    <thead><tr><th style='border-bottom:solid #000 1pt'>Header</th></tr></thead>
                    <tbody>
                        <tr><td style='border-bottom:solid #000 1pt;background-color:#ffff00'>Row 1</td></tr>
                        <tr><td style='border-bottom:solid #000 1pt'>Row 2</td></tr>
                    </tbody>
                </table>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var table = FindTable(root);

            var headerProxy = table.Boxes.OfType<CssProxyBox>()
                .First(p => p.Display.Value == DisplayMode.TableHeaderGroup);
            var segments = headerProxy.SourceBox.CollapsedBorderSegments;
            Assert.NotNull(segments);

            // The header's own boundary-to-body line is its own last (largest-Y) horizontal segment - its
            // only other horizontal segment is its own outer top edge, which has a smaller Y.
            var boundary = segments!.Where(s => s.IsHorizontal).OrderByDescending(s => s.Rect.Y).First();
            var proxyBottom = headerProxy.ActualBottom;

            Assert.Equal(proxyBottom - boundary.Width, boundary.Rect.Y, 3);
            Assert.Equal(proxyBottom, boundary.Rect.Y + boundary.Rect.Height, 3);
        }

        [Fact]
        public async Task Issue744Repro_TheadImmediatelyMeetsTfootWithNoBodyRows_BoundaryCoversFullOverlapBand()
        {
            // No <tbody> row exists on either side of the thead/tfoot seam, so EmitHeaderFooterBorderSegments
            // falls back to SnapshotLineY(boundaryLine) instead of ResolveRepeatedGroupBoundary - a distinct
            // code path from the other two geometry tests above, which both go through an adjacent body row.
            // The seam is still genuinely interior to the whole table (a real row - the footer's - sits on
            // its other side), so it still needs the overlap-band-to-center correction.
            var html = LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse;width:100%'>
                    <thead><tr><th style='border-bottom:solid #000 1pt'>Header</th></tr></thead>
                    <tfoot><tr><td style='border-top:solid #000 1pt'>Footer</td></tr></tfoot>
                </table>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var table = FindTable(root);

            var headerProxy = table.Boxes.OfType<CssProxyBox>()
                .First(p => p.Display.Value == DisplayMode.TableHeaderGroup);
            var footerProxy = table.Boxes.OfType<CssProxyBox>()
                .First(p => p.Display.Value == DisplayMode.TableFooterGroup);

            var segments = headerProxy.SourceBox.CollapsedBorderSegments;
            Assert.NotNull(segments);

            var boundary = segments!.Where(s => s.IsHorizontal).OrderByDescending(s => s.Rect.Y).First();
            var proxyBottom = headerProxy.ActualBottom;

            // Ground truth, independent of the segment itself: header and footer overlap by the full
            // resolved border width here too (the same border-collapse convention as two adjacent body
            // rows - see GetGridLineY's remarks), so the footer's own top names the overlap band's other
            // edge, not a point coincident with the header's own bottom.
            Assert.Equal(footerProxy.Location.Y, boundary.Rect.Y, 3);
            Assert.Equal(proxyBottom, boundary.Rect.Y + boundary.Rect.Height, 3);
        }

        [Fact]
        public async Task Issue744Repro_RepeatedTheadVerticalDividerMeetsBoundaryAtSameLine()
        {
            // A column divider spanning the header's full row range ends at the same grid line the
            // horizontal boundary-to-body segment sits on - both must agree on that line's exact
            // center, or the divider visibly overshoots/undershoots the horizontal border by half its
            // width at the corner. Both read the boundary line through SnapshotLineY, which has to
            // apply its correction consistently regardless of which loop calls it.
            var html = LayoutHarness.Wrap(@"
                <table style='border-collapse:collapse;width:100%'>
                    <thead><tr>
                        <th style='border-bottom:solid #000 1pt;border-right:solid #000 1pt'>A</th>
                        <th style='border-bottom:solid #000 1pt;border-left:solid #000 1pt'>B</th>
                    </tr></thead>
                    <tbody>
                        <tr>
                            <td style='border-bottom:solid #000 1pt;border-right:solid #000 1pt'>1</td>
                            <td style='border-bottom:solid #000 1pt;border-left:solid #000 1pt'>2</td>
                        </tr>
                        <tr>
                            <td style='border-right:solid #000 1pt'>3</td>
                            <td style='border-left:solid #000 1pt'>4</td>
                        </tr>
                    </tbody>
                </table>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var table = FindTable(root);

            var headerProxy = table.Boxes.OfType<CssProxyBox>()
                .First(p => p.Display.Value == DisplayMode.TableHeaderGroup);
            var segments = headerProxy.SourceBox.CollapsedBorderSegments;
            Assert.NotNull(segments);

            var proxyBottom = headerProxy.ActualBottom;

            // Ground truth, fully independent of any border-segment computation: the overlap band's
            // bottom is the header proxy's own ActualBottom, and its top is the first body row's own
            // Location.Y (set by the ordinary row cursor in LayoutBodyRows, which reaches this row via
            // "cursor.CurrentY += headerRoom" - an entirely separate code path from
            // EmitHeaderFooterBorderSegments). The true center is the midpoint of those two real,
            // independently-computed layout values - comparing the vertical segment's endpoint to the
            // horizontal boundary segment's own (Rect.Y + Rect.Height/2) instead would be circular, since
            // both are built from the same correction and would still agree even if that correction were
            // wrong.
            var firstBodyRow = LayoutHarness.Descendants(root)
                .First(b => b.DerivedStyle.ActualDisplay == Keywords.TableRow);
            var trueBoundaryCenterY = (proxyBottom + firstBodyRow.Location.Y) / 2;

            // Every vertical segment in the header spans the same single row, so each one's bottom
            // reaches the boundary line - all of them must land exactly there.
            var verticalSegments = segments!.Where(s => !s.IsHorizontal).ToList();
            Assert.NotEmpty(verticalSegments);
            foreach (var vertical in verticalSegments)
            {
                Assert.Equal(trueBoundaryCenterY, vertical.Rect.Y + vertical.Rect.Height, 3);
            }
        }
    }
}
