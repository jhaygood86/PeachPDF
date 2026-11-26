using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using W2dColorMatrixEffect = Microsoft.Graphics.Canvas.Effects.ColorMatrixEffect;
using W2dConvolveMatrixEffect = Microsoft.Graphics.Canvas.Effects.ConvolveMatrixEffect;
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
        Win2dPaintStatistics.Count(Win2dPaintStatistics.Counter.CommandList);
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
                        image = Apply(made, image, effect, options.Bounds);
                }

                if (options.Opacity < 1)
                    image = Track(made, new OpacityEffect { Source = image, Opacity = (float)Math.Clamp(options.Opacity, 0, 1) });

                FlushText();
                if (IsCompositeOperator(options.BlendMode) && TryDrawComposited(image, placement, options.BlendMode, options.Bounds, made))
                    return;

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

    /// <summary>Whether two standard deviations are the same for blurring purposes (so one blur amount does for both directions).</summary>
    private static bool SameDeviation(double sigmaX, double sigmaY) => Math.Abs(sigmaX - sigmaY) <= 1e-6 * Math.Max(Math.Abs(sigmaX), Math.Abs(sigmaY));

    /// <summary>
    /// <paramref name="image"/> blurred with standard deviation <paramref name="sigmaX"/> sideways and <paramref name="sigmaY"/> up and down. Equal
    /// deviations are one Gaussian blur; unequal ones are a horizontal blur and a vertical one in turn, which together are the two-dimensional
    /// Gaussian (it separates), so each direction gets its own amount instead of both getting their mean. A direction with no deviation is not blurred.
    /// </summary>
    private static ICanvasImage Blur(List<IDisposable> made, ICanvasImage image, double sigmaX, double sigmaY, EffectBorderMode border)
    {
        if (SameDeviation(sigmaX, sigmaY))
            return Track(made, new GaussianBlurEffect { Source = image, BlurAmount = (float)sigmaX, BorderMode = border });

        if (sigmaX > 0)
            image = Track(made, new DirectionalBlurEffect { Source = image, BlurAmount = (float)sigmaX, Angle = 0f, BorderMode = border });

        if (sigmaY > 0)
            image = Track(made, new DirectionalBlurEffect { Source = image, BlurAmount = (float)sigmaY, Angle = MathF.PI / 2, BorderMode = border });

        return image;
    }

    private static ICanvasImage Apply(List<IDisposable> made, ICanvasImage image, LayerEffect effect, Rect? bounds)
    {
        switch (effect)
        {
            case BlurEffect blur:
                return Blur(made, image, blur.SigmaX, blur.SigmaY, blur.ClampEdges ? EffectBorderMode.Hard : EffectBorderMode.Soft);

            case DropShadowEffect shadow:
                // The shadow's own effect blurs by one amount; an unequal pair is made by blurring the unblurred shadow in each direction after.
                var equal = SameDeviation(shadow.SigmaX, shadow.SigmaY);
                ICanvasImage shade = Track(made, new ShadowEffect
                {
                    Source = image,
                    BlurAmount = equal ? (float)shadow.SigmaX : 0f,
                    ShadowColor = ToColor(shadow.Color),
                });
                if (!equal)
                    shade = Blur(made, shade, shadow.SigmaX, shadow.SigmaY, EffectBorderMode.Soft);

                var moved = Track(made, new Transform2DEffect { Source = shade, TransformMatrix = Matrix3x2.CreateTranslation((float)shadow.OffsetX, (float)shadow.OffsetY) });
                return Track(made, new CompositeEffect { Mode = CanvasComposite.SourceOver, Sources = { moved, image } });

            case PeachDrawing.Core.ColorMatrixEffect colorMatrix:
                return Track(made, ColorMatrixFor(image, colorMatrix.Matrix));

            case ComponentTransferEffect transfer:
            {
                float[]? Table(byte[]? values) => values is null ? null : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(values, v => v / 255f));
                var table = new TableTransferEffect { Source = image, ClampOutput = true };
                if (Table(transfer.Red) is { } red) table.RedTable = red; else table.RedDisable = true;
                if (Table(transfer.Green) is { } green) table.GreenTable = green; else table.GreenDisable = true;
                if (Table(transfer.Blue) is { } blue) table.BlueTable = blue; else table.BlueDisable = true;
                if (Table(transfer.Alpha) is { } alpha) table.AlphaTable = alpha; else table.AlphaDisable = true;
                return Track(made, table);
            }

            case PeachDrawing.Core.ConvolveMatrixEffect convolve:
            {
                var kernel = new float[convolve.Kernel.Count];
                for (var i = 0; i < kernel.Length; i++)
                    kernel[i] = (float)convolve.Kernel[i];

                ICanvasImage result = Track(made, new W2dConvolveMatrixEffect
                {
                    Source = image,
                    KernelMatrix = kernel,
                    KernelWidth = convolve.Columns,
                    KernelHeight = convolve.Rows,
                    Divisor = (float)convolve.Divisor,
                    PreserveAlpha = convolve.PreserveAlpha,
                    ClampOutput = true,
                    BorderMode = convolve.EdgeMode == FilterEdgeMode.None ? EffectBorderMode.Soft : EffectBorderMode.Hard,
                });

                // The effect keeps the middle of the kernel over the pixel; the target is moved there by shifting the result.
                var (shiftX, shiftY) = (convolve.TargetX - convolve.Columns / 2, convolve.TargetY - convolve.Rows / 2);
                if (shiftX != 0 || shiftY != 0)
                    result = Track(made, new Transform2DEffect { Source = result, TransformMatrix = Matrix3x2.CreateTranslation(shiftX, shiftY) });

                return result;
            }

            // Win2D's turbulence effect takes no seed, so it cannot draw the noise the specification fixes: that effect is left to a canvas that can.

            default:
                return image;
        }
    }
}
