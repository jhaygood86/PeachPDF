namespace PeachDrawing.Text.Outlines
{
    /// <summary>
    /// A rectangle in design units that holds everything a colour glyph paints, which the font gives so that a renderer can size a surface
    /// or clip to it without walking the glyph's paint graph. A variable font's box follows the location of the <see cref="Typeface"/> it
    /// is read from.
    /// </summary>
    /// <param name="XMin">The left edge.</param>
    /// <param name="YMin">The bottom edge.</param>
    /// <param name="XMax">The right edge.</param>
    /// <param name="YMax">The top edge.</param>
    public readonly record struct ColorClipBox(double XMin, double YMin, double XMax, double YMax);
}
