using PeachDrawing.Core;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Skia;

public sealed partial class SkiaCanvas
{
    // Images from another backend (anything that is not this one's own) are converted once per canvas and released with it.
    private Dictionary<PeachDrawing.Core.Image, SkiaImage>? _converted;

    private SkiaImage ImageFor(PeachDrawing.Core.Image image)
    {
        if (image is SkiaImage own)
            return own;

        _converted ??= new Dictionary<PeachDrawing.Core.Image, SkiaImage>(ReferenceEqualityComparer.Instance);
        if (!_converted.TryGetValue(image, out var converted))
        {
            converted = image.GetPixels() is { } pixels
                ? SkiaImage.FromPixels(pixels, _context.Counts)
                : throw new NotSupportedException("An image drawn on a Skia canvas must come from the Skia render context, a tile of this canvas, or expose its pixels.");
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

    private static SKSamplingOptions Sampling(PeachDrawing.Core.Image image, ImageSampling sampling)
    {
        if (sampling is ImageSampling.Nearest or ImageSampling.Pixelated || sampling == ImageSampling.Automatic && !image.Interpolate)
            return new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

        return sampling == ImageSampling.Bicubic
            ? new SKSamplingOptions(SKCubicResampler.Mitchell)
            : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
    }

    /// <summary>What an image draws as: a recorded picture (a tile) or a raster image. Neither is owned by the caller.</summary>
    private readonly record struct Source(SKImage? Image, SKPicture? Picture, double Width, double Height);

    private Source SourceOf(PeachDrawing.Core.Image image) => image is SkiaTileImage tile
        ? new Source(null, tile.Content, image.Width, image.Height)
        : new Source(ImageFor(image).GetImage(_context.GpuContext), null, image.Width, image.Height);

    /// <summary>
    /// Draws <paramref name="source"/> so that its <paramref name="part"/> (the whole image when null) fills <paramref name="dest"/>, in the
    /// canvas's layout units. A picture drawn with a paint is drawn as one group, so the paint's opacity, blend mode and filters apply to the whole.
    /// </summary>
    private void DrawSource(Source source, Rect dest, Rect? part, SKSamplingOptions sampling, SKPaint? paint)
    {
        var src = part ?? new Rect(0, 0, source.Width, source.Height);
        if (!(source.Width > 0) || !(source.Height > 0) || !(dest.Width > 0) || !(dest.Height > 0) || !(src.Width > 0) || !(src.Height > 0))
            return;

        var from = SKRect.Create((float)src.X, (float)src.Y, (float)src.Width, (float)src.Height);
        var to = SKRect.Create((float)dest.X, (float)dest.Y, (float)dest.Width, (float)dest.Height);
        if (source.Image is { } image)
        {
            _canvas.DrawImage(image, from, to, sampling, paint);
            return;
        }

        _canvas.Save();
        try
        {
            // Drawing the picture at its own place under a clip to the source rectangle is what crops it; the matrix carries it to the destination.
            _canvas.Translate(to.Left, to.Top);
            _canvas.Scale(to.Width / from.Width, to.Height / from.Height);
            _canvas.Translate(-from.Left, -from.Top);
            _canvas.ClipRect(from, SKClipOperation.Intersect, _smooth);
            _canvas.DrawPicture(source.Picture!, paint);
        }
        finally
        {
            _canvas.RestoreToCount(_canvas.SaveCount - 1);
        }
    }

    private void DrawImageCore(PeachDrawing.Core.Image image, Rect dest, Rect? part, ImageSampling sampling, SKPaint? paint = null)
    {
        Guard();
        DrawSource(SourceOf(image), dest, part, Sampling(image, sampling), WithBlend(paint, out var owned));
        owned?.Dispose();
    }

    // The blend mode pushed on the canvas applies to an image draw too; a paint made for the draw carries it, and a draw with no paint of its
    // own needs one only when a blend is pushed.
    private SKPaint? WithBlend(SKPaint? paint, out SKPaint? owned)
    {
        owned = null;
        if (paint is not null)
        {
            if (_blend != PaintBlendMode.Normal)
                paint.BlendMode = BlendModeFor(_blend);

            return paint;
        }

        if (_blend == PaintBlendMode.Normal)
            return null;

        owned = new SKPaint { BlendMode = BlendModeFor(_blend), IsAntialias = _smooth };
        return owned;
    }

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
        using var paint = new SKPaint
        {
            IsAntialias = _smooth,
            Color = new SKColor(0, 0, 0, (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255)),
            BlendMode = BlendModeFor(blendMode != PaintBlendMode.Normal ? blendMode : _blend),
        };

        DrawSource(SourceOf(image), destRect, null, Sampling(image, ImageSampling.Automatic), paint);
    }

    /// <inheritdoc />
    public override void DrawImageWithColorMatrix(PeachDrawing.Core.Image image, Rect destRect, ColorMatrix matrix)
    {
        Guard();
        using var filter = ColorMatrixFilter(matrix);
        using var paint = new SKPaint { IsAntialias = _smooth, ColorFilter = filter, BlendMode = BlendModeFor(_blend) };
        DrawSource(SourceOf(image), destRect, null, Sampling(image, ImageSampling.Automatic), paint);
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

        var bounds = SKRect.Create((float)dest.X, (float)dest.Y, (float)dest.Width, (float)dest.Height);
        var content = SourceOf(image);
        var mask = SourceOf(maskImage);

        // The content is drawn into an isolated group the size of the destination, then the mask is drawn over it so that only what the
        // mask leaves shows: DstIn keeps the content where the mask is opaque (an alpha mask) or bright (a luminosity mask, whose colour filter
        // turns brightness into opacity), DstOut keeps it where the mask is not opaque. Both images are carried into the destination's place,
        // so the mask lines up with what it masks wherever each came from.
        using var group = new SKPaint { BlendMode = BlendModeFor(_blend), IsAntialias = _smooth };
        _canvas.SaveLayer(bounds, group);
        try
        {
            _canvas.ClipRect(bounds, SKClipOperation.Intersect, _smooth);
            DrawSource(content, dest, null, Sampling(image, ImageSampling.Automatic), null);

            using var luma = alphaMask ? null : SKColorFilter.CreateLumaColor();
            using var apply = new SKPaint
            {
                IsAntialias = _smooth,
                BlendMode = alphaMask && invert ? SKBlendMode.DstOut : SKBlendMode.DstIn,
                ColorFilter = luma,
            };
            DrawSource(mask, dest, null, Sampling(maskImage, ImageSampling.Automatic), apply);
        }
        finally
        {
            _canvas.RestoreToCount(_canvas.SaveCount - 1);
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

        // The two sides blend with each other in a group of their own, not with whatever the page already holds under them.
        var bounds = SKRect.Create((float)destRect.X, (float)destRect.Y, (float)destRect.Width, (float)destRect.Height);
        using var group = new SKPaint { BlendMode = BlendModeFor(_blend), IsAntialias = _smooth };
        _canvas.SaveLayer(bounds, group);
        try
        {
            _canvas.ClipRect(bounds, SKClipOperation.Intersect, _smooth);
            DrawSource(SourceOf(bottom), destRect, null, Sampling(bottom, ImageSampling.Automatic), null);
            using var blend = new SKPaint { BlendMode = BlendModeFor(blendMode), IsAntialias = _smooth };
            DrawSource(SourceOf(top), destRect, null, Sampling(top, ImageSampling.Automatic), blend);
        }
        finally
        {
            _canvas.RestoreToCount(_canvas.SaveCount - 1);
        }
    }

    private static SKColorFilter ColorMatrixFilter(ColorMatrix matrix)
    {
        // Canvas colours are row vectors multiplied by the matrix; Skia's 4x5 matrix multiplies a column vector, so it is the transpose with the
        // offset as its fifth column.
        var m = matrix.Linear;
        var o = matrix.Offset;
        return SKColorFilter.CreateColorMatrix(
        [
            m.M11, m.M21, m.M31, m.M41, o.X,
            m.M12, m.M22, m.M32, m.M42, o.Y,
            m.M13, m.M23, m.M33, m.M43, o.Z,
            m.M14, m.M24, m.M34, m.M44, o.W,
        ]);
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
        var info = new SKImageInfo(surface.Width, surface.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var image = SKImage.FromPixelCopy(info, surface.Pixels, info.RowBytes);
        if (image is null)
            return;

        var paint = WithBlend(null, out var owned);
        try
        {
            var layout = surface.LayoutRect;
            _canvas.DrawImage(image, SKRect.Create(0, 0, surface.Width, surface.Height),
                SKRect.Create((float)layout.X, (float)layout.Y, (float)layout.Width, (float)layout.Height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        }
        finally
        {
            owned?.Dispose();
        }
    }
}
