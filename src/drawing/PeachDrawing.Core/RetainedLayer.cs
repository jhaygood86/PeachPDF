using System;
using System.Numerics;

namespace PeachDrawing.Core
{
    /// <summary>
    /// A group of drawing recorded once and drawn any number of times, each time with its own opacity and under whatever transform the
    /// target canvas has at that moment. It is what lets an animation that only moves or fades a subtree (a CSS <c>transform</c> or
    /// <c>opacity</c> animation) cost a composite per frame instead of a repaint: a canvas backed by a GPU keeps the recorded group as a
    /// texture, and drawing it again is a single textured quad.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Begin one with <see cref="Canvas.CreateRetainedLayer"/>, draw its content onto <see cref="Recorder"/> in the coordinates of the
    /// canvas that created it, call <see cref="Seal"/>, and then <see cref="Draw"/> it. The content is recorded once, so it must not
    /// depend on anything that changes between draws: opacity is the one thing a draw supplies, and the transform is the target's.
    /// </para>
    /// <para>
    /// A layer belongs to the surface that created it. <see cref="CanDraw"/> says whether a particular canvas can still draw it (a lost
    /// graphics device or a different document makes the answer no); when it cannot, discard the layer and record a new one.
    /// </para>
    /// </remarks>
    public abstract class RetainedLayer : IDisposable
    {
        /// <summary>Creates a layer covering <paramref name="bounds"/>.</summary>
        /// <param name="bounds">the region the layer covers, in the coordinates of the canvas that created it</param>
        protected RetainedLayer(Rect bounds)
        {
            Bounds = bounds;
        }

        /// <summary>The region the layer covers, in the coordinates of the canvas that created it. Content outside it is not kept.</summary>
        public Rect Bounds { get; }

        /// <summary>
        /// The canvas to record the layer's content onto, in the coordinates of the canvas that created the layer. Valid from creation
        /// until <see cref="Seal"/>.
        /// </summary>
        public abstract Canvas Recorder { get; }

        /// <summary>Whether <see cref="Seal"/> has been called, so the layer can be drawn and no longer recorded onto.</summary>
        public abstract bool IsSealed { get; }

        /// <summary>Finishes recording. After this the layer can be drawn, and <see cref="Recorder"/> is no longer valid.</summary>
        public abstract void Seal();

        /// <summary>Whether <paramref name="target"/> can draw this layer.</summary>
        /// <param name="target">the canvas that would draw it</param>
        /// <returns><see langword="true"/> when <see cref="Draw"/> on <paramref name="target"/> will show the recorded content</returns>
        public abstract bool CanDraw(Canvas target);

        /// <summary>Draws the sealed layer onto <paramref name="target"/> under its current transform, at <see cref="Bounds"/>.</summary>
        /// <param name="target">the canvas to draw onto</param>
        /// <param name="opacity">how opaque the layer is drawn, from 0 (invisible) to 1</param>
        /// <exception cref="InvalidOperationException">the layer is not sealed</exception>
        public abstract void Draw(Canvas target, double opacity = 1.0);

        /// <summary>Releases what the layer holds.</summary>
        public abstract void Dispose();
    }

    /// <summary>
    /// The layer every canvas can make, built from <see cref="Canvas.CreateTile"/>: the recording is the tile and a draw replays it as an
    /// image. A canvas with something better (a texture) overrides <see cref="Canvas.CreateRetainedLayer"/>.
    /// </summary>
    internal sealed class TileRetainedLayer : RetainedLayer
    {
        private readonly object? _owner;
        private Canvas? _recorder;
        private Image? _image;
        private readonly bool _shifted;

        internal TileRetainedLayer(Rect bounds, Canvas recorder, Image image, object? owner)
            : base(bounds)
        {
            _recorder = recorder;
            _image = image;
            _owner = owner;
            _shifted = bounds.X != 0 || bounds.Y != 0;

            // A tile starts at its own origin; the content is recorded in the creating canvas's coordinates.
            if (_shifted)
                recorder.PushTransform(new Matrix3x2(1, 0, 0, 1, (float)-bounds.X, (float)-bounds.Y));

            // The clip a painter consults to decide what is visible is the layer's own region, in the creating canvas's coordinates.
            recorder.PushClip(bounds);
        }

        public override Canvas Recorder => _recorder ?? throw new InvalidOperationException("The layer is sealed.");

        public override bool IsSealed => _recorder is null;

        public override void Seal()
        {
            if (_recorder is not { } recorder)
                return;

            recorder.PopClip();
            if (_shifted)
                recorder.PopTransform();

            recorder.Dispose();
            _recorder = null;
        }

        public override bool CanDraw(Canvas target) => _image is not null && Equals(target.TileCacheOwner, _owner);

        public override void Draw(Canvas target, double opacity = 1.0)
        {
            if (_recorder is not null)
                throw new InvalidOperationException("The layer is not sealed.");

            if (_image is not { } image)
                throw new ObjectDisposedException(nameof(RetainedLayer));

            target.DrawImageWithOpacity(image, Bounds, opacity);
        }

        public override void Dispose()
        {
            Seal();
            (_image as IDisposable)?.Dispose();
            _image = null;
        }
    }
}
