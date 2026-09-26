namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// Messages of the errors the font readers raise, kept beside the readers so the font code does not
    /// depend on the PDF writer's message table.
    /// </summary>
    internal static class FontMessages
    {
        public static string ErrorReadingFontData => "Error while parsing an OpenType font.";
    }
}
