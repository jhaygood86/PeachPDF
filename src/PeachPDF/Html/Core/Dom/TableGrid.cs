using PeachPDF.CSS;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>Where one cell sits on the grid and how far it reaches.</summary>
    internal readonly record struct CellSpan(int Row, int Column, int RowSpan, int ColSpan)
    {
        internal int LastRow => Row + RowSpan - 1;
        internal int LastColumn => Column + ColSpan - 1;
    }

    /// <summary>A cell's real starting column, its <c>colspan</c>, and the last grid row it reaches - <see cref="TableGrid.ComputeColumnPlacements"/>'s own output.</summary>
    internal readonly record struct CellPlacement(int Column, int ColSpan, int LastRow);

    /// <summary>
    /// The table's logical row×column grid - which real <see cref="CssBox"/> occupies each cell (honoring
    /// <c>colspan</c>/<c>rowspan</c>), and which row-group/column/column-group each grid line belongs to.
    /// Built once per layout pass by <see cref="CssLayoutEngineTable"/> and consumed to register
    /// CSS 2.1 §17.6.2 border-conflict candidates. Carries no geometry - purely topology, so it can be
    /// built (and tested) before column widths or row heights exist.
    /// </summary>
    internal sealed class TableGrid
    {
        private readonly CssBox?[,] _slots;
        private readonly CssBox[] _rows;
        private readonly CssBox?[] _rowGroups;
        private readonly CssBox?[] _columns;
        private readonly CssBox?[] _columnGroups;
        private readonly bool[] _columnStartsHere;
        private readonly bool[] _columnGroupStartsHere;
        private readonly Dictionary<CssBox, CellSpan> _spans;

        internal int RowCount { get; }
        internal int ColumnCount { get; }

        private TableGrid(
            CssBox?[,] slots, CssBox[] rows, CssBox?[] rowGroups,
            CssBox?[] columns, CssBox?[] columnGroups, bool[] columnStartsHere, bool[] columnGroupStartsHere,
            Dictionary<CssBox, CellSpan> spans)
        {
            _slots = slots;
            _rows = rows;
            _rowGroups = rowGroups;
            _columns = columns;
            _columnGroups = columnGroups;
            _columnStartsHere = columnStartsHere;
            _columnGroupStartsHere = columnGroupStartsHere;
            _spans = spans;

            RowCount = rows.Length;
            ColumnCount = columnStartsHere.Length;
        }

        /// <summary>The real box occupying (row, column) - a cell, or the extended cell of a rowspan/colspan reaching in from elsewhere. Null for an out-of-range or genuinely empty slot.</summary>
        internal CssBox? CellAt(int row, int column) =>
            row >= 0 && row < RowCount && column >= 0 && column < ColumnCount ? _slots[row, column] : null;

        internal CssBox RowAt(int row) => _rows[row];

        internal CssBox? RowGroupAt(int row) => row >= 0 && row < RowCount ? _rowGroups[row] : null;

        internal CssBox? ColumnAt(int column) => column >= 0 && column < ColumnCount ? _columns[column] : null;

        internal CssBox? ColumnGroupAt(int column) => column >= 0 && column < ColumnCount ? _columnGroups[column] : null;

        /// <summary>True where the &lt;col&gt;/&lt;colgroup span&gt; covering <paramref name="column"/> begins.</summary>
        internal bool ColumnBoxStartsAt(int column) => column >= 0 && column < ColumnCount && _columnStartsHere[column];

        internal bool ColumnBoxEndsAt(int column) =>
            column >= 0 && column < ColumnCount && (column == ColumnCount - 1 || _columnStartsHere[column + 1]);

        internal bool ColumnGroupStartsAt(int column) => column >= 0 && column < ColumnCount && _columnGroupStartsHere[column];

        internal bool ColumnGroupEndsAt(int column) =>
            column >= 0 && column < ColumnCount && (column == ColumnCount - 1 || _columnGroupStartsHere[column + 1]);

        /// <summary>Where <paramref name="cell"/> begins and how far it reaches, in O(1) from any of its occupied slots.</summary>
        internal bool TryGetSpan(CssBox cell, out CellSpan span) => _spans.TryGetValue(cell, out span);

        internal bool IsFirstRowOfGroup(int row) =>
            row == 0 || !ReferenceEquals(_rowGroups[row], _rowGroups[row - 1]);

        internal bool IsLastRowOfGroup(int row) =>
            row == RowCount - 1 || !ReferenceEquals(_rowGroups[row], _rowGroups[row + 1]);

        /// <summary>
        /// Convenience overload of <see cref="Build(IReadOnlyList{CssBox}, IReadOnlyList{CssBox}, Dictionary{CssBox, CellPlacement}, int)"/>
        /// for a caller with no placements of its own already computed - runs
        /// <see cref="ComputeColumnPlacements"/> itself. <see cref="CssLayoutEngineTable"/> has its own
        /// (<c>_columnPlacements</c>, needed regardless of <c>border-collapse</c> for
        /// <c>GetCellRealColumnIndex</c> - issue #740) and calls the other overload directly instead, so
        /// the placement algorithm runs once per layout pass rather than twice.
        /// </summary>
        /// <param name="rows">Row-group-ordered, <c>visibility: collapse</c>-filtered rows (<c>_allRows</c>).</param>
        /// <param name="columns">One entry per column, repeated across a <c>span</c> (<c>_columns</c>).</param>
        /// <param name="getRowSpan">A cell's raw <c>rowspan</c> (already clamped to at least 1).</param>
        /// <param name="getColSpan">A cell's raw <c>colspan</c> (already clamped to at least 1).</param>
        /// <param name="getLastRow">
        /// The last grid row (inclusive, 0-based into <paramref name="rows"/>) a cell starting at grid
        /// row <c>gridRow</c> with the given <c>rowSpan</c> actually reaches - the caller's own concern,
        /// since a body row's rowspan counts against <c>CssLayoutEngineTable</c>'s original (pre-filter)
        /// row indices (issue #665) while a header/footer row's does not.
        /// </param>
        internal static TableGrid Build(
            IReadOnlyList<CssBox> rows,
            IReadOnlyList<CssBox?> columns,
            Func<CssBox, int> getRowSpan,
            Func<CssBox, int> getColSpan,
            Func<int, CssBox, int, int> getLastRow)
        {
            var initialColumnCount = 0;
            foreach (var box in columns) initialColumnCount++;

            var (placements, columnCount) =
                ComputeColumnPlacements(rows, initialColumnCount, getRowSpan, getColSpan, getLastRow);

            return Build(rows, columns, placements, columnCount);
        }

        /// <summary>
        /// Builds the grid from <paramref name="rows"/> (already row-group-ordered and
        /// <c>visibility: collapse</c>-filtered - <c>CssLayoutEngineTable._allRows</c>), <paramref name="columns"/>
        /// (one entry per column, repeated across a <c>span</c> - matching <c>_columns</c>), and
        /// <paramref name="placements"/>/<paramref name="columnCount"/> (<see cref="ComputeColumnPlacements"/>'s
        /// own output - already computed by the caller, since <see cref="CssLayoutEngineTable"/> needs it
        /// regardless of whether this grid gets built at all).
        /// </summary>
        internal static TableGrid Build(
            IReadOnlyList<CssBox> rows,
            IReadOnlyList<CssBox?> columns,
            Dictionary<CssBox, CellPlacement> placements,
            int columnCount)
        {
            var rowCount = rows.Count;
            var slots = new CssBox?[rowCount, Math.Max(columnCount, 1)];
            var spans = new Dictionary<CssBox, CellSpan>(ReferenceEqualityComparer.Instance);

            for (var r = 0; r < rowCount; r++)
            {
                foreach (var cell in rows[r].Boxes)
                {
                    // A rowspan placeholder an earlier row's fill-forward already claimed this slot for -
                    // nothing new to record, the origin row already computed the full span.
                    if (cell is CssSpacingBox) continue;

                    var (c, colSpan, lastRow) = placements[cell];

                    // A rowspan declared past the grid's own last row (e.g. a header cell's rowspan
                    // exceeding its own row-group's row count - CssLayoutEngineTable.GetLastRowInGrid's
                    // header/footer arithmetic doesn't clamp it, unlike a body row's GetEffectiveEndRowIndex)
                    // still has to report a real row inside the grid - a CellSpan naming one outside it is
                    // exactly the kind of value CollapsedBorderModel indexes its own line-width arrays with,
                    // with no bounds check of its own. colSpan needs no equivalent clamp: ComputeColumnPlacements
                    // grows columnCount to fit every cell's own start+colSpan as it places it, so c+colSpan
                    // is always within columnCount already.
                    var clampedLastRow = Math.Min(lastRow, rowCount - 1);

                    spans[cell] = new CellSpan(r, c, clampedLastRow - r + 1, colSpan);

                    for (var rr = r; rr <= lastRow && rr < rowCount; rr++)
                    {
                        for (var cc = c; cc < c + colSpan && cc < columnCount; cc++)
                        {
                            // A malformed table can declare overlapping cells; the parser tolerates it,
                            // so the grid does too - first writer keeps the slot.
                            slots[rr, cc] ??= cell;
                        }
                    }
                }
            }

            var rowGroups = new CssBox?[rowCount];
            for (var r = 0; r < rowCount; r++)
            {
                var parent = rows[r].ParentBox;
                rowGroups[r] = parent?.DerivedStyle.ActualDisplay == Keywords.TableRowGroup ? parent : null;
            }

            var columnBoxes = new CssBox?[columnCount];
            var columnGroupBoxes = new CssBox?[columnCount];
            var columnStartsHere = new bool[columnCount];
            var columnGroupStartsHere = new bool[columnCount];
            for (var c = 0; c < columnCount; c++)
            {
                var box = c < columns.Count ? columns[c] : null;
                var isGroup = box is not null && box.DerivedStyle.ActualDisplay == Keywords.TableColumnGroup;

                columnBoxes[c] = isGroup ? null : box;
                columnGroupBoxes[c] = isGroup
                    ? box
                    : box?.ParentBox?.DerivedStyle.ActualDisplay == Keywords.TableColumnGroup ? box!.ParentBox : null;

                columnStartsHere[c] = c == 0 || !ReferenceEquals(columnBoxes[c], columnBoxes[c - 1]);
                columnGroupStartsHere[c] = c == 0 || !ReferenceEquals(columnGroupBoxes[c], columnGroupBoxes[c - 1]);
            }

            return new TableGrid(
                slots, [.. rows], rowGroups, columnBoxes, columnGroupBoxes, columnStartsHere, columnGroupStartsHere, spans);
        }

        /// <summary>
        /// Works out each cell's real column, one row at a time, tracking which columns an earlier row's
        /// rowspan has already claimed and through which row - the standard HTML table grid
        /// "downward-growing cell" placement algorithm. This has to be self-contained rather than summing
        /// a row's own preceding cells' colspans: that sum is only correct when every rowspan gap in the
        /// row has a placeholder standing in for it, which <c>CssLayoutEngineTable.InsertEmptyBoxes</c>
        /// only ever arranges for <c>CssLayoutEngineTable._bodyRows</c> - a detached <c>&lt;thead&gt;</c>/
        /// <c>&lt;tfoot&gt;</c> group's own rows never get one, so a rowspan starting in one header/footer
        /// row and reaching into a later row of the same group left every later cell in that row placed
        /// one or more columns short of where it actually belongs (issue #736, and - for
        /// <c>CssLayoutEngineTable.GetCellRealColumnIndex</c>'s own direct callers, which use this same
        /// method rather than either overload of <see cref="Build(IReadOnlyList{CssBox}, IReadOnlyList{CssBox}, Dictionary{CssBox, CellPlacement}, int)"/> -
        /// issue #740).
        /// </summary>
        /// <param name="rows">Row-group-ordered, <c>visibility: collapse</c>-filtered rows (<c>_allRows</c>).</param>
        /// <param name="initialColumnCount">A floor for the returned column count (e.g. from declared <c>&lt;col&gt;</c>s) - the real count is never narrower than the widest cell placement.</param>
        /// <param name="getRowSpan">A cell's raw <c>rowspan</c> (already clamped to at least 1).</param>
        /// <param name="getColSpan">A cell's raw <c>colspan</c> (already clamped to at least 1).</param>
        /// <param name="getLastRow">
        /// The last grid row (inclusive, 0-based into <paramref name="rows"/>) a cell starting at grid
        /// row <c>gridRow</c> with the given <c>rowSpan</c> actually reaches - the caller's own concern,
        /// since a body row's rowspan counts against <c>CssLayoutEngineTable</c>'s original (pre-filter)
        /// row indices (issue #665) while a header/footer row's does not.
        /// </param>
        internal static (Dictionary<CssBox, CellPlacement> Placements, int ColumnCount) ComputeColumnPlacements(
            IReadOnlyList<CssBox> rows,
            int initialColumnCount,
            Func<CssBox, int> getRowSpan,
            Func<CssBox, int> getColSpan,
            Func<int, CssBox, int, int> getLastRow)
        {
            var columnCount = initialColumnCount;
            List<int> columnOccupiedThroughRow = [];
            var placements = new Dictionary<CssBox, CellPlacement>(ReferenceEqualityComparer.Instance);

            for (var r = 0; r < rows.Count; r++)
            {
                var nextColumn = 0;

                foreach (var cell in rows[r].Boxes)
                {
                    if (cell is CssSpacingBox) continue;

                    var colSpan = Math.Max(1, getColSpan(cell));
                    var rowSpan = Math.Max(1, getRowSpan(cell));
                    var lastRow = Math.Max(r, getLastRow(r, cell, rowSpan));

                    var start = nextColumn;
                    while (true)
                    {
                        while (columnOccupiedThroughRow.Count < start + colSpan) columnOccupiedThroughRow.Add(-1);

                        var conflictAt = -1;
                        for (var c = start; c < start + colSpan; c++)
                        {
                            if (columnOccupiedThroughRow[c] < r) continue;
                            conflictAt = c;
                            break;
                        }

                        if (conflictAt < 0) break;
                        start = conflictAt + 1;
                    }

                    for (var c = start; c < start + colSpan; c++)
                        columnOccupiedThroughRow[c] = Math.Max(columnOccupiedThroughRow[c], lastRow);

                    placements[cell] = new CellPlacement(start, colSpan, lastRow);
                    nextColumn = start + colSpan;
                    columnCount = Math.Max(columnCount, start + colSpan);
                }
            }

            return (placements, columnCount);
        }
    }
}
