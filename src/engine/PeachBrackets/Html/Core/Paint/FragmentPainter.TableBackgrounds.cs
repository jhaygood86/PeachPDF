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
    /// <c>border-spacing</c> between cells, which is the table's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// So a track paints its background where it always did in the tree - a column before the row groups, a row group before
    /// its rows, a row before its cells, which is §17.5.1's layer order - but only over the cells that originate in it, each
    /// positioned against the track's own rectangle on this page. The areas of different rows' (or columns') cells never
    /// overlap, so tree order cannot let a later track cover an earlier track's cell. Painting the track's whole rectangle
    /// instead let a later row's background cover a cell spanning into it from an earlier row.
    /// </para>
    /// <para>
    /// The track paints, not the cell (css-tables-3 §5.3.2 describes it as the cell drawing its tracks' backgrounds, with the
    /// same result), so a track's background sits under the effects of the track and its ancestors only: a cell's
    /// <c>opacity</c>, <c>filter</c> or <c>transform</c> does not fade or move its row's background, and a row's does not
    /// reach its column's, as in a browser. A cell paints a track's background beneath its own only where the track has no
    /// fragment over the cell on this page - a <c>rowspan</c> cell continuing onto a page its own row does not reach - or
    /// where a <c>visibility: hidden</c> column, which paints nothing itself, still has a background (CSS 2.1 §17.3 gives a
    /// column's <c>visibility</c> a meaning for <c>collapse</c> only).
    /// </para>
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        /// <summary>Who paints which table track background on this page: built the first time a table box needs it.</summary>
        private TrackBackgrounds? _trackBackgrounds;

        /// <summary>
        /// The table track backgrounds of one fragment tree: the cell areas each track fragment paints its background over,
        /// and the tracks a cell paints beneath its own instead.
        /// </summary>
        private sealed class TrackBackgrounds(BoxFragment root)
        {
            public BoxFragment Root { get; } = root;

            public Dictionary<BoxFragment, List<Rect>> CellAreasByTrack { get; } = new(ReferenceEqualityComparer.Instance);

            public Dictionary<BoxFragment, List<(CssBox Track, BoxFragment? TrackFragment, Rect CellArea)>> TracksByCell { get; } =
                new(ReferenceEqualityComparer.Instance);
        }

        /// <summary>
        /// Whether <paramref name="box"/> is a table track - a row, row group, column or column group - whose background covers
        /// only the cells that originate in it.
        /// </summary>
        internal static bool IsTableTrack(CssBox box) =>
            box.DerivedStyle.ActualDisplay is Keywords.TableRow or Keywords.TableRowGroup or Keywords.TableHeaderGroup
                or Keywords.TableFooterGroup or Keywords.TableColumn or Keywords.TableColumnGroup;

        /// <summary>
        /// Paints the background of the track <paramref name="fragment"/> belongs to over the cells that originate in the track
        /// and lie under this fragment, positioned against <paramref name="trackGeometry"/>.
        /// </summary>
        /// <param name="g">the canvas</param>
        /// <param name="fragment">a fragment of a table track</param>
        /// <param name="trackGeometry">the fragment's own decoration geometry: what positions the background's images</param>
        private void PaintTrackBackgroundOverItsCells(Canvas g, BoxFragment fragment, in BoxDecorationGeometry trackGeometry)
        {
            if (TrackBackgroundsFor(g) is not { } backgrounds || !backgrounds.CellAreasByTrack.TryGetValue(fragment, out var cellAreas))
                return;

            // The cell's area is where it paints, even the part beyond the track - a row's background runs on down a cell
            // spanning several rows - so the track's own break clip is not pushed: it would cut the cell back to the track's
            // strip. The track's rectangle only positions its images.
            foreach (var cellArea in cellAreas)
                PaintBackground(g, fragment.Box, trackGeometry, paintingArea: cellArea);
        }

        /// <summary>
        /// Paints, beneath the cell <paramref name="fragment"/>'s own background, the backgrounds of the tracks it originates in
        /// that no track fragment on this page paints over it (see the class remarks), column group first and row last.
        /// </summary>
        private void PaintTrackBackgroundsBeneathCell(Canvas g, BoxFragment fragment)
        {
            if (TrackBackgroundsFor(g) is not { } backgrounds || !backgrounds.TracksByCell.TryGetValue(fragment, out var tracks))
                return;

            foreach (var (track, trackFragment, cellArea) in tracks)
            {
                var trackGeometry = trackFragment is { Lines: [var line, ..] }
                    ? BoxDecorationGeometry.For(track, line)
                    : BoxDecorationGeometry.Unbroken(cellArea);

                // Snapped as the track's own paint would be. Only an image or gradient positioned against a hidden column's
                // rectangle could show it, by half a CSS pixel at most; no test reaches that.
                if (container.SnapBoxDecorationsToCssPixels)
                    trackGeometry = DecorationPixelSnapping.Snap(g, trackGeometry);

                PaintBackground(g, track, trackGeometry, paintingArea: cellArea);
            }
        }

        /// <summary>The track backgrounds of the tree being painted, or null where there is none.</summary>
        private TrackBackgrounds? TrackBackgroundsFor(Canvas g)
        {
            // A footnote, running element or margin box is painted from its own detached tree.
            if ((_detachedRoot ?? _pageRoot) is not { } root) return null;

            if (!ReferenceEquals(_trackBackgrounds?.Root, root)) _trackBackgrounds = CollectTrackBackgrounds(g, root);

            return _trackBackgrounds;
        }

        private TrackBackgrounds CollectTrackBackgrounds(Canvas g, BoxFragment root)
        {
            var result = new TrackBackgrounds(root);
            var trackFragments = new Dictionary<CssBox, List<BoxFragment>>(ReferenceEqualityComparer.Instance);
            List<BoxFragment> cells = [];
            Collect(root);

            // A rowspan cell reached through more than one row on this page lands at one place each time: its tracks paint
            // under it once, or a translucent one would darken with every copy.
            HashSet<(CssBox Cell, Rect Area)> seen = [];

            foreach (var cell in cells)
            {
                var (columnGroup, column, rowGroup, row) = TracksOf(cell.Box);

                foreach (var line in cell.Lines)
                {
                    var geometry = BoxDecorationGeometry.For(cell.Box, line);
                    if (container.SnapBoxDecorationsToCssPixels) geometry = DecorationPixelSnapping.Snap(g, geometry);

                    // Where the cell is decorated as one unbroken box cut at a break, only its slice here is its area - the clip its
                    // own background is painted in. No tested layout gives a cell a slice narrower than its rectangle on the page.
                    var area = geometry.NeedsClip ? Rect.Intersect(geometry.DecorationRect, geometry.ClipRect) : geometry.DecorationRect;
                    if (area is not { Width: > 0, Height: > 0 } || !seen.Add((cell.Box, area))) continue;

                    // Once one track is painted by the cell, so is every track layered above it: the cell paints them after the
                    // track fragments have painted, so one painted by its own fragment would land beneath one painted by the cell.
                    var beneathCell = false;
                    Assign(cell, columnGroup, area, ref beneathCell);
                    Assign(cell, column, area, ref beneathCell);
                    Assign(cell, rowGroup, area, ref beneathCell);
                    Assign(cell, row, area, ref beneathCell);
                }
            }

            return result;

            void Collect(BoxFragment fragment)
            {
                var box = fragment.Box;

                if (IsTableTrack(box))
                {
                    if (!trackFragments.TryGetValue(box, out var fragments)) trackFragments[box] = fragments = [];
                    fragments.Add(fragment);
                }
                // A CssSpacingBox is the placeholder a spanning cell leaves in a later row: the cell is reached through it, and it
                // has no area of its own, so leaving it out changes nothing but keeps a later row from being asked to paint there.
                else if (box is not CssSpacingBox && box.DerivedStyle.ActualDisplay == Keywords.TableCell
                         && box.Visibility.Value == Visibility.Visible && !HidesAsAnEmptyCell(box))
                {
                    cells.Add(fragment);
                }

                foreach (var child in fragment.Children)
                    Collect(child);
            }

            void Assign(BoxFragment cell, CssBox? track, Rect area, ref bool beneathCell)
            {
                if (track is null || !HasBackground(track)) return;

                // A table split across columns has one fragment of a track per column: the one over the cell, on both axes. Only
                // a fragment with a rectangle of its own paints: a row whose cell continues onto a page the row itself does not
                // reach still has a fragment there, holding the cell and nothing of its own.
                BoxFragment? over = null;
                if (trackFragments.TryGetValue(track, out var fragments))
                {
                    foreach (var fragment in fragments)
                    {
                        if (fragment.Lines is [var line, ..] && Rect.Intersect(line.Rect, area) is { Width: > 0, Height: > 0 })
                        {
                            over = fragment;
                            break;
                        }
                    }
                }

                // A track that is not visible paints nothing itself, so its cells paint its background. That matters for a
                // column only: CSS 2.1 §17.3 gives a column's visibility a meaning for collapse alone, and css-tables-3 §5.4.2
                // keeps a collapsed track's background on the cells it does not collapse. A hidden row's cells are never painted -
                // PaintFragment skips its whole subtree - so neither is what is handed to them.
                if (over is not null && track.Visibility.Value == Visibility.Visible && !beneathCell)
                {
                    if (!result.CellAreasByTrack.TryGetValue(over, out var areas)) result.CellAreasByTrack[over] = areas = [];
                    areas.Add(area);
                }
                else
                {
                    if (!result.TracksByCell.TryGetValue(cell, out var tracks)) result.TracksByCell[cell] = tracks = [];
                    tracks.Add((track, over, area));
                    beneathCell = true;
                }
            }
        }

        private static bool HasBackground(CssBox track) =>
            RenderUtils.IsColorVisible(track.ActualBackgroundColor) || track.BackgroundImages is { Count: > 0 };

        /// <summary>The column group, column, row group and row <paramref name="cell"/> originates in, where it has them.</summary>
        private static (CssBox? ColumnGroup, CssBox? Column, CssBox? RowGroup, CssBox? Row) TracksOf(CssBox cell)
        {
            // The row a cell originates in is its parent in the box tree, wherever a fragment of the cell is reached from.
            var row = cell.ParentBox is { DerivedStyle.ActualDisplay: Keywords.TableRow } parent ? parent : null;
            var rowGroup = row?.ParentBox is { DerivedStyle.ActualDisplay: Keywords.TableRowGroup or Keywords.TableHeaderGroup or Keywords.TableFooterGroup } group
                ? group
                : null;

            if (TableOf(row ?? cell) is { TableGrid: { } grid } && grid.TryGetSpan(cell, out var span))
                return (grid.ColumnGroupAt(span.Column), grid.ColumnAt(span.Column), rowGroup, row);

            return (null, null, rowGroup, row);
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
