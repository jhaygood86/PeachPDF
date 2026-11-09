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
        /// Whether the box is sized as if it were empty in an axis: size containment, or inline-size containment where that axis is the inline one (the
        /// width in a horizontal writing mode, the height in a vertical one).
        /// </summary>
        /// <param name="widthAxis">true for the width, false for the height</param>
        internal bool AppliesSizeContainmentInAxis(bool widthAxis)
        {
            var applied = DomUtils.AppliedContainment(this);

            if ((applied & Containment.Size) != 0) return true;
            if ((applied & Containment.InlineSize) == 0) return false;

            return widthAxis != WritingModeFrame.For(this).IsVertical;
        }

        /// <summary>
        /// Whether the box's overflow on an axis is cut at its overflow clip edge without scrolling (css-overflow-3 §3.2): <c>overflow: clip</c> on it, or
        /// paint containment turning a <c>visible</c> axis into <c>clip</c> (css-contain-2 §3.4). Neither holds once the box is a scroll container, so
        /// <c>overflow-clip-margin</c> applies to this axis alone.
        /// </summary>
        internal bool ClipsAxisAtTheOverflowClipEdge(bool horizontal)
        {
            if (Overflow.Value != Css.Overflow.Visible) return false;

            var axis = horizontal ? OverflowX.Value : OverflowY.Value;

            return axis == Css.Overflow.Clip || (axis == Css.Overflow.Visible && DomUtils.AppliesPaintContainment(this));
        }

        /// <summary>
        /// The <c>overflow-clip-margin</c> of one side of the box: the visual box it is measured from and the margin (negative to shrink), in layout units. The
        /// physical longhand of the side and the logical one that names it under this box's writing mode and direction are one cascade, the later
        /// declaration winning (CSS Logical Properties 1 §1.1), which <see cref="LogicalDeclarationOrder"/> settles.
        /// </summary>
        /// <param name="side">the physical side</param>
        internal (OverflowClipVisualBox VisualBox, double Margin) OverflowClipMarginOf(PhysicalSide side)
        {
            var writingMode = WritingMode.Value;
            var direction = Direction.Value;

            var (physical, physicalName) = side switch
            {
                PhysicalSide.Top => (OverflowClipMarginTop, "overflow-clip-margin-top"),
                PhysicalSide.Right => (OverflowClipMarginRight, "overflow-clip-margin-right"),
                PhysicalSide.Bottom => (OverflowClipMarginBottom, "overflow-clip-margin-bottom"),
                _ => (OverflowClipMarginLeft, "overflow-clip-margin-left")
            };

            string? logical;
            string logicalName;
            if (side == LogicalPropertyResolver.BlockStart(writingMode)) (logical, logicalName) = (OverflowClipMarginBlockStart, "overflow-clip-margin-block-start");
            else if (side == LogicalPropertyResolver.BlockEnd(writingMode)) (logical, logicalName) = (OverflowClipMarginBlockEnd, "overflow-clip-margin-block-end");
            else if (side == LogicalPropertyResolver.InlineStart(writingMode, direction)) (logical, logicalName) = (OverflowClipMarginInlineStart, "overflow-clip-margin-inline-start");
            else (logical, logicalName) = (OverflowClipMarginInlineEnd, "overflow-clip-margin-inline-end");

            var text = _rare?.LogicalOrder is { } order && order.Of(logicalName) > order.Of(physicalName) ? logical : physical;
            var visualBox = OverflowClipMarginGrammar.Read(text, out var length);

            return (visualBox, length is null ? 0 : CssValueParser.ParseLength(length, 0, this));
        }

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

            var length = ContainGrammar.ParseIntrinsic(text, out var auto);

            // auto stands for the size the box last had while its contents were shown (css-sizing-4 §5.1), where there is one.
            if (auto && HtmlTag is { } tag && HtmlContainer is { } container && container.RememberedAutoSizes.TryGetValue(tag.SourceOrdinal, out var remembered))
                return widthAxis ? remembered.Width : remembered.Height;

            return length is not null ? CssValueParser.ParseLength(length, 0, this) : null;
        }
    }
}
