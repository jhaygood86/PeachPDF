using CoreGraphics;
using Foundation;
using ImageIO;
using PeachDrawing.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PeachDrawing.CoreGraphics;

/// <summary>How a <see cref="CoreGraphicsComposer"/> sets up its canvas.</summary>
public sealed class CoreGraphicsComposerOptions
{
    /// <summary>What the bitmap is cleared to before anything is drawn; transparent by default. <see langword="null"/> leaves the bitmap as it is.</summary>
    public PaintColor? Background { get; init; } = PaintColor.Transparent;

    /// <summary>Which links the canvas's <see cref="CoreGraphicsCanvas.Links"/> may open; <see langword="null"/> for the defaults.</summary>
    public CoreGraphicsLinkOptions? Links { get; init; }
}

/// <summary>
/// A drawing of one page onto a bitmap: it makes a bitmap context of the size asked for, scales the page to fill it, clears it, and gives the
/// <see cref="CoreGraphicsCanvas"/> to draw with, then reads the result back as pixels or saves it as an image file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> The composer owns the bitmap it draws on and releases it in <see cref="Dispose"/>; it never releases the context it was made
/// with. <see cref="Finish"/> (called by <see cref="Save"/> and <see cref="ToPixelBuffer"/> when needed) disposes the canvas; after that the drawing
/// is complete and no more can be added.
/// </para>
/// </remarks>
public sealed class CoreGraphicsComposer : IDisposable
{
    private readonly CGBitmapContext _bitmap;
    private readonly CoreGraphicsCanvas _canvas;
    private bool _finished;
    private bool _disposed;

    /// <summary>Makes a drawing <paramref name="widthPoints"/> by <paramref name="heightPoints"/> points on a bitmap of <paramref name="pixelWidth"/> by <paramref name="pixelHeight"/> pixels.</summary>
    /// <param name="renderContext">where fonts and images come from; its <see cref="CoreGraphicsRenderContext.PixelsPerPoint"/> sets the canvas's scale</param>
    /// <param name="widthPoints">the width of the page, in points</param>
    /// <param name="heightPoints">the height of the page, in points</param>
    /// <param name="pixelWidth">the width of the bitmap, in pixels</param>
    /// <param name="pixelHeight">the height of the bitmap, in pixels</param>
    /// <param name="options">how the canvas is set up, or <see langword="null"/> for the defaults</param>
    public CoreGraphicsComposer(CoreGraphicsRenderContext renderContext, double widthPoints, double heightPoints, int pixelWidth, int pixelHeight,
        CoreGraphicsComposerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(renderContext);
        if (!(widthPoints > 0) || !(heightPoints > 0) || double.IsInfinity(widthPoints) || double.IsInfinity(heightPoints))
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "The page must have a positive, finite size.");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);

        options ??= new CoreGraphicsComposerOptions();
        RenderContext = renderContext;
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;

        var ppp = renderContext.PixelsPerPoint > 0 ? renderContext.PixelsPerPoint : 1.0;
        var width = widthPoints * ppp;
        var height = heightPoints * ppp;

        _bitmap = new CGBitmapContext(IntPtr.Zero, pixelWidth, pixelHeight, 8, 0, CoreGraphicsCanvas.Rgb, CGImageAlphaInfo.PremultipliedLast)
            ?? throw new InvalidOperationException("A bitmap context of that size could not be made.");
        try
        {
            if (options.Background is { } background)
            {
                using var color = CoreGraphicsCanvas.ToColor(background);
                _bitmap.SetBlendMode(CGBlendMode.Copy);
                _bitmap.SetFillColor(color);
                _bitmap.FillRect(new CGRect(0, 0, pixelWidth, pixelHeight));
                _bitmap.SetBlendMode(CGBlendMode.Normal);
            }

            // The page is laid out in layout units; the bitmap is in pixels. The canvas draws under this scale like under any host matrix.
            _bitmap.ScaleCTM((nfloat)(pixelWidth / width), (nfloat)(pixelHeight / height));
            _canvas = CoreGraphicsCanvas.FromContext(renderContext, _bitmap, new Rect(0, 0, width, height), options.Links);
        }
        catch
        {
            _bitmap.Dispose();
            throw;
        }
    }

    /// <summary>The canvas that draws on the bitmap. It is disposed by <see cref="Finish"/>.</summary>
    public CoreGraphicsCanvas Canvas => _canvas;

    /// <summary>The links and named places painted so far; usable after the composer is disposed.</summary>
    public CoreGraphicsLinkMap Links => _canvas.Links;

    /// <summary>The render context the canvas draws with.</summary>
    public CoreGraphicsRenderContext RenderContext { get; }

    /// <summary>The width of the page, in points.</summary>
    public double WidthPoints { get; }

    /// <summary>The height of the page, in points.</summary>
    public double HeightPoints { get; }

    /// <summary>The width of the bitmap, in pixels.</summary>
    public int PixelWidth { get; }

    /// <summary>The height of the bitmap, in pixels.</summary>
    public int PixelHeight { get; }

    /// <summary>Completes the drawing: pops anything still pushed on the canvas, so the bitmap can be read. Safe to call more than once; nothing can be drawn afterwards.</summary>
    public void Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
            return;

        _finished = true;
        _canvas.Dispose();
    }

    /// <summary>The finished drawing as premultiplied RGBA pixels, the form the rest of PeachDrawing exchanges images in.</summary>
    public PixelBuffer ToPixelBuffer()
    {
        Finish();
        var stride = (int)_bitmap.BytesPerRow;
        var rgba = new byte[PixelWidth * PixelHeight * 4];
        for (var row = 0; row < PixelHeight; row++)
            Marshal.Copy(_bitmap.Data + row * stride, rgba, row * PixelWidth * 4, PixelWidth * 4);

        return new PixelBuffer(PixelWidth, PixelHeight, rgba);
    }

    /// <summary>Writes the finished drawing to <paramref name="destination"/> as an image file. The stream is left open.</summary>
    /// <param name="destination">where to write</param>
    /// <param name="format">the file format</param>
    /// <param name="quality">for a lossy format, from 0 to 100</param>
    public void Save(Stream destination, CoreGraphicsImageFormat format = CoreGraphicsImageFormat.Png, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Finish();

        using var image = _bitmap.ToImage() ?? throw new InvalidOperationException("The drawing could not be read back.");
        using var data = new NSMutableData();
        var type = format switch
        {
            CoreGraphicsImageFormat.Jpeg => "public.jpeg",
            CoreGraphicsImageFormat.Tiff => "public.tiff",
            _ => "public.png",
        };

        using var target = CGImageDestination.Create(data, type, 1)
            ?? throw new InvalidOperationException("The drawing could not be encoded.");
        target.AddImage(image, new CGImageDestinationOptions { LossyCompressionQuality = Math.Clamp(quality, 0, 100) / 100f });
        if (!target.Close())
            throw new InvalidOperationException("The drawing could not be encoded.");

        destination.Write(data.ToArray());
    }

    /// <summary>Finishes the drawing if it is not already and releases the bitmap. Safe to call more than once.</summary>
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
            _bitmap.Dispose();
        }
    }
}
