using CoreGraphics;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.CoreGraphics;

public sealed partial class CoreGraphicsCanvas
{
    /// <summary>A bitmap context with the canvas that draws on it, in this canvas's layout units; the canvas is released before the bitmap.</summary>
    private sealed class OffscreenCanvas(CoreGraphicsOffscreen offscreen, CoreGraphicsCanvas canvas) : IDisposable
    {
        public CoreGraphicsCanvas Canvas { get; } = canvas;

        /// <summary>Pixels per layout unit the bitmap was made at (the mean of both axes).</summary>
        public double PixelsPerUnit => (offscreen.PixelsPerUnitX + offscreen.PixelsPerUnitY) / 2;

        /// <summary>The finished drawing; disposes the canvas first so everything it pushed is put back.</summary>
        public CGImage Snapshot
        {
            get
            {
                Canvas.Dispose();
                return offscreen.Snapshot();
            }
        }

        public void Dispose()
        {
            Canvas.Dispose();
            offscreen.Dispose();
        }
    }

    /// <summary>A bitmap for <paramref name="region"/> at the resolution this canvas currently draws at, and a canvas over it that shares this one's units.</summary>
    private OffscreenCanvas NewOffscreen(Rect region)
    {
        var offscreen = new CoreGraphicsOffscreen(region, DeviceScale, _context.MaxRasterPixels, _context.Counts);
        try
        {
            var canvas = new CoreGraphicsCanvas(_context, offscreen.Bitmap, region, _ppp, true, false, _ctm, null, _smooth, true);
            return new OffscreenCanvas(offscreen, canvas);
        }
        catch
        {
            offscreen.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public override (Canvas Graphics, PeachDrawing.Core.Image Image)? CreateTile(double width, double height)
    {
        Guard();
        if (!(width > 0) || !(height > 0) || double.IsInfinity(width) || double.IsInfinity(height))
            return null;

        var region = new Rect(0, 0, width, height);
        var offscreen = new CoreGraphicsOffscreen(region, DeviceScale, _context.MaxRasterPixels, _context.Counts);
        try
        {
            _context.Counts.Tile(1);

            // The recording starts at its own origin; the transform a reader consults is seeded from this canvas's (see Canvas.CreateTile).
            var tile = new CoreGraphicsCanvas(_context, offscreen.Bitmap, region, _ppp, true, true, _ctm, null, _smooth, true);
            return (tile, new CoreGraphicsTileImage(width, height, tile, offscreen));
        }
        catch
        {
            offscreen.Dispose();
            _context.Counts.Tile(-1);
            throw;
        }
    }

    /// <summary>A layer begun and not yet finished: the canvas its content is drawn on and what composites it.</summary>
    private sealed record PendingLayer(OffscreenCanvas Content, LayerOptions Options, Rect Region);

    private List<PendingLayer>? _pendingLayers;

    /// <inheritdoc />
    public override CanvasLayer? BeginLayer(LayerOptions options)
    {
        Guard();

        var region = options.Bounds ?? GetClip();
        if (!(region.Width > 0) || !(region.Height > 0) || double.IsInfinity(region.Width) || double.IsInfinity(region.Height))
            return null;

        // The group is drawn aside in a bitmap of its own and put back, through the opacity, blend mode and effects given here, when it is finished.
        var content = NewOffscreen(region);
        _context.Counts.Layer(1);
        (_pendingLayers ??= []).Add(new PendingLayer(content, options, region));
        return new CanvasLayer(content.Canvas, () => CompleteLayer(content.Canvas));
    }

    // Layers begun and not yet finished. A canvas disposed before its layers are finishes them: the groups are put back as they stand.
    private void AbandonLayers()
    {
        if (_pendingLayers is null)
            return;

        var pending = _pendingLayers;
        _pendingLayers = null;
        List<Exception>? failures = null;
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            // One layer failing to composite must not leave the others' bitmaps unreleased.
            try
            {
                Finish(pending[i]);
            }
            catch (Exception e)
            {
                (failures ??= []).Add(e);
            }
        }

        if (failures is not null)
            throw new AggregateException(failures);
    }

    private void CompleteLayer(CoreGraphicsCanvas content)
    {
        // Already abandoned by Dispose (or finished once before): nothing is left to composite or release.
        if (_pendingLayers is null || _pendingLayers.FindIndex(p => ReferenceEquals(p.Content.Canvas, content)) is not (>= 0 and var index))
            return;

        var layer = _pendingLayers[index];
        _pendingLayers.RemoveAt(index);
        Finish(layer);
    }

    private void Finish(PendingLayer layer)
    {
        try
        {
            var picture = layer.Content.Snapshot;

            // The colour matrix applies to the finished group before its effects, as it does when there are none.
            var chain = new List<LayerEffect>();
            if (layer.Options.ColorMatrix is { } matrix)
                chain.Add(new PeachDrawing.Core.ColorMatrixEffect(matrix));

            if (layer.Options.Effects is { Count: > 0 } effects)
                chain.AddRange(effects);

            CGImage? processed = chain.Count > 0 ? _context.Effects.Apply(picture, chain, layer.Content.PixelsPerUnit) : null;
            try
            {
                var depth = _depth;
                Save();
                try
                {
                    _cg.SetAlpha((nfloat)Math.Clamp(layer.Options.Opacity, 0, 1));
                    _cg.SetBlendMode(BlendModeFor(layer.Options.BlendMode != PaintBlendMode.Normal ? layer.Options.BlendMode : _blend));
                    DrawCgImage(processed ?? picture, layer.Region, ImageSampling.Automatic, null, 1, 1, CGInterpolationQuality.Default);
                }
                finally
                {
                    RestoreTo(depth);
                }
            }
            finally
            {
                processed?.Dispose();
            }
        }
        finally
        {
            layer.Content.Dispose();
            _context.Counts.Layer(-1);
        }
    }
}
