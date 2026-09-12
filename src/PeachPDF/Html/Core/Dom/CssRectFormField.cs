namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The phantom word carrying a <see cref="CssBoxFormField"/>'s intrinsic size (see
    /// <see cref="CssLayoutEngine.MeasureIntrinsicSize"/>) - a field has no decoded external content
    /// the way <see cref="CssRectImage"/>'s owner does, only a default size to fall back to when the
    /// author sets no explicit CSS width/height.
    /// </summary>
    internal sealed class CssRectFormField : CssRect
    {
        public CssRectFormField(CssBox owner)
            : base(owner)
        { }

        /// <summary>Not a droppable empty run - same reasoning as <see cref="CssRectImage.IsSpaces"/>.</summary>
        public override bool IsSpaces => false;

        /// <summary>
        /// True, like every other phantom word type (<see cref="CssRectImage"/>, <c>CssRectSvg</c>,
        /// <c>CssRectShape</c> - the last of which carries no image either). Despite the name,
        /// <see cref="CssRect.IsImage"/> is this engine's "atomic, non-text word that carries its
        /// owner box's whole replaced geometry" flag, not a statement about pixels, and
        /// <see cref="CssLineBox.UpdateRectangle"/> keys the box-model arithmetic off it on both
        /// axes: the horizontal border+padding is added there, while the vertical border+padding is
        /// deliberately NOT, because <see cref="CssLayoutEngine.MeasureIntrinsicSize"/> already
        /// folded it into <see cref="CssRect.Height"/>. Leaving this false (as it originally was, on
        /// the reasoning that a field carries no image) meant a field's line rectangle got the
        /// vertical border+padding twice and the horizontal not at all - a UA-default checkbox came
        /// out 9.75pt wide by 16.75pt tall instead of square, and every text field's rectangle was
        /// its content box rather than its border box.
        /// </summary>
        public override bool IsImage => true;

        /// <summary>
        /// False, unlike every other <see cref="IsImage"/> word. See
        /// <see cref="CssRect.ReservesTrailingSpace"/>: a field has no source whitespace of its own
        /// to stand in for, and reserving one would put a whole space's width between a checkbox and
        /// the label written immediately after it.
        /// </summary>
        public override bool ReservesTrailingSpace => false;

        /// <summary>
        /// Empty, not null - defensive only. <c>FragmentPainter.PaintWords</c> skips any
        /// <see cref="IsImage"/> word before it reaches <c>DrawString</c>, and
        /// <c>FormFieldFragmentPainter</c> handles this box's content anyway, but a null
        /// <see cref="Text"/> would reach any future caller as a null argument.
        /// </summary>
        public override string? Text => string.Empty;

        public override string ToString() => "FormField";
    }
}
