namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves a CSS <c>font-stretch</c> keyword to the 1-9 numeric scale matching the OpenType OS/2
    /// table's <c>usWidthClass</c> field directly (1=ultra-condensed ... 5=normal ... 9=ultra-expanded) -
    /// see <see cref="PdfSharpCore.Utils.TtfFontDescription.Stretch"/>, which reads that same field for
    /// each registered face, so the two are directly comparable without any extra translation.
    /// </summary>
    internal static class FontStretchResolver
    {
        internal const int Normal = 5;

        internal static int Resolve(string fontStretchValue) => fontStretchValue switch
        {
            CssConstants.UltraCondensed => 1,
            CssConstants.ExtraCondensed => 2,
            CssConstants.Condensed => 3,
            CssConstants.SemiCondensed => 4,
            CssConstants.SemiExpanded => 6,
            CssConstants.Expanded => 7,
            CssConstants.ExtraExpanded => 8,
            CssConstants.UltraExpanded => 9,
            _ => Normal
        };
    }
}
