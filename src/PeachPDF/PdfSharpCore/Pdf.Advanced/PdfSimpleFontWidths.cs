using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Pdf.Internal;
using System.Text;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// The /Widths of a simple (single-byte, WinAnsi) TrueType font: the PDF width of the glyph each of the 256
    /// character codes selects. This is a property of the PDF font dictionary, not of the font file, so it lives
    /// with the PDF writer rather than in the font engine.
    /// </summary>
    internal static class PdfSimpleFontWidths
    {
        public static int[] Compute(OpenTypeDescriptor descriptor)
        {
            Encoding ansi = PdfEncoders.WinAnsiEncoding;
            byte[] bytes = new byte[256];
            bool symbol = descriptor.FontFace.cmap.symbol;
            var widths = new int[256];

            for (int idx = 0; idx < 256; idx++)
            {
                bytes[idx] = (byte)idx;

                char ch = (char)idx;
                string s = ansi.GetString(bytes, idx, 1);
                if (s.Length != 0 && s[0] != ch)
                    ch = s[0];

                if (symbol)
                {
                    // Remap ch for symbol fonts.
                    ch = (char)(ch | (descriptor.FontFace.os2.usFirstCharIndex & 0xFF00));
                }

                int glyphIndex = descriptor.CharCodeToGlyphIndex(new Rune(ch));
                widths[idx] = descriptor.GlyphIndexToPdfWidth(glyphIndex);
            }

            return widths;
        }
    }
}
