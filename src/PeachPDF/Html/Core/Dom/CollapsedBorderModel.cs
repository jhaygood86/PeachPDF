using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The whole table's CSS 2.1 §17.6.2 resolution, one <see cref="CollapsedBorder"/> per unit
    /// grid-line segment - resolving on the unit grid rather than on abstract whole-table lines is what
    /// lets a <c>colspan</c>/<c>rowspan</c> cell produce correctly-differing per-segment results with no
    /// special splitting logic (a horizontal line under a 3-column <c>colspan</c> cell independently
    /// resolves once per column it crosses, because the cell <i>below</i> it can differ per column).
    /// </summary>
    internal sealed class CollapsedBorderModel
    {
        // [line, column] for line in 0..RowCount, column in 0..ColumnCount-1.
        private readonly CollapsedBorder[,] _horizontal;

        // [row, line] for row in 0..RowCount-1, line in 0..ColumnCount.
        private readonly CollapsedBorder[,] _vertical;

        /// <summary>The room horizontal grid line <c>i</c> reserves - the max resolved width across every column it crosses. Length <c>RowCount + 1</c>.</summary>
        internal double[] HorizontalLineWidth { get; }

        /// <summary>The room vertical grid line <c>j</c> reserves - the max resolved width across every row it crosses. Length <c>ColumnCount + 1</c>.</summary>
        internal double[] VerticalLineWidth { get; }

        private CollapsedBorderModel(
            CollapsedBorder[,] horizontal, CollapsedBorder[,] vertical,
            double[] horizontalLineWidth, double[] verticalLineWidth)
        {
            _horizontal = horizontal;
            _vertical = vertical;
            HorizontalLineWidth = horizontalLineWidth;
            VerticalLineWidth = verticalLineWidth;
        }

        /// <summary>The resolved border on horizontal grid line <paramref name="line"/> (0..RowCount), over column <paramref name="column"/>.</summary>
        internal CollapsedBorder Horizontal(int line, int column) => _horizontal[line, column];

        /// <summary>The resolved border on vertical grid line <paramref name="line"/> (0..ColumnCount), over row <paramref name="row"/>.</summary>
        internal CollapsedBorder Vertical(int row, int line) => _vertical[row, line];

        /// <param name="grid">The table's topology.</param>
        /// <param name="tableBox">The table's own box - its border participates at the grid's outer edges.</param>
        /// <param name="isColumnCollapsed">
        /// Whether the given column index is <c>visibility: collapse</c> (CSS 2.1 §17.6.1) - a collapsed
        /// column contributes no candidates of its own and reserves no room, matching
        /// <c>CssLayoutEngineTable.CollapseColumnWidths</c> already zeroing its width.
        /// </param>
        /// <param name="leftToRight">The table's own <c>direction</c>, for the resolver's position tiebreak.</param>
        /// <param name="blockStart">
        /// The table's own resolved block-start physical side (<see cref="Border.Top"/> for
        /// <c>horizontal-tb</c>, <see cref="Border.Right"/>/<see cref="Border.Left"/> for
        /// <c>vertical-rl</c>/<c>vertical-lr</c>) - rows always stack along the block axis per
        /// css-tables-3, so every horizontal grid-line candidate this method collects reads from this
        /// physical side (or <paramref name="blockEnd"/>) rather than a hardcoded <see cref="Border.Top"/>/
        /// <see cref="Border.Bottom"/>. Resolved once by the caller (<c>CssLayoutEngineTable</c>, which
        /// already derives <c>_isVertical</c>/<c>_rowAxisStartIsAtMax</c> from the identical
        /// <c>LogicalPropertyResolver.BlockStart</c> call) rather than re-derived here - this class stays
        /// unaware of <c>WritingMode</c> entirely.
        /// </param>
        /// <param name="blockEnd">The table's own resolved block-end physical side - see <paramref name="blockStart"/>.</param>
        /// <param name="inlineStart">
        /// The table's own resolved inline-start physical side (<see cref="Border.Left"/> for
        /// <c>horizontal-tb</c>, <see cref="Border.Top"/> for both vertical writing modes - this engine has
        /// no <c>direction: rtl</c> column/inline axis) - columns always run along the inline axis, so
        /// every vertical grid-line candidate reads from this side (or <paramref name="inlineEnd"/>)
        /// instead of a hardcoded <see cref="Border.Left"/>/<see cref="Border.Right"/>.
        /// </param>
        /// <param name="inlineEnd">The table's own resolved inline-end physical side - see <paramref name="inlineStart"/>.</param>
        internal static CollapsedBorderModel Resolve(
            TableGrid grid, CssBox tableBox, Func<int, bool> isColumnCollapsed, bool leftToRight,
            Border blockStart, Border blockEnd, Border inlineStart, Border inlineEnd)
        {
            var columnCount = Math.Max(grid.ColumnCount, 1);
            var horizontal = new CollapsedBorder[grid.RowCount + 1, columnCount];
            var vertical = new CollapsedBorder[Math.Max(grid.RowCount, 1), grid.ColumnCount + 1];
            var horizontalLineWidth = new double[grid.RowCount + 1];
            var verticalLineWidth = new double[grid.ColumnCount + 1];

            var candidates = new List<CollapsedBorderCandidate>(8);

            for (var line = 0; line <= grid.RowCount; line++)
            {
                for (var column = 0; column < grid.ColumnCount; column++)
                {
                    if (isColumnCollapsed(column))
                    {
                        horizontal[line, column] = CollapsedBorder.None;
                        continue;
                    }

                    candidates.Clear();
                    CollectHorizontal(grid, tableBox, line, column, candidates, blockStart, blockEnd);
                    var resolved = CollapsedBorderResolver.Resolve(CollectionsMarshal.AsSpan(candidates), leftToRight);

                    horizontal[line, column] = resolved;
                    horizontalLineWidth[line] = Math.Max(horizontalLineWidth[line], resolved.UsedWidth);
                }
            }

            for (var line = 0; line <= grid.ColumnCount; line++)
            {
                var leftCollapsed = line > 0 && isColumnCollapsed(line - 1);
                var rightCollapsed = line < grid.ColumnCount && isColumnCollapsed(line);

                for (var row = 0; row < grid.RowCount; row++)
                {
                    if (leftCollapsed && rightCollapsed)
                    {
                        vertical[row, line] = CollapsedBorder.None;
                        continue;
                    }

                    candidates.Clear();
                    CollectVertical(
                        grid, tableBox, row, line, candidates, skipLeft: leftCollapsed, skipRight: rightCollapsed,
                        inlineStart, inlineEnd);
                    var resolved = CollapsedBorderResolver.Resolve(CollectionsMarshal.AsSpan(candidates), leftToRight);

                    vertical[row, line] = resolved;
                    verticalLineWidth[line] = Math.Max(verticalLineWidth[line], resolved.UsedWidth);
                }
            }

            return new CollapsedBorderModel(horizontal, vertical, horizontalLineWidth, verticalLineWidth);
        }

        private static void CollectHorizontal(
            TableGrid grid, CssBox tableBox, int line, int column, List<CollapsedBorderCandidate> into,
            Border blockStart, Border blockEnd)
        {
            var above = line > 0 ? grid.CellAt(line - 1, column) : null;
            var below = line < grid.RowCount ? grid.CellAt(line, column) : null;

            // Interior to a rowspanning cell - the same cell occupies both sides, so this line is inside
            // its own border box, not on its edge. No candidate from anywhere participates here.
            if (above is not null && ReferenceEquals(above, below)) return;

            // If above/below survived the check above, contiguous spans guarantee this really is that
            // cell's own top/bottom (block-end/block-start) edge (see CollapsedBorderModel's own remarks
            // for why).
            Add(into, above, blockEnd, CollapsedBorderOrigin.Cell, line - 1, column);
            Add(into, below, blockStart, CollapsedBorderOrigin.Cell, line, column);

            if (line > 0) Add(into, grid.RowAt(line - 1), blockEnd, CollapsedBorderOrigin.Row, line - 1, 0);
            if (line < grid.RowCount) Add(into, grid.RowAt(line), blockStart, CollapsedBorderOrigin.Row, line, 0);

            if (line > 0 && grid.IsLastRowOfGroup(line - 1))
                Add(into, grid.RowGroupAt(line - 1), blockEnd, CollapsedBorderOrigin.RowGroup, line - 1, 0);
            if (line < grid.RowCount && grid.IsFirstRowOfGroup(line))
                Add(into, grid.RowGroupAt(line), blockStart, CollapsedBorderOrigin.RowGroup, line, 0);

            // A column/column-group has no per-row block-start/-end boundary of its own - only at the
            // table's very block-start/-end (topologically line 0 / line RowCount - a pure grid-index
            // fact, unaffected by which physical side either actually is) does its own border-block-start/
            // -end compete at all (a column spans every row).
            if (line == 0)
            {
                Add(into, grid.ColumnAt(column), blockStart, CollapsedBorderOrigin.Column, 0, column);
                Add(into, grid.ColumnGroupAt(column), blockStart, CollapsedBorderOrigin.ColumnGroup, 0, column);
                Add(into, tableBox, blockStart, CollapsedBorderOrigin.Table, 0, 0);
            }
            if (line == grid.RowCount)
            {
                Add(into, grid.ColumnAt(column), blockEnd, CollapsedBorderOrigin.Column, 0, column);
                Add(into, grid.ColumnGroupAt(column), blockEnd, CollapsedBorderOrigin.ColumnGroup, 0, column);
                Add(into, tableBox, blockEnd, CollapsedBorderOrigin.Table, 0, 0);
            }
        }

        private static void CollectVertical(
            TableGrid grid, CssBox tableBox, int row, int line, List<CollapsedBorderCandidate> into,
            bool skipLeft, bool skipRight, Border inlineStart, Border inlineEnd)
        {
            var left = !skipLeft && line > 0 ? grid.CellAt(row, line - 1) : null;
            var right = !skipRight && line < grid.ColumnCount ? grid.CellAt(row, line) : null;

            // Interior to a colspanning cell - same reasoning as CollectHorizontal.
            if (left is not null && ReferenceEquals(left, right)) return;

            Add(into, left, inlineEnd, CollapsedBorderOrigin.Cell, row, line - 1);
            Add(into, right, inlineStart, CollapsedBorderOrigin.Cell, row, line);

            if (!skipLeft && line == 0) Add(into, grid.RowAt(row), inlineStart, CollapsedBorderOrigin.Row, row, 0);
            if (!skipRight && line == grid.ColumnCount) Add(into, grid.RowAt(row), inlineEnd, CollapsedBorderOrigin.Row, row, 0);

            if (line == 0)
            {
                Add(into, grid.RowGroupAt(row), inlineStart, CollapsedBorderOrigin.RowGroup, row, 0);
                Add(into, tableBox, inlineStart, CollapsedBorderOrigin.Table, 0, 0);
            }
            if (line == grid.ColumnCount)
            {
                Add(into, grid.RowGroupAt(row), inlineEnd, CollapsedBorderOrigin.RowGroup, row, 0);
                Add(into, tableBox, inlineEnd, CollapsedBorderOrigin.Table, 0, 0);
            }

            // A column/column-group's own border participates at every vertical line its own boundary
            // falls on - unlike row/row-group/table above, this is not confined to the table's outer
            // edges, since a column runs the table's full block-axis extent but only *begins*/*ends* at
            // its own inline-start/-end boundary, which can be any interior line.
            if (!skipLeft && line > 0 && grid.ColumnBoxEndsAt(line - 1))
                Add(into, grid.ColumnAt(line - 1), inlineEnd, CollapsedBorderOrigin.Column, 0, line - 1);
            if (!skipRight && line < grid.ColumnCount && grid.ColumnBoxStartsAt(line))
                Add(into, grid.ColumnAt(line), inlineStart, CollapsedBorderOrigin.Column, 0, line);
            if (!skipLeft && line > 0 && grid.ColumnGroupEndsAt(line - 1))
                Add(into, grid.ColumnGroupAt(line - 1), inlineEnd, CollapsedBorderOrigin.ColumnGroup, 0, line - 1);
            if (!skipRight && line < grid.ColumnCount && grid.ColumnGroupStartsAt(line))
                Add(into, grid.ColumnGroupAt(line), inlineStart, CollapsedBorderOrigin.ColumnGroup, 0, line);
        }

        /// <summary>
        /// Resolves the border between a repeated <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>'s own last/first
        /// row and whichever row visually follows/precedes it on one specific page - a horizontal line
        /// this table's main grid does not itself model, since a repeated group's visual neighbor differs
        /// per page while its logical (DOM-order) neighbor does not. Border-collapse is about visual
        /// adjacency, so this is resolved fresh, per call, rather than read off <see cref="Horizontal"/>.
        /// Column/column-group/table origins never apply here (an interior horizontal line - not the
        /// table's own outer top/bottom edge - is never a column's own top/bottom boundary or the table's
        /// own edge, see <c>CollectHorizontal</c>'s identical gating), so this needs only
        /// cell/row/row-group, on *both* sides of the line.
        /// </summary>
        /// <param name="grid">
        /// The table's topology - <paramref name="groupRow"/>/<paramref name="adjacentRow"/> are grid row
        /// indices into it, so occupancy (including a <c>rowspan</c> cell reaching into the group's own
        /// last/first row from an earlier row in the *same* group - <see cref="TableGrid.CellAt"/> resolves
        /// this correctly via the grid's own rowspan/colspan accounting, unlike scanning a row's own
        /// <c>Boxes</c> list, which is dense only for a body row (<c>CssLayoutEngineTable.InsertEmptyBoxes</c>
        /// never touches a detached header/footer's rows) - and row-group lookup both come from the grid
        /// rather than being passed in separately.
        /// </param>
        /// <param name="groupRow">The repeated group's own last row index (for a header) or first row index (for a footer).</param>
        /// <param name="groupRowGroup">The repeated group's own row-group box (<c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>).</param>
        /// <param name="adjacentRow">Whichever row index this page's layout actually places next to the repeated group.</param>
        /// <param name="groupIsAbove">True for a header's own block-end edge (group above, adjacent row below); false for a footer's own block-start edge.</param>
        /// <param name="leftToRight">The table's own <c>direction</c>, for the resolver's position tiebreak.</param>
        /// <param name="blockStart">The table's own resolved block-start physical side - see <see cref="Resolve"/>'s identical parameter.</param>
        /// <param name="blockEnd">The table's own resolved block-end physical side - see <see cref="Resolve"/>'s identical parameter.</param>
        internal static CollapsedBorder[] ResolveRepeatedGroupBoundary(
            TableGrid grid, int groupRow, CssBox? groupRowGroup, int adjacentRow,
            bool groupIsAbove, bool leftToRight, Border blockStart, Border blockEnd)
        {
            var result = new CollapsedBorder[Math.Max(grid.ColumnCount, 1)];
            var candidates = new List<CollapsedBorderCandidate>(6);

            var groupSide = groupIsAbove ? blockEnd : blockStart;
            var adjacentSide = groupIsAbove ? blockStart : blockEnd;
            var adjacentRowGroup = grid.RowGroupAt(adjacentRow);

            for (var column = 0; column < grid.ColumnCount; column++)
            {
                candidates.Clear();

                Add(candidates, grid.CellAt(groupRow, column), groupSide, CollapsedBorderOrigin.Cell, 0, column, natural: true);
                Add(candidates, grid.CellAt(adjacentRow, column), adjacentSide, CollapsedBorderOrigin.Cell, 0, column, natural: true);
                Add(candidates, grid.RowAt(groupRow), groupSide, CollapsedBorderOrigin.Row, 0, 0, natural: true);
                Add(candidates, grid.RowAt(adjacentRow), adjacentSide, CollapsedBorderOrigin.Row, 0, 0, natural: true);
                Add(candidates, groupRowGroup, groupSide, CollapsedBorderOrigin.RowGroup, 0, 0, natural: true);
                Add(candidates, adjacentRowGroup, adjacentSide, CollapsedBorderOrigin.RowGroup, 0, 0, natural: true);

                result[column] = CollapsedBorderResolver.Resolve(CollectionsMarshal.AsSpan(candidates), leftToRight);
            }

            return result;
        }

        /// <param name="into">The candidate list to append to.</param>
        /// <param name="box">The box whose edge to read, or null to contribute nothing.</param>
        /// <param name="side">Which of the box's four edges names this grid line.</param>
        /// <param name="origin">This candidate's CSS 2.1 §17.6.2 origin-priority tier.</param>
        /// <param name="row">The candidate's own row, for the resolver's position tiebreak.</param>
        /// <param name="column">The candidate's own column, for the resolver's position tiebreak.</param>
        /// <param name="natural">
        /// True to read <see cref="CssBox.NaturalBorderTopWidth"/>/etc instead of the cached
        /// <see cref="CssBox.ActualBorderTopWidth"/>/etc - required for any candidate collected after
        /// <c>CssLayoutEngineTable.ApplyCollapsedUsedBorderWidths</c> has overwritten that cache with the
        /// box-model *used* half-width (see <see cref="ResolveRepeatedGroupBoundary"/>'s call site).
        /// <see cref="Resolve"/>'s own candidates run before that override, so they read the cheaper
        /// cached property.
        /// </param>
        private static void Add(
            List<CollapsedBorderCandidate> into, CssBox? box, Border side,
            CollapsedBorderOrigin origin, int row, int column, bool natural = false)
        {
            if (box is null) return;

            var (style, width, color) = side switch
            {
                Border.Top => (box.BorderTopStyle.Value,
                    natural ? box.NaturalBorderTopWidth : box.ActualBorderTopWidth, box.ActualBorderTopColor),
                Border.Right => (box.BorderRightStyle.Value,
                    natural ? box.NaturalBorderRightWidth : box.ActualBorderRightWidth, box.ActualBorderRightColor),
                Border.Bottom => (box.BorderBottomStyle.Value,
                    natural ? box.NaturalBorderBottomWidth : box.ActualBorderBottomWidth, box.ActualBorderBottomColor),
                Border.Left => (box.BorderLeftStyle.Value,
                    natural ? box.NaturalBorderLeftWidth : box.ActualBorderLeftWidth, box.ActualBorderLeftColor),
                _ => throw new ArgumentOutOfRangeException(nameof(side)),
            };

            into.Add(new CollapsedBorderCandidate(style, width, color, origin, row, column));
        }
    }
}
