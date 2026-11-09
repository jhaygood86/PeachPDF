using PeachBrackets.Css;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// What CSS Containment 2 §3.1 (size containment) and css-sizing-4 §5.1 (<c>contain-intrinsic-*</c>) ask of a box's sizing.
    /// </summary>
    internal partial class CssBox
    {
        /// <summary>
        /// Whether size containment applies to this box (<see cref="DomUtils.AppliedContainment"/>): it is sized as if it had no contents, the contents then being
        /// laid out into that size.
        /// </summary>
        internal bool AppliesSizeContainment => (DomUtils.AppliedContainment(this) & Containment.Size) != 0;

        /// <summary>
        /// The size, in layout units, a box with size containment has instead of its contents' in the physical axis: its <c>contain-intrinsic-width</c> or
        /// <c>-height</c>, or the <c>-inline-size</c> or <c>-block-size</c> that names the same axis under this box's writing mode, whichever was declared last
        /// (css-sizing-4 §5.1: the logical and physical longhands are one cascade, as in CSS Logical Properties 1 §1.1). Null for <c>none</c>, which is a size
        /// of 0. <c>auto</c> remembers the last rendered size, which a document that was never rendered before does not have, so it is the length that
        /// follows. Does not look at whether containment applies.
        /// </summary>
        /// <param name="widthAxis">true for the width, false for the height</param>
        internal double? ContainIntrinsicLength(bool widthAxis)
        {
            // Horizontal text: the width is the inline axis. A vertical writing mode swaps the two.
            var logicalIsInline = widthAxis != WritingModeFrame.For(this).IsVertical;
            var physical = widthAxis ? ContainIntrinsicWidth : ContainIntrinsicHeight;
            var logical = logicalIsInline ? ContainIntrinsicInlineSize : ContainIntrinsicBlockSize;

            var physicalName = widthAxis ? "contain-intrinsic-width" : "contain-intrinsic-height";
            var logicalName = logicalIsInline ? "contain-intrinsic-inline-size" : "contain-intrinsic-block-size";
            var text = _rare?.LogicalOrder is { } order && order.Of(logicalName) > order.Of(physicalName) ? logical : physical;

            return ContainGrammar.ParseIntrinsic(text, out _) is { } length ? CssValueParser.ParseLength(length, 0, this) : null;
        }
    }
}
