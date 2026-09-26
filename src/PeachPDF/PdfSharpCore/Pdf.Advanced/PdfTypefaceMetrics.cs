using PeachDrawing.Text;
using System;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// What the PDF font dictionaries need to know about a <see cref="Typeface"/> that is a property of the PDF format
    /// (thousandths of an em, the base font name) and so belongs with the PDF writer, not with the font engine.
    /// </summary>
    internal static class PdfTypefaceMetrics
    {
        /// <summary>Converts a length in design units to the 1000 units per em a PDF font dictionary uses.</summary>
        internal static int DesignUnitsToPdf(Typeface typeface, double value)
        {
            return (int)Math.Round(value * 1000.0 / typeface.Metrics.UnitsPerEm);
        }

        /// <summary>The advance width of a glyph in the 1000 units per em a PDF font dictionary uses, truncated as it always has been.</summary>
        internal static int GlyphWidth(Typeface typeface, int glyphIndex)
        {
            int unitsPerEm = typeface.Metrics.UnitsPerEm;
            int width = typeface.GetAdvance((ushort)glyphIndex);

            // Sometimes the unitsPerEm is 1000, sometimes a power of 2.
            if (unitsPerEm == 1000)
                return width;
            return width * 1000 / unitsPerEm; // normalize
        }

        /// <summary>
        /// The name of the face in a PDF font and font descriptor: its full name without the words bold and italic, and then
        /// the suffix Microsoft Word gives for the effective bold and italic of the face.
        /// </summary>
        internal static string GetBaseName(Typeface typeface)
        {
            string name = typeface.FullName;
            int ich = name.IndexOf("bold", StringComparison.OrdinalIgnoreCase);
            if (ich > 0)
                name = name.Substring(0, ich) + name.Substring(ich + 4, name.Length - ich - 4);
            ich = name.IndexOf("italic", StringComparison.OrdinalIgnoreCase);
            if (ich > 0)
                name = name.Substring(0, ich) + name.Substring(ich + 6, name.Length - ich - 6);
            name = name.Trim();

            if (typeface.IsBold)
                return name + (typeface.IsItalic ? ",BoldItalic" : ",Bold");
            return name + (typeface.IsItalic ? ",Italic" : "");
        }
    }
}
