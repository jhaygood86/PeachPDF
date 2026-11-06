using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// What makes one blurred outer <c>box-shadow</c> look the way it does, wherever on the page its box is: the shadow's size, blur, colour and corners, and the
    /// border box that is cut out of it, relative to the shadow, at the scale it is drawn at.
    /// </summary>
    internal readonly record struct ShadowKey(double Width, double Height, double Blur, double BoxX, double BoxY, double BoxWidth, double BoxHeight, PaintColor Color,
        BorderRadii Radii, bool BoxRounded, BorderRadii HoleRadii, double ScaleX, double ScaleY);

    /// <summary>
    /// The blurred shadows a document paints, each kept as a <see cref="RetainedLayer"/> and drawn again wherever a box with the same shadow is. A Gaussian blur is the
    /// costliest thing a card does when it is painted, and a page that animates repaints the same few shadows (the same cards, in another place) every frame; a layer
    /// that is moved costs a composite. Held for the document and dropped with it; the oldest goes once there are more than <see cref="Capacity"/>.
    /// </summary>
    internal sealed class ShadowLayerCache : IDisposable
    {
        /// <summary>How many shadows are kept.</summary>
        internal const int Capacity = 64;

        private readonly Dictionary<ShadowKey, RetainedLayer> _layers = [];
        private readonly Queue<ShadowKey> _order = new();

        /// <summary>How many shadows are held.</summary>
        internal int Count => _layers.Count;

        /// <summary>The layer for <paramref name="key"/> if one was made and <paramref name="canvas"/> can still draw it (a lost device or another surface drops it).</summary>
        internal bool TryGet(ShadowKey key, Canvas canvas, out RetainedLayer layer)
        {
            if (_layers.TryGetValue(key, out var found))
            {
                if (found.CanDraw(canvas))
                {
                    layer = found;
                    return true;
                }

                _layers.Remove(key);
                found.Dispose();
            }

            layer = null!;
            return false;
        }

        /// <summary>Keeps <paramref name="layer"/> for <paramref name="key"/>, making room by dropping the oldest.</summary>
        internal void Add(ShadowKey key, RetainedLayer layer)
        {
            if (_layers.Remove(key, out var replaced))
                replaced.Dispose();
            else
                _order.Enqueue(key);

            _layers[key] = layer;

            while (_layers.Count > Capacity && _order.TryDequeue(out var oldest))
            {
                if (_layers.Remove(oldest, out var dropped))
                    dropped.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (var layer in _layers.Values)
                layer.Dispose();

            _layers.Clear();
            _order.Clear();
        }
    }
}
