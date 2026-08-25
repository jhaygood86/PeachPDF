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

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
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
        /// resolved by <see cref="Fragmentation.FragmentEmitter"/> and carried on
        /// <see cref="Fragments.BoxFragment.OverflowClip"/>/<see cref="Fragments.BoxFragment.OverflowClipCurve"/>.
        /// Already in the fragment's own space, so there is nothing here to map. When the clipping
        /// ancestor has a <c>border-radius</c>, an additional path clip confines content to the rounded
        /// curve itself (CSS Backgrounds and Borders Level 3 §5.5), not just its padding-edge rectangle.
        /// </summary>
        /// <param name="g">the graphics to clip</param>
        /// <param name="overflowClip">
        /// the rectangular clip resolved for the fragment being painted, or null when no ancestor clips it
        /// </param>
        /// <param name="curve">
        /// the clipping ancestor's rounded-corner curve, or null when it has no <c>border-radius</c>
        /// </param>
        /// <returns>the number of clips actually pushed (callers must pop exactly this many afterward)</returns>
        public static int ClipGraphicsByOverflow(RGraphics g, RRect? overflowClip, OverflowClipCurve? curve)
        {
            if (overflowClip is not { } clip) return 0;

            // Intersecting with what is already on the stack is the one part that cannot be precomputed:
            // it depends on where in the paint walk this fragment is reached.
            clip.Intersect(g.GetClip());
            g.PushClip(clip);
            var pushed = 1;

            if (curve is { } c) pushed += PushRoundedClipIfRounded(g, c.Rect, c.Radii);

            return pushed;
        }

        /// <summary>
        /// Pushes <paramref name="overflowBox"/>'s own clip (padding-edge rect, per CSS spec, plus its
        /// rounded-corner curve if it has a <c>border-radius</c>) if it has <c>overflow: hidden</c>,
        /// mapped into the coordinate space of the fragment being painted by subtracting its
        /// <paramref name="originY"/> (zero for a fixed fragment, which does not move with the page).
        /// </summary>
        /// <returns>the number of clips actually pushed (callers must pop exactly this many afterward)</returns>
        private static int TryPushOverflowClip(RGraphics g, CssBox overflowBox, double originY)
        {
            if (overflowBox.Overflow.Value != Overflow.Hidden) return 0;

            var prevClip = g.GetClip();
            var paddingRect = PaddingEdgeOf(overflowBox, overflowBox.Bounds);

            var rect = paddingRect;
            rect.Offset(0, -originY);
            rect.Intersect(prevClip);
            g.PushClip(rect);
            var pushed = 1;

            if (overflowBox.IsRounded)
            {
                var curveRect = paddingRect;
                curveRect.Offset(0, -originY);
                var radii = overflowBox.ComputeInnerRadii(overflowBox.Bounds, paddingRect,
                    overflowBox.ActualBorderLeftWidth, overflowBox.ActualBorderTopWidth,
                    overflowBox.ActualBorderRightWidth, overflowBox.ActualBorderBottomWidth);
                pushed += PushRoundedClipIfRounded(g, curveRect, radii);
            }

            return pushed;
        }

        /// <summary>
        /// Pushes a rounded-path clip for <paramref name="radii"/> over <paramref name="rect"/>, shared by
        /// <see cref="ClipGraphicsByOverflow"/> (a precomputed <see cref="OverflowClipCurve"/>) and
        /// <see cref="TryPushOverflowClip"/> (a live <see cref="CssBox.ComputeInnerRadii"/> call) so the
        /// two don't each carry their own copy of the same "build the path, push it, count it" sequence.
        /// </summary>
        /// <returns>1 if a clip was pushed, 0 if <paramref name="radii"/> has no rounded corner</returns>
        private static int PushRoundedClipIfRounded(RGraphics g, RRect rect, BorderRadii radii)
        {
            if (!radii.IsRounded) return 0;

            using var path = GetRoundRect(g, rect,
                radii.TLX, radii.TLY, radii.TRX, radii.TRY,
                radii.BRX, radii.BRY, radii.BLX, radii.BLY);
            g.PushClip(path);
            return 1;
        }

        /// <summary>
        /// The rectangle an <c>overflow: hidden</c> box clips its descendants to: its padding edge, not
        /// its content edge (<see href="https://www.w3.org/TR/css-overflow-3/#overflow-propagation">CSS
        /// Overflow Level 3 §2</see>).
        /// </summary>
        /// <param name="box">the clipping box, read for its border widths only</param>
        /// <param name="borderBox">
        /// that box's border box. Passed in rather than read from <paramref name="box"/> so a caller
        /// holding a <see cref="Fragments.BoxGeometrySnapshot"/> can resolve the box at the position that
        /// snapshot recorded — the same box can be shown at several places in one document.
        /// </param>
        internal static RRect PaddingEdgeOf(CssBox box, RRect borderBox) => RRect.FromLTRB(
            borderBox.Left + box.ActualBorderLeftWidth,
            borderBox.Top + box.ActualBorderTopWidth,
            borderBox.Right - box.ActualBorderRightWidth,
            borderBox.Bottom - box.ActualBorderBottomWidth);

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
                pushed += TryPushOverflowClip(g, ancestor, originY);
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

            path.CloseFigure();
            return path;
        }
    }
}