using PeachDrawing.Core;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Skia;

public sealed partial class SkiaCanvas
{
    /// <inheritdoc />
    public override (Canvas Graphics, PeachDrawing.Core.Image Image)? CreateTile(double width, double height)
    {
        Guard();
        if (!(width > 0) || !(height > 0) || double.IsInfinity(width) || double.IsInfinity(height))
            return null;

        var recorder = new SKPictureRecorder();
        _context.Counts.Recorder(1);
        try
        {
            var recording = recorder.BeginRecording(SKRect.Create(0, 0, (float)width, (float)height));
            var tile = NewTileCanvas(recording, new Rect(0, 0, width, height));
            return (tile, new SkiaTileImage(width, height, tile, recorder, _context.Counts));
        }
        catch
        {
            recorder.Dispose();
            _context.Counts.Recorder(-1);
            throw;
        }
    }

    /// <summary>A layer begun and not yet finished: the canvas its content is drawn on, the save level it sits on and what composites it.</summary>
    private sealed record PendingLayer(SkiaCanvas Content, int SaveCount, List<IDisposable> Made);

    private List<PendingLayer>? _pendingLayers;

    /// <inheritdoc />
    protected override bool SupportsLayerEffects => true;

    /// <inheritdoc />
    public override CanvasLayer? BeginLayer(LayerOptions options)
    {
        Guard();

        // Skia composites an isolated group itself: the layer is a save level whose content is drawn aside and put back, through the paint
        // given here, when the level is restored.
        var made = new List<IDisposable>();
        try
        {
            var paint = new SKPaint
            {
                IsAntialias = _smooth,
                Color = new SKColor(0, 0, 0, (byte)Math.Round(Math.Clamp(options.Opacity, 0, 1) * 255)),
                BlendMode = BlendModeFor(options.BlendMode != PaintBlendMode.Normal ? options.BlendMode : _blend),
            };
            made.Add(paint);

            // The colour matrix applies to the finished group before its effects, as it does when there are none.
            var chain = new List<LayerEffect>();
            if (options.ColorMatrix is { } matrix)
                chain.Add(new PeachDrawing.Core.ColorMatrixEffect(matrix));

            if (options.Effects is { Count: > 0 } effects)
                chain.AddRange(effects);

            if (chain is [PeachDrawing.Core.ColorMatrixEffect only])
            {
                var colors = ColorMatrixFilter(only.Matrix);
                made.Add(colors);
                paint.ColorFilter = colors;
            }
            else if (chain.Count > 0)
            {
                paint.ImageFilter = ImageFilterFor(made, chain);
            }

            var clip = GetClip();
            SKRect? bounds = options.Bounds is { } b
                ? SKRect.Create((float)b.X, (float)b.Y, (float)Math.Max(b.Width, 0), (float)Math.Max(b.Height, 0))
                : null;

            var save = bounds is { } region ? _canvas.SaveLayer(region, paint) : _canvas.SaveLayer(paint);
            _context.Counts.Layer(1);

            var content = NewLayerCanvas(clip);
            if (options.Bounds is { } clipBounds)
                content.PushClip(clipBounds);

            (_pendingLayers ??= []).Add(new PendingLayer(content, save, made));
            return new CanvasLayer(content, () => CompleteLayer(content));
        }
        catch
        {
            for (var i = made.Count - 1; i >= 0; i--)
                made[i].Dispose();

            throw;
        }
    }

    private static SKImageFilter? ImageFilterFor(List<IDisposable> made, IReadOnlyList<LayerEffect> effects)
    {
        SKImageFilter? filter = null;
        foreach (var effect in effects)
        {
            var before = filter;
            switch (effect)
            {
                case BlurEffect blur:
                    filter = SKImageFilter.CreateBlur((float)blur.SigmaX, (float)blur.SigmaY, filter);
                    break;

                case DropShadowEffect shadow:
                    filter = SKImageFilter.CreateDropShadow((float)shadow.OffsetX, (float)shadow.OffsetY, (float)shadow.SigmaX, (float)shadow.SigmaY,
                        ToColor(shadow.Color), filter);
                    break;

                case PeachDrawing.Core.ColorMatrixEffect colorMatrix:
                    var colorFilter = ColorMatrixFilter(colorMatrix.Matrix);
                    made.Add(colorFilter);
                    filter = SKImageFilter.CreateColorFilter(colorFilter, filter);
                    break;
            }

            if (filter is not null && !ReferenceEquals(filter, before))
                made.Add(filter);
        }

        return filter;
    }

    // Layers begun and not yet finished. A canvas disposed before its layers are finishes them: the groups are put back as they stand, which
    // is what restoring their save levels does, and their paints are released.
    private void AbandonLayers()
    {
        if (_pendingLayers is null)
            return;

        var pending = _pendingLayers;
        _pendingLayers = null;
        for (var i = pending.Count - 1; i >= 0; i--)
            Finish(pending[i]);
    }

    private void CompleteLayer(SkiaCanvas content)
    {
        // Already abandoned by Dispose (or finished once before): nothing is left to composite or release.
        if (_pendingLayers is null || _pendingLayers.FindIndex(p => ReferenceEquals(p.Content, content)) is not (>= 0 and var index))
            return;

        var layer = _pendingLayers[index];
        _pendingLayers.RemoveAt(index);
        Finish(layer);
    }

    private void Finish(PendingLayer layer)
    {
        try
        {
            // The content canvas puts back what it pushed, then the layer's own level is restored, which composites the group.
            layer.Content.Dispose();
            _canvas.RestoreToCount(layer.SaveCount);
        }
        finally
        {
            _context.Counts.Layer(-1);
            for (var i = layer.Made.Count - 1; i >= 0; i--)
                layer.Made[i].Dispose();
        }
    }
}
