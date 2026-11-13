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

using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.Handlers
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
        /// <param name="image">the image to draw (a natural bitmap, or a <see cref="Canvas.CreateTile"/>-produced tile standing in for a gradient layer)</param>
        /// <param name="sizeValue">the resolved background-size CSS value for this layer</param>
        /// <param name="positionValue">the resolved background-position CSS value for this layer</param>
        /// <param name="backgroundRepeat">the background-repeat CSS value</param>
        /// <param name="positioningRect">the background positioning area (background-origin)</param>
        /// <param name="clipRect">the background painting area (background-clip), used when <paramref name="roundedClipPath"/> is null</param>
        /// <param name="roundedClipPath">a rounded-corner clip path to use instead of <paramref name="clipRect"/> when the box has border-radius</param>
        /// <param name="box">the box the image is painted on, needed for em/rem-relative length resolution</param>
        /// <param name="intrinsicSizeInCssPixels">true when <paramref name="image"/> is a natural bitmap whose
        /// Width/Height are CSS pixels (1px = 1/96in) needing conversion to layout points; false when it is a
        /// <see cref="Canvas.CreateTile"/>-produced tile already sized in layout units</param>
        /// <param name="imageDensity">the resolution in dppx an <c>image-set()</c> option declared for a natural bitmap; 1 otherwise</param>
        public static void DrawBackgroundImage(
            Canvas g, Image image,
            string sizeValue, string positionValue, string backgroundRepeat,
            Rect positioningRect, Rect clipRect,
            GraphicsPath? roundedClipPath,
            CssBox box,
            bool intrinsicSizeInCssPixels,
            double imageDensity = 1)
        {
            var pxFactor = intrinsicSizeInCssPixels ? Length.PointsPerPx * g.PixelsPerPoint : 1d;
            backgroundRepeat = BackgroundLayerResolver.NormalizeRepeat(backgroundRepeat);

            // image-orientation turns a natural bitmap upright; a generated tile (gradient) has no orientation.
            var orientation = intrinsicSizeInCssPixels ? ImageOrientationResolver.Effective(box, image) : ImageOrientation.Upright;
            var orientedWidth = orientation.SwapsAxes ? image.Height : image.Width;
            var orientedHeight = orientation.SwapsAxes ? image.Width : image.Height;

            // image-resolution sets how many of a natural bitmap's pixels make a CSS pixel; a generated tile is already in layout units.
            var (naturalWidth, naturalHeight) = intrinsicSizeInCssPixels
                ? ImageResolutionResolver.NaturalSize(box, image)
                : (orientedWidth, orientedHeight);
            // An image-set() option declares its own resolution: its pixels per CSS pixel.
            naturalWidth /= imageDensity;
            naturalHeight /= imageDensity;
            var intrinsicWidth = orientedWidth > 0 ? naturalWidth * pxFactor : (double?)null;
            var intrinsicHeight = orientedHeight > 0 ? naturalHeight * pxFactor : (double?)null;
            var intrinsicRatio = intrinsicWidth is not null && intrinsicHeight is not null
                ? intrinsicWidth.Value / intrinsicHeight.Value
                : (double?)null;

            var (tileWidth, tileHeight) = BackgroundLayerResolver.ResolveSize(
                sizeValue, positioningRect.Width, positioningRect.Height,
                intrinsicWidth, intrinsicHeight, intrinsicRatio, box);

            if (tileWidth <= 0 || tileHeight <= 0)
                return;

            var (horizontalRepeat, verticalRepeat) = SplitRepeat(backgroundRepeat);
            (tileWidth, tileHeight) = RoundTiles(horizontalRepeat, verticalRepeat, sizeValue, positioningRect, tileWidth, tileHeight);

            var (offsetX, offsetY) = BackgroundLayerResolver.ResolvePosition(
                positionValue, positioningRect.Width, positioningRect.Height, tileWidth, tileHeight, box);

            var location = new PaintPoint(positioningRect.X + offsetX, positioningRect.Y + offsetY);

            // The whole picture, in oriented pixels (the same as the stored raster's when there is no rotation).
            var srcRect = new Rect(0, 0, orientedWidth, orientedHeight);
            var destRect = new Rect(location, new Size(tileWidth, tileHeight));

            // The actual visible painting area - clipRect (rounded corners only ever shrink this
            // further, so the plain rectangle is always a safe superset) intersected with the current
            // graphics clip. Used below to bound the repeat loops to the tiles that can actually be
            // seen, not positioningRect's full extent (the whole page viewport for a `background-
            // attachment: fixed` layer) - a small clipped element otherwise repeats its tile across the
            // entire page, emitting tens of thousands of invisible-but-present draw operators.
            var visibleRect = clipRect;
            visibleRect.Intersect(g.GetClip());

            // clip to the painting area (background-clip, rounded if the box has border-radius)
            // intersected with the current graphics clip
            if (roundedClipPath != null)
            {
                g.PushClip(roundedClipPath);
            }
            else
            {
                g.PushClip(visibleRect);
            }

            // A repeating tile is drawn many times edge-to-edge (or, for two intentionally-offset
            // layers meant to interlock into one solid color - Acid2's own checkerboard trick - drawn
            // over another tiled layer) - any interpolation/smoothing at all leaves a soft seam between
            // adjacent copies, or between the two layers, that never resolves crisp/solid regardless of
            // rasterization DPI. Force nearest-neighbor for the duration of a repeating draw, restoring
            // afterward since the same Image may be reused elsewhere (a plain <img>, or a differently-
            // configured background layer) where smoothing is still wanted. See Image.Interpolate's
            // own doc comment.
            // An explicit `image-rendering` wins over that default.
            var sampling = ImageRenderingResolver.Resolve(
                box.ImageRendering.Value, backgroundRepeat != "no-repeat" ? ImageSampling.Pixelated : ImageSampling.Automatic);

            // Bound the repeat loops to the tiles that can actually land in the visible area, not
            // positioningRect's full extent - see visibleRect's own doc comment above.
            // Not bounded by the positioning area as well: a tile repeats outward from it as far as the painting area reaches, which is further
            // than it when background-clip is border-box and background-origin padding-box (the default pair), under a transparent border.
            var tileBounds = visibleRect;

            if (horizontalRepeat is "space" or "round" || verticalRepeat is "space" or "round")
            {
                var xs = AxisPositions(horizontalRepeat, positioningRect.X, positioningRect.Width, destRect.X, destRect.Width, tileBounds.X, tileBounds.Right);
                var ys = AxisPositions(verticalRepeat, positioningRect.Y, positioningRect.Height, destRect.Y, destRect.Height, tileBounds.Y, tileBounds.Bottom);
                foreach (var y in ys)
                    foreach (var x in xs)
                        ImageOrientationPainter.Draw(g, image, new Rect(x, y, destRect.Width, destRect.Height), srcRect, sampling, orientation);
            }
            else
            {
                switch (backgroundRepeat)
                {
                    case "no-repeat":
                        ImageOrientationPainter.Draw(g, image, destRect, srcRect, sampling, orientation);
                        break;
                    case "repeat-x":
                        DrawRepeatX(g, image, tileBounds, srcRect, destRect, sampling, orientation);
                        break;
                    case "repeat-y":
                        DrawRepeatY(g, image, tileBounds, srcRect, destRect, sampling, orientation);
                        break;
                    default:
                        DrawRepeat(g, image, tileBounds, srcRect, destRect, sampling, orientation);
                        break;
                }
            }

            g.PopClip();
        }


        #region Private methods

        /// <summary>The horizontal and vertical repeat of a normalized <c>background-repeat</c> value (<c>repeat-x</c> and <c>repeat-y</c> being the two-value forms they name).</summary>
        private static (string Horizontal, string Vertical) SplitRepeat(string normalized)
        {
            switch (normalized)
            {
                case "repeat-x": return ("repeat", "no-repeat");
                case "repeat-y": return ("no-repeat", "repeat");
            }

            var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? (parts[0], parts[1]) : (normalized, normalized);
        }

        /// <summary>
        /// <c>round</c> (CSS Backgrounds 3 section 3.4): the tile is rescaled so a whole number of them fills the positioning area along that axis. When the
        /// other axis' size is <c>auto</c> and it does not round itself, it is rescaled by the same factor to keep the image's proportions.
        /// </summary>
        private static (double Width, double Height) RoundTiles(string horizontal, string vertical, string sizeValue, Rect area, double width, double height)
        {
            var parts = (sizeValue ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var heightIsAuto = parts.Length < 2 || parts[1].Equals("auto", StringComparison.OrdinalIgnoreCase);
            var widthIsAuto = parts.Length < 1 || parts[0].Equals("auto", StringComparison.OrdinalIgnoreCase);
            var (newWidth, newHeight) = (width, height);

            if (horizontal == "round" && area.Width > 0)
            {
                newWidth = area.Width / Math.Max(1, Math.Round(area.Width / width));
                if (vertical != "round" && heightIsAuto) newHeight = height * newWidth / width;
            }

            if (vertical == "round" && area.Height > 0)
            {
                newHeight = area.Height / Math.Max(1, Math.Round(area.Height / height));
                if (horizontal != "round" && widthIsAuto) newWidth = width * newHeight / height;
            }

            return (newWidth, newHeight);
        }

        /// <summary>
        /// Where the tiles start along one axis. <c>space</c> puts as many whole tiles as fit across the positioning area with the leftover shared between
        /// them (one tile that fits is placed where <c>background-position</c> puts it; none that fit is that tile, clipped); <c>round</c> (already resized)
        /// and <c>repeat</c> repeat from the positioned tile across the area that shows; <c>no-repeat</c> is the one tile.
        /// </summary>
        private static List<double> AxisPositions(string mode, double areaStart, double areaSize, double tileStart, double tileSize, double visibleStart, double visibleEnd)
        {
            var positions = new List<double>();
            switch (mode)
            {
                case "space":
                {
                    var count = (int)Math.Floor((areaSize + 0.01) / tileSize);
                    if (count <= 1)
                    {
                        positions.Add(tileStart);
                        break;
                    }

                    // The tiles go on at the same pitch past the positioning area, as far as the painting area shows them (a transparent border).
                    var pitch = tileSize + (areaSize - count * tileSize) / (count - 1);
                    var first = (int)Math.Floor((visibleStart - areaStart) / pitch);
                    var last = Math.Max(count - 1, (int)Math.Ceiling((visibleEnd - areaStart) / pitch));
                    for (var i = Math.Min(0, first); i <= last && i - Math.Min(0, first) < MaxTilesPerAxis; i++)
                    {
                        var position = areaStart + i * pitch;
                        if (position + tileSize > visibleStart && position < visibleEnd || i is >= 0 && i < count)
                            positions.Add(position);
                    }

                    break;
                }
                case "no-repeat":
                    positions.Add(tileStart);
                    break;
                default:
                {
                    var x = FirstTileStart(tileStart, tileSize, visibleStart);
                    for (var i = 0; i < MaxTilesPerAxis && x < visibleEnd; i++, x += tileSize)
                        positions.Add(x);
                    break;
                }
            }

            return positions;
        }

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
        private static void DrawRepeatX(Canvas g, Image image, Rect rectangle, Rect srcRect, Rect destRect, ImageSampling sampling, ImageOrientation orientation)
        {
            var startX = FirstTileStart(destRect.X, destRect.Width, rectangle.X);

            var x = startX;
            for (var i = 0; i < MaxTilesPerAxis && x < rectangle.Right; i++, x += destRect.Width)
                ImageOrientationPainter.Draw(g, image, new Rect(x, destRect.Y, destRect.Width, destRect.Height), srcRect, sampling, orientation);
        }

        /// <summary>
        /// Draw the background image repeating it over the Y axis, at the resolved tile size.
        /// </summary>
        private static void DrawRepeatY(Canvas g, Image image, Rect rectangle, Rect srcRect, Rect destRect, ImageSampling sampling, ImageOrientation orientation)
        {
            var startY = FirstTileStart(destRect.Y, destRect.Height, rectangle.Y);

            var y = startY;
            for (var i = 0; i < MaxTilesPerAxis && y < rectangle.Bottom; i++, y += destRect.Height)
                ImageOrientationPainter.Draw(g, image, new Rect(destRect.X, y, destRect.Width, destRect.Height), srcRect, sampling, orientation);
        }

        /// <summary>
        /// Draw the background image repeating it over both X and Y axes, at the resolved tile size.
        /// </summary>
        private static void DrawRepeat(Canvas g, Image image, Rect rectangle, Rect srcRect, Rect destRect, ImageSampling sampling, ImageOrientation orientation)
        {
            var startX = FirstTileStart(destRect.X, destRect.Width, rectangle.X);
            var startY = FirstTileStart(destRect.Y, destRect.Height, rectangle.Y);

            var y = startY;
            for (var j = 0; j < MaxTilesPerAxis && y < rectangle.Bottom; j++, y += destRect.Height)
            {
                var x = startX;
                for (var i = 0; i < MaxTilesPerAxis && x < rectangle.Right; i++, x += destRect.Width)
                    ImageOrientationPainter.Draw(g, image, new Rect(x, y, destRect.Width, destRect.Height), srcRect, sampling, orientation);
            }
        }

        #endregion
    }
}
