using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// A table's row loop stopped part-way through its body rows: which row to re-enter, which of that
    /// row's cells to continue and from where, and the part of <see cref="TableRowCursor"/> that a later
    /// pass cannot work out for itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the second thing a table pass hands the next, alongside <see cref="TableSetup"/>, and the
    /// two divide cleanly: the setup is what the table settled <b>once</b> and every pass inherits
    /// unchanged, while this is where <i>this</i> pass got to. A table that finished its rows publishes
    /// none.
    /// </para>
    /// <para>
    /// It is a <see cref="BreakToken"/> already, though nothing hands it to
    /// <c>CssBox.SetPendingBreakToken</c> yet. That record means "resume me" to three consumers at once —
    /// <c>CssBox.PerformLayoutImp</c> returns early on it, <c>PublishBreakToTheContextRoot</c> hands it to
    /// the fragmentation context, and the parent's child loop stops and wraps it — so setting it is the
    /// step where a table stops being invisible from outside, and it is reachable from real markup today.
    /// The engine publishes this on <see cref="CssBox.TableContinuation"/> instead, which nothing outside
    /// the engine reads.
    /// </para>
    /// </remarks>
    /// <param name="Box">the table to resume</param>
    /// <param name="ResumeSlotIndex">
    /// the pagination slot to resume in, taken from the cells that actually stopped rather than from "the
    /// pass after this one": each carries its own <see cref="BreakToken.ResumeSlotIndex"/>, and the table
    /// cannot resume before the last of its
    /// <see href="https://www.w3.org/TR/css-break-3/#parallel-flows">§2.1 parallel flows</see> does.
    /// </param>
    /// <param name="ResumeRowIndex">
    /// the index into the table's body rows of the row that did not finish — the row a resumed pass
    /// re-enters, not the one after it. Its cells are parallel flows and only some of them stopped.
    /// </param>
    /// <param name="MaxRight">
    /// the rightmost edge the table had reached, which the resumed pass's cursor starts from so the
    /// table's own width does not shrink to what its remaining rows happen to need.
    /// </param>
    /// <param name="UnfinishedCells">
    /// the cells of <paramref name="ResumeRowIndex"/> that stopped, each with its own record. Per cell
    /// rather than per row, which is what
    /// <see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see> asks for.
    /// </param>
    /// <param name="FinishedCells">
    /// the cells of <paramref name="ResumeRowIndex"/> that <b>finished</b>, which a continuation must be
    /// able to tell from a cell no pass has entered: both are absent from
    /// <paramref name="UnfinishedCells"/>, and only one of them has content already sitting in an earlier
    /// fragmentainer. A cell that stopped nowhere continues nowhere.
    /// </param>
    /// <param name="RowSpannedBoxes">
    /// the <c>rowspan &gt; 1</c> bookkeeping, keyed by the <b>absolute</b> body-row index each cell ends
    /// on. Absolute is why a resumed pass cannot start this empty: a cell begun on one page and ending on
    /// a later one was entered in the map by the row that started it, several pages earlier, and the row
    /// that ends its span has to find it there to align it.
    /// </param>
    internal sealed record TableBreakToken(
        CssBox Box,
        int ResumeSlotIndex,
        int ResumeRowIndex,
        double MaxRight,
        IReadOnlyList<UnfinishedTableCell> UnfinishedCells,
        IReadOnlyList<CssBox> FinishedCells,
        IReadOnlyDictionary<int, IReadOnlyList<CssBox>> RowSpannedBoxes) : BreakToken(Box, ResumeSlotIndex);
}
