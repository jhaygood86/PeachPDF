using System;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves a <c>font-style</c> value's CSS Fonts Level 4 <c>oblique &lt;angle&gt;</c> form (e.g.
    /// <c>oblique 10deg</c>) to the sine of that angle - the exact quantity the faux-italic renderer
    /// needs as its glyph-shear factor (see <c>XGraphicsPdfRenderer</c>'s use of
    /// <c>Const.ItalicSkewAngleSinus</c>, a fixed sin(20°) approximation used whenever no explicit angle
    /// was declared). Reuses <see cref="PeachPDF.CSS.Angle"/> (the same angle grammar/unit conversion the
    /// CSS-OM layer already uses for <c>transform: skewX()</c> etc.) rather than re-parsing angle units
    /// independently - see CLAUDE.md's shared-parser convention.
    /// </summary>
    internal static class FontObliqueAngleResolver
    {
        /// <summary>
        /// Returns null for anything other than <c>oblique &lt;angle&gt;</c> (a bare <c>oblique</c>/
        /// <c>italic</c>, <c>normal</c>, or an unparseable angle) so the caller falls back to the fixed
        /// default skew instead of being handed a wrong/zero one.
        /// </summary>
        internal static double? ResolveSkewSinus(string fontStyleValue)
        {
            if (string.IsNullOrEmpty(fontStyleValue) || !fontStyleValue.StartsWith(CssConstants.Oblique, StringComparison.Ordinal))
                return null;

            var rest = fontStyleValue[CssConstants.Oblique.Length..].Trim();
            if (rest.Length == 0)
                return null;

            return PeachPDF.CSS.Angle.TryParse(rest, out var angle) ? Math.Sin(angle.ToRadian()) : null;
        }
    }
}
