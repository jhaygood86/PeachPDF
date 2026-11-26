using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using System;
using System.Numerics;

namespace PeachDrawing.Win2D;

/// <summary>
/// An <see cref="Image"/> that is a bitmap on the graphics device: a snapshot of an <see cref="OffscreenSurface"/>, drawn from the device without a read back.
/// </summary>
internal sealed class Win2dBitmapImage : PeachDrawing.Core.Image
{
    private readonly Win2dResourceCounts _counts;
    private CanvasRenderTarget? _target;

    public Win2dBitmapImage(CanvasRenderTarget target, int width, int height, Win2dResourceCounts counts)
    {
        _target = target;
        Width = width;
        Height = height;
        _counts = counts;
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    public CanvasBitmap Bitmap => _target ?? throw new ObjectDisposedException(nameof(Win2dBitmapImage));

    public override PixelBuffer? GetPixels() => Win2dOffscreenSurface.Read(Bitmap, (int)Width, (int)Height);

    public override void Dispose()
    {
        if (_target is null)
            return;

        _target.Dispose();
        _target = null;
        _counts.OffscreenTarget(-1);
    }
}

/// <summary>
/// The Win2D backend's <see cref="OffscreenSurface"/>: a <see cref="CanvasRenderTarget"/> on the device and a <see cref="Win2dCanvas"/> over a session on it. Drawing,
/// compositing with operators and drawing a snapshot onto another canvas all happen on the device; only <see cref="ReadPixels"/> reads back. A snapshot is a copy of
/// the target made on the device.
/// </summary>
internal sealed class Win2dOffscreenSurface : OffscreenSurface
{
    private readonly Win2dRenderContext _context;
    private CanvasRenderTarget? _target;
    private Win2dCanvas? _canvas;

    public Win2dOffscreenSurface(Win2dRenderContext context, CanvasRenderTarget target, Win2dCanvas canvas, int width, int height)
        : base(width, height)
    {
        _context = context;
        _target = target;
        _canvas = canvas;
    }

    public override Canvas Canvas => _canvas ?? throw new ObjectDisposedException(nameof(Win2dOffscreenSurface));

    public override bool CanDraw(Canvas target) => target is Win2dCanvas win2d && ReferenceEquals(win2d.Context, _context);

    public override PeachDrawing.Core.Image CreateSnapshot()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        _canvas!.Flush();

        var copy = new CanvasRenderTarget(_context.Device, Width, Height, 96f);
        _context.Counts.OffscreenTarget(1);
        try
        {
            using (var session = copy.CreateDrawingSession())
            {
                session.Blend = CanvasBlend.Copy;
                session.DrawImage(_target);
            }

            return new Win2dBitmapImage(copy, Width, Height, _context.Counts);
        }
        catch
        {
            copy.Dispose();
            _context.Counts.OffscreenTarget(-1);
            throw;
        }
    }

    public override PixelBuffer ReadPixels()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        _canvas!.Flush();
        return Read(_target, Width, Height);
    }

    internal static PixelBuffer Read(CanvasBitmap bitmap, int width, int height)
    {
        var bgra = bitmap.GetPixelBytes();
        var rgba = new byte[bgra.Length];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return new PixelBuffer(width, height, rgba);
    }

    public override void Clear()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        _canvas!.Flush();
        _canvas.ClearTarget();
    }

    public override void Dispose()
    {
        if (_target is null)
            return;

        _canvas!.Dispose();
        _canvas = null;
        _target.Dispose();
        _target = null;
        _context.Counts.OffscreenTarget(-1);
    }
}

public sealed partial class Win2dCanvas
{
    /// <summary>Writes whatever the canvas has queued (text runs waiting to be blended) and the session has batched into the target.</summary>
    internal void Flush()
    {
        FlushText();
        _session.Flush();
    }

    /// <summary>Makes the whole target transparent. A session clears ignoring clips and layers, which is what a surface needs.</summary>
    internal void ClearTarget() => _session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    /// <inheritdoc />
    public override OffscreenSurface? CreateOffscreenSurface(int widthPixels, int heightPixels)
    {
        Guard();
        if (widthPixels <= 0 || heightPixels <= 0 || (long)widthPixels * heightPixels > _context.MaxRasterPixels)
            return null;

        CanvasRenderTarget? target = null;
        try
        {
            // 96 dpi, so one device-independent pixel is one pixel and the canvas draws one unit a pixel.
            target = new CanvasRenderTarget(_context.Device, widthPixels, heightPixels, 96f);
            _context.Counts.OffscreenTarget(1);
            var session = target.CreateDrawingSession();
            session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            var canvas = OverSurface(_context, session, new Rect(0, 0, widthPixels, heightPixels), target);
            return new Win2dOffscreenSurface(_context, target, canvas, widthPixels, heightPixels);
        }
        catch
        {
            if (target is not null)
            {
                target.Dispose();
                _context.Counts.OffscreenTarget(-1);
            }

            throw;
        }
    }

    /// <summary>A canvas over a session this canvas owns, on a target of its own that is also its backdrop (so it composites with operators); one unit is one pixel.</summary>
    private static Win2dCanvas OverSurface(Win2dRenderContext context, Microsoft.Graphics.Canvas.CanvasDrawingSession session, Rect bounds, CanvasRenderTarget target)
    {
        context.Counts.Session(1);
        try
        {
            var canvas = new Win2dCanvas(context, session, true, bounds, 1.0, Matrix3x2.Identity, true, Matrix3x2.Identity, null);
            canvas.SetBackdrop(target);
            return canvas;
        }
        catch
        {
            context.Counts.Session(-1);
            throw;
        }
    }
}
