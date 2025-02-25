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
                    var rect = box.ContainingBlock.ClientRectangle;
                    rect.X -= 2; // TODO:a find better way to fix it
                    rect.Width += 2;

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
        /// Creates a rounded rectangle using the specified corner radius<br/>
        /// NW-----NE
        ///  |       |
        ///  |       |
        /// SW-----SE
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="rect">Rectangle to round</param>
        /// <param name="nwRadius">Radius of the north east corner</param>
        /// <param name="neRadius">Radius of the north west corner</param>
        /// <param name="seRadius">Radius of the south east corner</param>
        /// <param name="swRadius">Radius of the south west corner</param>
        /// <returns>GraphicsPath with the lines of the rounded rectangle ready to be painted</returns>
        public static RGraphicsPath GetRoundRect(RGraphics g, RRect rect, double nwRadius, double neRadius, double seRadius, double swRadius)
        {
            var path = g.GetGraphicsPath();

            path.Start(rect.Left + nwRadius, rect.Top);

            path.LineTo(rect.Right - neRadius, rect.Y);

            if (neRadius > 0f)
                path.ArcTo(rect.Right, rect.Top + neRadius, neRadius, RGraphicsPath.Corner.TopRight);

            path.LineTo(rect.Right, rect.Bottom - seRadius);

            if (seRadius > 0f)
                path.ArcTo(rect.Right - seRadius, rect.Bottom, seRadius, RGraphicsPath.Corner.BottomRight);

            path.LineTo(rect.Left + swRadius, rect.Bottom);

            if (swRadius > 0f)
                path.ArcTo(rect.Left, rect.Bottom - swRadius, swRadius, RGraphicsPath.Corner.BottomLeft);

            path.LineTo(rect.Left, rect.Top + nwRadius);

            if (nwRadius > 0f)
                path.ArcTo(rect.Left + nwRadius, rect.Top, nwRadius, RGraphicsPath.Corner.TopLeft);

            return path;
        }
    }
}