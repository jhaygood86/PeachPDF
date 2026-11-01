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

namespace PeachDrawing.Core
{
    /// <summary>
    /// A decoded image's pixels, tightly packed premultiplied RGBA8 (<c>Width * Height * 4</c> bytes,
    /// row-major, no padding) - what <see cref="Image.GetPixels"/> hands any <see cref="Canvas"/>
    /// backend that needs to sample an image's actual pixels (the raster backend; a third party's own
    /// backend), without that backend needing to know which concrete <see cref="Image"/> decoded them.
    /// </summary>
    public readonly record struct PixelBuffer(int Width, int Height, ReadOnlyMemory<byte> PremultipliedRgba);
}
