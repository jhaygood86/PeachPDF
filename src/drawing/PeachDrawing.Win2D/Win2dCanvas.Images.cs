using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using W2dColorMatrixEffect = Microsoft.Graphics.Canvas.Effects.ColorMatrixEffect;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D;

public sealed partial class Win2dCanvas
{
    // Images from another backend (anything that is not this one's own) are converted once per canvas and released with it.
    private Dictionary<PeachDrawing.Core.Image, Win2dImage>? _converted;

    private Win2dImage ImageFor(PeachDrawing.Core.Image image)
    {
        if (image is Win2dImage own)
            return own;

        _converted ??= [with(ReferenceEqualityComparer.Instance)];
        if (!_converted.TryGetValue(image, out var converted))
        {
            converted = image.GetPixels() is { } pixels
                ? Win2dImage.FromPixels(pixels)
                : throw new NotSupportedException("An image drawn on a Win2D canvas must come from the Win2D render context, a tile of this canvas, or expose its pixels.");
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

    private static CanvasImageInterpolation Interpolation(PeachDrawing.Core.Image image, ImageSampling sampling)
    {
        if (sampling is ImageSampling.Nearest or ImageSampling.Pixelated || sampling == ImageSampling.Automatic && !image.Interpolate)
            return CanvasImageInterpolation.NearestNeighbor;

        return sampling == ImageSampling.Bicubic ? CanvasImageInterpolation.HighQualityCubic : CanvasImageInterpolation.Linear;
    }

    /// <summary>The Win2D image for <paramref name="image"/>: not owned by the caller, which must not dispose it.</summary>
    private ICanvasImage Source(PeachDrawing.Core.Image image) =>
        image is Win2dTileImage tile ? tile.Content : ImageFor(image).GetBitmap(_session);

    /// <summary>
    /// Draws <paramref name="source"/> so that its <paramref name="part"/> (the whole <paramref name="width"/> x <paramref name="height"/>
    /// image when null) fills <paramref name="dest"/>, in the canvas's layout units.
    /// </summary>
    private void DrawSource(ICanvasImage source, double width, double height, Rect dest, Rect? part, CanvasImageInterpolation interpolation, float opacity = 1f)
    {
        var src = part ?? new Rect(0, 0, width, height);
        if (!(width > 0) || !(height > 0) || !(dest.Width > 0) || !(dest.Height > 0) || !(src.Width > 0) || !(src.Height > 0))
            return;

        // Bilinear reads four texels of the block each output pixel covers, which is fine for a small reduction and breaks thin strokes up when
        // the source is much larger than where it is drawn (a 4000 px logo shown 100 px wide). The cubic mode pre-filters a reduction, so every
        // source pixel counts, as it does when a browser shrinks an image.
        if (interpolation == CanvasImageInterpolation.Linear && Math.Min(dest.Width / src.Width, dest.Height / src.Height) * Math.Max(Math.Abs(EffectiveTransform.M11), Math.Abs(EffectiveTransform.M22)) < 0.5)
            interpolation = CanvasImageInterpolation.HighQualityCubic;

        NoteDrawn(dest.X, dest.Y, dest.X + dest.Width, dest.Y + dest.Height);
        var saved = _session.Transform;
        try
        {
            _session.Transform = Placement(src, dest).Then(EffectiveTransform);

            // Drawing the source rectangle at its own place is what crops the image to it; the transform then carries it to the destination.
            _session.DrawImage(source, (float)src.X, (float)src.Y, new Windows.Foundation.Rect(src.X, src.Y, src.Width, src.Height), opacity, interpolation);
        }
        finally
        {
            _session.Transform = saved;
        }
    }

    private static Matrix3x2 Placement(Rect src, Rect dest)
    {
        var sx = (float)(dest.Width / src.Width);
        var sy = (float)(dest.Height / src.Height);
        return new Matrix3x2(sx, 0, 0, sy, (float)(dest.X - src.X * sx), (float)(dest.Y - src.Y * sy));
    }

    private void DrawImageCore(PeachDrawing.Core.Image image, Rect dest, Rect? part, ImageSampling sampling, float opacity = 1f)
    {
        Guard();
        if (image is not Win2dImage own || !(image.Width > 0) || !(image.Height > 0))
        {
            DrawSource(Source(image), image.Width, image.Height, dest, part, Interpolation(image, sampling), opacity);
            return;
        }

        // An image Windows decodes from its encoded bytes is held at the resolution this draw reads (a news thumbnail is a few hundred pixels wide,
        // its file a few thousand), not at its own: a fraction of the device memory, a cheaper first decode, and no multi-pass reduction on every
        // paint. A larger draw later (a zoom) decodes it again at the size it then needs and keeps that.
        var drawStarted = Win2dPaintStatistics.Start();
        var src = part ?? new Rect(0, 0, image.Width, image.Height);
        var m = EffectiveTransform;
        var scaleX = Math.Sqrt((m.M11 * m.M11) + (m.M12 * m.M12));
        var scaleY = Math.Sqrt((m.M21 * m.M21) + (m.M22 * m.M22));
        var needed = src.Width > 0 && src.Height > 0 ? Math.Max(dest.Width / src.Width * scaleX, dest.Height / src.Height * scaleY) * DpiScale : 1;
        var bitmap = own.GetBitmap(_session, Win2dImage.ScaleFor(needed));

        var ratio = bitmap.SizeInPixels.Width / image.Width;
        var ratioY = bitmap.SizeInPixels.Height / image.Height;
        Rect? scaledPart = part is { } p ? new Rect(p.X * ratio, p.Y * ratioY, p.Width * ratio, p.Height * ratioY) : null;
        DrawSource(bitmap, bitmap.SizeInPixels.Width, bitmap.SizeInPixels.Height, dest, scaledPart, Interpolation(image, sampling), opacity);
        Win2dPaintStatistics.Stop(Win2dPaintStatistics.Counter.ImageDraw, drawStarted);
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
        var previous = _session.Blend;
        if (blendMode != PaintBlendMode.Normal)
            _session.Blend = SessionBlendFor(blendMode);

        try
        {
            DrawImageCore(image, destRect, null, ImageSampling.Automatic, (float)Math.Clamp(opacity, 0, 1));
        }
        finally
        {
            _session.Blend = previous;
        }
    }

    /// <inheritdoc />
    public override void DrawImageWithColorMatrix(PeachDrawing.Core.Image image, Rect destRect, ColorMatrix matrix)
    {
        Guard();
        using var effect = ColorMatrixFor(Source(image), matrix);
        DrawSource(effect, image.Width, image.Height, destRect, null, CanvasImageInterpolation.Linear);
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

        // Both images are carried into the destination's place first, so the mask lines up with what it masks wherever each came from.
        NoteDrawn(dest.X, dest.Y, dest.X + dest.Width, dest.Y + dest.Height);
        using var content = new Transform2DEffect { Source = Source(image), TransformMatrix = Placement(new Rect(0, 0, image.Width, image.Height), dest) };
        using var mask = new Transform2DEffect { Source = Source(maskImage), TransformMatrix = Placement(new Rect(0, 0, maskImage.Width, maskImage.Height), dest) };

        using var clip = _session.CreateLayer(1f, new Windows.Foundation.Rect(dest.X, dest.Y, dest.Width, dest.Height));

        // An inverted alpha mask removes the content wherever the mask is opaque. It is not a colour matrix on the mask: a command list is
        // transparent beyond what was drawn on it, and an effect cannot turn that infinite transparent area opaque.
        if (alphaMask && invert)
        {
            using var cut = new CompositeEffect { Mode = CanvasComposite.DestinationOut };
            cut.Sources.Add(content);
            cut.Sources.Add(mask);
            _session.DrawImage(cut);
            return;
        }

        // A luminosity mask reads its values from the mask's brightness, an alpha mask from its opacity.
        using var luminance = alphaMask ? null : new LuminanceToAlphaEffect { Source = mask };
        using var masked = new AlphaMaskEffect { Source = content, AlphaMask = (ICanvasImage?)luminance ?? mask };
        _session.DrawImage(masked);
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

        NoteDrawn(destRect.X, destRect.Y, destRect.X + destRect.Width, destRect.Y + destRect.Height);
        using var back = new Transform2DEffect { Source = Source(bottom), TransformMatrix = Placement(new Rect(0, 0, bottom.Width, bottom.Height), destRect) };
        using var front = new Transform2DEffect { Source = Source(top), TransformMatrix = Placement(new Rect(0, 0, top.Width, top.Height), destRect) };
        using var blended = new BlendEffect { Background = back, Foreground = front, Mode = BlendModeFor(blendMode) };
        using var clip = _session.CreateLayer(1f, new Windows.Foundation.Rect(destRect.X, destRect.Y, destRect.Width, destRect.Height));
        _session.DrawImage(blended);
    }

    private static BlendEffectMode BlendModeFor(PaintBlendMode mode) => mode switch
    {
        PaintBlendMode.Multiply => BlendEffectMode.Multiply,
        PaintBlendMode.Screen => BlendEffectMode.Screen,
        PaintBlendMode.Overlay => BlendEffectMode.Overlay,
        PaintBlendMode.Darken => BlendEffectMode.Darken,
        PaintBlendMode.Lighten => BlendEffectMode.Lighten,
        PaintBlendMode.ColorDodge => BlendEffectMode.ColorDodge,
        PaintBlendMode.ColorBurn => BlendEffectMode.ColorBurn,
        PaintBlendMode.HardLight => BlendEffectMode.HardLight,
        PaintBlendMode.SoftLight => BlendEffectMode.SoftLight,
        PaintBlendMode.Difference => BlendEffectMode.Difference,
        PaintBlendMode.Exclusion => BlendEffectMode.Exclusion,
        PaintBlendMode.Hue => BlendEffectMode.Hue,
        PaintBlendMode.Saturation => BlendEffectMode.Saturation,
        PaintBlendMode.Color => BlendEffectMode.Color,
        PaintBlendMode.Luminosity => BlendEffectMode.Luminosity,
        PaintBlendMode.Plus => BlendEffectMode.LinearDodge,
        _ => BlendEffectMode.Multiply,
    };

    private static W2dColorMatrixEffect ColorMatrixFor(ICanvasImage source, ColorMatrix matrix)
    {
        // Canvas colours are row vectors multiplied by the matrix; Win2D's 5x4 matrix is the same, with the offset as its fifth row.
        var m = matrix.Linear;
        var o = matrix.Offset;
        return new W2dColorMatrixEffect
        {
            Source = source,
            AlphaMode = CanvasAlphaMode.Straight,
            ColorMatrix = new Matrix5x4
            {
                M11 = m.M11, M12 = m.M12, M13 = m.M13, M14 = m.M14,
                M21 = m.M21, M22 = m.M22, M23 = m.M23, M24 = m.M24,
                M31 = m.M31, M32 = m.M32, M33 = m.M33, M34 = m.M34,
                M41 = m.M41, M42 = m.M42, M43 = m.M43, M44 = m.M44,
                M51 = o.X, M52 = o.Y, M53 = o.Z, M54 = o.W,
            },
        };
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

        // The pixels are premultiplied RGBA, which is what the bitmap is told they are; it lives for this one draw. The copy goes through a
        // pooled array instead of a fresh allocation (a full-page raster is megabytes, so a fresh array each draw lands on the large object
        // heap). The bitmap itself is deliberately not reused: a command list a layer or tile records keeps a reference to it, so
        // overwriting its pixels for the next draw would change what was already recorded.
        var pixels = surface.Pixels;
        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(pixels.Length);
        try
        {
            pixels.CopyTo(rented);
            using var bitmap = CanvasBitmap.CreateFromBytes(_session, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(rented, 0, pixels.Length), surface.Width, surface.Height,
                DirectXPixelFormat.R8G8B8A8UIntNormalized, 96f, CanvasAlphaMode.Premultiplied);
            DrawSource(bitmap, surface.Width, surface.Height, surface.LayoutRect, null, CanvasImageInterpolation.Linear);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
