// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Provides some drawing functionality
    /// </summary>
    internal static class RenderUtils
    {
        /// <summary>
        /// Check if the given color is visible if painted (has alpha and color values)
        /// </summary>
        /// <param name="color">the color to check</param>
        /// <returns>true - visible, false - not visible</returns>
        public static bool IsColorVisible(RColor color)
        {
            return color.A > 0;
        }

        /// <summary>
        /// Pushes the clip an <c>overflow: hidden</c> ancestor imposes on the fragment being painted, as
        /// resolved by <see cref="Fragments.FragmentTreeBuilder"/> and carried on
        /// <see cref="Fragments.BoxFragment.OverflowClip"/>. Already in the fragment's own space, so
        /// there is nothing here to map.
        /// </summary>
        /// <returns>true - was clipped, false - not clipped</returns>
        public static bool ClipGraphicsByOverflow(RGraphics g, RRect? overflowClip)
        {
            if (overflowClip is not { } clip) return false;

            // Intersecting with what is already on the stack is the one part that cannot be precomputed:
            // it depends on where in the paint walk this fragment is reached.
            clip.Intersect(g.GetClip());
            g.PushClip(clip);
            return true;
        }

        /// <summary>
        /// Pushes <paramref name="overflowBox"/>'s own clip (padding-edge rect, per CSS spec) if it has
        /// <c>overflow: hidden</c>, mapped into the coordinate space of the fragment being painted by
        /// subtracting its <paramref name="originY"/> (zero for a fixed fragment, which does not move
        /// with the page).
        /// </summary>
        private static bool TryPushOverflowClip(RGraphics g, CssBox overflowBox, double originY)
        {
            if (overflowBox.Overflow != CssConstants.Hidden) return false;

            var prevClip = g.GetClip();
            // CSS spec: overflow clips at the padding edge, not the content edge.
            // Expand ClientRectangle (content-box) outward by the containing block's padding.
            var rect = overflowBox.ClientRectangle;
            rect.X -= overflowBox.ActualPaddingLeft;
            rect.Width += overflowBox.ActualPaddingLeft + overflowBox.ActualPaddingRight;
            rect.Y -= overflowBox.ActualPaddingTop;
            rect.Height += overflowBox.ActualPaddingTop + overflowBox.ActualPaddingBottom;

            rect.Offset(0, -originY);

            rect.Intersect(prevClip);
            g.PushClip(rect);
            return true;
        }

        /// <summary>
        /// Pushes the <c>overflow: hidden</c> clip of every box in <paramref name="ancestors"/> that has
        /// one, in order. Used when painting a box that <see cref="Paint.StackingOrder.Flatten"/>
        /// hoisted past one or more plain ancestor boxes for stacking-context z-order purposes - since it
        /// paints via the claiming stacking context's own paint loop rather than those ancestors' own
        /// (nested) paint calls, their overflow clipping isn't already active on the graphics
        /// clip stack the way it would be for normally-painted content, and must be applied explicitly
        /// here instead. <paramref name="ancestors"/> is the exact, already-known chain of DOM ancestors
        /// between the claiming stacking context and the box being painted (see
        /// <see cref="Paint.StackingOrder.StackingParticipant"/>), so no walk is needed; each ancestor is
        /// checked directly.
        /// </summary>
        /// <remarks>
        /// This one still reads the live boxes, because the chain is discovered during the paint walk
        /// and so is not available to the builder. That is only wrong for a box shown at several places
        /// at once — a hoisted stacking participant inside a repeated table header — which is exotic
        /// enough that no fixture in the suite reaches it.
        /// </remarks>
        /// <returns>the number of clips actually pushed (callers must pop exactly this many afterward)</returns>
        public static int PushAncestorOverflowClips(RGraphics g, IReadOnlyList<CssBox> ancestors, double originY)
        {
            var pushed = 0;
            foreach (var ancestor in ancestors)
            {
                if (TryPushOverflowClip(g, ancestor, originY)) pushed++;
            }
            return pushed;
        }


        /// <summary>
        /// Creates a rounded rectangle path. Each corner has separate horizontal (X) and vertical (Y) radii,
        /// supporting elliptical corners per the CSS border-radius spec.
        /// <code>
        /// NW-----NE
        ///  |       |
        /// SW-----SE
        /// </code>
        /// </summary>
        public static RGraphicsPath GetRoundRect(RGraphics g, RRect rect,
            double nwX, double nwY, double neX, double neY,
            double seX, double seY, double swX, double swY)
        {
            var path = g.GetGraphicsPath();

            // Top edge: start after NW corner, end before NE corner.
            path.Start(rect.Left + nwX, rect.Top);
            path.LineTo(rect.Right - neX, rect.Top);
            if (neX > 0 || neY > 0)
                path.ArcTo(rect.Right, rect.Top + neY, neX, neY, RGraphicsPath.Corner.TopRight);

            // Right edge.
            path.LineTo(rect.Right, rect.Bottom - seY);
            if (seX > 0 || seY > 0)
                path.ArcTo(rect.Right - seX, rect.Bottom, seX, seY, RGraphicsPath.Corner.BottomRight);

            // Bottom edge.
            path.LineTo(rect.Left + swX, rect.Bottom);
            if (swX > 0 || swY > 0)
                path.ArcTo(rect.Left, rect.Bottom - swY, swX, swY, RGraphicsPath.Corner.BottomLeft);

            // Left edge.
            path.LineTo(rect.Left, rect.Top + nwY);
            if (nwX > 0 || nwY > 0)
                path.ArcTo(rect.Left + nwX, rect.Top, nwX, nwY, RGraphicsPath.Corner.TopLeft);

            return path;
        }
    }
}