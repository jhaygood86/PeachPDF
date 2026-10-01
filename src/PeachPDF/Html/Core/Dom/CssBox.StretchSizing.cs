using System;
using PeachPDF.CSS;
using PeachPDF.Adapters;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS Sizing 3's <c>stretch</c> size (and its legacy <c>-webkit-fill-available</c>/<c>-moz-available</c>
    /// spellings): the box's margin box fills its containing block, auto margins counting as zero.
    /// </summary>
    /// <remarks>
    /// The stretch fit is <c>100% - margins - (border + padding, for content-box)</c>, so it is rewritten into
    /// exactly that <c>calc()</c> the first time layout needs it. Everything downstream then sees an
    /// ordinary percentage size: the "axis is not definite" rules (a percentage block size against an auto-height
    /// parent behaves as <c>auto</c>/<c>0</c>/<c>none</c> for a preferred/min/max size, which is what the spec
    /// asks of <c>stretch</c> too) and the per-page measure the percentage basis already follows come along
    /// with no stretch-specific code in the layout engines.
    /// </remarks>
    internal partial class CssBox
    {
        /// <summary>Whether <paramref name="value"/> is <c>stretch</c> or one of its legacy aliases.</summary>
        internal static bool IsStretchKeyword(string? value) =>
            value is not null
            && (value.Equals(Keywords.Stretch, StringComparison.OrdinalIgnoreCase)
                || value.Equals("-webkit-fill-available", StringComparison.OrdinalIgnoreCase)
                || value.Equals("-moz-available", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Rewrites any <c>stretch</c> width/height/min/max size of this box into its stretch-fit
        /// <c>calc()</c>. Idempotent: once rewritten there is no keyword left to find.
        /// </summary>
        internal void ResolveStretchSizes()
        {
            if (!IsStretchKeyword(Width) && !IsStretchKeyword(MinWidth) && !IsStretchKeyword(MaxWidth)
                && !IsStretchKeyword(Height) && !IsStretchKeyword(MinHeight) && !IsStretchKeyword(MaxHeight))
            {
                return;
            }

            var inline = ActualMarginLeft + ActualMarginRight + ActualBoxSizeIncludedWidth;

            if (IsStretchKeyword(Width)) Width = StretchFit(inline);
            if (IsStretchKeyword(MinWidth)) MinWidth = StretchFit(inline);
            if (IsStretchKeyword(MaxWidth)) MaxWidth = StretchFit(inline);

            if (!IsStretchKeyword(Height) && !IsStretchKeyword(MinHeight) && !IsStretchKeyword(MaxHeight)) return;

            // Margins that collapse through a parent with no border/padding of its own are treated as zero, so
            // the box fits the parent exactly rather than overflowing by margins that will not stay inside it.
            var block = ActualBoxSizeIncludedHeight
                        + (ParentCollapsesMargin(start: true) ? 0 : ActualMarginTop)
                        + (ParentCollapsesMargin(start: false) ? 0 : ActualMarginBottom);

            if (IsStretchKeyword(Height)) Height = StretchFit(block);
            if (IsStretchKeyword(MinHeight)) MinHeight = StretchFit(block);
            if (IsStretchKeyword(MaxHeight)) MaxHeight = StretchFit(block);
        }

        private bool ParentCollapsesMargin(bool start)
        {
            var parent = ParentBox;
            if (parent is null || parent.DerivedStyle.ActualDisplay != Keywords.Block) return false;
            if (parent.Overflow.Value != PeachPDF.CSS.Overflow.Visible) return false;

            return start
                ? parent.ActualBorderTopWidth + parent.ActualPaddingTop == 0
                : parent.ActualBorderBottomWidth + parent.ActualPaddingBottom == 0;
        }

        private string StretchFit(double subtract)
        {
            if (subtract <= 0) return "100%";

            var points = subtract / ((HtmlContainer?.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0);
            return "calc(100% - " + points.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "pt)";
        }
    }
}
