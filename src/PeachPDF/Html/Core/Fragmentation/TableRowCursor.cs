using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragmentation
{
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
        /// <param name="top">where the first row starts</param>
        /// <param name="maxRight">
        /// the rightmost edge reached so far, which for a fresh cursor is the table's own content left
        /// edge — a table with no cells is as wide as its own left edge, not as wide as zero.
        /// </param>
        /// <param name="slotIndex">the pagination slot the loop begins filling</param>
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

        /// <summary>Moves the cursor onto the next slot, at <paramref name="top"/>.</summary>
        internal void OpenNextSlot(double top)
        {
            CurrentY = top;
            SlotIndex++;
        }

        /// <summary>
        /// Places the cursor at <paramref name="top"/> in slot <paramref name="slotIndex"/>, without
        /// disturbing what it has measured. Used to seed the loop once the page grid can be asked, and
        /// again by each whole-table pre-check, which moves the table and so restarts the row loop rather
        /// than continuing it.
        /// </summary>
        /// <remarks>
        /// <see cref="MaxBottom"/> deliberately survives: a pre-check moves where the rows will go, not
        /// what a repeating header measured above them.
        /// </remarks>
        internal void PlaceAt(double top, int slotIndex)
        {
            CurrentY = top;
            SlotIndex = slotIndex;
        }

        /// <summary>
        /// A cursor for measuring one row group's own rows, which are laid out once to learn their height
        /// and then repeated by proxy. It shares only <see cref="MaxRight"/> with the body's cursor: its
        /// rows are not body rows, so neither its row numbering nor its rowspan bookkeeping is theirs, and
        /// it takes no break, so it has no slot of its own to name.
        /// </summary>
        internal TableRowCursor ForRowGroupMeasurement(double top) =>
            new(top, MaxRight, slotIndex: 0) { MaxBottom = top };
    }
}
