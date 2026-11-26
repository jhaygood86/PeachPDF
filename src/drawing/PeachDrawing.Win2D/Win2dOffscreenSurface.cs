using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX;
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
    private readonly bool _wide;

    public Win2dOffscreenSurface(Win2dRenderContext context, CanvasRenderTarget target, Win2dCanvas canvas, int width, int height, bool wide = false)
        : base(width, height)
    {
        _wide = wide;
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

        var copy = _wide
            ? new CanvasRenderTarget(_context.Device, Width, Height, 96f, DirectXPixelFormat.R16G16B16A16Float, CanvasAlphaMode.Premultiplied)
            : new CanvasRenderTarget(_context.Device, Width, Height, 96f);
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

    public override bool IsWide => _wide;

    public override float[]? ReadWidePixels()
    {
        if (!_wide)
            return null;

        ObjectDisposedException.ThrowIf(_target is null, this);
        _canvas!.Flush();

        // Half floats, premultiplied, R G B A: straight floats out.
        var bytes = _target.GetPixelBytes();
        var halves = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(bytes);
        var result = new float[halves.Length];
        for (var i = 0; i + 3 < result.Length; i += 4)
        {
            var a = (float)halves[i + 3];
            if (a <= 0f)
                continue;

            result[i] = (float)halves[i] / a;
            result[i + 1] = (float)halves[i + 1] / a;
            result[i + 2] = (float)halves[i + 2] / a;
            result[i + 3] = a;
        }

        return result;
    }

    internal static PixelBuffer Read(CanvasBitmap bitmap, int width, int height)
    {
        if (bitmap.Format == DirectXPixelFormat.R16G16B16A16Float)
        {
            // Half floats: the eight-bit view brings each into range.
            var half = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(bitmap.GetPixelBytes());
            var view = new byte[half.Length];
            for (var i = 0; i + 3 < view.Length; i += 4)
            {
                var a = Math.Clamp((float)half[i + 3], 0f, 1f);
                view[i] = (byte)Math.Round(Math.Clamp(Math.Min((float)half[i], a), 0f, 1f) * 255f);
                view[i + 1] = (byte)Math.Round(Math.Clamp(Math.Min((float)half[i + 1], a), 0f, 1f) * 255f);
                view[i + 2] = (byte)Math.Round(Math.Clamp(Math.Min((float)half[i + 2], a), 0f, 1f) * 255f);
                view[i + 3] = (byte)Math.Round(a * 255f);
            }

            return new PixelBuffer(width, height, view);
        }

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
    public override OffscreenSurface? CreateOffscreenSurface(int widthPixels, int heightPixels) => CreateOffscreenSurface(widthPixels, heightPixels, wide: false);

    /// <inheritdoc />
    public override OffscreenSurface? CreateOffscreenSurface(int widthPixels, int heightPixels, bool wide)
    {
        Guard();
        if (widthPixels <= 0 || heightPixels <= 0 || (long)widthPixels * heightPixels > _context.MaxRasterPixels / (wide ? 2 : 1))
            return null;

        CanvasRenderTarget? target = null;
        try
        {
            // 96 dpi, so one device-independent pixel is one pixel and the canvas draws one unit a pixel. A wide surface is of half floats, which keep what is drawn outside 0 to 1.
            target = wide
                ? new CanvasRenderTarget(_context.Device, widthPixels, heightPixels, 96f, DirectXPixelFormat.R16G16B16A16Float, CanvasAlphaMode.Premultiplied)
                : new CanvasRenderTarget(_context.Device, widthPixels, heightPixels, 96f);
            _context.Counts.OffscreenTarget(1);
            var session = target.CreateDrawingSession();
            // The effects drawn onto a wide target (blur, shadow, filters) are worked out in half floats too, so what is outside 0 to 1 stays.
            if (wide)
                session.EffectBufferPrecision = CanvasBufferPrecision.Precision16Float;

            session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            var canvas = OverSurface(_context, session, new Rect(0, 0, widthPixels, heightPixels), target);
            return new Win2dOffscreenSurface(_context, target, canvas, widthPixels, heightPixels, wide);
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

/// <summary>An <see cref="Image"/> of half-float pixels held on the device: what <see cref="Canvas.CreateWideImage"/> makes, drawn without being brought into range.</summary>
internal sealed class Win2dWideImage : PeachDrawing.Core.Image
{
    private CanvasBitmap? _bitmap;
    private readonly Win2dResourceCounts _counts;

    public Win2dWideImage(CanvasBitmap bitmap, int width, int height, Win2dResourceCounts counts)
    {
        _bitmap = bitmap;
        Width = width;
        Height = height;
        _counts = counts;
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    public CanvasBitmap Bitmap => _bitmap ?? throw new ObjectDisposedException(nameof(Win2dWideImage));

    public override PixelBuffer? GetPixels() => Win2dOffscreenSurface.Read(Bitmap, (int)Width, (int)Height);

    public override void Dispose()
    {
        if (_bitmap is null)
            return;

        _bitmap.Dispose();
        _bitmap = null;
        _counts.OffscreenTarget(-1);
    }
}

public sealed partial class Win2dCanvas
{
    /// <inheritdoc />
    public override PeachDrawing.Core.Image? CreateWideImage(int widthPixels, int heightPixels, ReadOnlySpan<float> straightRgba)
    {
        Guard();
        if (widthPixels <= 0 || heightPixels <= 0 || (long)widthPixels * heightPixels > _context.MaxRasterPixels / 2 || straightRgba.Length < widthPixels * heightPixels * 4)
            return null;

        var halves = new Half[widthPixels * heightPixels * 4];
        for (var i = 0; i + 3 < halves.Length; i += 4)
        {
            var a = Math.Clamp(float.IsNaN(straightRgba[i + 3]) ? 0f : straightRgba[i + 3], 0f, 1f);
            halves[i] = (Half)((float.IsNaN(straightRgba[i]) ? 0f : straightRgba[i]) * a);
            halves[i + 1] = (Half)((float.IsNaN(straightRgba[i + 1]) ? 0f : straightRgba[i + 1]) * a);
            halves[i + 2] = (Half)((float.IsNaN(straightRgba[i + 2]) ? 0f : straightRgba[i + 2]) * a);
            halves[i + 3] = (Half)a;
        }

        var bitmap = CanvasBitmap.CreateFromBytes(_context.Device, System.Runtime.InteropServices.MemoryMarshal.AsBytes(halves.AsSpan()).ToArray(), widthPixels, heightPixels,
            DirectXPixelFormat.R16G16B16A16Float, 96f, CanvasAlphaMode.Premultiplied);
        _context.Counts.OffscreenTarget(1);
        return new Win2dWideImage(bitmap, widthPixels, heightPixels, _context.Counts);
    }
}
