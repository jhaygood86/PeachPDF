using System;

namespace PeachDrawing.Core
{
    /// <summary>
    /// A drawing surface a canvas keeps between frames: a bitmap of its own that a <see cref="Canvas"/> draws into whenever it likes and that can be put on another canvas
    /// of the same backend without being read back. On a backend with a graphics device it lives on the device (a GPU texture, a render target), so what is drawn
    /// into it and what is later drawn from it never leaves the GPU; only <see cref="ReadPixels"/> brings pixels to the CPU.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A surface comes from <see cref="Canvas.CreateOffscreenSurface"/>, and only a canvas that can make one does: the answer is <see langword="null"/> for a document writer
    /// (a PDF or SVG has no bitmap to keep drawing into) and for a measuring canvas. The caller then keeps what was drawn some other way, as a recording to play again.
    /// </para>
    /// <para>
    /// <see cref="Canvas"/> draws in pixels of the surface, one unit a pixel. It is the canvas that was made for the surface and is released with it; do not dispose it separately.
    /// <see cref="CreateSnapshot"/> is the way to use the content as an <see cref="Image"/>: the image keeps what the surface held then, whatever is drawn afterwards,
    /// and is the caller's to dispose. Where the backend can, a snapshot shares the pixels with the surface until the surface is drawn on again, so taking one is cheap.
    /// </para>
    /// </remarks>
    public abstract class OffscreenSurface : IDisposable
    {
        /// <summary>Sets the size.</summary>
        /// <param name="width">the width, in pixels</param>
        /// <param name="height">the height, in pixels</param>
        protected OffscreenSurface(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "An offscreen surface needs a positive size.");

            Width = width;
            Height = height;
        }

        /// <summary>The width, in pixels.</summary>
        public int Width { get; }

        /// <summary>The height, in pixels.</summary>
        public int Height { get; }

        /// <summary>The canvas that draws into the surface. One unit is one pixel and the top left is the origin. It belongs to the surface.</summary>
        public abstract Canvas Canvas { get; }

        /// <summary>Whether a snapshot of this surface can be drawn onto <paramref name="target"/> without a conversion (it is a canvas of the same backend and the same device).</summary>
        /// <param name="target">the canvas that would draw it</param>
        public abstract bool CanDraw(Canvas target);

        /// <summary>
        /// The surface's content now, as an image the caller disposes. Later drawing on the surface does not change it. The image is drawn with
        /// <see cref="Canvas.DrawImage(Image, Rect)"/> on a canvas <see cref="CanDraw"/> accepts, where its size is <see cref="Width"/> by <see cref="Height"/>.
        /// </summary>
        public abstract Image CreateSnapshot();

        /// <summary>The surface's pixels, premultiplied RGBA8, copied to the CPU. This is the one call that reads back from the device.</summary>
        public abstract PixelBuffer ReadPixels();

        /// <summary>Makes every pixel transparent.</summary>
        public abstract void Clear();

        /// <summary>Releases the surface, its canvas and the memory they hold.</summary>
        public abstract void Dispose();
    }
}
