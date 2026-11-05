using CoreGraphics;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachDrawing.CoreGraphics;

public sealed partial class CoreGraphicsCanvas
{
    // Images from another backend (anything that is not this one's own) are converted once per canvas and released with it.
    private Dictionary<PeachDrawing.Core.Image, CoreGraphicsImage>? _converted;

    private CoreGraphicsImage ImageFor(PeachDrawing.Core.Image image)
    {
        if (image is CoreGraphicsImage own)
            return own;

        _converted ??= new Dictionary<PeachDrawing.Core.Image, CoreGraphicsImage>(ReferenceEqualityComparer.Instance);
        if (!_converted.TryGetValue(image, out var converted))
        {
            converted = image.GetPixels() is { } pixels
                ? CoreGraphicsImage.FromPixels(pixels, _context.Counts)
                : throw new NotSupportedException("An image drawn on a CoreGraphics canvas must come from the CoreGraphics render context, a tile of this canvas, or expose its pixels.");
            _converted[image] = converted;
        }

        return converted;
    }

    private void ReleaseConverted()
    {
        if (_converted is null)
            return;

        foreach (var image in _converted.Values)
            image.Dispose();

        _converted = null;
    }

    /// <summary>The picture an image draws as: a tile's recorded content or a raster image's. Owned by the image, not the caller.</summary>
    private CGImage CgImageOf(PeachDrawing.Core.Image image) =>
        image is CoreGraphicsTileImage tile ? tile.Content : ImageFor(image).GetCgImage();

    private static CGInterpolationQuality QualityFor(PeachDrawing.Core.Image image, ImageSampling sampling)
    {
        if (sampling is ImageSampling.Nearest or ImageSampling.Pixelated || sampling == ImageSampling.Automatic && !image.Interpolate)
            return CGInterpolationQuality.None;

        return sampling == ImageSampling.Bilinear ? CGInterpolationQuality.Medium : CGInterpolationQuality.High;
    }

    /// <summary>
    /// Draws <paramref name="image"/> so that its <paramref name="part"/> (the whole picture when null; in the layout units of the image it came
    /// from) fills <paramref name="dest"/>. The graphics state's alpha, blend mode and clip apply.
    /// </summary>
    private void DrawCgImage(CGImage image, Rect dest, ImageSampling sampling, Rect? part = null, double pixelsPerImageUnitX = 1, double pixelsPerImageUnitY = 1,
        CGInterpolationQuality? quality = null)
    {
        if (!(dest.Width > 0) || !(dest.Height > 0))
            return;

        CGImage? cropped = null;
        try
        {
            var drawn = image;
            if (part is { } src)
            {
                if (!(src.Width > 0) || !(src.Height > 0))
                    return;

                cropped = image.WithImageInRect(new CGRect(src.X * pixelsPerImageUnitX, src.Y * pixelsPerImageUnitY, src.Width * pixelsPerImageUnitX, src.Height * pixelsPerImageUnitY));
                if (cropped is null)
                    return;

                drawn = cropped;
            }

            var depth = _depth;
            Save();
            try
            {
                if (quality is { } q)
                    _cg.InterpolationQuality = q;

                // The canvas is y-down and CoreGraphics draws an image upright in y-up space, so the image is drawn in a frame flipped about its own rectangle.
                _cg.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, (nfloat)dest.X, (nfloat)(dest.Y + dest.Height)));
                _cg.DrawImage(new CGRect(0, 0, dest.Width, dest.Height), drawn);
            }
            finally
            {
                RestoreTo(depth);
            }
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    private void DrawImageCore(PeachDrawing.Core.Image image, Rect dest, Rect? part, ImageSampling sampling)
    {
        Guard();
        var cg = CgImageOf(image);
        DrawCgImage(cg, dest, sampling, part, PixelsPerUnit(cg, image.Width), PixelsPerUnit(cg, image.Height, vertical: true), QualityFor(image, sampling));
    }

    private static double PixelsPerUnit(CGImage cg, double units, bool vertical = false) =>
        units > 0 ? (vertical ? cg.Height : cg.Width) / units : 1;

    /// <inheritdoc />
    public override void DrawImage(PeachDrawing.Core.Image image, Rect destRect) => DrawImageCore(image, destRect, null, ImageSampling.Automatic);

    /// <inheritdoc />
    public override void DrawImage(PeachDrawing.Core.Image image, Rect destRect, Rect srcRect) => DrawImageCore(image, destRect, srcRect, ImageSampling.Automatic);

    /// <inheritdoc />
    public override void DrawImage(PeachDrawing.Core.Image image, Rect destRect, ImageSampling sampling) => DrawImageCore(image, destRect, null, sampling);

    /// <inheritdoc />
    public override void DrawImage(PeachDrawing.Core.Image image, Rect destRect, Rect srcRect, ImageSampling sampling) => DrawImageCore(image, destRect, srcRect, sampling);

    /// <inheritdoc />
    public override void DrawImageWithOpacity(PeachDrawing.Core.Image image, Rect destRect, double opacity, PaintBlendMode blendMode = PaintBlendMode.Normal)
    {
        Guard();
        var depth = _depth;
        Save();
        try
        {
            _cg.SetAlpha((nfloat)Math.Clamp(opacity, 0, 1));
            _cg.SetBlendMode(BlendModeFor(blendMode != PaintBlendMode.Normal ? blendMode : _blend));
            DrawImageCore(image, destRect, null, ImageSampling.Automatic);
        }
        finally
        {
            RestoreTo(depth);
        }
    }

    /// <inheritdoc />
    public override void DrawImageWithColorMatrix(PeachDrawing.Core.Image image, Rect destRect, ColorMatrix matrix)
    {
        Guard();
        var cg = CgImageOf(image);
        using var transformed = _context.Effects.ApplyColorMatrix(cg, matrix);
        DrawCgImage(transformed, destRect, ImageSampling.Automatic, null, 1, 1, QualityFor(image, ImageSampling.Automatic));
    }

    /// <inheritdoc />
    public override void DrawImageMasked(PeachDrawing.Core.Image image, PeachDrawing.Core.Image maskImage, Rect destRect) =>
        DrawMasked(image, maskImage, destRect, alphaMask: false, invert: false);

    /// <inheritdoc />
    public override void DrawImageAlphaMasked(PeachDrawing.Core.Image image, PeachDrawing.Core.Image maskImage, Rect destRect, bool invert = false) =>
        DrawMasked(image, maskImage, destRect, alphaMask: true, invert);

    private void DrawMasked(PeachDrawing.Core.Image image, PeachDrawing.Core.Image maskImage, Rect dest, bool alphaMask, bool invert)
    {
        Guard();
        if (!(dest.Width > 0) || !(dest.Height > 0) || !(image.Width > 0) || !(image.Height > 0) || !(maskImage.Width > 0) || !(maskImage.Height > 0))
            return;

        // The content is drawn into an isolated bitmap the size of the destination, then the mask is drawn over it so that only what the mask
        // leaves shows: DestinationIn keeps the content where the mask is opaque (an alpha mask) or bright (a luminosity mask, converted to opacity
        // first), DestinationOut keeps it where the mask is not opaque.
        using var offscreen = NewOffscreen(dest);
        var content = offscreen.Canvas;
        content.DrawImage(image, dest);

        using var luma = alphaMask ? null : _context.Effects.LuminanceToAlpha(CgImageOf(maskImage));
        var maskCg = luma ?? CgImageOf(maskImage);
        content.DrawMaskPart(maskCg, dest, alphaMask && invert ? CGBlendMode.DestinationOut : CGBlendMode.DestinationIn, QualityFor(maskImage, ImageSampling.Automatic));

        content.Dispose();
        DrawCgImage(offscreen.Snapshot, dest, ImageSampling.Automatic, null, 1, 1, CGInterpolationQuality.Default);
    }

    /// <summary>Draws a mask picture over everything in a layer with a compositing blend mode, so it keeps or removes what is under it.</summary>
    private void DrawMaskPart(CGImage mask, Rect dest, CGBlendMode mode, CGInterpolationQuality quality)
    {
        var depth = _depth;
        Save();
        try
        {
            _cg.SetBlendMode(mode);
            DrawCgImage(mask, dest, ImageSampling.Automatic, null, 1, 1, quality);
        }
        finally
        {
            RestoreTo(depth);
        }
    }

    /// <inheritdoc />
    public override void DrawImageBlendedOver(PeachDrawing.Core.Image top, PeachDrawing.Core.Image bottom, Rect destRect, PaintBlendMode blendMode)
    {
        Guard();
        if (!(destRect.Width > 0) || !(destRect.Height > 0))
            return;

        if (blendMode == PaintBlendMode.Normal)
        {
            DrawImageCore(bottom, destRect, null, ImageSampling.Automatic);
            DrawImageCore(top, destRect, null, ImageSampling.Automatic);
            return;
        }

        // The two sides blend with each other in a bitmap of their own, not with whatever the page already holds under them.
        using var offscreen = NewOffscreen(destRect);
        var content = offscreen.Canvas;
        content.DrawImage(bottom, destRect);
        content.PushBlendMode(blendMode);
        content.DrawImage(top, destRect);
        content.PopBlendMode();
        content.Dispose();
        DrawCgImage(offscreen.Snapshot, destRect, ImageSampling.Automatic, null, 1, 1, CGInterpolationQuality.Default);
    }

    // ---- the raster seam ----------------------------------------------------------------------------------

    /// <inheritdoc />
    public override RasterRegion? BeginRasterSurface(Rect layoutBounds, double? dpiOverride = null)
    {
        Guard();
        return RasterSurfaceFactory.Create(_context, _ppp, layoutBounds, dpiOverride ?? _context.RasterizationDpi, _context.MaxRasterPixels, TransformScale, _ctm);
    }

    /// <inheritdoc />
    public override void DrawRaster(RasterSurface surface)
    {
        Guard();
        ArgumentNullException.ThrowIfNull(surface);

        // The pixels are premultiplied RGBA, which is what the image is told they are; it lives for this one draw.
        using var image = CoreGraphicsImage.FromPixels(new PixelBuffer(surface.Width, surface.Height, surface.Pixels.Slice(0, surface.Width * surface.Height * 4).ToArray()), _context.Counts);
        DrawCgImage(image.GetCgImage(), surface.LayoutRect, ImageSampling.Bilinear, null, 1, 1, CGInterpolationQuality.Medium);
    }
}
