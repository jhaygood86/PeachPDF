using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// A repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> group that <c>CssLayoutEngineTable</c> took out
    /// of the table's child list, and the two facts about it nothing else records.
    /// </summary>
    /// <param name="Box">
    /// the detached group. Its <see cref="CssBox.ParentBox"/> is null and it is absent from the table's
    /// <see cref="CssBox.Boxes"/>, so this reference and the group's <c>CssProxyBox</c>es are the only
    /// things still pointing at it.
    /// </param>
    /// <param name="Index">where the group sat among the table's children before it was detached</param>
    /// <param name="Height">
    /// what the group measured when it was laid out once for that purpose — the height every proxy of it
    /// reserves on its own page.
    /// </param>
    internal sealed record DetachedRowGroup(CssBox Box, int Index, double Height);

    /// <summary>
    /// What <c>CssLayoutEngineTable</c> settles once for a table, which a resumed fragmentainer pass
    /// inherits rather than works out again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine is constructed afresh every time it runs, so everything it decides lives either on the
    /// table's <see cref="CssBox"/> or in one method call. That is correct for the reasons it usually runs
    /// again over the same table — the per-page-width reflow loop, <c>ShrinkToFit</c>, a
    /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">§4.3</see> relocation laying the
    /// subtree out again at its destination — because each of those is a fresh layout that must start from
    /// the markup. A resumed pass is the one case that is <b>not</b> a fresh layout: it continues the table
    /// whose earlier rows are already emitted, and the once-per-table half of the engine's work is
    /// destructive when repeated on top of that.
    /// </para>
    /// <para>
    /// Five of those, each measured against the code that does them
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/390">#390</see> stage 4):
    /// <list type="bullet">
    /// <item><description>
    /// <b>Restoring the structure of a previous run</b> pulls the detached group back out of its proxies
    /// and drops them — and the proxies are the only surviving reference to it, so on a continuation that
    /// destroys every earlier page's repeated header.
    /// </description></item>
    /// <item><description>
    /// <b>Clearing <see cref="CssBox.PageBreakBottoms"/></b> throws away where the table's slice ended on
    /// the pages earlier passes filled, which is what clips its borders there.
    /// </description></item>
    /// <item><description>
    /// <b>The two whole-table pre-checks</b> move the table's own <c>Location</c>, and so would move a
    /// table whose earlier rows have already been emitted at their own coordinates.
    /// </description></item>
    /// <item><description>
    /// <b>The <c>margin-left: auto</c> centering</b> in the engine's entry point is the same hazard on the
    /// other axis: it adds an offset derived from the containing block rather than from where the table
    /// currently is, so applying it twice centers it twice.
    /// </description></item>
    /// <item><description>
    /// <b>Laying the header and footer out to measure them</b> re-positions the one shared source subtree
    /// — <c>CssProxyBox</c> moves it to the proxy's own position before snapshotting it — whose earlier
    /// snapshots are already frozen in the fragment emitter. <see cref="DetachedRowGroup.Height"/> is
    /// what makes re-measuring unnecessary.
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Two things deliberately absent. <c>InsertEmptyBoxes</c> is already once-only per box
    /// (<c>CssBox._tableFixed</c>) and the column widths are recomputed deterministically from style and
    /// word metrics, so neither needs carrying. And where the row loop had got to is <b>not</b> here: that
    /// is <see cref="TableRowCursor"/>, and a pass that resumes the loop rather than restarting it is the
    /// step after this one.
    /// </para>
    /// </remarks>
    internal sealed class TableSetup
    {
        /// <summary>The detached repeating <c>&lt;thead&gt;</c>, or null when the table has none.</summary>
        internal DetachedRowGroup? Header { get; set; }

        /// <summary>The detached repeating <c>&lt;tfoot&gt;</c>, or null when the table has none.</summary>
        internal DetachedRowGroup? Footer { get; set; }
    }
}
