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
        /// Clip the region the graphics will draw on by the overflow style of the containing block.<br/>
        /// Recursively travel up the tree to find containing block that has overflow style set to hidden. if not
        /// block found there will be no clipping and null will be returned.
        /// </summary>
        /// <param name="g">the graphics to clip</param>
        /// <param name="box">the box that is rendered to get containing blocks</param>
        /// <returns>true - was clipped, false - not clipped</returns>
        public static bool ClipGraphicsByOverflow(RGraphics g, CssBox box)
        {
            var containingBlock = box.ContainingBlock;
            while (true)
            {
                if (TryPushOverflowClip(g, containingBlock, box))
                {
                    return true;
                }

                var cBlock = containingBlock.ContainingBlock;
                if (cBlock == containingBlock)
                    return false;
                containingBlock = cBlock;
            }
        }

        /// <summary>
        /// Pushes <paramref name="overflowBox"/>'s own clip (padding-edge rect, per CSS spec) if it has
        /// <c>overflow: hidden</c>, scoped for painting <paramref name="forBox"/> (whose fixed-ness
        /// determines whether the rect needs the page scroll offset applied). Shared by
        /// <see cref="ClipGraphicsByOverflow"/> (which walks up a single box's own containing-block
        /// chain looking for the nearest hidden ancestor) and <see cref="PushAncestorOverflowClips"/>
        /// (which already knows the exact ancestor chain to check, with no walking needed).
        /// </summary>
        private static bool TryPushOverflowClip(RGraphics g, CssBox overflowBox, CssBox forBox)
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

            if (!forBox.IsFixed)
                rect.Offset(forBox.HtmlContainer!.ScrollOffset);

            rect.Intersect(prevClip);
            g.PushClip(rect);
            return true;
        }

        /// <summary>
        /// Pushes the <c>overflow: hidden</c> clip of every box in <paramref name="ancestors"/> that has
        /// one, in order. Used when painting a box that <see cref="DomUtils.FlattenStackingContext"/>
        /// hoisted past one or more plain ancestor boxes for stacking-context z-order purposes - since it
        /// paints via the claiming stacking context's own paint loop rather than those ancestors' own
        /// (nested) <c>Paint()</c> calls, their overflow clipping isn't already active on the graphics
        /// clip stack the way it would be for normally-painted content, and must be applied explicitly
        /// here instead. <paramref name="ancestors"/> is the exact, already-known chain of DOM ancestors
        /// between the claiming stacking context and the box being painted (see
        /// <see cref="DomUtils.StackingParticipant"/>), so - unlike <see cref="ClipGraphicsByOverflow"/> -
        /// no containing-block walk/search is needed; each ancestor is checked directly.
        /// </summary>
        /// <returns>the number of clips actually pushed (callers must pop exactly this many afterward)</returns>
        public static int PushAncestorOverflowClips(RGraphics g, CssBox forBox, IReadOnlyList<CssBox> ancestors)
        {
            var pushed = 0;
            foreach (var ancestor in ancestors)
            {
                if (TryPushOverflowClip(g, ancestor, forBox)) pushed++;
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