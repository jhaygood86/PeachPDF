#nullable enable

using System;

namespace PeachPDF
{
    /// <summary>
    /// A file to embed in the generated PDF, added through <see cref="PdfGenerateConfig.Attachments"/>.
    /// The file shows up in a viewer's attachments panel and is indexed in the document catalog's
    /// <c>/AF</c> (Associated Files) array and <c>/Names /EmbeddedFiles</c> name tree.
    /// </summary>
    /// <remarks>
    /// Embedding arbitrary files is what separates PDF/A-3 from PDF/A-1 and PDF/A-2, so requesting an
    /// attachment together with a <see cref="PdfAConformance"/> level below PDF/A-3 is rejected at
    /// generation time. Without any PDF/A level attachments are simply ordinary PDF attachments.
    /// </remarks>
    public sealed class PdfAttachment
    {
        /// <summary>
        /// The file name shown to the reader (and used as the attachment's key). Required, unique among the
        /// document's attachments, and limited to characters a PDF name-tree key can carry (Latin-1); a
        /// non-ASCII name is additionally written as a Unicode string.
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>The file's content. Required.</summary>
        public byte[] Data { get; set; } = [];

        /// <summary>
        /// The file's MIME type, for example <c>text/xml</c> or <c>text/csv</c>. Defaults to
        /// <c>application/octet-stream</c>.
        /// </summary>
        public string MimeType { get; set; } = "application/octet-stream";

        /// <summary>
        /// How the file relates to the PDF's content. Defaults to <see cref="PdfAttachmentRelationship.Unspecified"/>.
        /// </summary>
        public PdfAttachmentRelationship Relationship { get; set; } = PdfAttachmentRelationship.Unspecified;

        /// <summary>An optional human-readable description of the file.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// The file's last-modification date. When left <c>null</c>, the document's resolved creation date
        /// is used (so output stays deterministic) and, when there is none, the current time.
        /// </summary>
        public DateTimeOffset? ModificationDate { get; set; }
    }
}
