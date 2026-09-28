using System;

namespace PeachDrawing.Core
{
    /// <summary>How a layer is composited onto the canvas that began it.</summary>
    /// <param name="Opacity">how opaque the finished layer is, from 0 (invisible) to 1; the layer's content is composited once, as a
    /// whole, so overlapping content inside it does not show through itself</param>
    /// <param name="BlendMode">how the finished layer is blended with what is already on the canvas</param>
    /// <param name="ColorMatrix">a colour transform applied to the finished layer before it is composited, or <see langword="null"/> for none</param>
    /// <param name="Bounds">the region the layer covers, in the coordinates of the canvas that began it, or <see langword="null"/> for
    /// everything from the canvas's origin to the bottom-right of its current clip. Content outside the region is not part of the layer.</param>
    public readonly record struct LayerOptions(
        double Opacity = 1.0,
        PaintBlendMode BlendMode = PaintBlendMode.Normal,
        ColorMatrix? ColorMatrix = null,
        Rect? Bounds = null);

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
