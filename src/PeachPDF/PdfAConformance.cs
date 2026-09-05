#nullable enable

namespace PeachPDF
{
    /// <summary>
    /// The PDF/A (ISO 19005) conformance level to target, set via
    /// <see cref="PdfGenerateConfig.PdfAConformance"/>. Defaults to <see cref="None"/> - no PDF/A
    /// -specific work is done: no <c>/OutputIntents</c>, no forced XMP metadata, no PDF-version bump,
    /// no PDF/A-1 transparency rejection, and no accessible-level language/alt-text requirements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PDF/A-1 (<see cref="PdfA1B"/>/<see cref="PdfA1A"/>) is defined in terms of PDF 1.4 and forbids
    /// PDF transparency groups entirely. PeachPDF has no transparency-flattening engine, so instead of
    /// silently producing a non-conformant file, generation throws an <see cref="System.InvalidOperationException"/>
    /// if the document actually uses a feature that requires a transparency group - CSS/SVG
    /// <c>opacity</c> below 1, an SVG <c>&lt;mask&gt;</c>, a semi-transparent gradient color stop, or
    /// <c>fill-opacity</c>/<c>stroke-opacity</c> below 1 - and succeeds normally for documents that
    /// don't use any of these. PDF/A-2 and PDF/A-3 are defined in terms of PDF 1.7 and permit
    /// transparency groups, so no such restriction applies to those levels.
    /// </para>
    /// <para>
    /// The accessible "A" levels (<see cref="PdfA1A"/>/<see cref="PdfA2A"/>/<see cref="PdfA3A"/>)
    /// additionally require a real document language: requesting one implicitly enables tagged-PDF
    /// output (see <see cref="PdfGenerateConfig.EnableTaggedPdf"/>) for that render, and generation
    /// throws if neither the document's own <c>&lt;html lang&gt;</c> nor
    /// <see cref="PdfGenerateConfig.DefaultLanguage"/> resolves to a language.
    /// </para>
    /// </remarks>
    public enum PdfAConformance
    {
        /// <summary>No PDF/A conformance is requested. Default.</summary>
        None = 0,

        /// <summary>PDF/A-1, level B (visual conformance only).</summary>
        PdfA1B,

        /// <summary>PDF/A-1, level A (visual conformance plus accessibility/tagged-structure requirements).</summary>
        PdfA1A,

        /// <summary>PDF/A-2, level B (visual conformance only).</summary>
        PdfA2B,

        /// <summary>PDF/A-2, level U (visual conformance plus guaranteed Unicode text-extraction mapping).</summary>
        PdfA2U,

        /// <summary>PDF/A-2, level A (visual conformance plus accessibility/tagged-structure requirements).</summary>
        PdfA2A,

        /// <summary>PDF/A-3, level B (visual conformance only; also permits arbitrary embedded files).</summary>
        PdfA3B,

        /// <summary>PDF/A-3, level U (visual conformance plus guaranteed Unicode text-extraction mapping; also permits arbitrary embedded files).</summary>
        PdfA3U,

        /// <summary>PDF/A-3, level A (visual conformance plus accessibility/tagged-structure requirements; also permits arbitrary embedded files).</summary>
        PdfA3A,
    }
}
