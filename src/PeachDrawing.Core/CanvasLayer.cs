using System;
using System.Collections.Generic;

namespace PeachDrawing.Core
{
    /// <summary>How a layer is composited onto the canvas that began it.</summary>
    /// <param name="Opacity">how opaque the finished layer is, from 0 (invisible) to 1; the layer's content is composited once, as a
    /// whole, so overlapping content inside it does not show through itself</param>
    /// <param name="BlendMode">how the finished layer is blended with what is already on the canvas</param>
    /// <param name="ColorMatrix">a colour transform applied to the finished layer before it is composited, or <see langword="null"/> for none</param>
    /// <param name="Bounds">the region the layer covers, in the coordinates of the canvas that began it, or <see langword="null"/> for
    /// everything from the canvas's origin to the bottom-right of its current clip. Content outside the region is not part of the layer.</param>
    /// <param name="Effects">effects applied to the finished layer, in order, before it is composited, or <see langword="null"/> for
    /// none. Effects work on pixels, so the layer is drawn into a bitmap covering exactly <paramref name="Bounds"/> and nothing
    /// outside it is kept: make the region large enough for the effects to spread into (see <c>PeachDrawing.RasterLayerEffects.GetInkMargin</c>).
    /// A canvas that cannot apply effects returns no layer at all (see <see cref="Canvas.BeginLayer"/>).</param>
    public readonly record struct LayerOptions(
        double Opacity = 1.0,
        PaintBlendMode BlendMode = PaintBlendMode.Normal,
        ColorMatrix? ColorMatrix = null,
        Rect? Bounds = null,
        IReadOnlyList<LayerEffect>? Effects = null);

    /// <summary>An effect applied to a finished layer. All distances are in the units of the canvas that began the layer.</summary>
    public abstract record LayerEffect
    {
        private protected LayerEffect()
        {
        }
    }

    /// <summary>Blurs the layer with a Gaussian of the given standard deviations.</summary>
    /// <param name="SigmaX">the horizontal standard deviation</param>
    /// <param name="SigmaY">the vertical standard deviation</param>
    public sealed record BlurEffect(double SigmaX, double SigmaY) : LayerEffect
    {
        /// <summary>A blur with the same standard deviation in both directions.</summary>
        /// <param name="sigma">the standard deviation</param>
        public BlurEffect(double sigma) : this(sigma, sigma)
        {
        }
    }

    /// <summary>Draws a blurred, offset, single-colour copy of the layer's shape underneath it.</summary>
    /// <param name="OffsetX">how far the shadow is moved to the right</param>
    /// <param name="OffsetY">how far the shadow is moved down</param>
    /// <param name="SigmaX">the horizontal standard deviation of the shadow's blur</param>
    /// <param name="SigmaY">the vertical standard deviation of the shadow's blur</param>
    /// <param name="Color">the shadow's colour; its alpha scales the shadow</param>
    public sealed record DropShadowEffect(double OffsetX, double OffsetY, double SigmaX, double SigmaY, PaintColor Color) : LayerEffect;

    /// <summary>Transforms the layer's colours.</summary>
    /// <param name="Matrix">the transform</param>
    public sealed record ColorMatrixEffect(ColorMatrix Matrix) : LayerEffect;

    /// <summary>
    /// An isolated group of drawing begun with <see cref="Canvas.BeginLayer"/>: draw onto <see cref="Canvas"/>, in the same
    /// coordinates as the canvas that began the layer, and disposing the layer composites everything drawn as one piece onto that
    /// canvas.
    /// </summary>
    public sealed class CanvasLayer : IDisposable
    {
        private Action? _complete;

        /// <summary>Creates a layer that runs <paramref name="complete"/> once, when it is disposed.</summary>
        /// <param name="canvas">the canvas to draw the layer's content onto</param>
        /// <param name="complete">composites the finished layer onto the canvas that began it</param>
        /// <exception cref="ArgumentNullException">an argument is <see langword="null"/></exception>
        public CanvasLayer(Canvas canvas, Action complete)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(complete);
            Canvas = canvas;
            _complete = complete;
        }

        /// <summary>The canvas to draw the layer's content onto.</summary>
        public Canvas Canvas { get; }

        /// <summary>Finishes the layer and composites it onto the canvas that began it. Disposing more than once does nothing.</summary>
        public void Dispose()
        {
            var complete = _complete;
            _complete = null;
            complete?.Invoke();
        }
    }
}
