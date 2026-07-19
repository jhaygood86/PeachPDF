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
using PeachPDF.Html.Core.Utils;
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
        /// Handles background-position, background-size, and background-repeat, per CSS
        /// Backgrounds and Borders Module Level 3.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="image">the image to draw (a natural bitmap, or a <see cref="RGraphics.CreateTile"/>-produced tile standing in for a gradient layer)</param>
        /// <param name="sizeValue">the resolved background-size CSS value for this layer</param>
        /// <param name="positionValue">the resolved background-position CSS value for this layer</param>
        /// <param name="backgroundRepeat">the background-repeat CSS value</param>
        /// <param name="positioningRect">the background positioning area (background-origin)</param>
        /// <param name="clipRect">the background painting area (background-clip), used when <paramref name="roundedClipPath"/> is null</param>
        /// <param name="roundedClipPath">a rounded-corner clip path to use instead of <paramref name="clipRect"/> when the box has border-radius</param>
        /// <param name="box">the box the image is painted on, needed for em/rem-relative length resolution</param>
        public static void DrawBackgroundImage(
            RGraphics g, RImage image,
            string sizeValue, string positionValue, string backgroundRepeat,
            RRect positioningRect, RRect clipRect,
            RGraphicsPath? roundedClipPath,
            CssBoxProperties box)
        {
            var intrinsicWidth = image.Width > 0 ? image.Width : (double?)null;
            var intrinsicHeight = image.Height > 0 ? image.Height : (double?)null;
            var intrinsicRatio = intrinsicWidth is not null && intrinsicHeight is not null
                ? intrinsicWidth.Value / intrinsicHeight.Value
                : (double?)null;

            var (tileWidth, tileHeight) = BackgroundLayerResolver.ResolveSize(
                sizeValue, positioningRect.Width, positioningRect.Height,
                intrinsicWidth, intrinsicHeight, intrinsicRatio, box);

            if (tileWidth <= 0 || tileHeight <= 0)
                return;

            var (offsetX, offsetY) = BackgroundLayerResolver.ResolvePosition(
                positionValue, positioningRect.Width, positioningRect.Height, tileWidth, tileHeight, box);

            var location = new RPoint(positioningRect.X + offsetX, positioningRect.Y + offsetY);

            var srcRect = new RRect(0, 0, image.Width, image.Height);
            var destRect = new RRect(location, new RSize(tileWidth, tileHeight));

            // clip to the painting area (background-clip, rounded if the box has border-radius)
            // intersected with the current graphics clip
            if (roundedClipPath != null)
            {
                g.PushClip(roundedClipPath);
            }
            else
            {
                var lClipRect = clipRect;
                lClipRect.Intersect(g.GetClip());
                g.PushClip(lClipRect);
            }

            // A repeating tile is drawn many times edge-to-edge (or, for two intentionally-offset
            // layers meant to interlock into one solid color - Acid2's own checkerboard trick - drawn
            // over another tiled layer) - any interpolation/smoothing at all leaves a soft seam between
            // adjacent copies, or between the two layers, that never resolves crisp/solid regardless of
            // rasterization DPI. Force nearest-neighbor for the duration of a repeating draw, restoring
            // afterward since the same RImage may be reused elsewhere (a plain <img>, or a differently-
            // configured background layer) where smoothing is still wanted. See RImage.Interpolate's
            // own doc comment.
            var wasInterpolate = image.Interpolate;
            if (backgroundRepeat != "no-repeat")
                image.Interpolate = false;

            switch (backgroundRepeat)
            {
                case "no-repeat":
                    g.DrawImage(image, destRect, srcRect);
                    break;
                case "repeat-x":
                    DrawRepeatX(g, image, positioningRect, srcRect, destRect);
                    break;
                case "repeat-y":
                    DrawRepeatY(g, image, positioningRect, srcRect, destRect);
                    break;
                default:
                    DrawRepeat(g, image, positioningRect, srcRect, destRect);
                    break;
            }

            image.Interpolate = wasInterpolate;

            g.PopClip();
        }


        #region Private methods

        /// <summary>
        /// Hard cap on tiles drawn per axis, guarding against a pathologically tiny resolved
        /// background-size (e.g. a fraction-of-a-pixel tile) repeating across a large box from
        /// looping for an unreasonable amount of time or emitting an unreasonable number of PDF
        /// draw operators. Ordinary content never approaches this.
        /// </summary>
        private const int MaxTilesPerAxis = 20_000;

        /// <summary>
        /// Computes the first tile position at or before <paramref name="rangeStart"/>, without
        /// looping (a naive "subtract tileSize until in range" loop can run unreasonably long for
        /// a tiny tile size positioned far from the range start).
        /// </summary>
        private static double FirstTileStart(double tileStart, double tileSize, double rangeStart)
        {
            if (tileStart <= rangeStart)
                return tileStart;

            var tilesBack = Math.Ceiling((tileStart - rangeStart) / tileSize);
            return tileStart - tilesBack * tileSize;
        }

        /// <summary>
        /// Draw the background image repeating it over the X axis, at the resolved tile size.
        /// </summary>
        private static void DrawRepeatX(RGraphics g, RImage image, RRect rectangle, RRect srcRect, RRect destRect)
        {
            var startX = FirstTileStart(destRect.X, destRect.Width, rectangle.X);

            var x = startX;
            for (var i = 0; i < MaxTilesPerAxis && x < rectangle.Right; i++, x += destRect.Width)
                g.DrawImage(image, new RRect(x, destRect.Y, destRect.Width, destRect.Height), srcRect);
        }

        /// <summary>
        /// Draw the background image repeating it over the Y axis, at the resolved tile size.
        /// </summary>
        private static void DrawRepeatY(RGraphics g, RImage image, RRect rectangle, RRect srcRect, RRect destRect)
        {
            var startY = FirstTileStart(destRect.Y, destRect.Height, rectangle.Y);

            var y = startY;
            for (var i = 0; i < MaxTilesPerAxis && y < rectangle.Bottom; i++, y += destRect.Height)
                g.DrawImage(image, new RRect(destRect.X, y, destRect.Width, destRect.Height), srcRect);
        }

        /// <summary>
        /// Draw the background image repeating it over both X and Y axes, at the resolved tile size.
        /// </summary>
        private static void DrawRepeat(RGraphics g, RImage image, RRect rectangle, RRect srcRect, RRect destRect)
        {
            var startX = FirstTileStart(destRect.X, destRect.Width, rectangle.X);
            var startY = FirstTileStart(destRect.Y, destRect.Height, rectangle.Y);

            var y = startY;
            for (var j = 0; j < MaxTilesPerAxis && y < rectangle.Bottom; j++, y += destRect.Height)
            {
                var x = startX;
                for (var i = 0; i < MaxTilesPerAxis && x < rectangle.Right; i++, x += destRect.Width)
                    g.DrawImage(image, new RRect(x, y, destRect.Width, destRect.Height), srcRect);
            }
        }

        #endregion
    }
}
