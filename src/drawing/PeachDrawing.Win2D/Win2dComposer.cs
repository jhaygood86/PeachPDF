using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace PeachDrawing.Win2D;

/// <summary>The file format <see cref="Win2dComposer.SaveAsync"/> writes.</summary>
public enum Win2dImageFormat
{
    /// <summary>PNG, lossless and with transparency.</summary>
    Png,

    /// <summary>JPEG, lossy and without transparency.</summary>
    Jpeg,

    /// <summary>Windows bitmap.</summary>
    Bmp,

    /// <summary>TIFF.</summary>
    Tiff,

    /// <summary>GIF.</summary>
    Gif,
}

/// <summary>How a <see cref="Win2dComposer"/> sets up its bitmap and its canvas.</summary>
public sealed class Win2dComposerOptions
{
    /// <summary>Dots per inch of the bitmap. One layout unit is one device-independent pixel, so a page 612 units wide is 612 pixels at 96 and 1275 at 200.</summary>
    public float Dpi { get; init; } = 96f;

    /// <summary>What the bitmap is cleared to before anything is drawn; transparent by default.</summary>
    public PaintColor Background { get; init; } = PaintColor.Transparent;

    /// <summary>Which links the canvas's <see cref="Win2dCanvas.Links"/> may open; <see langword="null"/> for the defaults.</summary>
    public Win2dLinkOptions? Links { get; init; }
}

/// <summary>The device a drawing was made on was lost (a driver reset, a removed adapter) before it could be read; draw it again.</summary>
public sealed class Win2dDeviceLostException : Exception
{
    /// <summary>Makes the exception.</summary>
    /// <param name="inner">the error Win2D reported</param>
    public Win2dDeviceLostException(Exception inner)
        : base("The graphics device was lost while the drawing was being produced. Draw it again.", inner)
    {
    }
}

/// <summary>
/// An offscreen drawing: a bitmap the size of a page and the <see cref="Win2dCanvas"/> that draws on it, to be read back as pixels or saved
/// as an image file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> The composer owns the bitmap, the drawing session on it and the canvas over that session. <see cref="Finish"/> (called by
/// <see cref="SaveAsync"/> and <see cref="ToPixelBuffer"/> when needed) disposes the canvas, which closes the session; after that the
/// drawing is complete and no more can be added. <see cref="Dispose"/> finishes and then releases the bitmap. The render context is not
/// owned: dispose it separately, after the composer.
/// </para>
/// <para>Reading or saving a drawing whose device was lost throws <see cref="Win2dDeviceLostException"/>.</para>
/// </remarks>
public sealed class Win2dComposer : IDisposable
{
    private readonly Win2dRenderContext _context;
    private readonly CanvasRenderTarget _target;
    private readonly Win2dCanvas _canvas;
    private bool _finished;
    private bool _disposed;

    /// <summary>Makes a drawing <paramref name="widthPoints"/> by <paramref name="heightPoints"/> points, cleared to the background.</summary>
    /// <param name="renderContext">where fonts and images come from; its device draws the bitmap and its <see cref="Win2dRenderContext.PixelsPerPoint"/> sets the canvas's scale</param>
    /// <param name="widthPoints">the width of the page, in points</param>
    /// <param name="heightPoints">the height of the page, in points</param>
    /// <param name="options">how the bitmap is set up, or <see langword="null"/> for the defaults</param>
    public Win2dComposer(Win2dRenderContext renderContext, double widthPoints, double heightPoints, Win2dComposerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(renderContext);
        if (!(widthPoints > 0) || !(heightPoints > 0) || double.IsInfinity(widthPoints) || double.IsInfinity(heightPoints))
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "The page must have a positive, finite size.");

        options ??= new Win2dComposerOptions();
        _context = renderContext;
        var ppp = renderContext.PixelsPerPoint > 0 ? renderContext.PixelsPerPoint : 1.0;
        var width = widthPoints * ppp;
        var height = heightPoints * ppp;

        _target = new CanvasRenderTarget(renderContext.Device, (float)width, (float)height, options.Dpi);
        try
        {
            var session = _target.CreateDrawingSession();
            try
            {
                var (r, g, b) = options.Background.ToRgb();
                session.Clear(Windows.UI.Color.FromArgb(options.Background.A, r, g, b));
                _canvas = Win2dCanvas.OverOwnedSession(renderContext, session, new Rect(0, 0, width, height), options.Links);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }
        catch
        {
            _target.Dispose();
            throw;
        }

        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        PixelWidth = (int)_target.SizeInPixels.Width;
        PixelHeight = (int)_target.SizeInPixels.Height;
    }

    /// <summary>The canvas that draws on the bitmap. It is disposed by <see cref="Finish"/>.</summary>
    public Win2dCanvas Canvas => _canvas;

    /// <summary>The links and named places painted so far; usable after the composer is disposed.</summary>
    public Win2dLinkMap Links => _canvas.Links;

    /// <summary>The render context the canvas draws with.</summary>
    public Win2dRenderContext RenderContext => _context;

    /// <summary>The width of the page, in points.</summary>
    public double WidthPoints { get; }

    /// <summary>The height of the page, in points.</summary>
    public double HeightPoints { get; }

    /// <summary>The width of the bitmap, in pixels.</summary>
    public int PixelWidth { get; }

    /// <summary>The height of the bitmap, in pixels.</summary>
    public int PixelHeight { get; }

    /// <summary>
    /// Completes the drawing: pops anything still pushed on the canvas and closes its session, so the bitmap can be read. Safe to call more
    /// than once; nothing can be drawn afterwards.
    /// </summary>
    public void Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished)
            return;

        _finished = true;
        Guarded(_canvas.Dispose);
    }

    /// <summary>The finished bitmap as premultiplied RGBA pixels, the form the rest of PeachDrawing exchanges images in.</summary>
    public PixelBuffer ToPixelBuffer()
    {
        Finish();
        var bgra = Guarded(_target.GetPixelBytes);
        var rgba = new byte[bgra.Length];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return new PixelBuffer(PixelWidth, PixelHeight, rgba);
    }

    /// <summary>Writes the finished bitmap to <paramref name="destination"/> as an image file. The stream is left open.</summary>
    /// <param name="destination">where to write</param>
    /// <param name="format">the file format</param>
    public async Task SaveAsync(Stream destination, Win2dImageFormat format = Win2dImageFormat.Png)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Finish();

        using var memory = new InMemoryRandomAccessStream();
        try
        {
            await _target.SaveAsync(memory, format switch
            {
                Win2dImageFormat.Jpeg => CanvasBitmapFileFormat.Jpeg,
                Win2dImageFormat.Bmp => CanvasBitmapFileFormat.Bmp,
                Win2dImageFormat.Tiff => CanvasBitmapFileFormat.Tiff,
                Win2dImageFormat.Gif => CanvasBitmapFileFormat.Gif,
                _ => CanvasBitmapFileFormat.Png,
            });
        }
        catch (COMException ex) when (IsDeviceLost(ex))
        {
            throw new Win2dDeviceLostException(ex);
        }

        var bytes = new byte[memory.Size];
        using (var reader = new DataReader(memory.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)bytes.Length);
            reader.ReadBytes(bytes);
            reader.DetachStream();
        }

        await destination.WriteAsync(bytes);
    }

    private bool IsDeviceLost(COMException ex)
    {
        try
        {
            // The device the drawing was made on, not the context current one, which may already be the replacement.
            return _target.Device.IsDeviceLost(ex.HResult);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (COMException ex) when (IsDeviceLost(ex))
        {
            throw new Win2dDeviceLostException(ex);
        }
    }

    private T Guarded<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (COMException ex) when (IsDeviceLost(ex))
        {
            throw new Win2dDeviceLostException(ex);
        }
    }

    /// <summary>Finishes the drawing if it is not already, then releases the bitmap. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (!_finished)
            {
                _finished = true;
                _canvas.Dispose();
            }
        }
        finally
        {
            _disposed = true;
            _target.Dispose();
        }
    }
}
