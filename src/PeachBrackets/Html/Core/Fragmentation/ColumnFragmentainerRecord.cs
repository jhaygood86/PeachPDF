using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// One column of one multi-column container, on one pagination slot, as the fill that produced it
    /// saw it - the durable identity a later pass needs to attribute a footnote call to its column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Identity has to come from the fill rather than from geometry. Every column of a container shares
    /// one block-axis band, so a call in an earlier column has a document Y indistinguishable from one
    /// in this column, and the page-level <c>PageIndexOf(...)</c> attribution says nothing about which
    /// column it landed in - see
    /// <c>.claude/invariants/fragmentation-inside-a-container-which-fragmentainer-a-box-is-in-is-an-index.md</c>.
    /// Columns are told apart by the <em>inline</em> axis, which is what
    /// <see cref="InlineLeft"/>/<see cref="InlineRight"/> carry; that also disambiguates a nested
    /// container filled twice under two different outer columns, since those two fills have disjoint
    /// inline spans.
    /// </para>
    /// <para>
    /// <see cref="BandBottom"/> is the column context's own band bottom, <em>not</em> the taller band
    /// the fill may have recorded for an unbreakable child that overflowed it (css-multicol-1 §3.3).
    /// A column-scoped note area is reserved against the context band, so it must be placed against the
    /// same edge - otherwise an oversized child would drag the note area down with it.
    /// </para>
    /// </remarks>
    internal readonly record struct ColumnFragmentainerRecord(
        CssBox ColumnsBox,
        int Slot,
        int ColumnIndex,
        double InlineLeft,
        double InlineRight,
        double BandTop,
        double BandBottom)
    {
        /// <summary>This column's identity as a dictionary key.</summary>
        internal ColumnAreaKey Key => ColumnAreaKey.For(ColumnsBox, Slot, ColumnIndex, InlineLeft);

        /// <summary>Whether <paramref name="inlineLeft"/> falls within this column's inline span.</summary>
        internal bool ContainsInline(double inlineLeft) =>
            inlineLeft >= InlineLeft - 0.01 && inlineLeft < InlineRight + 0.01;

        /// <summary>Whether <paramref name="documentTop"/> falls within this column's block band.</summary>
        internal bool ContainsBlock(double documentTop) =>
            documentTop >= BandTop - 0.01 && documentTop < BandBottom + 0.01;
    }

    /// <summary>
    /// A column's identity as a dictionary key. <see cref="InlineLeft"/> is quantized, because it is
    /// here only to tell two fills of the same nested container apart and must survive being recomputed
    /// to the last floating-point bit.
    /// </summary>
    internal readonly record struct ColumnAreaKey(
        CssBox ColumnsBox,
        int Slot,
        int ColumnIndex,
        double InlineLeft)
    {
        /// <summary>
        /// Builds a key from a column's raw inline offset, quantizing it - the offset is recomputed from
        /// the gap and pitch on every pass, so keying on its full precision would miss a match over a
        /// difference of no consequence.
        /// </summary>
        internal static ColumnAreaKey For(CssBox columnsBox, int slot, int columnIndex, double inlineLeft) =>
            new(columnsBox, slot, columnIndex, System.Math.Round(inlineLeft, 2));
    }
}
