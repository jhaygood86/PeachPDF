namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The phantom word carrying a <see cref="CssBoxFormField"/>'s intrinsic size (see
    /// <see cref="CssLayoutEngine.MeasureIntrinsicSize"/>) - a field has no decoded external content
    /// the way <see cref="CssRectImage"/>'s owner does, only a default size to fall back to when the
    /// author sets no explicit CSS width/height, so unlike <see cref="CssRectImage"/> this carries no
    /// image and <c>IsImage</c> stays at the base default (false).
    /// </summary>
    internal sealed class CssRectFormField : CssRect
    {
        public CssRectFormField(CssBox owner)
            : base(owner)
        { }

        /// <summary>Not a droppable empty run - same reasoning as <see cref="CssRectImage.IsSpaces"/>.</summary>
        public override bool IsSpaces => false;

        /// <summary>
        /// Empty, not null - <c>FragmentPainter.PaintWords</c>' generic text-painting path (the one
        /// this word takes until a dedicated form-field content painter is registered in
        /// <c>FragmentContentPainters.For</c>) calls <c>DrawString</c> unconditionally for any non-image,
        /// non-line-break word; a null <see cref="Text"/> would reach it as a null argument.
        /// </summary>
        public override string? Text => string.Empty;

        public override string ToString() => "FormField";
    }
}
