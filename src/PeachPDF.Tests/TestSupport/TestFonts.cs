using PeachDrawing.Text;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Builds an <see cref="XFont"/> the way the HTML pipeline does: match a typeface in a <see cref="FontSet"/>, then give
    /// it a size.
    /// </summary>
    internal static class TestFonts
    {
        /// <summary>The typeface a font file gives, as a caller outside the engine gets it: through a font set.</summary>
        public static PeachDrawing.Text.Typeface TypefaceFromFile(string path) => TypefaceFromBytes(System.IO.File.ReadAllBytes(path));

        /// <summary>The typeface font data gives, as a caller outside the engine gets it: through a font set.</summary>
        public static PeachDrawing.Text.Typeface TypefaceFromBytes(byte[] data)
        {
            var set = new PeachDrawing.Text.FontSet();
            var family = set.AddData(data, new PeachDrawing.Text.AddOptions { FamilyName = "TestFonts-" + System.Guid.NewGuid().ToString("N") });
            if (!family.TryMatch(new PeachDrawing.Text.TypefaceQuery(), out var match))
                throw new System.InvalidOperationException("The font data matched no face.");
            return match.Typeface;
        }

        /// <param name="family">The family to match; a family the set does not know falls back to the set's first family.</param>
        /// <param name="emSize">The em size, in points.</param>
        /// <param name="style">The bold and italic bits are what a box asked for; underline and strikeout ride along.</param>
        /// <param name="weight">The numeric weight to match, or null to derive 700 or 400 from the style's bold bit.</param>
        /// <param name="stretch">The width class to match, 5 being normal.</param>
        /// <param name="obliqueSkewSinus">The sine of a declared oblique angle, or null.</param>
        /// <param name="pdfOptions">PDF options, or null for the default encoding.</param>
        /// <param name="fontSet">The set to match in, or null for a new one that sees the installed fonts.</param>
        public static XFont Create(
            string family,
            double emSize,
            XFontStyle style = XFontStyle.Regular,
            int? weight = null,
            int stretch = TypefaceQuery.NormalWidth,
            double? obliqueSkewSinus = null,
            XPdfFontOptions? pdfOptions = null,
            FontSet? fontSet = null)
        {
            fontSet ??= new FontSet();

            var isItalic = (style & XFontStyle.Italic) == XFontStyle.Italic;
            var effectiveWeight = weight ?? ((style & XFontStyle.Bold) == XFontStyle.Bold ? TypefaceQuery.BoldWeight : TypefaceQuery.NormalWeight);
            var match = fontSet.MatchOrFallback(family, new TypefaceQuery(effectiveWeight, stretch, isItalic));

            return new XFont(emSize, style, pdfOptions ?? new XPdfFontOptions(GlobalFontSettings.DefaultFontEncoding), match, obliqueSkewSinus);
        }
    }
}
