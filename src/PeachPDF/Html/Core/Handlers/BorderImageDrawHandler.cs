using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;
using System;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Paints <c>border-image</c> (CSS Backgrounds and Borders Module Level 3 §13) via the standard 9-slice
    /// algorithm: a box's resolved <c>border-image-source</c> is sliced into 4 corners (always scaled 1:1 to
    /// fit, never tiled), 4 edges (stretched/tiled per <c>border-image-repeat</c> along their one free axis)
    /// and an optional center (painted only when <c>fill</c> is declared), following
    /// <see href="https://www.w3.org/TR/css-backgrounds-3/#border-image-process">§13.6's nine-region
    /// diagram</see>.
    /// </summary>
    internal static class BorderImageDrawHandler
    {
        private const int MaxTilesPerAxis = 20_000;

        /// <summary>
        /// Paints <paramref name="box"/>'s <c>border-image</c> over <paramref name="borderBoxRect"/>
        /// (extended by <c>border-image-outset</c>) if it has one that actually resolved to a real, loaded
        /// image. Per spec, a resolved <c>border-image-source</c> replaces the ordinary <c>border-style</c>
        /// rendering entirely - the caller skips <see cref="BordersDrawHandler.DrawBoxBorders"/> whenever
        /// this returns <c>true</c>, and falls back to it (border-image not declared, or its source failed
        /// to load) whenever this returns <c>false</c>.
        /// </summary>
        internal static bool TryDrawBorderImage(RGraphics g, CssBox box, RRect borderBoxRect)
        {
            if (box.BorderImageSource is not { } source) return false;

            using var resolved = ResolveSourceImage(g, source, box, borderBoxRect);
            if (resolved is not { } image || image.NaturalWidth <= 0 || image.NaturalHeight <= 0) return false;

            var borderTop = box.ActualBorderTopWidth;
            var borderRight = box.ActualBorderRightWidth;
            var borderBottom = box.ActualBorderBottomWidth;
            var borderLeft = box.ActualBorderLeftWidth;

            var outset = BorderImageLayerResolver.ResolveOutset(
                box.BorderImageOutset, borderTop, borderRight, borderBottom, borderLeft, box);

            var areaRect = new RRect(
                borderBoxRect.X - outset.Left,
                borderBoxRect.Y - outset.Top,
                borderBoxRect.Width + outset.Left + outset.Right,
                borderBoxRect.Height + outset.Top + outset.Bottom);

            if (areaRect.Width <= 0 || areaRect.Height <= 0) return false;

            var slice = BorderImageLayerResolver.ResolveSlice(box.BorderImageSlice, image.NaturalWidth, image.NaturalHeight);

            var width = BorderImageLayerResolver.ResolveWidth(box.BorderImageWidth,
                borderTop, borderRight, borderBottom, borderLeft, areaRect.Width, areaRect.Height, box);

            var (repeatHorizontal, repeatVertical) = BorderImageLayerResolver.ResolveRepeat(box.BorderImageRepeat);

            var pushedClip = false;
            if (box.IsRounded)
            {
                var radii = box.ComputeRadii(areaRect);
                if (radii.IsRounded)
                {
                    var roundedPath = RenderUtils.GetRoundRect(g, areaRect,
                        radii.TLX, radii.TLY, radii.TRX, radii.TRY, radii.BRX, radii.BRY, radii.BLX, radii.BLY);
                    g.PushClip(roundedPath);
                    pushedClip = true;
                }
            }

            try
            {
                PaintNineSlice(g, image.Image, image.NaturalWidth, image.NaturalHeight, slice, areaRect, width,
                    repeatHorizontal, repeatVertical);
            }
            finally
            {
                if (pushedClip) g.PopClip();
            }

            return true;
        }

        /// <summary>
        /// One resolved <c>border-image-source</c>, ready for slicing: a real <see cref="RImage"/> plus its
        /// "natural" size in the same pixel space <see cref="RGraphics.DrawImage(RImage,RRect,RRect)"/>'s own
        /// <c>srcRect</c> expects. A raster <c>url()</c> uses its own real natural size; a gradient, SVG, or
        /// any other source with no natural-size concept of its own is rendered once into a
        /// <see cref="RGraphics.CreateTile"/> tile sized to the border-image area itself - mirroring
        /// <see cref="CssImagePainter"/>'s identical "auto == fills the box" treatment of a generated
        /// background-image layer - so its own percentages in <c>border-image-slice</c> resolve against that
        /// same area. <see cref="Dispose"/> disposes the tile's own graphics context/image where one was
        /// created; a raster source's <see cref="RImage"/> is owned by its <see cref="CssImage.Url"/> and is
        /// left alone.
        /// </summary>
        private readonly struct ResolvedSourceImage(RImage image, double naturalWidth, double naturalHeight, bool ownsImage) : IDisposable
        {
            public RImage Image { get; } = image;
            public double NaturalWidth { get; } = naturalWidth;
            public double NaturalHeight { get; } = naturalHeight;

            public void Dispose()
            {
                if (ownsImage) Image.Dispose();
            }
        }

        private static ResolvedSourceImage? ResolveSourceImage(RGraphics g, CssImage source, CssBox box, RRect borderBoxRect)
        {
            if (source is CssImage.Url { Image: { } raster })
                return new ResolvedSourceImage(raster, raster.Width, raster.Height, ownsImage: false);

            // Every other source kind - an SVG url(), or a gradient - has no natural size of its own, so it
            // is rendered once into a tile sized to the border-image area, exactly as a background-image
            // layer with no explicit background-size renders one (CssImagePainter.PaintGradientLayer/
            // PaintSvgLayer's own "isFullBox"/tile-at-resolved-size treatment) - border-image has no sizing
            // property of its own to resolve against instead.
            var tileWidth = borderBoxRect.Width;
            var tileHeight = borderBoxRect.Height;
            if (tileWidth <= 0 || tileHeight <= 0) return null;

            var tile = g.CreateTile(tileWidth, tileHeight);
            if (tile is not { } t) return null; // no real page/document context (e.g. a measure-only pass)

            var tileRect = new RRect(0, 0, tileWidth, tileHeight);
            switch (source)
            {
                case CssImage.Url { SvgDocument: { } svg }:
                    SvgRenderer.RenderInto(t.Graphics, svg, tileRect);
                    break;
                case CssImage.LinearGradient lg:
                    using (var brush = CssImagePainter.GetLinearGradientBrush(t.Graphics, lg.Gradient, tileRect, box, null))
                        t.Graphics.DrawRectangle(brush, 0, 0, tileWidth, tileHeight);
                    break;
                case CssImage.RadialGradient rg:
                    using (var brush = CssImagePainter.GetRadialGradientBrush(t.Graphics, rg.Gradient, tileRect, box, null))
                        t.Graphics.DrawRectangle(brush, 0, 0, tileWidth, tileHeight);
                    break;
                case CssImage.ConicGradient cg:
                    using (var brush = CssImagePainter.GetConicGradientBrush(t.Graphics, cg.Gradient, tileRect))
                        t.Graphics.DrawRectangle(brush, 0, 0, tileWidth, tileHeight);
                    break;
                default:
                    t.Graphics.Dispose();
                    t.Image.Dispose();
                    return null;
            }

            t.Graphics.Dispose();
            return new ResolvedSourceImage(t.Image, tileWidth, tileHeight, ownsImage: true);
        }

        /// <summary>
        /// The nine-region layout itself (CSS Backgrounds and Borders 3 §13.6): four corners scaled to fit
        /// exactly (never tiled), four edges tiled along their one free axis per
        /// <paramref name="repeatHorizontal"/>/<paramref name="repeatVertical"/>, and (only when
        /// <paramref name="slice"/> declared <c>fill</c>) a center tiled along both axes.
        /// </summary>
        private static void PaintNineSlice(RGraphics g, RImage image, double naturalWidth, double naturalHeight,
            BorderImageSlice slice, RRect areaRect, BorderImageSides width,
            BorderRepeat repeatHorizontal, BorderRepeat repeatVertical)
        {
            var srcMidLeft = slice.Left;
            var srcMidTop = slice.Top;
            var srcMidRight = naturalWidth - slice.Right;
            var srcMidBottom = naturalHeight - slice.Bottom;

            var destMidLeft = areaRect.Left + width.Left;
            var destMidTop = areaRect.Top + width.Top;
            var destMidRight = areaRect.Right - width.Right;
            var destMidBottom = areaRect.Bottom - width.Bottom;

            // Corners
            DrawScaled(g, image,
                new RRect(0, 0, srcMidLeft, srcMidTop),
                new RRect(areaRect.Left, areaRect.Top, destMidLeft - areaRect.Left, destMidTop - areaRect.Top));
            DrawScaled(g, image,
                new RRect(srcMidRight, 0, naturalWidth - srcMidRight, srcMidTop),
                new RRect(destMidRight, areaRect.Top, areaRect.Right - destMidRight, destMidTop - areaRect.Top));
            DrawScaled(g, image,
                new RRect(0, srcMidBottom, srcMidLeft, naturalHeight - srcMidBottom),
                new RRect(areaRect.Left, destMidBottom, destMidLeft - areaRect.Left, areaRect.Bottom - destMidBottom));
            DrawScaled(g, image,
                new RRect(srcMidRight, srcMidBottom, naturalWidth - srcMidRight, naturalHeight - srcMidBottom),
                new RRect(destMidRight, destMidBottom, areaRect.Right - destMidRight, areaRect.Bottom - destMidBottom));

            // Edges - tiled along their one free axis
            DrawEdge(g, image,
                new RRect(srcMidLeft, 0, srcMidRight - srcMidLeft, srcMidTop),
                new RRect(destMidLeft, areaRect.Top, destMidRight - destMidLeft, destMidTop - areaRect.Top),
                tileAlongX: true, repeatHorizontal);
            DrawEdge(g, image,
                new RRect(srcMidLeft, srcMidBottom, srcMidRight - srcMidLeft, naturalHeight - srcMidBottom),
                new RRect(destMidLeft, destMidBottom, destMidRight - destMidLeft, areaRect.Bottom - destMidBottom),
                tileAlongX: true, repeatHorizontal);
            DrawEdge(g, image,
                new RRect(0, srcMidTop, srcMidLeft, srcMidBottom - srcMidTop),
                new RRect(areaRect.Left, destMidTop, destMidLeft - areaRect.Left, destMidBottom - destMidTop),
                tileAlongX: false, repeatVertical);
            DrawEdge(g, image,
                new RRect(srcMidRight, srcMidTop, naturalWidth - srcMidRight, srcMidBottom - srcMidTop),
                new RRect(destMidRight, destMidTop, areaRect.Right - destMidRight, destMidBottom - destMidTop),
                tileAlongX: false, repeatVertical);

            // Center
            if (slice.Fill)
            {
                DrawMiddle(g, image,
                    new RRect(srcMidLeft, srcMidTop, srcMidRight - srcMidLeft, srcMidBottom - srcMidTop),
                    new RRect(destMidLeft, destMidTop, destMidRight - destMidLeft, destMidBottom - destMidTop),
                    repeatHorizontal, repeatVertical);
            }
        }

        private static void DrawScaled(RGraphics g, RImage image, RRect src, RRect dest)
        {
            if (src.Width <= 0 || src.Height <= 0 || dest.Width <= 0 || dest.Height <= 0) return;
            g.DrawImage(image, dest, src);
        }

        /// <summary>
        /// Draws one edge region, stretched to fill it in one call under <c>stretch</c>, or tiled
        /// edge-to-edge along its one free axis otherwise - clipped to <paramref name="dest"/> itself so a
        /// partial final tile (the source slice's own natural aspect against the edge's fixed cross-axis
        /// thickness rarely divides it evenly) is cut off rather than overrunning into a neighboring corner.
        /// <c>round</c> is accepted but not distinguished from <c>repeat</c> here - it degrades to the same
        /// edge-to-edge tiling rather than resizing each tile to fit an integer count evenly, matching this
        /// repo's own pre-existing <c>background-repeat: round</c> treatment (see the accepted-gap note).
        /// </summary>
        private static void DrawEdge(RGraphics g, RImage image, RRect src, RRect dest, bool tileAlongX, BorderRepeat repeat)
        {
            if (src.Width <= 0 || src.Height <= 0 || dest.Width <= 0 || dest.Height <= 0) return;

            if (repeat == BorderRepeat.Stretch)
            {
                g.DrawImage(image, dest, src);
                return;
            }

            var wasInterpolate = image.Interpolate;
            image.Interpolate = false;
            g.PushClip(dest);
            try
            {
                if (tileAlongX)
                {
                    var tileWidth = src.Width * (dest.Height / src.Height);
                    if (tileWidth <= 0) return;
                    var x = dest.Left;
                    for (var i = 0; i < MaxTilesPerAxis && x < dest.Right; i++, x += tileWidth)
                        g.DrawImage(image, new RRect(x, dest.Top, tileWidth, dest.Height), src);
                }
                else
                {
                    var tileHeight = src.Height * (dest.Width / src.Width);
                    if (tileHeight <= 0) return;
                    var y = dest.Top;
                    for (var i = 0; i < MaxTilesPerAxis && y < dest.Bottom; i++, y += tileHeight)
                        g.DrawImage(image, new RRect(dest.Left, y, dest.Width, tileHeight), src);
                }
            }
            finally
            {
                g.PopClip();
                image.Interpolate = wasInterpolate;
            }
        }

        /// <summary>
        /// Draws the center region: a single stretched call when both axes are <c>stretch</c>, otherwise
        /// tiled along whichever axis (or both) is <c>repeat</c>/<c>round</c> - the stretched axis's own
        /// tile size always spans <paramref name="dest"/>'s full extent on that axis, so a "repeat
        /// horizontally, stretch vertically" center still comes out one tile tall. See <see cref="DrawEdge"/>
        /// for why <c>round</c> is not distinguished from <c>repeat</c>.
        /// </summary>
        private static void DrawMiddle(RGraphics g, RImage image, RRect src, RRect dest,
            BorderRepeat repeatHorizontal, BorderRepeat repeatVertical)
        {
            if (src.Width <= 0 || src.Height <= 0 || dest.Width <= 0 || dest.Height <= 0) return;

            if (repeatHorizontal == BorderRepeat.Stretch && repeatVertical == BorderRepeat.Stretch)
            {
                g.DrawImage(image, dest, src);
                return;
            }

            var tileWidth = repeatHorizontal == BorderRepeat.Stretch ? dest.Width : src.Width;
            var tileHeight = repeatVertical == BorderRepeat.Stretch ? dest.Height : src.Height;
            if (tileWidth <= 0 || tileHeight <= 0) return;

            var wasInterpolate = image.Interpolate;
            image.Interpolate = false;
            g.PushClip(dest);
            try
            {
                var y = dest.Top;
                for (var j = 0; j < MaxTilesPerAxis && y < dest.Bottom; j++, y += tileHeight)
                {
                    var x = dest.Left;
                    for (var i = 0; i < MaxTilesPerAxis && x < dest.Right; i++, x += tileWidth)
                        g.DrawImage(image, new RRect(x, y, tileWidth, tileHeight), src);
                }
            }
            finally
            {
                g.PopClip();
                image.Interpolate = wasInterpolate;
            }
        }
    }
}
