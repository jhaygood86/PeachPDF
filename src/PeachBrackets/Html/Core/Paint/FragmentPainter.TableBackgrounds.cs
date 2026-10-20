using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System.Collections.Generic;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// Table backgrounds. <see href="https://www.w3.org/TR/CSS21/tables.html#table-layers">CSS 2.1 §17.5.1</see> puts the
    /// column groups, columns, row groups and rows on layers beneath every cell, and has each one's background cover
    /// "exactly the full area of all cells that originate in" it - so a row's background covers a cell spanning down from
    /// it over all the rows it spans, never a cell spanning into it from an earlier row, and never the
    /// <c>border-spacing</c> between cells, which is the table's. css-tables-3 §5.3.2 says the same by having each cell
    /// draw them.
    /// </summary>
    /// <remarks>
    /// So that is how they are painted here: a track does not paint its own background; each cell paints the backgrounds
    /// of the column group, column, row group and row it originates in, in that order, just before its own - each
    /// positioned against the track's own rectangle on this page and clipped to the cell. Painting the track's whole
    /// rectangle in tree order instead let a later row's background cover a cell spanning into it from an earlier row.
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        /// <summary>The fragments of this page's table tracks, by box: built the first time a cell needs one.</summary>
        private Dictionary<CssBox, List<BoxFragment>>? _trackFragments;

        /// <summary>
        /// Whether <paramref name="box"/> is a table track - a row, row group, column or column group - whose background
        /// its cells paint rather than the track itself.
        /// </summary>
        internal static bool IsTableTrack(CssBox box) =>
            box.DerivedStyle.ActualDisplay is Keywords.TableRow or Keywords.TableRowGroup or Keywords.TableHeaderGroup
                or Keywords.TableFooterGroup or Keywords.TableColumn or Keywords.TableColumnGroup;

        /// <summary>
        /// Paints, beneath <paramref name="cell"/>'s own background, the backgrounds of the tracks it originates in: its
        /// column group, column, row group and row, in that order. Does nothing for a box that is not a table cell.
        /// </summary>
        /// <param name="g">the canvas</param>
        /// <param name="cell">the box whose background is about to be painted</param>
        /// <param name="cellGeometry">where that background goes on this page</param>
        private void PaintTrackBackgroundsBehind(Canvas g, CssBox cell, in BoxDecorationGeometry cellGeometry)
        {
            if (cell is CssSpacingBox || cell.DerivedStyle.ActualDisplay != Keywords.TableCell) return;

            // The row a cell originates in is its parent in the box tree, wherever a fragment of the cell is reached from.
            var row = cell.ParentBox is { DerivedStyle.ActualDisplay: Keywords.TableRow } parent ? parent : null;
            var rowGroup = row?.ParentBox is { DerivedStyle.ActualDisplay: Keywords.TableRowGroup or Keywords.TableHeaderGroup or Keywords.TableFooterGroup } group
                ? group
                : null;

            CssBox? column = null;
            CssBox? columnGroup = null;

            if (TableOf(row ?? cell) is { TableGrid: { } grid } && grid.TryGetSpan(cell, out var span))
            {
                column = grid.ColumnAt(span.Column);
                columnGroup = grid.ColumnGroupAt(span.Column);
            }

            PaintTrackBackground(g, columnGroup, cellGeometry);
            PaintTrackBackground(g, column, cellGeometry);
            PaintTrackBackground(g, rowGroup, cellGeometry);
            PaintTrackBackground(g, row, cellGeometry);
        }

        /// <summary>
        /// Paints <paramref name="track"/>'s background over the cell <paramref name="cellGeometry"/> describes, its images
        /// positioned against the track's own rectangle on this page - or against the cell's, where the track has no
        /// fragment here. §17.5.1: "this difference in area does not affect background image positioning".
        /// </summary>
        private void PaintTrackBackground(Canvas g, CssBox? track, in BoxDecorationGeometry cellGeometry)
        {
            if (track is null || track.Visibility.Value != Visibility.Visible
                || (!RenderUtils.IsColorVisible(track.ActualBackgroundColor) && track.BackgroundImages is not { Count: > 0 }))
            {
                return;
            }

            var cellArea = cellGeometry.DecorationRect;
            var trackGeometry = TrackFragmentOver(track, cellArea) is { Lines: [var line, ..] }
                ? BoxDecorationGeometry.For(track, line)
                : BoxDecorationGeometry.Unbroken(cellArea);

            if (container.SnapBoxDecorationsToCssPixels)
                trackGeometry = DecorationPixelSnapping.Snap(g, trackGeometry);

            // The cell's area is where it paints, even the part beyond the track - a row's background runs on down a cell
            // spanning several rows - so the track's own break clip is not pushed: it would cut the cell back to the
            // track's strip. The track's rectangle only positions its images.
            PaintBackground(g, track, trackGeometry, paintingArea: cellArea);
        }

        /// <summary>
        /// The fragment of <paramref name="track"/> on this page that overlaps <paramref name="cellArea"/> - a table split
        /// across columns has one per column - or null where none does.
        /// </summary>
        private BoxFragment? TrackFragmentOver(CssBox track, Rect cellArea)
        {
            // A footnote, running element or margin box is painted from its own detached tree.
            if ((_detachedRoot ?? _pageRoot) is not { } root) return null;

            if (_trackFragments is null)
            {
                _trackFragments = new(ReferenceEqualityComparer.Instance);
                CollectTrackFragments(root, _trackFragments);
            }

            if (!_trackFragments.TryGetValue(track, out var fragments)) return null;

            foreach (var fragment in fragments)
            {
                if (Rect.Intersect(fragment.Rect, cellArea) is { Width: > 0, Height: > 0 }) return fragment;
            }

            return null;
        }

        private static void CollectTrackFragments(BoxFragment fragment, Dictionary<CssBox, List<BoxFragment>> into)
        {
            if (IsTableTrack(fragment.Box))
            {
                if (!into.TryGetValue(fragment.Box, out var fragments)) into[fragment.Box] = fragments = [];
                fragments.Add(fragment);
            }

            foreach (var child in fragment.Children)
                CollectTrackFragments(child, into);
        }

        /// <summary>
        /// Whether <paramref name="box"/> is an empty cell that <c>empty-cells: hide</c> leaves unpainted - its own background
        /// and borders and the tracks' beneath it. The property applies to the separated borders model only
        /// (<see href="https://www.w3.org/TR/CSS21/tables.html#empty-cells">CSS 2.1 §17.6.1.1</see>): a collapsed table paints
        /// its empty cells.
        /// </summary>
        private static bool HidesAsAnEmptyCell(CssBox box) =>
            box.DerivedStyle.ActualDisplay == Keywords.TableCell && box.EmptyCells.Value == EmptyCellsMode.Hide && box.IsSpaceOrEmpty
            && TableOf(box) is not { BorderCollapse.Value: BorderCollapseMode.Collapse };

        /// <summary>
        /// The table <paramref name="box"/> lies in, or null. Walks <see cref="CssBox.EffectiveParentBox"/>: a repeated
        /// <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> is detached from the table's children for layout, and keeps the table only
        /// as its DOM parent.
        /// </summary>
        private static CssBox? TableOf(CssBox box)
        {
            for (var current = box.EffectiveParentBox; current is not null; current = current.EffectiveParentBox)
            {
                if (current.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable) return current;
            }

            return null;
        }
    }
}
