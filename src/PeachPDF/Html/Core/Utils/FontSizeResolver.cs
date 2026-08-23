using System;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves a CSS font-size value (absolute keyword, <c>smaller</c>/<c>larger</c>, or any length unit
    /// <see cref="CssValueParser.ParseLength(string, double, double, double, string?, bool, double?, double?, double?, double?, double?, double?, double?, double?)"/>
    /// understands) to a numeric size. Extracted from <c>CssBox.ActualFont</c>'s original inline
    /// switch so in-flow content and <c>MarginBoxRenderer.BuildFont</c> (@page margin boxes, which have no
    /// real inheritance chain and pass <see cref="Utils.DefaultFontResolver.FontSize"/> for both
    /// <c>parentSize</c>/<c>remSize</c>) share one implementation.
    /// </summary>
    internal static class FontSizeResolver
    {
        /// <summary>
        /// Overload for a caller with no typed <see cref="CssKeywordOrValue{TEnum,TValue}"/> in hand -
        /// today, only <c>MarginBoxRenderer</c>'s <c>@page</c> margin-box font resolution, which reads the
        /// CSS-OM <see cref="PeachPDF.CSS.StyleDeclaration"/>'s own raw <c>font-size</c> string rather than
        /// a <c>CssBox</c>'s cascaded value.
        /// </summary>
        /// <param name="fontSizeValue">The raw CSS font-size value (keyword or length).</param>
        /// <param name="parentSize">Reference size for <c>smaller</c>/<c>larger</c>/<c>em</c>.</param>
        /// <param name="remSize">Reference size for <c>rem</c>.</param>
        /// <param name="containerInlineSizePt">See <see cref="PeachPDF.CSS.Length.ToPixels"/>'s parameter
        /// of the same name - the nearest ancestor <c>@container</c> size query container's content-box
        /// inline size, for <c>cqw</c>/<c>cqi</c>/<c>cqmin</c>/<c>cqmax</c> resolution. <c>null</c> for
        /// callers with no real box in scope (<c>MarginBoxRenderer.BuildFont</c>'s page margin boxes),
        /// which resolves those units to the viewport fallback (or 0 with neither), same as the box-aware
        /// length overload's own fallback.</param>
        /// <param name="containerBlockSizePt">See <see cref="PeachPDF.CSS.Length.ToPixels"/>'s parameter of
        /// the same name.</param>
        /// <param name="viewportWidthPt">See <see cref="PeachPDF.CSS.Length.ToPixels"/>'s parameter of the
        /// same name - the page box's own width, for <c>vw</c>/etc. resolution. <c>null</c> for callers with
        /// no real box in scope (<c>MarginBoxRenderer.BuildFont</c>'s page margin boxes), which resolves
        /// those units to 0.</param>
        /// <param name="viewportHeightPt">See <see cref="PeachPDF.CSS.Length.ToPixels"/>'s parameter of the
        /// same name.</param>
        /// <param name="containerWidthPt">See <see cref="PeachPDF.CSS.Length.ToPixels"/>'s parameter of the
        /// same name.</param>
        /// <param name="containerHeightPt">See <see cref="PeachPDF.CSS.Length.ToPixels"/>'s parameter of the
        /// same name.</param>
        /// <param name="viewportInlineSizePt">See <see cref="PeachPDF.CSS.Length.ToPixels"/>'s parameter of
        /// the same name.</param>
        /// <param name="viewportBlockSizePt">See <see cref="PeachPDF.CSS.Length.ToPixels"/>'s parameter of
        /// the same name.</param>
        internal static double Resolve(string fontSizeValue, double parentSize, double remSize,
            double? containerInlineSizePt = null, double? containerBlockSizePt = null,
            double? viewportWidthPt = null, double? viewportHeightPt = null,
            double? containerWidthPt = null, double? containerHeightPt = null,
            double? viewportInlineSizePt = null, double? viewportBlockSizePt = null)
        {
            var fsize = fontSizeValue switch
            {
                Keywords.Medium => DefaultFontResolver.FontSize,
                Keywords.XxSmall => DefaultFontResolver.FontSize - 4,
                Keywords.XSmall => DefaultFontResolver.FontSize - 3,
                Keywords.Small => DefaultFontResolver.FontSize - 2,
                Keywords.Large => DefaultFontResolver.FontSize + 2,
                Keywords.XLarge => DefaultFontResolver.FontSize + 3,
                Keywords.XxLarge => DefaultFontResolver.FontSize + 4,
                Keywords.Smaller => parentSize - 2,
                Keywords.Larger => parentSize + 2,
                _ => CssValueParser.ParseLength(fontSizeValue, parentSize, parentSize, remSize, null, true,
                    containerInlineSizePt, containerBlockSizePt, viewportWidthPt, viewportHeightPt,
                    containerWidthPt, containerHeightPt, viewportInlineSizePt, viewportBlockSizePt)
            };

            // A legitimately-parsed font-size of 0 (or any other small value) must be honored, not
            // silently replaced with the medium default - only a negative computed value (e.g. from a
            // calc() expression) is spec-clamped, to zero, not to the default.
            return Math.Max(0, fsize);
        }

        /// <param name="fontSize">The parsed <c>font-size</c> value. A resolved <see cref="LengthOrCalc"/>
        /// value plays the role the string overload's <c>_</c> switch arm does (a length/<c>calc()</c> to
        /// parse); a resolved <see cref="FontSizeKeyword"/> plays the role of its 9 explicit keyword arms.
        /// By the time this runs, <c>em</c>/<c>ex</c>/<c>ch</c>/<c>%</c>/<c>smaller</c>/<c>larger</c> have
        /// already been eagerly resolved to an absolute point <see cref="Length"/> at cascade time (see
        /// <c>CssBox.ResolveFontSizeValueComputation</c>), so only a <c>calc()</c> (deferred via
        /// <see cref="LengthOrCalc.CalcText"/>, evaluated here against <paramref name="parentSize"/> as its
        /// own <c>em</c> basis, matching the pre-eager-resolution behavior a bare relative value would have
        /// gone through), <c>rem</c>, or <c>vw</c>/etc. (not parent-relative, so not eagerly resolved -
        /// see <c>CssBox.ResolveFontSizeValueComputation</c>'s own doc comment) genuinely still needs
        /// <paramref name="parentSize"/>/<paramref name="remSize"/>/<paramref name="viewportWidthPt"/>/
        /// <paramref name="viewportHeightPt"/> at this point.</param>
        /// <param name="parentSize">Reference size for <c>smaller</c>/<c>larger</c>/a <c>calc()</c>'s own <c>em</c>.</param>
        /// <param name="remSize">Reference size for <c>rem</c>.</param>
        /// <param name="containerInlineSizePt">See the string overload's parameter of the same name.</param>
        /// <param name="containerBlockSizePt">See the string overload's parameter of the same name.</param>
        /// <param name="viewportWidthPt">See the string overload's parameter of the same name.</param>
        /// <param name="viewportHeightPt">See the string overload's parameter of the same name.</param>
        /// <param name="containerWidthPt">See the string overload's parameter of the same name.</param>
        /// <param name="containerHeightPt">See the string overload's parameter of the same name.</param>
        /// <param name="viewportInlineSizePt">See the string overload's parameter of the same name.</param>
        /// <param name="viewportBlockSizePt">See the string overload's parameter of the same name.</param>
        internal static double Resolve(CssKeywordOrValue<FontSizeKeyword, LengthOrCalc> fontSize, double parentSize, double remSize,
            double? containerInlineSizePt = null, double? containerBlockSizePt = null,
            double? viewportWidthPt = null, double? viewportHeightPt = null,
            double? containerWidthPt = null, double? containerHeightPt = null,
            double? viewportInlineSizePt = null, double? viewportBlockSizePt = null)
        {
            double fsize;
            if (fontSize.Value is { } value)
            {
                fsize = value.IsCalc
                    ? CssValueParser.ParseLength(value.CalcText!, parentSize, parentSize, remSize, null, true,
                        containerInlineSizePt, containerBlockSizePt, viewportWidthPt, viewportHeightPt,
                        containerWidthPt, containerHeightPt, viewportInlineSizePt, viewportBlockSizePt)
                    : value.Length!.Value.ToPixels(parentSize, remSize, parentSize, containerInlineSizePt, containerBlockSizePt,
                        viewportWidthPt, viewportHeightPt, containerWidthPt, containerHeightPt,
                        viewportInlineSizePt, viewportBlockSizePt);
            }
            else
            {
                fsize = fontSize.Keyword switch
                {
                    FontSizeKeyword.XxSmall => DefaultFontResolver.FontSize - 4,
                    FontSizeKeyword.XSmall => DefaultFontResolver.FontSize - 3,
                    FontSizeKeyword.Small => DefaultFontResolver.FontSize - 2,
                    FontSizeKeyword.Large => DefaultFontResolver.FontSize + 2,
                    FontSizeKeyword.XLarge => DefaultFontResolver.FontSize + 3,
                    FontSizeKeyword.XxLarge => DefaultFontResolver.FontSize + 4,
                    FontSizeKeyword.Smaller => parentSize - 2,
                    FontSizeKeyword.Larger => parentSize + 2,
                    _ => DefaultFontResolver.FontSize // Medium, or unset (unreachable in practice - see CssKeywordOrValue's own doc comment)
                };
            }

            // A legitimately-parsed font-size of 0 (or any other small value) must be honored, not
            // silently replaced with the medium default - only a negative computed value (e.g. from a
            // calc() expression) is spec-clamped, to zero, not to the default.
            return Math.Max(0, fsize);
        }
    }
}
