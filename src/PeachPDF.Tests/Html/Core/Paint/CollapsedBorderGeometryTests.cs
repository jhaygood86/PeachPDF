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
    /// from <see cref="CollapsedBorderPaintTests"/>, which only checks paint <i>order</i>. Two neighbors
    /// on either side of an interior line meet exactly flush (each already carries its own half of the
    /// resolved border width as its own used border, per <see cref="CssLayoutEngineTable.ApplyCollapsedUsedBorderWidths"/> -
    /// see <see href="https://github.com/jhaygood86/PeachPDF/issues/1138">issue #1138</see>, which fixed
    /// an earlier version of this file's own doc comment that had it overlapping by the whole width
    /// instead), so the border segment painted at that line must be centered exactly on the single point
    /// they coincide at, reaching half its own width to each side - a segment centered on the wrong point
    /// still "draws a border" and still passes a paint-order check, but leaves a sliver of the shared
    /// border uncovered where the neighbor's background shows through past it
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/744">issue #744</see>, which this file
    /// was originally written for).
    /// </summary>
    public class CollapsedBorderGeometryTests
    {
        private static CssBox FindTable(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable);

        [Fact]
        public async Task Issue744Repro_InteriorHorizontalBorderIsCenteredOnTheSharedBoundary()
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

            // Ground truth, independent of the segment itself: rowAbove/rowBelow meet exactly flush
            // (issue #1138), so the shared boundary is the single point they coincide at - the segment
            // must be centered on it, reaching half its own thickness to each side.
            Assert.Equal(rowAbove.ActualBottom, rowBelow.Location.Y, 3);
            Assert.Equal(rowAbove.ActualBottom - interior.Rect.Height / 2, interior.Rect.Y, 3);
            Assert.Equal(rowAbove.ActualBottom + interior.Rect.Height / 2, interior.Rect.Y + interior.Rect.Height, 3);
        }

        [Fact]
        public async Task Issue744Repro_InteriorVerticalBorderIsCenteredOnTheSharedBoundary()
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

            // Ground truth: leftCell/rightCell meet exactly flush (issue #1138), so the shared boundary
            // is the single point they coincide at.
            Assert.Equal(leftCell.ActualRight, rightCell.Location.X, 3);
            Assert.Equal(leftCell.ActualRight - interior.Rect.Width / 2, interior.Rect.X, 3);
            Assert.Equal(leftCell.ActualRight + interior.Rect.Width / 2, interior.Rect.X + interior.Rect.Width, 3);
        }

        [Fact]
        public async Task Issue744Repro_RepeatedTheadBoundaryIsCenteredOnTheSharedBoundary()
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

            Assert.Equal(proxyBottom - boundary.Width / 2, boundary.Rect.Y, 3);
            Assert.Equal(proxyBottom + boundary.Width / 2, boundary.Rect.Y + boundary.Rect.Height, 3);
        }

        [Fact]
        public async Task Issue744Repro_TheadImmediatelyMeetsTfootWithNoBodyRows_BoundaryIsCenteredOnTheSharedBoundary()
        {
            // No <tbody> row exists on either side of the thead/tfoot seam, so EmitHeaderFooterBorderSegments
            // falls back to SnapshotLine(boundaryLine) instead of ResolveRepeatedGroupBoundary - a distinct
            // code path from the other two geometry tests above, which both go through an adjacent body row.
            // The seam is still genuinely interior to the whole table (a real row - the footer's - sits on
            // its other side), so its reported position still has to agree exactly with the footer's own.
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

            // Ground truth, independent of the segment itself: header and footer meet exactly flush here
            // too (the same border-collapse convention as two adjacent body rows - see GetGridLineY's
            // remarks), so the footer's own top coincides with the header's own bottom, and the segment
            // is centered on that single shared point.
            Assert.Equal(footerProxy.Location.Y, proxyBottom, 3);
            Assert.Equal(proxyBottom - boundary.Width / 2, boundary.Rect.Y, 3);
            Assert.Equal(proxyBottom + boundary.Width / 2, boundary.Rect.Y + boundary.Rect.Height, 3);
        }

        [Fact]
        public async Task ABodyDividerStopsOnTheRepeatedGroupBoundarysCentre_NotHalfItsStaticWidth()
        {
            // A repeated group's boundary-to-body line is resolved per page, against whichever row
            // starts that page - but the body's own dividers are emitted once, from live geometry, so
            // there is no one per-page answer for them to retract by. Retracting by the whole-table,
            // DOM-order resolution instead leaves a hole wherever the two disagree, which is every page
            // but the first: here the static resolution is the first body row's 40pt border-top while
            // every later page's boundary is the header's own 4pt, so a 20pt retraction opened an 18pt
            // gap in every column divider directly under the repeated header. The body run stops on the
            // line's centre and lets the boundary segment cover its own side.
            var rows = string.Concat(Enumerable.Range(0, 3).Select(i =>
                $"<tr><td style='border-top:{(i == 0 ? 40 : 2)}pt solid #000;border-right:6pt solid #000'>r{i}</td>" +
                "<td>x</td></tr>"));

            var html = LayoutHarness.Wrap(
                "<table style='border-collapse:collapse;width:100%'>" +
                "<thead><tr><th style='border:4pt solid #000'>H</th><th style='border:4pt solid #000'>H2</th></tr></thead>" +
                $"<tbody>{rows}</tbody></table>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var table = FindTable(root);

            var headerProxy = table.Boxes.OfType<CssProxyBox>()
                .First(p => p.Display.Value == DisplayMode.TableHeaderGroup);

            // Ground truth for the boundary's own centre, independent of any segment: the proxy's own
            // far edge, which the first body row is flush against (issue #1138).
            var boundaryCenter = headerProxy.ActualBottom;

            Assert.NotNull(table.CollapsedBorderSegments);
            var divider = table.CollapsedBorderSegments!
                .Where(s => !s.IsHorizontal)
                .OrderBy(s => s.Rect.Y)
                .First();

            Assert.Equal(boundaryCenter, divider.Rect.Y, 3);
        }

        [Fact]
        public async Task Issue744Repro_RepeatedTheadVerticalDividerMeetsBoundaryAtSameLine()
        {
            // A column divider spanning the header's full row range ends at the same grid line the
            // horizontal boundary-to-body segment sits on - both must agree on that line's exact
            // position, or the divider visibly overshoots/undershoots the horizontal border at the
            // corner. Both read the boundary line through SnapshotLine, which has to report it
            // consistently regardless of which loop calls it.
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

            // Ground truth, fully independent of any border-segment computation: the header and the
            // first body row meet exactly flush (issue #1138), so the header proxy's own ActualBottom
            // and the first body row's own Location.Y (set by the ordinary row cursor in
            // LayoutBodyRows, which reaches this row via "cursor.CurrentY += headerRoom" - an entirely
            // separate code path from EmitHeaderFooterBorderSegments) are the same point - taking their
            // midpoint (rather than either one alone) keeps this ground truth independent of which of
            // the two real, independently-computed values happens to be read; comparing the vertical
            // segment's endpoint to the horizontal boundary segment's own (Rect.Y + Rect.Height/2)
            // instead would be circular, since both are built from the same code and would still agree
            // even if it were wrong.
            var firstBodyRow = LayoutHarness.Descendants(root)
                .First(b => b.DerivedStyle.ActualDisplay == Keywords.TableRow);
            var trueBoundaryCenterY = (proxyBottom + firstBodyRow.Location.Y) / 2;

            // Every vertical segment in the header spans the same single row, so each one's bottom
            // reaches the boundary line - all of them must land exactly there. The boundary owns the
            // joints where those dividers cross it (it is not the table's block-start line, so the
            // block-axis line wins at equal width and style - see InlineLineOwnsJoint), and a divider
            // that loses a joint stops on the line's centre rather than retracting clear of it, so this
            // is the centre either way.
            var verticalSegments = segments!.Where(s => !s.IsHorizontal).ToList();
            Assert.NotEmpty(verticalSegments);
            foreach (var vertical in verticalSegments)
            {
                Assert.Equal(trueBoundaryCenterY, vertical.Rect.Y + vertical.Rect.Height, 3);
            }
        }
    }
}
