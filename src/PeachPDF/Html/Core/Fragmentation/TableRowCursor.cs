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
    /// A table paginates its own content: the row loop places its own rows against the page grid and
    /// decides where the breaks between them fall. Everything saying how far through the table layout has
    /// got would otherwise live in local variables of a single method call, which is exactly what a
    /// <see cref="BreakToken"/> has to carry for a resumed pass to pick the table up mid-flight — so this
    /// type <i>is</i> that record's contents, and <see cref="Continuation"/> publishes them as a
    /// <see cref="TableBreakToken"/> when a cell runs out of fragmentainer before it runs out of content.
    /// </para>
    /// <para>
    /// <see cref="SlotIndex"/> used to be a <i>counter</i> — advanced by one per break — so it named the
    /// band the loop last <b>opened</b> rather than the band <see cref="CurrentY"/> had reached, and a row
    /// taller than a band left the two several fragmentainers apart. It is now a <b>floor</b>: the lowest
    /// band the loop may still be filling. <see cref="BandReached"/> is what raises it, and it is the one
    /// the row loop asks — the floor unless <see cref="CurrentY"/> has fallen past that band, in which
    /// case the band the coordinate lies in. <see cref="MoveToSlot"/> and <see cref="RestartAt"/> assign
    /// it outright, for a break and a whole-table relocation respectively.
    /// </para>
    /// <para>
    /// It is still read directly in four places, and each is asking something other than "which band is
    /// this row in": the two whole-table pre-checks, which run before any row is placed; the sweep of the
    /// continuation geometry an earlier run stated, which wants the slot this pass <i>resumes</i> at; and
    /// the shells a row states, which are keyed by the field <see cref="BandReached"/> has just written.
    /// A fifth is the unpaginated fallback, where there is no page grid to derive anything from.
    /// </para>
    /// <para>
    /// That derivation was unsafe while the loop's only question was <c>EstimateRowHeight</c>, one line of
    /// text and blind to block content inside a cell: re-deriving the band then found every row
    /// comfortably inside a fresh band and the loop stopped breaking at all. It is safe now because the
    /// loop no longer decides from the estimate alone — it places the row and asks
    /// <see cref="HtmlContainerInt.FallsPast"/> of the bottom the row actually reached, retracting and
    /// re-placing it where that says the break falls
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/432">issue #432</see>). The order matters
    /// and is the point: the estimate went first, and the floor followed.
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
        /// <c>if (child.DerivedStyle.ActualDisplay != Keywords.TableCell)</c>, so a cell cannot carry
        /// that request at all. It is not that the row loop's per-row break check covers it; that check is a different
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

        /// <summary>
        /// Moves the cursor onto slot <paramref name="slot"/>, at <paramref name="top"/>.
        /// </summary>
        /// <remarks>
        /// The slot is named rather than incremented, because the band a break moves to is the one after
        /// the band the content already placed ends in — which is not the one after the band the loop last
        /// opened whenever a row has overflowed
        /// (<see href="https://www.w3.org/TR/css-break-3/#possible-breaks">§4.3</see>: a break moves
        /// content to the <i>next</i> fragmentainer, so a target above the content it must follow is not a
        /// break).
        /// </remarks>
        internal void MoveToSlot(int slot, double top, HtmlContainerInt? container = null)
        {
            CurrentY = top;
            SlotIndex = slot;

            // A row break moves the table's own cursor several bands in one step, between rows -
            // never mid-row, since the §6.2 reservations a row's cells read are keyed by the band
            // BandReached has just settled. The document-level pass cursor has to follow, or a cell's
            // own content asks its fragmentation questions of a band the pass has already left - #435.
            container?.CurrentFragmentainer?.StepOverTo(slot);
        }

        /// <summary>
        /// The band this cursor has actually reached: the band it was filling, unless
        /// <see cref="CurrentY"/> has fallen past that band, in which case the band the coordinate lies
        /// in. Raises the floor to it, so everything keyed to "the band the loop is filling" —
        /// <c>CssBox.PageBreakBottoms</c>, the continuation shells a row states — names the same one.
        /// </summary>
        /// <remarks>
        /// The gate is <see cref="HtmlContainerInt.FallsPast"/> rather than
        /// <see cref="HtmlContainerInt.SlotStartingAt"/> alone, and the difference is a real case rather
        /// than a nicety. <c>SlotStartingAt</c> applies the <i>top-edge</i> convention — a coordinate
        /// within <c>PageBoundaryEpsilon</c> above a boundary begins the later band — which is right for a
        /// box that was placed at a band top and has accumulated arithmetic noise, and wrong for this
        /// cursor, which is a derived position (the last row's bottom plus the table's vertical spacing).
        /// Reading it that way let a row whose predecessor ended half a point short of the band bottom
        /// count as beginning the next band, so the loop asked its questions of a band it had not reached,
        /// took no break, and left the table crossing a page boundary with no slice bottom recorded for
        /// the page it left. Caught on Windows only, where the font metrics put that row half a point
        /// further up than they do elsewhere — the tolerance is the same everywhere, but only some metrics
        /// land inside it.
        /// </remarks>
        /// <param name="container">the container whose page grid resolves the coordinate</param>
        internal int BandReached(HtmlContainerInt container)
        {
            if (!HtmlContainerInt.FallsPast(CurrentY, container.BandOfSlot(SlotIndex))) return SlotIndex;

            var reached = container.SlotStartingAt(CurrentY);

            if (reached > SlotIndex)
            {
                SlotIndex = reached;

                // The table's own floor just rose, between rows, with no break recorded for the
                // crossing - the same silent spill a line's tolerance-boundary case has, one level
                // down. The document-level pass cursor has to see it too - #435.
                container.CurrentFragmentainer?.StepOverTo(reached);
            }

            return SlotIndex;
        }

        /// <summary>
        /// Moves the cursor to <paramref name="top"/> in slot <paramref name="slotIndex"/>, for a
        /// relocation the engine has already worked out — a whole-table pre-check moving the table onto
        /// the next page, which restarts the row loop rather than continuing it.
        /// </summary>
        /// <param name="top">the row loop's new starting position</param>
        /// <param name="slotIndex">the band the table was moved to</param>
        /// <param name="container">
        /// the container whose document-level pass cursor must follow - see the identical reasoning on
        /// <see cref="MoveToSlot"/>. Missing here left a whole-table relocation invisible to
        /// <see cref="HtmlContainerInt.BandBeingFilled"/>: row 0's own content asked its fragmentation
        /// questions of the band the table was relocated *from*, read a spurious straddle against a band
        /// it had already left, and deferred every word to a same-slot "resume" that then landed with no
        /// vertical alignment applied - #435.
        /// </param>
        internal void RestartAt(double top, int slotIndex, HtmlContainerInt? container = null)
        {
            CurrentY = top;
            SlotIndex = slotIndex;
            container?.CurrentFragmentainer?.StepOverTo(slotIndex);
        }

        /// <summary>
        /// Everything one row's placement adds to this cursor, so a placement the row loop decides against
        /// can be taken back before the row is placed again.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="UnfinishedCells"/> is deliberately absent: the loop only retracts a row no cell
        /// stopped in, so there is nothing there to take back. <see cref="FinishedCells"/> is absent for a
        /// different reason — re-assigning <see cref="RowIndex"/> clears it, which the re-placement does
        /// anyway.
        /// </para>
        /// <para>
        /// So is the continuation geometry a row states
        /// (<c>HtmlContainerInt.RecordContinuationShell</c>), but only half of it, and the half that is
        /// <b>not</b> covered here is worth naming. A shell for a cell an <i>earlier pass</i> finished is
        /// safe by an argument that is not local: those cells are named by the carried record, and a record
        /// names only the row a continuation re-enters — which is the one row the loop never retracts. A
        /// shell for a <c>rowspan</c> cell fragmented at a band boundary has no such protection, because
        /// the row that states it is exactly the row the straddle correction moves. The row loop sweeps
        /// those itself, before calling this, using <see cref="ForeignCellsWritten"/>.
        /// </para>
        /// </remarks>
        internal readonly struct RowPlacement(double maxBottom, double maxRight, Dictionary<int, int>? spannedCounts)
        {
            internal double MaxBottom { get; } = maxBottom;

            internal double MaxRight { get; } = maxRight;

            /// <summary>
            /// How many boxes each <see cref="RowSpannedBoxes"/> list held, or null where the map was
            /// empty — which is every table with no <c>rowspan</c>, so it is worth not allocating for.
            /// </summary>
            internal Dictionary<int, int>? SpannedCounts { get; } = spannedCounts;
        }

        /// <summary>
        /// A write one row made to a cell belonging to a <b>different, earlier</b> row — the cell a
        /// <c>rowspan</c> begun there ends on this one.
        /// </summary>
        /// <param name="Cell">the spanning cell, a child of the row that opened the span</param>
        /// <param name="PreviousBottom">the <c>ActualBottom</c> it held before this row wrote one</param>
        /// <param name="AppliedOffset">
        /// how far <c>CssLayoutEngine.ApplyCellVerticalAlignment</c> moved its children, which has to be
        /// recorded rather than recomputed: it <i>offsets a subtree</i> rather than assigning a position,
        /// so it is neither idempotent nor derivable after the fact.
        /// </param>
        private readonly record struct ForeignCellWrite(CssBox Cell, double PreviousBottom, double AppliedOffset);

        private readonly List<ForeignCellWrite> _foreignWrites = [];

        /// <summary>
        /// Notes that the row being placed wrote to <paramref name="cell"/>, which belongs to an earlier
        /// row, so <see cref="Retract"/> can put it back.
        /// </summary>
        /// <remarks>
        /// This is the geometry a retraction could not reach before, and the reason the row loop used to
        /// decline to move a row that <i>ends</i> a <c>rowspan</c> at all
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/511">issue #511</see>):
        /// <see cref="Retract"/> takes back what the row added to this cursor and
        /// <c>PassRewind.RollBackTo</c> resets the row's own boxes, and the spanning cell is neither.
        /// </remarks>
        internal void RecordForeignWrite(CssBox cell, double previousBottom, double appliedOffset) =>
            _foreignWrites.Add(new ForeignCellWrite(cell, previousBottom, appliedOffset));

        /// <summary>
        /// Whether the row being placed has already written to <paramref name="cell"/>, so a caller reached
        /// by more than one route does not write to it twice.
        /// </summary>
        /// <remarks>
        /// A row reaches a spanning cell both as a <c>CssSpacingBox</c>'s <c>ExtendedBox</c> and through
        /// <see cref="RowSpannedBoxes"/>, and what it does to the cell —
        /// <c>CssLayoutEngine.ApplyCellVerticalAlignment</c> — composes rather than settling.
        /// </remarks>
        internal bool AlreadyWroteTo(CssBox cell)
        {
            foreach (var write in _foreignWrites)
            {
                if (ReferenceEquals(write.Cell, cell)) return true;
            }

            return false;
        }

        /// <summary>The cells belonging to earlier rows that the row being placed has written to.</summary>
        internal IEnumerable<CssBox> ForeignCellsWritten
        {
            get
            {
                foreach (var write in _foreignWrites) yield return write.Cell;
            }
        }

        /// <summary>What this cursor holds before the row about to be placed touches it.</summary>
        internal RowPlacement BeginRow()
        {
            // Per row rather than per placement, and this is the clear that carries the invariant: a row
            // that is *not* retracted leaves its entries behind, and the next row's Retract would otherwise
            // undo a previous row's writes. The one in Retract is belt against a second retraction.
            _foreignWrites.Clear();

            if (RowSpannedBoxes.Count == 0) return new RowPlacement(MaxBottom, MaxRight, null);

            var spanned = new Dictionary<int, int>(RowSpannedBoxes.Count);

            foreach (var (endRow, boxes) in RowSpannedBoxes)
            {
                spanned[endRow] = boxes.Count;
            }

            return new RowPlacement(MaxBottom, MaxRight, spanned);
        }

        /// <summary>
        /// Takes back what the row placed since <paramref name="placement"/> was taken, for a loop that has
        /// seen where the row landed and decided the break falls before it instead.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The rowspan map is truncated rather than rebuilt: <c>CssLayoutEngineTable.LayoutBodyRow</c>
        /// appends to the list for the row a <c>rowspan &gt; 1</c> cell <i>ends</i> on, so a retracted row
        /// leaves entries a row several fragmentainers later would otherwise align against twice. A key
        /// whose list the row created outright is removed, so an empty list is never left behind for
        /// <see cref="Continuation"/> to publish.
        /// </para>
        /// <para>
        /// The spanning cells the row wrote to go back too (<see cref="RecordForeignWrite"/>), which
        /// neither this cursor's own totals nor <c>PassRewind.RollBackTo(null, row.Boxes)</c> covers: the
        /// cell is a child of an <i>earlier</i> row. Undone in reverse, since the alignment offsets a
        /// subtree and two writes to the same cell would otherwise compose.
        /// </para>
        /// </remarks>
        internal void Retract(RowPlacement placement)
        {
            MaxBottom = placement.MaxBottom;
            MaxRight = placement.MaxRight;

            for (var i = _foreignWrites.Count - 1; i >= 0; i--)
            {
                var (cell, previousBottom, appliedOffset) = _foreignWrites[i];

                if (appliedOffset != 0d)
                {
                    foreach (var child in cell.Boxes) child.OffsetTop(-appliedOffset);
                }

                cell.ActualBottom = previousBottom;
            }

            _foreignWrites.Clear();

            if (RowSpannedBoxes.Count == 0) return;

            foreach (var endRow in (int[])[.. RowSpannedBoxes.Keys])
            {
                var boxes = RowSpannedBoxes[endRow];

                if (placement.SpannedCounts?.TryGetValue(endRow, out var count) is not true)
                {
                    RowSpannedBoxes.Remove(endRow);
                    continue;
                }

                if (boxes.Count > count) boxes.RemoveRange(count, boxes.Count - count);
            }
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
