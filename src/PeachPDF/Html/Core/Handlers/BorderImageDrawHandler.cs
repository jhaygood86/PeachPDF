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

            var slice = BorderImageLayerResolver.ResolveSlice(box.BorderImageSlice, image.NaturalWidth, image.NaturalHeight,
                image.NumberUnit);

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

            // Every region is cut out of the source with a clip (there is no "draw this sub-rectangle"
            // operator in PDF - see GraphicsAdapter.DrawImage's own note), and a smoothing renderer
            // samples across that clip edge: the neighbouring slice's pixels bleed half a source pixel
            // into this one, which at a slice-to-border scale of 6x is a quarter of the whole border. Any
            // seam between two tiles of a repeated edge smooths the same way. Nearest-neighbour keeps each
            // region to its own pixels; restore afterwards, since the same RImage is shared with any <img>
            // or background layer using the same url().
            var wasInterpolate = image.Image.Interpolate;
            image.Image.Interpolate = false;

            try
            {
                PaintNineSlice(g, image.Image, image.NaturalWidth, image.NaturalHeight, slice, areaRect, width,
                    repeatHorizontal, repeatVertical);
            }
            finally
            {
                image.Image.Interpolate = wasInterpolate;
                if (pushedClip) g.PopClip();
            }

            return true;
        }

        /// <summary>
        /// One resolved <c>border-image-source</c>, ready for slicing: a real <see cref="RImage"/> plus its
        /// "natural" size in the same pixel space <see cref="RGraphics.DrawImage(RImage,RRect,RRect)"/>'s own
        /// <c>srcRect</c> expects. A raster <c>url()</c> uses its own real natural size, and an SVG its own
        /// intrinsic size (width/height, or the viewBox they default to); a gradient - or an SVG with no
        /// intrinsic size at all - has nothing to slice against, so it is rendered into a
        /// <see cref="RGraphics.CreateTile"/> tile sized to the border-image area itself, the default object
        /// size CSS Images 3 gives a border-image, mirroring <see cref="CssImagePainter"/>'s identical
        /// "auto == fills the box" treatment of a generated background-image layer. An SVG form is cached
        /// per PDF document and reused at the same size. <see cref="Dispose"/>
        /// disposes only a newly created, uncached tile image; a raster source's <see cref="RImage"/>
        /// belongs to its <see cref="CssImage.Url"/> and is left alone.
        /// </summary>
        /// <param name="image">The resolved image itself - a raster's own <see cref="RImage"/>, or a tile.</param>
        /// <param name="naturalWidth">The image's natural width, the horizontal basis every slice is cut against.</param>
        /// <param name="naturalHeight">The image's natural height, the vertical basis every slice is cut against.</param>
        /// <param name="numberUnit">
        /// How much of <see cref="NaturalWidth"/>/<see cref="NaturalHeight"/> one bare
        /// <c>border-image-slice</c> <c>&lt;number&gt;</c> buys. A raster's natural size is counted in its
        /// own device pixels and a number "represents pixels in the image", so the two already agree (1).
        /// Every other source is rendered into a tile measured in layout points, while a number there is a
        /// vector coordinate / CSS pixel - so one number is <see cref="Length.PointsPerPx"/> of it.
        /// </param>
        /// <param name="ownsImage">
        /// Whether <see cref="Dispose"/> owns <paramref name="image"/> - true only for a tile created for
        /// this paint alone, false for a raster's shared image or a document-cached SVG form.
        /// </param>
        private readonly struct ResolvedSourceImage(RImage image, double naturalWidth, double naturalHeight, double numberUnit, bool ownsImage) : IDisposable
        {
            public RImage Image { get; } = image;
            public double NaturalWidth { get; } = naturalWidth;
            public double NaturalHeight { get; } = naturalHeight;
            public double NumberUnit { get; } = numberUnit;

            public void Dispose()
            {
                if (ownsImage) Image.Dispose();
            }
        }

        private static ResolvedSourceImage? ResolveSourceImage(RGraphics g, CssImage source, CssBox box, RRect borderBoxRect)
        {
            if (source is CssImage.Url { Image: { } raster })
                return new ResolvedSourceImage(raster, raster.Width, raster.Height, numberUnit: 1, ownsImage: false);

            // A gradient has no size of its own, so it is rendered at the border-image area itself - the
            // default object size CSS Images 3 §5.3 hands a border-image - matching a generated background
            // image with auto size.
            var tileWidth = borderBoxRect.Width;
            var tileHeight = borderBoxRect.Height;
            if (tileWidth <= 0 || tileHeight <= 0) return null;

            if (source is CssImage.Url { SvgDocument: { } svg })
            {
                // ...but an SVG that carries its own width/height (or a viewBox to take them from) does
                // have an intrinsic size, and CSS Images 3's default sizing algorithm uses it verbatim when
                // no size is specified - so the slices are cut from the artwork at its own scale, exactly
                // as <img src="x.svg"> would draw it. Sizing it to the border-image area instead stretched
                // the whole drawing over the box before slicing it, which both distorted the motif and made
                // every edge tile the wrong aspect. Only a genuinely sizeless SVG falls back to the area.
                var (intrinsicWidth, intrinsicHeight) = SvgIntrinsicSize.Resolve(svg);
                var svgWidth = intrinsicWidth is > 0 && intrinsicHeight is > 0
                    ? intrinsicWidth.Value * Length.PointsPerPx : tileWidth;
                var svgHeight = intrinsicWidth is > 0 && intrinsicHeight is > 0
                    ? intrinsicHeight.Value * Length.PointsPerPx : tileHeight;

                var form = SvgRenderer.GetOrCreateForm(g, svg, svgWidth, svgHeight);
                return form is null ? null : new ResolvedSourceImage(form, svgWidth, svgHeight,
                    numberUnit: Length.PointsPerPx, ownsImage: g.FormCacheOwner is null);
            }

            var tile = g.CreateTile(tileWidth, tileHeight);
            if (tile is not { } t) return null; // no real page/document context (e.g. a measure-only pass)

            var tileRect = new RRect(0, 0, tileWidth, tileHeight);
            switch (source)
            {
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
            return new ResolvedSourceImage(t.Image, tileWidth, tileHeight, numberUnit: Length.PointsPerPx, ownsImage: true);
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
            }
        }
    }
}
