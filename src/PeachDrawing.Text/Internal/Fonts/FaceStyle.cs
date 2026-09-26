using System;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// The style of a font face as the engine tells faces apart: bold and/or italic. (Underline and strikeout
    /// are drawing decorations, not properties of a face, so they have no place here.) The values match the
    /// bold/italic bits of the PDF layer's <c>XFontStyle</c>.
    /// </summary>
    [Flags]
    internal enum FaceStyle
    {
        Regular = 0,
        Bold = 1,
        Italic = 2,
        BoldItalic = Bold | Italic,
    }
}
