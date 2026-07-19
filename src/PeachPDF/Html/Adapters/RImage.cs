// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using System;

namespace PeachPDF.Html.Adapters
{
    /// <summary>
    /// Adapter for platform specific image object - used to render images.
    /// </summary>
    internal abstract class RImage : IDisposable
    {
        /// <summary>
        /// Get the width, in pixels, of the image.
        /// </summary>
        public abstract double Width { get; }

        /// <summary>
        /// Get the height, in pixels, of the image.
        /// </summary>
        public abstract double Height { get; }

        /// <summary>
        /// Whether the PDF viewer should smooth this image when it's drawn at a size other than its
        /// native pixel dimensions. Defaults to <c>true</c> (the common case: photos/gradients look
        /// better smoothed when scaled). A repeating <c>background-image</c> tile - see
        /// <see cref="Html.Core.Handlers.BackgroundImageDrawHandler"/> - sets this to <c>false</c>
        /// while painting: a tiled pattern is drawn many times edge-to-edge, and any softened/blurred
        /// edge (from interpolation) leaves visible seams between tiles/layers instead of a crisp,
        /// contiguous pattern - most visibly, two intentionally-offset transparent tiles meant to
        /// interlock into one solid color (Acid2's own checkerboard-interlock background trick) never
        /// resolve to solid at any rasterization DPI above the image's tiny native size, since blurred
        /// edges from two independently-smoothed layers don't cancel out the way crisp, hard pixel
        /// edges would.
        /// </summary>
        public abstract bool Interpolate { get; set; }

        public abstract void Dispose();
    }
}