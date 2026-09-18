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
        /// One axis's tiling plan, shared by <see cref="DrawEdge"/> and <see cref="DrawMiddle"/> - see
        /// <see cref="ResolveTiling"/>.
        /// </summary>
        private readonly record struct TilingPlan(double TileExtent, int Count, double Gap);

        /// <summary>
        /// Resolves one axis of <c>border-image-repeat</c> tiling (CSS Backgrounds and Borders 3 §13.5,
        /// which explicitly reuses <see href="https://www.w3.org/TR/css-backgrounds-3/#background-repeat">
        /// <c>background-repeat</c>'s own <c>round</c>/<c>space</c> algorithm</see>) against one axis's
        /// natural tile extent and the destination extent available to tile across. Axis-neutral: the
        /// caller picks which of a rect's two axes <paramref name="naturalExtent"/>/<paramref name="destExtent"/>
        /// name, so this one implementation serves <see cref="DrawEdge"/>'s single free axis and both of
        /// <see cref="DrawMiddle"/>'s independent axes without duplicating the round/space math per call site.
        /// </summary>
        /// <param name="naturalExtent">The tile's own size along this axis, before any resizing.</param>
        /// <param name="destExtent">The destination extent available to tile across.</param>
        /// <param name="mode">Which of the four keywords to resolve.</param>
        /// <returns>
        /// The size each tile should be painted at, how many tiles to paint, and the gap to leave between
        /// one tile's trailing edge and the next one's leading edge (0 except under <c>space</c>).
        /// </returns>
        private static TilingPlan ResolveTiling(double naturalExtent, double destExtent, BorderRepeat mode)
        {
            if (naturalExtent <= 0 || destExtent <= 0) return new TilingPlan(0, 0, 0);

            switch (mode)
            {
                case BorderRepeat.Stretch:
                    return new TilingPlan(destExtent, 1, 0);

                case BorderRepeat.Round:
                {
                    // "The image is repeated as often as will fit ... If it doesn't fit a whole number of
                    // times, it is rescaled so that it does": count = round(dest/natural), min 1, then each
                    // tile is resized to dest/count so the count fits exactly with no partial tile.
                    var count = (int)Math.Clamp(
                        Math.Round(destExtent / naturalExtent, MidpointRounding.AwayFromZero),
                        1, MaxTilesPerAxis);
                    return new TilingPlan(destExtent / count, count, 0);
                }

                case BorderRepeat.Space:
                {
                    // "Repeated as often as will fit ... without being clipped, and then the images are
                    // spaced out to fill the area. The first and last images touch the edges": count =
                    // floor(dest/natural); with fewer than one whole tile fitting, fall back to a single
                    // tile at its natural size rather than clipping or resizing it.
                    var count = (int)Math.Clamp(Math.Floor(destExtent / naturalExtent), 0, MaxTilesPerAxis);
                    if (count < 1) return new TilingPlan(naturalExtent, 1, 0);
                    var gap = count > 1 ? (destExtent - count * naturalExtent) / (count - 1) : 0;
                    return new TilingPlan(naturalExtent, count, gap);
                }

                default: // Repeat - edge-to-edge at natural size; the caller's own clip cuts off the
                         // partial final tile rather than this resizing or spacing anything to fit evenly.
                {
                    var count = (int)Math.Clamp(Math.Ceiling(destExtent / naturalExtent), 1, MaxTilesPerAxis);
                    return new TilingPlan(naturalExtent, count, 0);
                }
            }
        }

        /// <summary>
        /// Draws one edge region, stretched to fill it in one call under <c>stretch</c>, or tiled along its
        /// one free axis otherwise per <see cref="ResolveTiling"/> - clipped to <paramref name="dest"/>
        /// itself so a <c>repeat</c> tiling's partial final tile (the source slice's own natural aspect
        /// against the edge's fixed cross-axis thickness rarely divides it evenly) is cut off rather than
        /// overrunning into a neighboring corner.
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
                    var naturalTileWidth = src.Width * (dest.Height / src.Height);
                    var plan = ResolveTiling(naturalTileWidth, dest.Width, repeat);
                    if (plan.TileExtent <= 0 || plan.Count <= 0) return;
                    var x = dest.Left;
                    for (var i = 0; i < plan.Count; i++, x += plan.TileExtent + plan.Gap)
                        g.DrawImage(image, new RRect(x, dest.Top, plan.TileExtent, dest.Height), src);
                }
                else
                {
                    var naturalTileHeight = src.Height * (dest.Width / src.Width);
                    var plan = ResolveTiling(naturalTileHeight, dest.Height, repeat);
                    if (plan.TileExtent <= 0 || plan.Count <= 0) return;
                    var y = dest.Top;
                    for (var i = 0; i < plan.Count; i++, y += plan.TileExtent + plan.Gap)
                        g.DrawImage(image, new RRect(dest.Left, y, dest.Width, plan.TileExtent), src);
                }
            }
            finally
            {
                g.PopClip();
            }
        }

        /// <summary>
        /// Draws the center region: a single stretched call when both axes are <c>stretch</c>, otherwise
        /// tiled along whichever axis (or both) is <c>repeat</c>/<c>round</c>/<c>space</c> per
        /// <see cref="ResolveTiling"/>, resolved independently per axis - so a "repeat horizontally, stretch
        /// vertically" center still comes out one tile tall, each with its own tile size/count/gap.
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

            var horizontal = ResolveTiling(src.Width, dest.Width, repeatHorizontal);
            var vertical = ResolveTiling(src.Height, dest.Height, repeatVertical);
            if (horizontal.TileExtent <= 0 || horizontal.Count <= 0 ||
                vertical.TileExtent <= 0 || vertical.Count <= 0) return;

            g.PushClip(dest);
            try
            {
                var y = dest.Top;
                for (var j = 0; j < vertical.Count; j++, y += vertical.TileExtent + vertical.Gap)
                {
                    var x = dest.Left;
                    for (var i = 0; i < horizontal.Count; i++, x += horizontal.TileExtent + horizontal.Gap)
                        g.DrawImage(image, new RRect(x, y, horizontal.TileExtent, vertical.TileExtent), src);
                }
            }
            finally
            {
                g.PopClip();
            }
        }
    }
}
