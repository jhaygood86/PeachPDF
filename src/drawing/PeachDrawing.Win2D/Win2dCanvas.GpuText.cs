using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D;

public sealed partial class Win2dCanvas
{
    // ClearType blends each colour channel with its own coverage, so it needs the colour of the pixel behind. A drawing session cannot be read while it is
    // drawn into, and reading its target back from the GPU stalls the pipeline for every word. Over a render target this canvas can sample, text is queued
    // instead and composited by a pixel shader (Shaders/LcdBlend.hlsl) in one pass: backdrop * (1 - coverage) + colour * coverage, per channel.
    //
    // The queue is drawn before anything else is: a draw (or a clip, layer or blend change) that could touch queued text first makes the queue visible, so
    // the order things were drawn in is the order they appear in. Queued runs are kept apart: a run over another one starts a new queue, since both would
    // otherwise blend against the backdrop and not against each other.

    private CanvasRenderTarget? _lcdTarget;
    private readonly List<QueuedText> _queue = [];
    private (int Left, int Top, int Right, int Bottom) _queueBounds;
    private PixelShaderEffect? _lcdEffect;

    private readonly record struct QueuedText(Win2dTextMask Mask, PaintColor Color, Win2dTextMaskCache.Key Key, bool Cacheable, bool Bgr);

    /// <summary>
    /// Makes a canvas that draws into a drawing session over a render target the caller made, which lets text be blended on the GPU against the pixels
    /// already drawn there (no read-back of the pixels behind each word). Otherwise it is <see cref="FromSession"/>: the session is borrowed, and put back as it
    /// was when the canvas is disposed.
    /// </summary>
    /// <param name="context">where fonts and images come from</param>
    /// <param name="target">the render target the session draws into. Its alpha mode must be <see cref="CanvasAlphaMode.Ignore"/> (an opaque surface); with any other the canvas works like <see cref="FromSession"/></param>
    /// <param name="session">a drawing session of <paramref name="target"/>, which stays the caller's</param>
    /// <param name="bounds">the area being drawn, in layout units</param>
    /// <param name="linkOptions">which links <see cref="Links"/> may open, or <see langword="null"/> for the defaults</param>
    /// <returns>the canvas; dispose it when the drawing is done, and before the session is closed</returns>
    public static Win2dCanvas FromRenderTarget(Win2dRenderContext context, CanvasRenderTarget target, CanvasDrawingSession session, Rect bounds, Win2dLinkOptions? linkOptions = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var canvas = FromSession(context, session, bounds, linkOptions);
        if (target.AlphaMode == CanvasAlphaMode.Ignore)
            canvas._lcdTarget = target;

        return canvas;
    }

    private void QueueText(Win2dTextMask run, PaintColor color, Win2dTextMaskCache.Key key, bool cacheable, bool bgrStripes)
    {
        // The rectangle of a mask has a pixel of margin all round, where there is no ink: two runs whose ink does not meet can share a queue.
        if (_queue.Count > 0 && OverlapsQueued(run.Left + 1, run.Top + 1, run.Left + run.Width - 1, run.Top + run.Height - 1, ink: true))
            FlushText();

        var bounds = (run.Left, run.Top, run.Left + run.Width, run.Top + run.Height);
        _queueBounds = _queue.Count == 0
            ? bounds
            : (Math.Min(_queueBounds.Left, bounds.Left), Math.Min(_queueBounds.Top, bounds.Top), Math.Max(_queueBounds.Right, bounds.Item3), Math.Max(_queueBounds.Bottom, bounds.Item4));
        _queue.Add(new QueuedText(run, color, key, cacheable, bgrStripes));
    }

    private bool OverlapsQueued(double left, double top, double right, double bottom, bool ink)
    {
        if (right <= _queueBounds.Left || left >= _queueBounds.Right || bottom <= _queueBounds.Top || top >= _queueBounds.Bottom)
            return false;

        var inset = ink ? 1 : 0;
        foreach (var queued in _queue)
        {
            var m = queued.Mask;
            if (right > m.Left + inset && left < m.Left + m.Width - inset && bottom > m.Top + inset && top < m.Top + m.Height - inset)
                return true;
        }

        return false;
    }

    /// <summary>Makes whatever is about to be drawn at this box (layout units, in the current transform) wait for the queued text that is in its way.</summary>
    private void NoteDrawn(double left, double top, double right, double bottom, int margin = 3)
    {
        if (_queue.Count == 0)
            return;

        var m = _session.Transform;
        var scale = DpiScale;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var i = 0; i < 4; i++)
        {
            var x = (i & 1) == 0 ? left : right;
            var y = (i & 2) == 0 ? top : bottom;
            var dx = ((x * m.M11) + (y * m.M21) + m.M31) * scale;
            var dy = ((x * m.M12) + (y * m.M22) + m.M32) * scale;
            if (!double.IsFinite(dx) || !double.IsFinite(dy))
            {
                FlushText();
                return;
            }

            minX = Math.Min(minX, dx);
            minY = Math.Min(minY, dy);
            maxX = Math.Max(maxX, dx);
            maxY = Math.Max(maxY, dy);
        }

        if (OverlapsQueued(minX - margin, minY - margin, maxX + margin, maxY + margin, ink: false))
            FlushText();
    }

    /// <summary>Draws the queued text onto the target, then forgets it. A no-op when nothing is queued.</summary>
    private void FlushText()
    {
        if (_queue.Count == 0)
            return;

        var items = _queue.ToArray();
        _queue.Clear();
        if (_lcdTarget is not { } target || _disposed)
            return;

        var started = Win2dTextStatistics.Start();
        var size = target.SizeInPixels;
        var left = Math.Max(0, _queueBounds.Left);
        var top = Math.Max(0, _queueBounds.Top);
        var right = Math.Min((int)size.Width, _queueBounds.Right);
        var bottom = Math.Min((int)size.Height, _queueBounds.Bottom);
        if (right <= left || bottom <= top)
            return;

        var dpi = _session.Dpi;
        var scale = dpi / 96f;
        var scratch = _context.LcdScratch;
        scratch.Ensure(_session.Device, dpi, (int)size.Width, (int)size.Height);

        var transient = new List<IDisposable>();
        try
        {
            // What is on the target now, for the shader to read (the session is being drawn into, so it cannot be sampled directly).
            _session.Flush();
            scratch.Backdrop.CopyPixelsFromBitmap(target, left, top, left, top, right - left, bottom - top);

            var area = new Windows.Foundation.Rect(left / scale, top / scale, (right - left) / scale, (bottom - top) / scale);
            AddLayer(scratch.Ink, items, area, scale, inkLayer: true, transient);
            AddLayer(scratch.Alpha, items, area, scale, inkLayer: false, transient);

            _lcdEffect ??= Win2dLcdShader.Create();
            _lcdEffect.Source1 = scratch.Backdrop;
            _lcdEffect.Source2 = scratch.Ink;
            _lcdEffect.Source3 = scratch.Alpha;

            // The shader's output has no edge, so it is made in a target of its own and only the area is put back.
            using (var result = scratch.Result.CreateDrawingSession())
                result.DrawImage(_lcdEffect, (float)area.X, (float)area.Y, area);

            var transform = _session.Transform;
            var blend = _session.Blend;
            try
            {
                _session.Transform = Matrix3x2.Identity;
                _session.Blend = CanvasBlend.Copy;
                _session.DrawImage(scratch.Result, (float)area.X, (float)area.Y, area);
            }
            finally
            {
                _session.Blend = blend;
                _session.Transform = transform;
            }
        }
        finally
        {
            for (var i = transient.Count - 1; i >= 0; i--)
                transient[i].Dispose();

            Win2dTextStatistics.Stop(Win2dTextStatistics.Stage.Blend, started);
            Win2dTextStatistics.Report(_context.TextMasks);
        }
    }

    // One layer: black over the area, then every queued run's bitmap added at its place. Where runs do not overlap, adding is exact.
    private void AddLayer(CanvasRenderTarget layer, QueuedText[] items, Windows.Foundation.Rect area, float scale, bool inkLayer, List<IDisposable> transient)
    {
        using var session = layer.CreateDrawingSession();
        session.Blend = CanvasBlend.Copy;
        session.FillRectangle(area, Windows.UI.Color.FromArgb(255, 0, 0, 0));
        session.Blend = CanvasBlend.Add;
        foreach (var item in items)
        {
            var bitmap = BitmapFor(item, inkLayer, transient);
            session.DrawImage(bitmap, item.Mask.Left / scale, item.Mask.Top / scale);
        }
    }

    private CanvasBitmap BitmapFor(QueuedText item, bool inkLayer, List<IDisposable> transient)
    {
        CanvasBitmap Make()
        {
            var alphas = Win2dTextBlend.StripeAlphas(item.Mask, item.Color, item.Bgr);
            var bytes = inkLayer ? Win2dTextBlend.InkTimesAlpha(alphas, item.Color) : alphas;
            return CanvasBitmap.CreateFromBytes(_session, bytes, item.Mask.Width, item.Mask.Height, DirectXPixelFormat.R8G8B8A8UIntNormalized, _session.Dpi, CanvasAlphaMode.Ignore);
        }

        if (!item.Cacheable)
        {
            // A run cut by the edge of the target is not the run the cache key names.
            var bitmap = Make();
            transient.Add(bitmap);
            return bitmap;
        }

        var (r, g, b) = item.Color.ToRgb();
        var ink = ((uint)item.Color.A << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        var key = new Win2dTextBitmapCache.Key(item.Key, ink, inkLayer, item.Bgr, _session.Dpi);
        return _context.TextBitmaps.GetOrAdd(_session.Device, key, Make, (long)item.Mask.Width * item.Mask.Height * 4);
    }
}
