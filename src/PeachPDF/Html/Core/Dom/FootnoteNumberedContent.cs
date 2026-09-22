using PeachPDF.CSS;
using System;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Re-resolves a <c>::footnote-call</c>/<c>::footnote-marker</c>'s own <c>content</c> once the
    /// footnote's number is known, so an author's <c>counter(footnote)</c>/<c>counters(footnote, …)</c>
    /// picks up the live, pagination-resolved value.
    /// </summary>
    /// <remarks>
    /// Modelled on <c>RunningElementLayout.RefreshPageCounterContent</c>, which does the same job for
    /// <c>counter(page)</c> inside a running element, down to the cheap substring pre-filter: a false
    /// positive costs one idempotent re-resolve, and a false negative is impossible. Reading the declared
    /// <c>content</c> rather than a flag set at parse time keeps this self-contained - there are only ever
    /// two boxes involved.
    /// </remarks>
    internal static class FootnoteNumberedContent
    {
        /// <summary>
        /// Whether <paramref name="content"/> could possibly name the footnote counter. Deliberately
        /// loose - it only decides whether the (idempotent) re-resolve below is worth running.
        /// </summary>
        internal static bool Mentions(string? content) =>
            content is not null
            && content.Contains(Keywords.Footnote, StringComparison.OrdinalIgnoreCase)
            && content.Contains("counter", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Re-runs <paramref name="box"/>'s own <c>content</c> resolution against the number currently in
        /// <c>HtmlContainerInt.FootnoteNumberContext</c>. A <c>content</c> that cannot mention the counter
        /// (a literal string, <c>attr()</c>, an image) is left completely untouched, which is what keeps
        /// an author's explicit override overriding.
        /// </summary>
        internal static void Reapply(CssBox box)
        {
            if (!Mentions(box.Content)) return;

            CssContentEngine.ApplyContent(box);

            // The bidi re-resolve that has to follow any text change here is done once by the caller
            // (HtmlContainerInt.ApplyFootnoteNumber), because the default numbering path needs it just as
            // much as this one does.
        }
    }
}
