#nullable enable

namespace PeachPDF
{
    /// <summary>
    /// The PDF/X (ISO 15930) conformance level to target, set via
    /// <see cref="PdfGenerateConfig.PdfXConformance"/>. Defaults to <see cref="None"/> - no PDF/X-specific
    /// work is done. PDF/X is a print-production conformance family (distinct from - and mutually
    /// exclusive with - <see cref="PdfAConformance"/>'s archival family): every non-<see cref="None"/>
    /// level requires an <c>/OutputIntents</c> ICC profile
    /// (<see cref="ColorOptions.OutputIntentProfile"/> on <see cref="PdfGenerateConfig.ColorOptions"/> -
    /// unlike PDF/A, PeachPDF bundles no default profile for this, since there is no single correct press
    /// profile to assume).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each level targets the 2003-era ISO revision (ISO 15930-4 for X1a, ISO 15930-6 for X3 - both PDF
    /// 1.4-based; ISO 15930-7 for X4, PDF 1.6-based) rather than the original, older 2001/2002 revisions
    /// (PDF 1.3-based). PeachPDF sets the document's PDF version header and its
    /// <c>GTS_PDFXVersion</c>/<c>GTS_PDFXConformance</c> identification (both the document information
    /// dictionary and, for X4, its XMP metadata) accordingly - see
    /// <see cref="PdfSharpCore.Pdf.Advanced.PdfMetadataStream.PdfXIdentifiers"/> for the exact identifier
    /// strings and how they were verified.
    /// </para>
    /// <para>
    /// <see cref="X1a"/> (ISO 15930-1/4) is the strictest level: every color in the document must be
    /// CMYK (or, for grayscale/black content, resolvable to CMYK without approximation - see
    /// <see cref="ColorOptions.BlackGeneration"/>) or a named spot color. PeachPDF has no whole-document
    /// RGB-&gt;CMYK color-management engine (see the CMYK/ICC epic's design notes), so rather than silently
    /// emit an RGB color under a CMYK-only conformance claim, generation rejects a chromatic (non-gray)
    /// RGB-authored color outright the moment it would be written - the same "reject, don't approximate"
    /// philosophy <see cref="PdfAConformance"/>-1 already uses for transparency. X1a also forbids live
    /// transparency groups entirely (same restriction as PDF/A-1, and the same rejection mechanism).
    /// </para>
    /// <para>
    /// <see cref="X3"/> (ISO 15930-3/6) permits ICC-managed color: an RGB- or Gray-tagged color may stay
    /// in its own space (the output intent profile describes how a RIP should interpret it), so no
    /// CMYK-only restriction applies - but, like X1a, it forbids live transparency groups.
    /// </para>
    /// <para>
    /// <see cref="X4"/> (ISO 15930-7), the modern, most commonly requested level, permits both ICC-managed
    /// color and live transparency groups - PeachPDF adds no construct restriction beyond the mandatory
    /// output intent for this level.
    /// </para>
    /// </remarks>
    public enum PdfXConformance
    {
        /// <summary>No PDF/X conformance is requested. Default.</summary>
        None = 0,

        /// <summary>PDF/X-1a: CMYK/spot-color-only content, no live transparency, output intent required.</summary>
        X1a,

        /// <summary>PDF/X-3: ICC-managed color permitted, no live transparency, output intent required.</summary>
        X3,

        /// <summary>PDF/X-4: ICC-managed color and live transparency both permitted, output intent required.</summary>
        X4,
    }
}
