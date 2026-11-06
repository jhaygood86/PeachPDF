using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using System;
using System.Numerics;

namespace PeachDrawing.Win2D;

/// <summary>
/// A <see cref="RetainedLayer"/> kept as a texture on the graphics device: the group is drawn once into a <see cref="CanvasRenderTarget"/> at the
/// resolution the recording canvas had, and every <see cref="Draw"/> is a single textured quad under the target's transform, so a frame that only moves or
/// fades it costs a composite and not a repaint. The texture belongs to the device it was made on; <see cref="CanDraw"/> turns false when that device is lost.
/// </summary>
internal sealed class Win2dRetainedLayer : RetainedLayer
{
    private readonly Win2dRenderContext _context;
    private readonly CanvasDevice _device;
    private readonly Win2dCanvas _recorder;
    private CanvasRenderTarget? _target;
    private bool _sealed;
    private bool _disposed;

    internal Win2dRetainedLayer(Win2dRenderContext context, CanvasDevice device, Rect bounds, CanvasRenderTarget target, Win2dCanvas recorder)
        : base(bounds)
    {
        _context = context;
        _device = device;
        _target = target;
        _recorder = recorder;
        _context.Counts.RetainedLayer(1);
    }

    public override Canvas Recorder => _sealed ? throw new InvalidOperationException("The layer is sealed.") : _recorder;

    public override bool IsSealed => _sealed;

    public override void Seal()
    {
        if (_sealed)
            return;

        _sealed = true;
        // The recording canvas flushes its queued text and closes the session that fills the texture; only then can the texture be drawn.
        _recorder.Dispose();
    }

    public override bool CanDraw(Canvas target) =>
        !_disposed && _sealed && _target is not null && target is Win2dCanvas canvas && ReferenceEquals(canvas.Context.Device, _device);

    public override void Draw(Canvas target, double opacity = 1.0)
    {
        if (!_sealed)
            throw new InvalidOperationException("The layer is not sealed.");

        ObjectDisposedException.ThrowIf(_disposed, this);
        if (target is not Win2dCanvas canvas)
            throw new ArgumentException("A Win2D retained layer can only be drawn on a Win2D canvas.", nameof(target));

        canvas.DrawRetained(_target!, Bounds, opacity);
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _recorder.Dispose();
        _target?.Dispose();
        _target = null;
        _context.Counts.RetainedLayer(-1);
    }
}

public sealed partial class Win2dCanvas
{
    // Never more device pixels than this either way: a layer that large costs more as a texture than repainting it.
    private const double MaxRetainedPixels = 8192;

    /// <inheritdoc />
    public override RetainedLayer? CreateRetainedLayer(Rect bounds)
    {
        Guard();
        if (!(bounds.Width > 0) || !(bounds.Height > 0) || double.IsInfinity(bounds.Width) || double.IsInfinity(bounds.Height))
            return null;

        // The texture has the density the host draws at: its session's DPI times the scale of the transform it set before handing us the session.
        var baseScale = Math.Sqrt(Math.Abs((double)_deviceBase.M11 * _deviceBase.M22 - (double)_deviceBase.M12 * _deviceBase.M21));
        var density = Math.Clamp(DpiScale * (baseScale > 1e-6 ? baseScale : 1.0), 0.5, 4.0);
        if (bounds.Width * density > MaxRetainedPixels || bounds.Height * density > MaxRetainedPixels)
            return null;

        CanvasRenderTarget? target = null;
        CanvasDrawingSession? session = null;
        try
        {
            target = new CanvasRenderTarget(_session, (float)bounds.Width, (float)bounds.Height, (float)(96.0 * density));
            session = target.CreateDrawingSession();
            session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            _context.Counts.Session(1);

            // The recording draws in this canvas's coordinates: the session's origin is the layer's corner.
            var recorder = new Win2dCanvas(_context, session, true, bounds, _ppp, Matrix3x2.CreateTranslation((float)-bounds.X, (float)-bounds.Y), true, _ctm, null);
            _context.Counts.Tile(1);
            return new Win2dRetainedLayer(_context, _context.Device, bounds, target, recorder);
        }
        catch
        {
            if (session is not null)
            {
                session.Dispose();
                _context.Counts.Session(-1);
            }

            target?.Dispose();
            throw;
        }
    }

    /// <summary>Draws a retained layer's texture over <paramref name="bounds"/> under the current transform.</summary>
    internal void DrawRetained(CanvasRenderTarget texture, Rect bounds, double opacity)
    {
        Guard();
        DrawSource(texture, bounds.Width, bounds.Height, bounds, null, Microsoft.Graphics.Canvas.CanvasImageInterpolation.Linear, (float)Math.Clamp(opacity, 0, 1));
    }
}
