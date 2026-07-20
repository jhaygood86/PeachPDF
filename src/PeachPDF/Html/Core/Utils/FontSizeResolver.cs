using System;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves a CSS font-size value (absolute keyword, <c>smaller</c>/<c>larger</c>, or any length unit
    /// <see cref="CssValueParser.ParseLength(string, double, double, double, string?, bool)"/>
    /// understands) to a numeric size. Extracted from <c>CssBoxProperties.ActualFont</c>'s original inline
    /// switch so in-flow content and <c>MarginBoxRenderer.BuildFont</c> (@page margin boxes, which have no
    /// real inheritance chain and pass <see cref="Utils.CssConstants.FontSize"/> for both
    /// <c>parentSize</c>/<c>remSize</c>) share one implementation.
    /// </summary>
    internal static class FontSizeResolver
    {
        /// <param name="fontSizeValue">The raw CSS font-size value (keyword or length).</param>
        /// <param name="parentSize">Reference size for <c>smaller</c>/<c>larger</c>/<c>em</c>.</param>
        /// <param name="remSize">Reference size for <c>rem</c>.</param>
        internal static double Resolve(string fontSizeValue, double parentSize, double remSize)
        {
            var fsize = fontSizeValue switch
            {
                CssConstants.Medium => CssConstants.FontSize,
                CssConstants.XXSmall => CssConstants.FontSize - 4,
                CssConstants.XSmall => CssConstants.FontSize - 3,
                CssConstants.Small => CssConstants.FontSize - 2,
                CssConstants.Large => CssConstants.FontSize + 2,
                CssConstants.XLarge => CssConstants.FontSize + 3,
                CssConstants.XXLarge => CssConstants.FontSize + 4,
                CssConstants.Smaller => parentSize - 2,
                CssConstants.Larger => parentSize + 2,
                _ => CssValueParser.ParseLength(fontSizeValue, parentSize, parentSize, remSize, null, true)
            };

            // A legitimately-parsed font-size of 0 (or any other small value) must be honored, not
            // silently replaced with the medium default - only a negative computed value (e.g. from a
            // calc() expression) is spec-clamped, to zero, not to the default.
            return Math.Max(0, fsize);
        }
    }
}
