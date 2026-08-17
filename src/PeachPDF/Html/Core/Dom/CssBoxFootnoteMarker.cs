using PeachPDF.CSS;
using System;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for a synthesized <c>::footnote-marker</c> pseudo-element (css-gcpm-3) - the leading
    /// number a footnote's own body carries once it is rendered in a page's footnote area. Inserted as
    /// the detached footnote body's first child by <c>DomParser.DetachFootnoteBodies</c>, so it flows
    /// ahead of the body's own content the same way an <c>inside</c> list marker (see
    /// <see cref="CssBoxMarker"/>) flows ahead of its list item's content - no layout override needed.
    /// </summary>
    internal sealed class CssBoxFootnoteMarker : CssBox
    {
        public CssBoxFootnoteMarker(CssBox body, CssBoxFootnoteCall call) : base(body, null)
        {
            FootnoteSourceBox = call.Body;
            IsFootnoteMarkerPseudoElement = true;
        }

        /// <summary>
        /// Formats <paramref name="number"/> as this marker's text, unless the author overrode
        /// <c>content</c> on <c>::footnote-marker</c> - the body-side counterpart of
        /// <see cref="CssBoxFootnoteCall.ApplyNumber"/>, which the two are always kept in sync with (see
        /// its own remarks for why this isn't resolved through <see cref="CssCounterEngine"/>).
        /// </summary>
        internal void ApplyNumber(int number)
        {
            if (!Content.Trim().Equals(Keywords.Normal, StringComparison.OrdinalIgnoreCase)) return;

            Text = CssCounterEngine.FormatCounterValue(number, Keywords.Decimal) + ".";
        }
    }
}
