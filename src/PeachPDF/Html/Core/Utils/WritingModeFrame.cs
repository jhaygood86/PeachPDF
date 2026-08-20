using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Converts a box's own content-relative logical geometry into true physical coordinates, given that
    /// box's own resolved <c>writing-mode</c>/<c>direction</c>. A logical rectangle is measured from the
    /// box's own inline-start/block-start corner: <see cref="RRect.X"/>/<see cref="RRect.Width"/> carry the
    /// inline offset/size (the axis a line's content progresses along), <see cref="RRect.Y"/>/
    /// <see cref="RRect.Height"/> carry the block offset/size (the axis lines stack along) - the same
    /// convention <see cref="LogicalPropertyResolver"/> uses for the box-model longhands, generalized to
    /// arbitrary geometry.
    /// </summary>
    /// <remarks>
    /// A box's own content-box edges (<see cref="CssBox.ClientLeft"/> etc.) are always fully resolved, true
    /// physical values by the time that box's own content-layout phase runs, at every nesting depth - so a
    /// frame built from one box's own edges never needs to know about, or undo, an ancestor's writing mode.
    /// That is what lets nested/orthogonal writing-mode boxes compose correctly without a transform stack:
    /// each box converts its own children's logical geometry straight to true physical coordinates, once,
    /// with nothing accumulated to reverse.
    /// <para>
    /// For <see cref="WritingMode.HorizontalTb"/> - and the <c>sideways-rl</c>/<c>sideways-lr</c> fallback,
    /// which this frame treats identically - conversion is a pure translation by the box's own content-box
    /// origin. <c>direction</c> is deliberately NOT consulted for the horizontal case: horizontal RTL is
    /// already handled directly inside <see cref="Dom.CssLayoutEngine"/>'s own cursor arithmetic (which
    /// stays untouched), not through this abstraction.
    /// </para>
    /// </remarks>
    internal readonly struct WritingModeFrame
    {
        private readonly double _clientLeft;
        private readonly double _clientTop;
        private readonly double _clientRight;
        private readonly double _clientBottom;
        private readonly bool _blockStartIsRight;
        private readonly bool _inlineStartIsBottom;

        private WritingModeFrame(double clientLeft, double clientTop, double clientRight, double clientBottom,
            bool isVertical, bool blockStartIsRight, bool inlineStartIsBottom)
        {
            _clientLeft = clientLeft;
            _clientTop = clientTop;
            _clientRight = clientRight;
            _clientBottom = clientBottom;
            IsVertical = isVertical;
            _blockStartIsRight = blockStartIsRight;
            _inlineStartIsBottom = inlineStartIsBottom;

            LogicalContentWidth = isVertical ? clientBottom - clientTop : clientRight - clientLeft;
            LogicalContentHeight = isVertical ? clientRight - clientLeft : clientBottom - clientTop;
        }

        /// <summary>Builds the frame for laying out <paramref name="box"/>'s own content.</summary>
        public static WritingModeFrame For(CssBox box) =>
            ForContentBox(box.ClientLeft, box.ClientTop, box.ClientRight, box.ClientBottom, box.WritingMode.Value, box.Direction.Value);

        /// <summary>
        /// The pure-geometry primitive <see cref="For"/> delegates to, split out so the axis math is
        /// directly unit-testable without constructing a <see cref="CssBox"/>.
        /// </summary>
        public static WritingModeFrame ForContentBox(double clientLeft, double clientTop, double clientRight,
            double clientBottom, WritingMode writingMode, DirectionMode direction)
        {
            var isVertical = writingMode is WritingMode.VerticalRl or WritingMode.VerticalLr;

            return new WritingModeFrame(
                clientLeft, clientTop, clientRight, clientBottom,
                isVertical,
                blockStartIsRight: isVertical && LogicalPropertyResolver.BlockStart(writingMode) == PhysicalSide.Right,
                inlineStartIsBottom: isVertical &&
                                      LogicalPropertyResolver.InlineStart(writingMode, direction) == PhysicalSide.Bottom);
        }

        /// <summary>
        /// <see langword="false"/> for <see cref="WritingMode.HorizontalTb"/> and the <c>sideways-*</c>
        /// fallback - every conversion below is then a pure translation, with no axis swap.
        /// </summary>
        public bool IsVertical { get; }

        /// <summary>
        /// Whether this box's own block-start edge is the physical-right one (<c>vertical-rl</c>) rather
        /// than physical-left (<c>vertical-lr</c>, or non-vertical) - exposed so a caller that needs to
        /// know which edge <see cref="ToPhysical(RRect)"/> is already anchoring block-axis geometry to
        /// (e.g. to decide which edge stays fixed when shrinking an auto block-size to content) reads the
        /// one derivation this frame already made, rather than calling
        /// <see cref="LogicalPropertyResolver.BlockStart"/> a second time and risking the two drifting
        /// apart under a future writing-mode addition.
        /// </summary>
        public bool BlockStartIsRight => _blockStartIsRight;

        /// <summary>
        /// Whether this box's own inline-start edge is the physical-bottom one (vertical +
        /// <c>direction: rtl</c>) rather than physical-top (vertical + <c>direction: ltr</c>, or
        /// non-vertical) - exposed for the same reason as <see cref="BlockStartIsRight"/>: a caller that
        /// needs to know which edge <see cref="ToPhysical(RRect)"/> is already anchoring inline-axis
        /// (physical Y, when vertical) geometry to - e.g. <see cref="Dom.CssBox.LayoutVerticalBlockChildren"/>,
        /// deciding whether a block child needs reflecting from its ltr-anchored placement to hang from the
        /// physical bottom instead - reads the one derivation this frame already made, rather than calling
        /// <see cref="LogicalPropertyResolver.InlineStart"/> a second time.
        /// </summary>
        public bool InlineStartIsBottom => _inlineStartIsBottom;

        /// <summary>The box's own content-box extent along its inline axis (physical height, when vertical).</summary>
        public double LogicalContentWidth { get; }

        /// <summary>The box's own content-box extent along its block axis (physical width, when vertical).</summary>
        public double LogicalContentHeight { get; }

        /// <summary>
        /// The physical point at logical (<paramref name="logicalInlineFromStart"/>, <paramref name="logicalBlockFromStart"/>)
        /// - i.e. the zero-size case of <see cref="ToPhysical(RRect)"/>.
        /// </summary>
        public RPoint ToPhysical(double logicalInlineFromStart, double logicalBlockFromStart) =>
            ToPhysical(new RRect(logicalInlineFromStart, logicalBlockFromStart, 0, 0)).Location;

        /// <summary>Swaps <see cref="RSize.Width"/>/<see cref="RSize.Height"/> when <see cref="IsVertical"/>.</summary>
        public RSize ToPhysical(RSize logical) =>
            IsVertical ? new RSize(logical.Height, logical.Width) : logical;

        /// <summary>Converts a logical rectangle (inline/block offset and size) to a true physical one.</summary>
        public RRect ToPhysical(RRect logical)
        {
            if (!IsVertical)
                return new RRect(_clientLeft + logical.X, _clientTop + logical.Y, logical.Width, logical.Height);

            var physicalX = _blockStartIsRight
                ? _clientRight - logical.Y - logical.Height
                : _clientLeft + logical.Y;

            var physicalY = _inlineStartIsBottom
                ? _clientBottom - logical.X - logical.Width
                : _clientTop + logical.X;

            return new RRect(physicalX, physicalY, logical.Height, logical.Width);
        }
    }
}
