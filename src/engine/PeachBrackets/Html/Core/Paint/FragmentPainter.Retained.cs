using System;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Paint
{
    internal sealed partial class FragmentPainter
    {
        // A layer is not worth keeping past this many device pixels in either direction: the texture would cost more than repainting.
        private const double MaxRetainedPixels = 8192;

        /// <summary>
        /// Paints a box whose <c>transform</c> or <c>opacity</c> is being animated (see <c>AnimationClock</c>) from a <see cref="RetainedLayer"/>: what the box and
        /// everything in it paints is recorded once, and each frame only draws that again under the current transform (already pushed by the caller) at the
        /// current opacity. A layer is recorded again when what it holds may have changed (the clock's content generation) or the canvas draws at another scale.
        /// </summary>
        /// <returns>false when the box is not promoted or the canvas cannot keep a layer; the caller then paints it the ordinary way</returns>
        private bool TryPaintRetained(Canvas g, BoxFragment fragment)
        {
            if (_textOnly || container.AnimationClock is not { } clock || clock.EntryFor(fragment.Box) is not { } entry)
                return false;

            var box = fragment.Box;

            // The scale at which the layer is recorded is the canvas's own, without the animated transform's: that one changes every frame.
            var matrix = box.HasTransform ? box.ActualTransformMatrix : System.Numerics.Matrix3x2.Identity;
            var animatedScale = Math.Sqrt(Math.Abs((double)matrix.M11 * matrix.M22 - (double)matrix.M12 * matrix.M21));
            var canvasScale = Math.Max(g.TransformScale.X, g.TransformScale.Y);
            var scale = animatedScale > 1e-6 ? canvasScale / animatedScale : canvasScale;
            scale = Math.Round(scale, 3);

            if (entry.Layer is null || !ReferenceEquals(entry.Fragment, fragment) || entry.Generation != clock.ContentGeneration || Math.Abs(entry.Scale - scale) > 0.0005 || !entry.Layer.CanDraw(g))
            {
                if (SubtreeExtent(fragment) is not { } extent)
                    return false;

                var bounds = Inflate(extent, SubtreeBleed(fragment) + 2);
                if (bounds.Width * scale > MaxRetainedPixels || bounds.Height * scale > MaxRetainedPixels)
                    return false;

                entry.Discard();
                var layer = g.CreateRetainedLayer(bounds);
                if (layer is null)
                    return false;

                try
                {
                    PaintTagged(layer.Recorder, fragment);
                }
                finally
                {
                    layer.Seal();
                }

                entry.Layer = layer;
                clock.RecordedLayers++;
                entry.Generation = clock.ContentGeneration;
                entry.Scale = scale;
                entry.Bounds = bounds;
                entry.Fragment = fragment;
            }

            entry.Layer.Draw(g, box.ActualOpacity);
            return true;
        }
    }
}
