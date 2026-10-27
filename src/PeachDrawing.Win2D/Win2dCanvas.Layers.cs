using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using W2dColorMatrixEffect = Microsoft.Graphics.Canvas.Effects.ColorMatrixEffect;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Win2D;

public sealed partial class Win2dCanvas
{
    /// <summary>
    /// A canvas that records into a new command list: a tile, or the content of a layer. The list and the session that fills it are counted so
    /// that a test can prove they were released; the returned canvas closes the session when it is disposed, and whoever receives the list
    /// disposes it.
    /// </summary>
    private (Win2dCanvas Canvas, CanvasCommandList List) NewRecording(Rect clip)
    {
        var list = new CanvasCommandList(_session);
        _context.Counts.CommandList(1);
        try
        {
            var session = list.CreateDrawingSession();
            _context.Counts.Session(1);
            _context.Counts.Tile(1);

            // The recording starts at its own origin; the transform a reader consults is seeded from this canvas's (see Canvas.CreateTile).
            return (new Win2dCanvas(_context, session, true, clip, _ppp, Matrix3x2.Identity, true, _ctm, null), list);
        }
        catch
        {
            list.Dispose();
            _context.Counts.CommandList(-1);
            throw;
        }
    }

    /// <inheritdoc />
    public override (Canvas Graphics, PeachDrawing.Core.Image Image)? CreateTile(double width, double height)
    {
        Guard();
        if (!(width > 0) || !(height > 0) || double.IsInfinity(width) || double.IsInfinity(height))
            return null;

        var (tile, list) = NewRecording(new Rect(0, 0, width, height));
        return (tile, new Win2dTileImage(width, height, tile, list, _context.Counts));
    }

    /// <inheritdoc />
    public override CanvasLayer? BeginLayer(LayerOptions options)
    {
        Guard();

        // The group is recorded in this canvas's coordinates as they are now, and put back under the same placement when it is finished.
        var placement = EffectiveTransform;
        var (recording, list) = NewRecording(GetClip());
        try
        {
            if (options.Bounds is { } bounds)
                recording.PushClip(bounds);
        }
        catch
        {
            recording.Dispose();
            list.Dispose();
            _context.Counts.CommandList(-1);
            throw;
        }

        (_pendingLayers ??= []).Add((recording, list));
        return new CanvasLayer(recording, () => CompleteLayer(recording, list, options, placement));
    }

    // Layers begun and not yet finished. A canvas disposed before its layers are finishes them without compositing: the group was never
    // asked for, and what it recorded has nowhere left to go.
    private List<(Win2dCanvas Recording, CanvasCommandList List)>? _pendingLayers;

    private void AbandonLayers()
    {
        if (_pendingLayers is null)
            return;

        var pending = _pendingLayers;
        _pendingLayers = null;
        foreach (var (recording, list) in pending)
        {
            recording.Dispose();
            list.Dispose();
            _context.Counts.CommandList(-1);
        }
    }

    private void CompleteLayer(Win2dCanvas recording, CanvasCommandList list, LayerOptions options, Matrix3x2 placement)
    {
        // Already abandoned by Dispose (or finished once before): nothing is left to composite or release.
        if (_pendingLayers is null || !_pendingLayers.Remove((recording, list)))
            return;

        try
        {
            // A command list can only be drawn once the session recording into it is closed.
            recording.Dispose();
            if (_disposed)
                return;

            var made = new List<IDisposable>();
            var savedTransform = _session.Transform;
            var savedBlend = _session.Blend;
            try
            {
                ICanvasImage image = list;
                if (options.ColorMatrix is { } matrix)
                    image = Track(made, ColorMatrixFor(image, matrix));

                if (options.Effects is { } effects)
                {
                    foreach (var effect in effects)
                        image = Apply(made, image, effect);
                }

                if (options.Opacity < 1)
                    image = Track(made, new OpacityEffect { Source = image, Opacity = (float)Math.Clamp(options.Opacity, 0, 1) });

                _session.Transform = placement;
                if (options.BlendMode != PaintBlendMode.Normal)
                    _session.Blend = SessionBlendFor(options.BlendMode);

                _session.DrawImage(image);
            }
            finally
            {
                _session.Transform = savedTransform;
                _session.Blend = savedBlend;
                for (var i = made.Count - 1; i >= 0; i--)
                    made[i].Dispose();
            }
        }
        finally
        {
            list.Dispose();
            _context.Counts.CommandList(-1);
        }
    }

    private static T Track<T>(List<IDisposable> made, T effect) where T : class, ICanvasImage, IDisposable
    {
        made.Add(effect);
        return effect;
    }

    private static ICanvasImage Apply(List<IDisposable> made, ICanvasImage image, LayerEffect effect)
    {
        switch (effect)
        {
            case BlurEffect blur:
                // Win2D blurs with one standard deviation; an unequal pair is blurred by their mean.
                return Track(made, new GaussianBlurEffect { Source = image, BlurAmount = (float)((blur.SigmaX + blur.SigmaY) / 2), BorderMode = EffectBorderMode.Soft });

            case DropShadowEffect shadow:
                var shade = Track(made, new ShadowEffect
                {
                    Source = image,
                    BlurAmount = (float)((shadow.SigmaX + shadow.SigmaY) / 2),
                    ShadowColor = ToColor(shadow.Color),
                });
                var moved = Track(made, new Transform2DEffect { Source = shade, TransformMatrix = Matrix3x2.CreateTranslation((float)shadow.OffsetX, (float)shadow.OffsetY) });
                return Track(made, new CompositeEffect { Mode = CanvasComposite.SourceOver, Sources = { moved, image } });

            case PeachDrawing.Core.ColorMatrixEffect colorMatrix:
                return Track(made, ColorMatrixFor(image, colorMatrix.Matrix));

            default:
                return image;
        }
    }
}
