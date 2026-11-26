using System;

namespace PeachDrawing.Core
{
    /// <summary>
    /// A color that is not held to eight bits or to the range 0 to 1: straight (not premultiplied) red, green and blue as floating-point numbers that may be
    /// negative (a color outside the gamut of the space they are written in) or above 1 (brighter than white), and an alpha from 0 to 1.
    /// What the numbers are the coordinates of - which color space, and whether the transfer function is applied - is the business of whoever made them: a canvas
    /// whose pixels are floating point keeps all of its colors in one space and gives its brushes colors in it.
    /// </summary>
    /// <param name="Red">the red coordinate</param>
    /// <param name="Green">the green coordinate</param>
    /// <param name="Blue">the blue coordinate</param>
    /// <param name="Alpha">the opacity, from 0 (transparent) to 1 (opaque); a number outside that is brought into it</param>
    public readonly record struct WideColor(float Red, float Green, float Blue, float Alpha)
    {
        /// <summary>The color of an eight-bit <see cref="PaintColor"/> (its coordinates divided by 255).</summary>
        /// <param name="color">the color</param>
        public static WideColor FromPaintColor(PaintColor color)
        {
            var (r, g, b) = color.ToRgb();
            return new WideColor(r / 255f, g / 255f, b / 255f, color.A / 255f);
        }

        /// <summary>This color as an eight-bit <see cref="PaintColor"/>: each coordinate brought into 0 to 1 and rounded.</summary>
        public PaintColor ToPaintColor()
        {
            static int Byte(float v) => (int)Math.Round(Math.Clamp(float.IsNaN(v) ? 0f : v, 0f, 1f) * 255f);
            return PaintColor.FromArgb(Byte(Alpha), Byte(Red), Byte(Green), Byte(Blue));
        }
    }
}
