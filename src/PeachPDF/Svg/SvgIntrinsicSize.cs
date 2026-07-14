// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

namespace PeachPDF.Svg
{
    /// <summary>
    /// Resolves an <see cref="SvgDocument"/>'s own natural size (independent of any CSS box sizing),
    /// for feeding into <c>CssLayoutEngine.MeasureIntrinsicSize</c> - shared by
    /// <c>CssBoxSvg.MeasureWordsSize</c> (inline <c>&lt;svg&gt;</c>) and
    /// <c>CssBoxImage.MeasureWordsSize</c> (<c>&lt;img src="x.svg"&gt;</c>).
    /// </summary>
    internal static class SvgIntrinsicSize
    {
        public static (double? Width, double? Height) Resolve(SvgDocument? document)
        {
            if (document is null)
                return (null, null);

            var width = document.Width ?? document.ViewBox?.Width;
            var height = document.Height ?? document.ViewBox?.Height;

            return (width, height);
        }
    }
}
