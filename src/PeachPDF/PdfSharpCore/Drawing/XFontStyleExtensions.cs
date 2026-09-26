using PeachDrawing.Text.Internal.Fonts;

namespace PeachPDF.PdfSharpCore.Drawing
{
    internal static class XFontStyleExtensions
    {
        /// <summary>The engine distinguishes faces by bold and italic only; underline/strikeout are decorations.</summary>
        internal static FaceStyle ToFaceStyle(this XFontStyle style) => (FaceStyle)((int)style & (int)XFontStyle.BoldItalic);
    }
}
