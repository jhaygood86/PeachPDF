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
        /// <remarks>
        /// Moving to another row clears <see cref="FinishedCells"/>: which cells finished is a fact about
        /// one row, and only the row the loop stops in ever has to hand it on.
        /// </remarks>
        internal int RowIndex
        {
            get => _rowIndex;
            set
            {
                _rowIndex = value;
                _finishedCells.Clear();
            }
        }

        private int _rowIndex = -1;

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
        /// Empty until issue #464 stopped running the engine behind a detached fragmentainer: while it
        /// was, nothing inside a cell had a fragmentainer to run out of, so no cell could stop and this
        /// list could never fill. A row loop that can be told a cell stopped was the prerequisite for
        /// lifting that gate, not a consequence of it.
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
            else
            {
                _finishedCells.Add(cell);
            }
        }

        /// <summary>
        /// Notes that <paramref name="cell"/> finished on a pass <i>before</i> this one, so the record this
        /// cursor publishes says so too.
        /// </summary>
        /// <remarks>
        /// "Finished" is a fact about the cell, not about one pass, and the row loop cannot re-derive it: it
        /// places nothing at all for such a cell, so <see cref="RecordIfUnfinished"/> never sees it and its
        /// <c>PendingBreakToken</c> is whatever some earlier layout left behind. Without this the answer
        /// <b>oscillates</b> — the pass that is told a cell finished forgets to say so, the pass after that
        /// is not told, enters the cell from the start and re-places content two fragmentainers back, and
        /// the pass after <i>that</i> is told again. Measured on a row whose second cell never finishes: the
        /// record alternated between naming one finished cell and none, which also kept the driver's
        /// no-progress backstop from ever seeing two equal records in a row (it compares consecutive passes,
        /// and a two-cycle has none), so layout ran to the 100,000-pass cap.
        /// </remarks>
        internal void CarryForwardFinished(CssBox cell) => _finishedCells.Add(cell);

        private readonly List<CssBox> _finishedCells = [];

        /// <summary>
        /// The cells of the row now being placed that <b>finished</b> — every cell this cursor has entered
        /// on this row and not recorded in <see cref="UnfinishedCells"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The point of recording these is that a finished cell and a cell no pass has entered are
        /// otherwise the same thing on a continuation: both are simply absent from the record, and the
        /// resumed row loop enters both from the start. A cell that finished has its whole content in the
        /// fragment an earlier fragmentainer already holds, so entering it again re-places that content in
        /// the fragmentainer this pass is filling —
        /// <see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see> says each
        /// cell of a fragmented row continues where <i>that</i> cell stopped, and a cell that stopped
        /// nowhere continues nowhere.
        /// </para>
        /// <para>
        /// Per row, not per pass: cleared whenever <see cref="RowIndex"/> moves on, so what a stopped
        /// cursor hands over is the row it stopped in and nothing else.
        /// </para>
        /// </remarks>
        internal IReadOnlyList<CssBox> FinishedCells => _finishedCells;

        /// <summary>
        /// Whether any cell this cursor has placed did not finish — which, because the row loop stops at
        /// the first such row, means the row it has just placed.
        /// </summary>
        internal bool Stopped => _unfinishedCells.Count > 0;

        /// <summary>
        /// The cells an earlier pass left part-way through, which this cursor's first row hands back to
        /// them. Empty on a cursor that is not continuing anything.
        /// </summary>
        /// <remarks>
        /// Matched by reference rather than consumed, which is enough because a <see cref="CssBox"/> is a
        /// child of exactly one row: no later row of the same pass holds a cell named here. A
        /// <c>rowspan</c> cell is reached again through a <c>CssSpacingBox</c> placeholder, which is a
        /// different box.
        /// </remarks>
        private readonly List<UnfinishedTableCell> _carriedRecords = [];

        /// <summary>
        /// The cells an earlier pass finished in the row this cursor re-enters, matched by reference for
        /// the same reason <see cref="_carriedRecords"/> is.
        /// </summary>
        private readonly List<CssBox> _carriedFinished = [];

        /// <summary>
        /// The record <paramref name="cell"/> resumes from on this pass, or null when it is being entered
        /// from the start.
        /// </summary>
        internal BreakToken? CarriedTokenFor(CssBox cell)
        {
            foreach (var carried in _carriedRecords)
            {
                if (ReferenceEquals(carried.Cell, cell)) return carried.Token;
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="cell"/> is one an earlier pass began and did not finish, so the
        /// fragment this pass gives it <i>continues</i> an earlier one rather than opening the cell.
        /// </summary>
        /// <remarks>
        /// The same question <see cref="CarriedTokenFor"/> answers, asked where only the yes/no matters:
        /// a cell whose content began in an earlier fragmentainer has no leftover room of its own here to
        /// distribute, because where its content sits was settled by where that earlier fragment stopped
        /// (<see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see>).
        /// </remarks>
        internal bool ResumedFromAnEarlierPass(CssBox cell) => CarriedTokenFor(cell) is not null;

        /// <summary>
        /// Whether <paramref name="cell"/> is one an earlier pass finished, so this pass has nothing to
        /// place for it — as against a cell no pass has entered, which this pass enters from the start.
        /// See <see cref="FinishedCells"/> for why the two have to be told apart.
        /// </summary>
        internal bool FinishedOnAnEarlierPass(CssBox cell)
        {
            foreach (var finished in _carriedFinished)
            {
                if (ReferenceEquals(finished, cell)) return true;
            }

            return false;
        }

        /// <summary>
        /// A cursor continuing the row loop <paramref name="carried"/> stopped, at <paramref name="top"/>
        /// in the fragmentainer the table resumes in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Everything here is something the resumed pass cannot derive: which row to re-enter and which
        /// of its cells to continue, the rowspan map keyed by absolute row index (see
        /// <see cref="TableBreakToken.RowSpannedBoxes"/>), the table's widest edge so far, and the slot
        /// the break actually fell in. <see cref="CurrentY"/> and <see cref="MaxBottom"/> are the two the
        /// caller resolves instead, from the band that slot names — a table that spans fragmentainers
        /// keeps the one <c>Location</c> it was placed at, so its own content top still names the page it
        /// <i>began</i> on rather than the one these rows go in.
        /// </para>
        /// <para>
        /// <see cref="SlotIndex"/> is seeded from where the break fell rather than re-derived from
        /// <paramref name="top"/>, which is what keeps this from being the correction this type's own
        /// remarks warn against: the counter is seeded once per pass here exactly as a fresh run seeds it
        /// from the table's top, and its deliberate staleness <i>within</i> a pass is untouched.
        /// </para>
        /// </remarks>
        internal static TableRowCursor Continuing(TableBreakToken carried, double top)
        {
            var cursor = new TableRowCursor(top, carried.MaxRight, carried.ResumeSlotIndex)
            {
                MaxBottom = top,
                RowIndex = carried.ResumeRowIndex
            };

            cursor._carriedRecords.AddRange(carried.UnfinishedCells);
            cursor._carriedFinished.AddRange(carried.FinishedCells);

            foreach (var (endRow, boxes) in carried.RowSpannedBoxes)
            {
                cursor.RowSpannedBoxes[endRow] = [.. boxes];
            }

            return cursor;
        }

        /// <summary>
        /// What this cursor has to hand the next pass, or null when the row loop reached the end of the
        /// body rows.
        /// </summary>
        /// <param name="tableBox">the table the record resumes</param>
        internal TableBreakToken? Continuation(CssBox tableBox)
        {
            if (_unfinishedCells.Count == 0) return null;

            var resumeSlot = 0;
            foreach (var unfinished in _unfinishedCells)
            {
                if (unfinished.Token.ResumeSlotIndex > resumeSlot) resumeSlot = unfinished.Token.ResumeSlotIndex;
            }

            var spanned = new Dictionary<int, IReadOnlyList<CssBox>>(RowSpannedBoxes.Count);
            foreach (var (endRow, boxes) in RowSpannedBoxes)
            {
                spanned[endRow] = [.. boxes];
            }

            return new TableBreakToken(tableBox, resumeSlot, _unfinishedCells[0].RowIndex, MaxRight,
                [.. _unfinishedCells], [.. _finishedCells], spanned);
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
