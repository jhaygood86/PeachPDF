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
                if (containingBlock.Overflow == CssConstants.Hidden)
                {
                    var prevClip = g.GetClip();
                    // CSS spec: overflow clips at the padding edge, not the content edge.
                    // Expand ClientRectangle (content-box) outward by the containing block's padding.
                    var rect = containingBlock.ClientRectangle;
                    rect.X -= containingBlock.ActualPaddingLeft;
                    rect.Width += containingBlock.ActualPaddingLeft + containingBlock.ActualPaddingRight;
                    rect.Y -= containingBlock.ActualPaddingTop;
                    rect.Height += containingBlock.ActualPaddingTop + containingBlock.ActualPaddingBottom;

                    if (!box.IsFixed)
                        rect.Offset(box.HtmlContainer!.ScrollOffset);

                    rect.Intersect(prevClip);
                    g.PushClip(rect);
                    return true;
                }
                else
                {
                    var cBlock = containingBlock.ContainingBlock;
                    if (cBlock == containingBlock)
                        return false;
                    containingBlock = cBlock;
                }
            }
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