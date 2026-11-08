namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// The place an atomic inline-level box (<c>inline-block</c>, <c>inline-table</c>, <c>inline-flex</c>,
    /// <c>inline-grid</c>) takes in a vertical box's column flow (<see cref="CssLayoutEngine.CreateVerticalLineBoxes"/>).
    /// </summary>
    /// <remarks>
    /// An atomic inline establishes a formatting context of its own and takes part in its parent's line as one
    /// unbreakable item (css-display-3 section 2.3, CSS 2.1 section 9.4.2), so the column flow does not walk its
    /// words: it lays the box out on its own, measures its margin box, and lets this item stand for that box in the
    /// word stream - wrapping, alignment and bidi reordering treat it like an image. Once the column's final
    /// position is known the box is moved to it (<c>CssLayoutEngine.PlaceAtomicInlines</c>). Never held in
    /// <see cref="CssBox.Words"/>: it exists only for the pass that created it, so a repeated layout starts afresh.
    /// </remarks>
    internal sealed class CssRectAtomicInline : CssRect
    {
        /// <summary>Creates the item standing for <paramref name="atomic"/> in its parent's column flow.</summary>
        /// <param name="atomic">the atomic inline-level box, which owns the item</param>
        public CssRectAtomicInline(CssBox atomic) : base(atomic)
        {
        }

        /// <summary>Like an image, an atomic inline claims space on the line and is never painted as text.</summary>
        public override bool IsImage => true;

        /// <summary>An atomic inline is real content, not white space.</summary>
        public override bool IsSpaces => false;

        /// <summary>Represents this item for debugging purposes.</summary>
        public override string ToString() => $"Atomic({OwnerBox.DerivedStyle.ActualDisplay})";
    }
}
