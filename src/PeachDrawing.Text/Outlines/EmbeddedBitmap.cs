namespace PeachDrawing.Text.Outlines
{
    /// <summary>One picture of a glyph from one strike of a font that draws its colour glyphs as bitmaps (<c>CBDT</c>/<c>CBLC</c> or <c>sbix</c>).</summary>
    /// <remarks>
    /// Two pictures are equal when their numbers are and their <see cref="Data"/> arrays are the same array; compare the
    /// bytes themselves to tell whether two pictures have the same image.
    /// </remarks>
    /// <param name="Data">The encoded image: PNG, or for <c>sbix</c> also JPEG.</param>
    /// <param name="Ppem">The pixels per em of the strike the picture belongs to. The picture is drawn at <c>fontSize / Ppem</c> per pixel.</param>
    /// <param name="Width">The width of the picture in strike pixels.</param>
    /// <param name="Height">The height of the picture in strike pixels.</param>
    /// <param name="BearingX">The distance from the glyph origin to the left edge of the picture, in strike pixels.</param>
    /// <param name="BearingTop">The distance from the baseline up to the top edge of the picture, in strike pixels.</param>
    public readonly record struct EmbeddedBitmap(byte[] Data, int Ppem, int Width, int Height, double BearingX, double BearingTop);
}
