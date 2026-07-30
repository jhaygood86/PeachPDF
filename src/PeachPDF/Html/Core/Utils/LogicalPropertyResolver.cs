using PeachPDF.CSS;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>The four physical box edges a logical (block-start/end, inline-start/end) edge resolves to.</summary>
    internal enum PhysicalSide
    {
        Top,
        Right,
        Bottom,
        Left
    }

    /// <summary>
    /// CSS Writing Modes Level 4's abstract-to-physical mapping (§7.1's flow-relative mapping table):
    /// resolves a box's own <c>block-start</c>/<c>block-end</c>/<c>inline-start</c>/<c>inline-end</c> edge
    /// to the physical <see cref="PhysicalSide"/> it names, given the box's own resolved
    /// <c>writing-mode</c> and <c>direction</c>. The single shared resolver every CSS Logical Properties
    /// consumer (margin/padding/inset/border longhands, in <c>DomParser.CascadeApplyStyles</c>'s logical
    /// resolution step) calls, rather than each re-deriving the table - see CLAUDE.md's "don't write two
    /// independent parsers for the same CSS value grammar across layers" convention.
    /// </summary>
    internal static class LogicalPropertyResolver
    {
        /// <summary>
        /// The block axis runs top-to-bottom for <c>horizontal-tb</c>, right-to-left for
        /// <c>vertical-rl</c>/<c>sideways-rl</c>, and left-to-right for <c>vertical-lr</c>/<c>sideways-lr</c>
        /// - <c>direction</c> never affects the block axis.
        /// </summary>
        public static PhysicalSide BlockStart(WritingMode writingMode) => writingMode switch
        {
            WritingMode.VerticalRl or WritingMode.SidewaysRl => PhysicalSide.Right,
            WritingMode.VerticalLr or WritingMode.SidewaysLr => PhysicalSide.Left,
            _ => PhysicalSide.Top
        };

        public static PhysicalSide BlockEnd(WritingMode writingMode) => Opposite(BlockStart(writingMode));

        /// <summary>
        /// The inline axis's start edge. <c>direction</c> chooses which of the axis's two physical ends
        /// is "start" for every writing mode. <c>sideways-lr</c> is the one exception to "vertical modes
        /// share vertical-rl's inline mapping": its inline axis runs the opposite way from
        /// <c>vertical-lr</c>/<c>vertical-rl</c>/<c>sideways-rl</c>, so <c>ltr</c> starts at the bottom
        /// there instead of the top.
        /// </summary>
        public static PhysicalSide InlineStart(WritingMode writingMode, DirectionMode direction)
        {
            var rtl = direction == DirectionMode.Rtl;

            return writingMode switch
            {
                WritingMode.HorizontalTb => rtl ? PhysicalSide.Right : PhysicalSide.Left,
                WritingMode.SidewaysLr => rtl ? PhysicalSide.Top : PhysicalSide.Bottom,
                _ => rtl ? PhysicalSide.Bottom : PhysicalSide.Top // VerticalRl, VerticalLr, SidewaysRl
            };
        }

        public static PhysicalSide InlineEnd(WritingMode writingMode, DirectionMode direction) =>
            Opposite(InlineStart(writingMode, direction));

        private static PhysicalSide Opposite(PhysicalSide side) => side switch
        {
            PhysicalSide.Top => PhysicalSide.Bottom,
            PhysicalSide.Bottom => PhysicalSide.Top,
            PhysicalSide.Left => PhysicalSide.Right,
            _ => PhysicalSide.Left
        };
    }
}
