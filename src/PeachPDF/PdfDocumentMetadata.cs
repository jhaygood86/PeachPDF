#nullable enable

using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace PeachPDF
{
    /// <summary>
    /// Optional document-information overrides applied to the generated PDF's Info dictionary via
    /// <see cref="PdfGenerateConfig.Metadata"/>. Each property is nullable: when a property is
    /// non-null it overrides the value extracted from the HTML source (<c>&lt;title&gt;</c> and the
    /// <c>&lt;meta name="author|subject|keywords|generator"&gt;</c> tags); when it is null the
    /// HTML-extracted value is used unchanged.
    /// </summary>
    /// <remarks>
    /// Document language is not set here — use <see cref="PdfGenerateConfig.DefaultLanguage"/>, which
    /// populates the PDF catalog <c>/Lang</c> entry when the document itself declares no language
    /// (a document's own <c>&lt;html lang&gt;</c> takes priority).
    /// </remarks>
    public sealed class PdfDocumentMetadata
    {
        /// <summary>Overrides the PDF document title (from <c>&lt;title&gt;</c>).</summary>
        public string? Title { get; set; }

        /// <summary>Overrides the PDF document author (from <c>&lt;meta name="author"&gt;</c>).</summary>
        public string? Author { get; set; }

        /// <summary>Overrides the PDF document subject (from <c>&lt;meta name="subject"&gt;</c>).</summary>
        public string? Subject { get; set; }

        /// <summary>Overrides the PDF document keywords (from <c>&lt;meta name="keywords"&gt;</c>).</summary>
        public string? Keywords { get; set; }

        /// <summary>Overrides the PDF document creator (from <c>&lt;meta name="generator"&gt;</c>).</summary>
        public string? Creator { get; set; }

        /// <summary>
        /// Overrides the PDF document's creation date. When non-null, wins over any date extracted
        /// from the HTML source; when null (the default), the HTML-extracted date is used unchanged.
        /// Required (directly or via a <c>&lt;meta&gt;</c> date in the source HTML) whenever an XMP
        /// metadata stream is written (<see cref="PdfGenerateConfig.EnableXmpMetadata"/> or
        /// <see cref="PdfGenerateConfig.PdfAConformance"/>) - <c>xmp:CreateDate</c> needs a real value,
        /// and generation throws rather than writing a default/placeholder date if neither is present.
        /// </summary>
        public DateTimeOffset? CreationDate { get; set; }

        /// <summary>
        /// Arbitrary additional XMP metadata to include in the document's XMP stream (see
        /// <see cref="PdfGenerateConfig.EnableXmpMetadata"/>/<see cref="PdfGenerateConfig.PdfAConformance"/>),
        /// beyond the built-in Dublin Core/<c>pdf:</c>/<c>xmp:</c> fields - e.g. an internal provenance
        /// or records-management schema. Each element is appended into the XMP packet as its own
        /// <c>rdf:Description</c>, using the element's own namespace unchanged; accepting
        /// <see cref="XElement"/> rather than a raw XML string keeps the packet well-formed by
        /// construction. Has no effect unless an XMP stream is actually written for the render, in
        /// which case a non-empty collection here also forces one to be written even if
        /// <see cref="PdfGenerateConfig.EnableXmpMetadata"/> is left <c>false</c> and
        /// <see cref="PdfGenerateConfig.PdfAConformance"/> is <see cref="PeachPDF.PdfAConformance.None"/>.
        /// </summary>
        public ICollection<XElement> CustomXmpProperties { get; } = [];
    }
}
