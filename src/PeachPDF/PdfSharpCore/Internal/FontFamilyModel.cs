using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Utils;
using System.Collections.Generic;

namespace PeachPDF.PdfSharpCore.Internal
{
    internal class FontFamilyModel
    {
        public string Name { get; set; } = null!;

        public Dictionary<XFontStyle, TtfFontDescription> FontFiles = new();

        public bool IsStyleAvailable(XFontStyle fontStyle)
        {
            return FontFiles.ContainsKey(fontStyle);
        }
    }
}