using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.PDF.Engine;

namespace PeachPDF.Tests.TestSupport
{
    internal static class TestFonts
    {
        public static TypefaceFont Create(
            string family,
            double emSize,
            PaintFontStyle style = PaintFontStyle.Regular,
            int? weight = null,
            int stretch = TypefaceQuery.NormalWidth,
            double? obliqueSkewSinus = null,
            FontSet? fontSet = null)
        {
            fontSet ??= new FontSet();

            var isItalic = (style & PaintFontStyle.Italic) == PaintFontStyle.Italic;
            var effectiveWeight = weight ?? ((style & PaintFontStyle.Bold) == PaintFontStyle.Bold ? TypefaceQuery.BoldWeight : TypefaceQuery.NormalWeight);
            var match = fontSet.MatchOrFallback(family, new TypefaceQuery(effectiveWeight, stretch, isItalic));

            return new TypefaceFont(match.Typeface, emSize, match.Synthesis, obliqueSkewSinus);
        }
    }
}
