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
using System;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Contains all the paint code to paint different background images.
    /// </summary>
    internal static class BackgroundImageDrawHandler
    {
        /// <summary>
        /// Draw the background image of the given box in the given rectangle.<br/>
        /// Handle background-repeat and background-position values.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="image">the image to draw</param>
        /// <param name="backgroundPosition">the background-position CSS value</param>
        /// <param name="backgroundRepeat">the background-repeat CSS value</param>
        /// <param name="positioningRect">the background positioning area (background-origin)</param>
        /// <param name="clipRect">the background painting area (background-clip)</param>
        public static void DrawBackgroundImage(RGraphics g, RImage image, string backgroundPosition, string backgroundRepeat, RRect positioningRect, RRect clipRect)
        {
            var imgSize = new RSize(image.Width, image.Height);

            var location = GetLocation(backgroundPosition, positioningRect, imgSize);

            var srcRect = new RRect(0, 0, imgSize.Width, imgSize.Height);
            var destRect = new RRect(location, imgSize);

            // clip to the painting area (background-clip) intersected with the current graphics clip
            var lClipRect = clipRect;
            lClipRect.Intersect(g.GetClip());
            g.PushClip(lClipRect);

            switch (backgroundRepeat)
            {
                case "no-repeat":
                    g.DrawImage(image, destRect, srcRect);
                    break;
                case "repeat-x":
                    DrawRepeatX(g, image, positioningRect, srcRect, destRect, imgSize);
                    break;
                case "repeat-y":
                    DrawRepeatY(g, image, positioningRect, srcRect, destRect, imgSize);
                    break;
                default:
                    DrawRepeat(g, image, positioningRect, srcRect, destRect, imgSize);
                    break;
            }

            g.PopClip();
        }


        #region Private methods

        /// <summary>
        /// Get top-left location to start drawing the image at depending on background-position value.
        /// </summary>
        private static RPoint GetLocation(string backgroundPosition, RRect rectangle, RSize imgSize)
        {
            double left = rectangle.Left;
            if (backgroundPosition.IndexOf("left", StringComparison.OrdinalIgnoreCase) > -1)
            {
                left = (rectangle.Left + .5f);
            }
            else if (backgroundPosition.IndexOf("right", StringComparison.OrdinalIgnoreCase) > -1)
            {
                left = rectangle.Right - imgSize.Width;
            }
            else if (backgroundPosition.IndexOf("0", StringComparison.OrdinalIgnoreCase) < 0)
            {
                left = (rectangle.Left + (rectangle.Width - imgSize.Width) / 2 + .5f);
            }

            double top = rectangle.Top;
            if (backgroundPosition.IndexOf("top", StringComparison.OrdinalIgnoreCase) > -1)
            {
                top = rectangle.Top;
            }
            else if (backgroundPosition.IndexOf("bottom", StringComparison.OrdinalIgnoreCase) > -1)
            {
                top = rectangle.Bottom - imgSize.Height;
            }
            else if (backgroundPosition.IndexOf("0", StringComparison.OrdinalIgnoreCase) < 0)
            {
                top = (rectangle.Top + (rectangle.Height - imgSize.Height) / 2 + .5f);
            }

            return new RPoint(left, top);
        }

        /// <summary>
        /// Draw the background image repeating it over the X axis.<br/>
        /// Adjust location to left if starting location doesn't include all the range (adjusted to center or right).
        /// </summary>
        private static void DrawRepeatX(RGraphics g, RImage image, RRect rectangle, RRect srcRect, RRect destRect, RSize imgSize)
        {
            while (destRect.X > rectangle.X)
                destRect.X -= imgSize.Width;

            using var brush = g.GetTextureBrush(image, srcRect, destRect.Location);
            g.DrawRectangle(brush, rectangle.X, destRect.Y, rectangle.Width, srcRect.Height);
        }

        /// <summary>
        /// Draw the background image repeating it over the Y axis.<br/>
        /// Adjust location to top if starting location doesn't include all the range (adjusted to center or bottom).
        /// </summary>
        private static void DrawRepeatY(RGraphics g, RImage image, RRect rectangle, RRect srcRect, RRect destRect, RSize imgSize)
        {
            while (destRect.Y > rectangle.Y)
                destRect.Y -= imgSize.Height;

            using var brush = g.GetTextureBrush(image, srcRect, destRect.Location);
            g.DrawRectangle(brush, destRect.X, rectangle.Y, srcRect.Width, rectangle.Height);
        }

        /// <summary>
        /// Draw the background image repeating it over both X and Y axes.<br/>
        /// Adjust location to left-top if starting location doesn't include all the range (adjusted to center or bottom/right).
        /// </summary>
        private static void DrawRepeat(RGraphics g, RImage image, RRect rectangle, RRect srcRect, RRect destRect, RSize imgSize)
        {
            while (destRect.X > rectangle.X)
                destRect.X -= imgSize.Width;
            while (destRect.Y > rectangle.Y)
                destRect.Y -= imgSize.Height;

            using var brush = g.GetTextureBrush(image, srcRect, destRect.Location);
            g.DrawRectangle(brush, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        }

        #endregion
    }
}
