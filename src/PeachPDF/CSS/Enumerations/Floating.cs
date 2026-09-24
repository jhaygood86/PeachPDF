namespace PeachPDF.CSS
{
    internal enum Floating : byte
    {
        None,
        Left,
        Right,

        /// <summary>
        /// CSS Generated Content for Paged Media (css-gcpm-3) footnotes: the box is pulled out of
        /// normal flow entirely, rather than positioned beside its siblings like <see cref="Left"/>/
        /// <see cref="Right"/> — see <c>DomParser.DetachFootnoteBodies</c> and
        /// <c>Html/Core/Dom/CssBoxFootnoteCall.cs</c>. Deliberately excluded from
        /// <c>CssBox.IsFloated</c>/<c>CssLayoutEngine.FloatBox</c>, which only ever handle
        /// <see cref="Left"/>/<see cref="Right"/>.
        /// </summary>
        Footnote,

        /// <summary>
        /// CSS Page Floats: floats to the block-start edge of its <c>float-reference</c> (page or
        /// column). See <c>CssBox.IsPageFloated</c>.
        /// </summary>
        Top,

        /// <summary>
        /// CSS Page Floats: floats to the block-end edge of its <c>float-reference</c>.
        /// </summary>
        Bottom,

        /// <summary>
        /// CSS Page Floats: tries <see cref="Top"/> first; falls back to <see cref="Bottom"/> when the
        /// float does not fit in the remaining top-reservable space.
        /// </summary>
        TopBottom,

        /// <summary>
        /// CSS Page Floats: floats to whichever of the block-start/block-end edges its own natural
        /// (in-flow) position is nearer to.
        /// </summary>
        Snap,

        /// <summary>
        /// CSS Page Floats: floats to the inline edge nearest the binding (the left edge on a right-hand
        /// page, the right edge on a left-hand page) - resolves to an effective <see cref="Left"/>/
        /// <see cref="Right"/> via <see cref="PeachPDF.Html.Core.Dom.PageRuleResolver.IsRightPage"/> and
        /// is included in <c>CssBox.IsFloated</c>, unlike <see cref="Top"/>/<see cref="Bottom"/>/
        /// <see cref="TopBottom"/>/<see cref="Snap"/>.
        /// </summary>
        Inside,

        /// <summary>
        /// CSS Page Floats: floats to the inline edge farthest from the binding - the mirror of
        /// <see cref="Inside"/>.
        /// </summary>
        Outside,

        /// <summary>
        /// CSS Logical Properties §2.2: floats to the inline-start side of the containing block -
        /// line-left when its used <c>direction</c> is <c>ltr</c>, line-right when <c>rtl</c>. The keyword
        /// stays the computed value; <c>CssBox.EffectiveFloatSide</c> resolves it to <see cref="Left"/>/
        /// <see cref="Right"/> when layout asks, since the containing block's direction is not final
        /// when the cascade runs.
        /// </summary>
        InlineStart,

        /// <summary>The mirror of <see cref="InlineStart"/>: the inline-end side of the containing block.</summary>
        InlineEnd
    }
}