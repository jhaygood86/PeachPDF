using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// A table cell that ran out of fragmentainer before it ran out of content, and where it stopped.
    /// </summary>
    /// <param name="RowIndex">
    /// the body row the cell belongs to, as <see cref="TableRowCursor.RowIndex"/> numbered it — the row a
    /// resumed pass would have to re-enter, and <c>-1</c> for a
    /// <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> measurement row, whose cursor is its own.
    /// </param>
    /// <param name="Cell">the cell box that did not finish</param>
    /// <param name="Token">
    /// the cell's own resumption record, read off <see cref="CssBox.PendingBreakToken"/> the moment its
    /// layout returned. It names a box inside the cell, never the cell's place in the table — the row and
    /// the cell are what <see cref="RowIndex"/> and <see cref="Cell"/> add.
    /// </param>
    internal sealed record UnfinishedTableCell(int RowIndex, CssBox Cell, BreakToken Token);

    /// <summary>
    /// Where a table's row loop has got to: the state <c>CssLayoutEngineTable.LayoutCells</c> carries from
    /// one body row to the next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table paginates its own content — it runs with the fragmentainer detached
    /// (<see cref="HtmlContainerInt.DetachFragmentainer"/>), so no break record escapes it and the whole
    /// table is laid out inside one fragmentainer pass however many pages it covers. Everything saying how
    /// far through the table layout has got therefore lives in local variables of a single method call,
    /// which is exactly what a <see cref="BreakToken"/> would have to carry for a resumed pass to pick the
    /// table up mid-flight. Naming it is the first half of that: <b>this type is the resumption record's
    /// contents, still held the way the engine holds them today.</b>
    /// </para>
    /// <para>
    /// <see cref="SlotIndex"/> is the member to be careful with, and the care is not obvious.
    /// It is a <i>counter</i> — the loop advances it by one every time it takes a break — so it names the
    /// band the loop last <b>opened</b>, not the band <see cref="CurrentY"/> has actually reached. Those
    /// differ whenever a row turns out taller than <c>EstimateRowHeight</c> predicted, which is often,
    /// since that estimate is one line of text and is blind to block content inside a cell.
    /// </para>
    /// <para>
    /// Deriving it from <see cref="CurrentY"/> instead is <b>not</b> a safe correction, and the reason is
    /// worth keeping: the stale counter is what compensates for the estimate. Once the loop believes it is
    /// on band <c>k</c> it keeps measuring every later row against band <c>k</c>'s bottom, so a row that
    /// overflowed is noticed one row late rather than never. A cursor that re-derives its band each row
    /// finds every row comfortably inside a fresh band and stops breaking at all — measured as a
    /// 40-row table recording no break anywhere and a repeating <c>&lt;thead&gt;</c> appearing on one page
    /// instead of five. The counter's own defect — a break offset that comes out negative, so the rows
    /// after a row taller than a band are placed back on a page it still occupies — is tracked as
    /// <see href="https://github.com/jhaygood86/PeachPDF/issues/432">issue #432</see>. The estimate has to
    /// go first, or the loop has to ask the question of the row's real height, which is only knowable once
    /// the row has been laid out.
    /// </para>
    /// </remarks>
    internal sealed class TableRowCursor
    {
        internal TableRowCursor(double top, double maxRight, int slotIndex)
        {
            CurrentY = top;
            MaxRight = maxRight;
            MaxBottom = 0d;
            SlotIndex = slotIndex;
        }

        /// <summary>Where the next row starts.</summary>
        internal double CurrentY { get; set; }

        /// <summary>The rightmost edge any cell placed so far reached.</summary>
        internal double MaxRight { get; set; }

        /// <summary>The lowest edge any cell placed so far reached.</summary>
        internal double MaxBottom { get; set; }

        /// <summary>
        /// The pagination slot the loop is filling — see this type's own remarks for what it does and does
        /// not mean.
        /// </summary>
        internal int SlotIndex { get; private set; }

        /// <summary>
        /// The index into the table's body rows of the row being placed, or <c>-1</c> while a
        /// <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> group is being measured — those rows are not part of
        /// the body's own row numbering, and a rowspan inside one must not reach the body's bookkeeping.
        /// </summary>
        internal int RowIndex { get; set; } = -1;

        /// <summary>
        /// Cells with <c>rowspan &gt; 1</c>, keyed by the <b>absolute</b> body-row index they end on, so a
        /// row can vertically align the cells that finish on it as well as its own.
        /// </summary>
        /// <remarks>
        /// Absolute, not relative to where a pass began — which is why a resumed pass could not simply
        /// start this empty: a cell begun on one page and ending on the next is entered in the map by the
        /// row that started it, several pages earlier.
        /// </remarks>
        internal Dictionary<int, List<CssBox>> RowSpannedBoxes { get; } = [];

        private readonly List<UnfinishedTableCell> _unfinishedCells = [];

        /// <summary>
        /// The cells this cursor has placed that did not finish, in the order the row loop placed them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Per cell rather than per row, which is what
        /// <see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see> asks
        /// for: a fragmented row fits as much content as it can in each of its cells <i>independently</i>,
        /// and the next fragment starts each cell where that cell stopped. The cells of one row are
        /// <see href="https://www.w3.org/TR/css-break-3/#parallel-flows">§2.1 parallel fragmentation
        /// flows</see>, so each has a stopping point of its own to record.
        /// </para>
        /// <para>
        /// Empty for every fixture measured so far, and that is a fact about the gate rather than about
        /// tables: <c>CssBox.LayoutMonolithicContent</c> detaches the fragmentainer around the whole
        /// engine, so nothing inside a cell has a fragmentainer to run out of. The list is what the gate
        /// is waiting on — a row loop that can be told a cell stopped is the prerequisite for lifting it,
        /// not a consequence.
        /// </para>
        /// </remarks>
        internal IReadOnlyList<UnfinishedTableCell> UnfinishedCells => _unfinishedCells;

        /// <summary>
        /// Notes that <paramref name="cell"/> did not finish, if it says so. Called by the row loop the
        /// moment the cell's layout returns, which is the only moment the answer is readable:
        /// <c>CssBox.BeginLayoutPass</c> clears the record at the start of every layout of the box.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This asks only <see cref="CssBox.PendingBreakToken"/> — a cell that was entered and stopped
        /// part-way — and <b>not</b> <c>CssBox.RequestedBreakBeforeTop</c>, the other channel
        /// <c>CssBox.PerformLayoutImp</c> treats as "did not finish". That is safe today for a reason
        /// worth naming exactly, because it is not the obvious one: <c>CssBox.PlaceBlockChild</c> wraps
        /// its whole body — both <c>RequestBreakBefore</c> call sites included — in
        /// <c>if (child.Display != CssConstants.TableCell)</c>, so a cell cannot carry that request at
        /// all. It is not that the row loop's per-row break check covers it; that check is a different
        /// decision, taken from <c>EstimateRowHeight</c> before the row is laid out. A change that gives
        /// a cell its own break-before has to add the second channel here.
        /// </para>
        /// <para>
        /// There is in any case no break point <i>before</i> a cell to record:
        /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">§4.1</see>'s class-A list names
        /// block-level boxes, floats, table row-group and table row boxes, and multi-column column-row
        /// boxes — cells are absent from it, being parallel flows rather than siblings in one flow.
        /// </para>
        /// </remarks>
        internal void RecordIfUnfinished(CssBox cell)
        {
            if (cell.PendingBreakToken is { } token)
            {
                _unfinishedCells.Add(new UnfinishedTableCell(RowIndex, cell, token));
            }
        }

        /// <summary>Moves the cursor onto the next slot, at <paramref name="top"/>.</summary>
        internal void OpenNextSlot(double top)
        {
            CurrentY = top;
            SlotIndex++;
        }

        /// <summary>
        /// Moves the cursor to <paramref name="top"/> in slot <paramref name="slotIndex"/>, for a
        /// relocation the engine has already worked out — a whole-table pre-check moving the table onto
        /// the next page, which restarts the row loop rather than continuing it.
        /// </summary>
        internal void RestartAt(double top, int slotIndex)
        {
            CurrentY = top;
            SlotIndex = slotIndex;
        }

        /// <summary>
        /// A cursor for measuring one row group's own rows, which are laid out once to learn their height
        /// and then repeated by proxy. It shares only <see cref="MaxRight"/> with the body's cursor: its
        /// rows are not body rows, so neither its row numbering nor its rowspan bookkeeping nor its
        /// <see cref="UnfinishedCells"/> is theirs — the group is laid out once and then <i>repeated</i> by
        /// proxy on every later page, so where one of its rows stopped says nothing about where the body
        /// has to resume.
        /// </summary>
        internal TableRowCursor ForRowGroupMeasurement(double top) =>
            new(top, MaxRight, SlotIndex) { MaxBottom = top };
    }
}
