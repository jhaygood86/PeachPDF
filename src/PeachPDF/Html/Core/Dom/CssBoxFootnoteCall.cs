using PeachPDF.CSS;
using System;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for a synthesized <c>::footnote-call</c> pseudo-element (css-gcpm-3) - the numbered
    /// in-flow reference <c>float: footnote</c> leaves at a footnote's original position once
    /// <c>DomParser.DetachFootnoteBodies</c> pulls the footnote's own content out of flow. Unlike
    /// <see cref="CssBoxMarker"/>'s <c>outside</c> case, this is an ordinary, fully in-flow inline
    /// participant - "simply the owner's first inline child, positioned by the ordinary inline-layout
    /// algorithm like any other flowed content" (see <see cref="CssBoxMarker.PerformLayoutImp"/>'s own
    /// remark on an <c>inside</c> marker) - so no layout override is needed here at all.
    /// </summary>
    internal sealed class CssBoxFootnoteCall : CssBox
    {
        /// <summary>The detached footnote body this call refers to.</summary>
        internal CssBox Body { get; }

        /// <summary>
        /// This footnote's 1-based number on the page its call landed on for the current layout
        /// attempt - assigned by <c>HtmlContainerInt</c>'s per-page footnote resolution step, which
        /// also assigns the same value to <see cref="Body"/>'s own <see cref="CssBoxFootnoteMarker"/>
        /// (see <see cref="CssBoxFootnoteMarker.ApplyNumber"/>).
        /// </summary>
        internal int Number { get; private set; } = 1;

        public CssBoxFootnoteCall(CssBox parent, CssBox body) : base(parent, null)
        {
            Body = body;
            FootnoteSourceBox = body;
            IsFootnoteCallPseudoElement = true;
        }

        /// <summary>
        /// Sets <see cref="Number"/> and, unless the author overrode <c>content</c> on
        /// <c>::footnote-call</c> (still the cascaded default "normal" otherwise), formats it as this
        /// call's text - via the same shared <see cref="CssCounterEngine.FormatCounterValue"/> every
        /// other counter-driven marker in this codebase uses. Deliberately not resolved through
        /// <see cref="CssCounterEngine"/>'s own counter-reset/increment machinery: that engine resolves
        /// purely from DOM position, with no notion of which page a box lands on, while a footnote's
        /// number depends on pagination - see the "Footnotes" section of docs/html-css-support.md for
        /// why an explicit author <c>content: counter(footnote)</c> is not supported. Does not itself
        /// call <see cref="CssBox.ParseToWords"/> - the caller re-parses once the whole page's calls
        /// have all been renumbered, the same "apply then re-parse words" contract
        /// <c>HtmlContainerInt.ReapplyPseudoElementContent</c> already follows.
        /// </summary>
        internal void ApplyNumber(int number)
        {
            Number = number;

            if (!Content.Trim().Equals(Keywords.Normal, StringComparison.OrdinalIgnoreCase)) return;

            Text = CssCounterEngine.FormatCounterValue(number, Keywords.Decimal);
        }
    }
}
