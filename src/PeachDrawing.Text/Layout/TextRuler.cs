using PeachDrawing.Text.Shaping;
using System;

namespace PeachDrawing.Text.Layout
{
    /// <summary>
    /// Measures a piece of text set in one face without laying out a paragraph.
    /// </summary>
    public static class TextRuler
    {
        /// <summary>
        /// The distance the pen travels along text shaped in a face, in the units <paramref name="size"/> is in.
        /// </summary>
        /// <remarks>The text is shaped as one run, left to right, with the script of its first character; use a <see cref="Paragraph"/> for text of mixed directions or scripts.</remarks>
        /// <param name="typeface">The face.</param>
        /// <param name="size">The size of the em.</param>
        /// <param name="text">The text.</param>
        /// <param name="settings">What to ask of the shaper, or <see langword="null"/> for the defaults.</param>
        /// <returns>The advance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="typeface"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
        public static double WidthOf(Typeface typeface, double size, string text, ShapeSettings? settings = null)
        {
            ArgumentNullException.ThrowIfNull(typeface);
            ArgumentNullException.ThrowIfNull(text);

            var run = Shaper.Shape(typeface, text, settings ?? ShapeSettings.Default);
            return run.Advance * size / typeface.Metrics.UnitsPerEm;
        }
    }
}
