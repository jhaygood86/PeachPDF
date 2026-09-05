#nullable enable

using System;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// The single choke point every PDF-transparency-group-requiring construct (a semi-transparent
    /// fill/stroke, a gradient with an alpha color stop, an SVG <c>&lt;mask&gt;</c>, or CSS/SVG
    /// <c>opacity</c> below 1) passes through on its way into the content stream - see
    /// <see cref="Drawing.Pdf.PdfGraphicsState"/> and <see cref="Drawing.Pdf.XGraphicsPdfRenderer"/>
    /// call sites. PDF/A-1 (ISO 19005-1) forbids transparency groups entirely, and PeachPDF has no
    /// flattening engine, so rather than silently emit a non-conformant file, generation rejects the
    /// document outright the moment such a construct is about to be written.
    /// </summary>
    internal static class PdfATransparencyGuard
    {
        /// <summary>
        /// Throws a <see cref="PdfAConformanceException"/> if <paramref name="document"/>'s
        /// <see cref="PdfAConformance"/> is <see cref="PdfAConformance.PdfA1B"/> or
        /// <see cref="PdfAConformance.PdfA1A"/> - PDF/A-2 and PDF/A-3 are based on PDF 1.7 and permit
        /// transparency groups, so no check applies to those (or <see cref="PdfAConformance.None"/>).
        /// Deliberately independent of whether a page is currently attached to the calling renderer
        /// (nested tile content - e.g. an SVG <c>&lt;pattern&gt;</c> tile - reaches this too), unlike
        /// the separate <c>TransparencyUsed</c> page-flag bookkeeping at each call site.
        /// </summary>
        internal static void RequireAllowed(PdfDocument document, string featureDescription)
        {
            var conformance = document.Options.PdfAConformance;
            if (conformance is PdfAConformance.PdfA1B or PdfAConformance.PdfA1A)
            {
                throw new PdfAConformanceException(
                    $"{featureDescription} requires a PDF transparency group, which PDF/A-1 forbids. " +
                    "Remove this feature from the document, or target PdfAConformance.PdfA2B/PdfA2U/PdfA2A " +
                    "or PdfA3B/PdfA3U/PdfA3A instead - PDF/A-2 and PDF/A-3 both permit transparency groups.");
            }
        }

        /// <summary>
        /// Same as <see cref="RequireAllowed(PdfDocument, string)"/>, and additionally marks
        /// <paramref name="page"/> as needing a page-level <c>/Group</c> (<see cref="PdfPage.TransparencyUsed"/>)
        /// once the check passes. This is the one call every page-bound transparency-group-requiring
        /// call site should make - pairing the reject-check with the page flag in a single call means a
        /// future call site literally cannot set one without the other, unlike the two-line
        /// "check-then-set" pattern this replaces. <paramref name="page"/> may be null (e.g. content
        /// being rendered into an offscreen tile with no page of its own yet) - the PDF/A-1 check still
        /// runs unconditionally; only the page-flag side is skipped.
        /// </summary>
        internal static void RequireAllowed(PdfDocument document, PdfPage? page, string featureDescription)
        {
            RequireAllowed(document, featureDescription);
            if (page != null)
                page.TransparencyUsed = true;
        }
    }

    /// <summary>
    /// Thrown by <see cref="PdfATransparencyGuard"/> when a document uses a feature that requires a
    /// PDF transparency group under a PDF/A-1 conformance request. A plain <see cref="InvalidOperationException"/>
    /// is the exception type callers should catch (a <c>catch (InvalidOperationException)</c> block
    /// still works polymorphically) - this subclass exists purely so
    /// <see cref="PeachPDF.Html.Core.Paint.FragmentPainter"/>'s generic paint-error wrapping can let it
    /// propagate unwrapped, as the deliberate validation failure it is, instead of folding it into a
    /// generic <see cref="PeachPDF.HtmlRenderException"/> the way an unexpected paint error otherwise
    /// would be.
    /// </summary>
    internal sealed class PdfAConformanceException(string message) : InvalidOperationException(message);
}
