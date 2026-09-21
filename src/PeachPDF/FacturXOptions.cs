#nullable enable

namespace PeachPDF
{
    /// <summary>
    /// Turns the generated PDF into a Factur-X / ZUGFeRD hybrid e-invoice, via
    /// <see cref="PdfGenerateConfig.FacturX"/>: a PDF/A-3 document that carries a human-readable invoice
    /// (the pages PeachPDF renders) together with the same invoice as structured XML, so software can process
    /// it without reading the pages. Factur-X (France) and ZUGFeRD (Germany) are one format under two names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PeachPDF embeds the XML you supply; it neither generates the invoice XML (use any Cross Industry
    /// Invoice / EN 16931 generator) nor validates it against the format's schemas and business rules
    /// (use a validator such as Mustang or ZUV). What PeachPDF does is everything on the PDF side: the
    /// embedded-file structure, the document-level <c>/AF</c> and <c>/EmbeddedFiles</c> entries, the
    /// <c>/AFRelationship</c>, and the XMP metadata block with its PDF/A extension schema that identifies the
    /// document as a Factur-X invoice.
    /// </para>
    /// <para>
    /// Requires one of the PDF/A-3 levels in <see cref="PdfGenerateConfig.PdfAConformance"/> (any of
    /// <c>PdfA3B</c>, <c>PdfA3U</c>, <c>PdfA3A</c>; the specification recommends the accessible <c>PdfA3A</c>)
    /// and, like every XMP-bearing PDF, a creation date (<see cref="PdfDocumentMetadata.CreationDate"/>, or a
    /// date in the source HTML). Targets Factur-X 1.0x / ZUGFeRD 2.1 and later (the <c>fx</c> XMP schema);
    /// the legacy ZUGFeRD 1.0 and 2.0 XMP schemas are not supported.
    /// </para>
    /// </remarks>
    public sealed class FacturXOptions
    {
        /// <summary>
        /// The invoice as Cross Industry Invoice (CII) XML, exactly as it should be embedded. Required.
        /// </summary>
        public byte[] Xml { get; set; } = [];

        /// <summary>
        /// The profile of <see cref="Xml"/>. When <c>null</c> (the default) it is derived from the XML's own
        /// guideline identifier (<c>GuidelineSpecifiedDocumentContextParameter/ID</c>, business term BT-24).
        /// When set it must agree with that identifier, so a document cannot claim a profile its XML does
        /// not meet; and it is required when the XML declares a guideline PeachPDF does not recognise.
        /// </summary>
        public FacturXProfile? Profile { get; set; }

        /// <summary>
        /// How the embedded XML relates to the PDF (<c>/AFRelationship</c>). When <c>null</c> (the default)
        /// the value the specification calls for is used: <see cref="PdfAttachmentRelationship.Data"/> for
        /// <see cref="FacturXProfile.Minimum"/> and <see cref="FacturXProfile.BasicWl"/>, otherwise
        /// <see cref="PdfAttachmentRelationship.Alternative"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="PdfAttachmentRelationship.Alternative"/> is the only value allowed for German invoices
        /// (it asserts the PDF and the XML carry the same invoice information); French invoices may also use
        /// <c>Source</c> or <c>Data</c> for <see cref="FacturXProfile.Basic"/>, <see cref="FacturXProfile.En16931"/>
        /// and <see cref="FacturXProfile.Extended"/>. A value the specification does not permit for the
        /// profile is rejected.
        /// </remarks>
        public PdfAttachmentRelationship? Relationship { get; set; }

        /// <summary>An optional human-readable description of the embedded XML file.</summary>
        public string? Description { get; set; }
    }
}
