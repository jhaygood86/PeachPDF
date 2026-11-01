using PeachDrawing.Core;
using SkiaSharp;
using System;
using System.IO;

namespace PeachDrawing.Skia;

/// <summary>How a <see cref="SkiaComposer"/> sets up its canvas.</summary>
public sealed class SkiaComposerOptions
{
    /// <summary>What the surface is cleared to before anything is drawn; transparent by default. <see langword="null"/> leaves the surface as it is.</summary>
    public PaintColor? Background { get; init; } = PaintColor.Transparent;

    /// <summary>Which links the canvas's <see cref="SkiaCanvas.Links"/> may open; <see langword="null"/> for the defaults.</summary>
    public SkiaLinkOptions? Links { get; init; }
}

/// <summary>
/// A drawing of one page onto a surface the caller supplies: it scales the page to fill the surface, clears it, and gives the
/// <see cref="SkiaCanvas"/> to draw with, then reads the result back as pixels or saves it as an image file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> The composer allocates no surface. It borrows the <see cref="SKSurface"/> it is given (CPU or GPU) and never disposes it or
/// the context. <see cref="Finish"/> (called by <see cref="Save"/> and <see cref="ToPixelBuffer"/> when needed) disposes the canvas, which puts
/// the surface's canvas back as it found it; after that the drawing is complete and no more can be added. <see cref="Dispose"/> finishes it.
/// </para>
/// </remarks>
public sealed class SkiaComposer : IDisposable
{
    private readonly SKSurface _surface;
    private readonly SkiaCanvas _canvas;
    private readonly int _hostSave;
    private bool _finished;
    private bool _disposed;

    /// <summary>Makes a drawing <paramref name="widthPoints"/> by <paramref name="heightPoints"/> points that fills <paramref name="surface"/>.</summary>
    /// <param name="renderContext">where fonts and images come from; its <see cref="SkiaRenderContext.PixelsPerPoint"/> sets the canvas's scale</param>
    /// <param name="surface">the surface to draw on, which the caller owns</param>
    /// <param name="widthPoints">the width of the page, in points</param>
    /// <param name="heightPoints">the height of the page, in points</param>
    /// <param name="options">how the canvas is set up, or <see langword="null"/> for the defaults</param>
    public SkiaComposer(SkiaRenderContext renderContext, SKSurface surface, double widthPoints, double heightPoints, SkiaComposerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(renderContext);
        ArgumentNullException.ThrowIfNull(surface);
        if (!(widthPoints > 0) || !(heightPoints > 0) || double.IsInfinity(widthPoints) || double.IsInfinity(heightPoints))
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "The page must have a positive, finite size.");

        options ??= new SkiaComposerOptions();
        RenderContext = renderContext;
        _surface = surface;
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;

        var ppp = renderContext.PixelsPerPoint > 0 ? renderContext.PixelsPerPoint : 1.0;
        var width = widthPoints * ppp;
        var height = heightPoints * ppp;
        PixelWidth = surface.Canvas.DeviceClipBounds.Width;
        PixelHeight = surface.Canvas.DeviceClipBounds.Height;

        var host = surface.Canvas;
        _hostSave = host.Save();
        try
        {
            if (options.Background is { } background)
            {
                var (r, g, b) = background.ToRgb();
                host.Clear(new SKColor(r, g, b, background.A));
            }

            // The page is laid out in layout units; the surface is in pixels. The canvas draws under this scale like under any host matrix.
            host.Scale((float)(PixelWidth / width), (float)(PixelHeight / height));
            _canvas = SkiaCanvas.FromCanvas(renderContext, host, new Rect(0, 0, width, height), options.Links);
        }
        catch
        {
            host.RestoreToCount(_hostSave);
            throw;
        }
    }

    /// <summary>The canvas that draws on the surface. It is disposed by <see cref="Finish"/>.</summary>
    public SkiaCanvas Canvas => _canvas;

    /// <summary>The links and named places painted so far; usable after the composer is disposed.</summary>
    public SkiaLinkMap Links => _canvas.Links;

    /// <summary>The render context the canvas draws with.</summary>
    public SkiaRenderContext RenderContext { get; }

    /// <summary>The width of the page, in points.</summary>
    public double WidthPoints { get; }

    /// <summary>The height of the page, in points.</summary>
    public double HeightPoints { get; }

    /// <summary>The width of the surface, in pixels.</summary>
    public int PixelWidth { get; }

    /// <summary>The height of the surface, in pixels.</summary>
    public int PixelHeight { get; }

    /// <summary>
    /// Completes the drawing: pops anything still pushed on the canvas and puts the surface's canvas back as it was, so the surface can be read.
    /// Safe to call more than once; nothing can be drawn afterwards.
    /// </summary>
    public void Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
            return;

        _finished = true;
        _canvas.Dispose();
        _surface.Canvas.RestoreToCount(_hostSave);
    }

    /// <summary>The finished drawing as premultiplied RGBA pixels, the form the rest of PeachDrawing exchanges images in.</summary>
    public PixelBuffer ToPixelBuffer()
    {
        Finish();
        var info = new SKImageInfo(PixelWidth, PixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using var snapshot = _surface.Snapshot();
        if (!snapshot.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0))
            throw new InvalidOperationException("The surface's pixels could not be read back.");

        return new PixelBuffer(PixelWidth, PixelHeight, bitmap.GetPixelSpan().ToArray());
    }

    /// <summary>Writes the finished drawing to <paramref name="destination"/> as an image file. The stream is left open.</summary>
    /// <param name="destination">where to write</param>
    /// <param name="format">the file format</param>
    /// <param name="quality">for a lossy format, from 0 to 100</param>
    public void Save(Stream destination, SkiaImageFormat format = SkiaImageFormat.Png, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Finish();

        using var snapshot = _surface.Snapshot();
        using var data = snapshot.Encode(format switch
        {
            SkiaImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            SkiaImageFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        }, Math.Clamp(quality, 0, 100)) ?? throw new InvalidOperationException("The drawing could not be encoded.");

        data.SaveTo(destination);
    }

    /// <summary>Finishes the drawing if it is not already. The surface is the caller's and stays open. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            Finish();
        }
        finally
        {
            _disposed = true;
        }
    }
}
