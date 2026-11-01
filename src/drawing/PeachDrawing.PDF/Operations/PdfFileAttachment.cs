using System;

namespace PeachDrawing.PDF
{
    /// <summary>A file to attach to a PDF with <see cref="PdfOperation.AddAttachment"/>.</summary>
    public sealed class PdfFileAttachment
    {
        /// <summary>The file to attach, as a path. Give this or <see cref="Data"/>.</summary>
        public string? FilePath { get; set; }

        /// <summary>The file to attach, as its bytes; needs <see cref="AttachmentName"/> or <see cref="Key"/>.</summary>
        public byte[]? Data { get; set; }

        /// <summary>The key that identifies the attachment in the PDF; the file name when <see langword="null"/>.</summary>
        public string? Key { get; set; }

        /// <summary>The name a viewer shows for the attachment; the key when <see langword="null"/>.</summary>
        public string? AttachmentName { get; set; }

        /// <summary>A description a viewer may show.</summary>
        public string? Description { get; set; }

        /// <summary>The media type, such as <c>text/csv</c>; <c>application/octet-stream</c> when <see langword="null"/>.</summary>
        public string? MimeType { get; set; }

        /// <summary>When the file was last modified; its timestamp when <see langword="null"/> and it came from a path.</summary>
        public DateTimeOffset? ModificationDate { get; set; }

        /// <summary>Whether to replace an attachment the PDF already has under the same <see cref="Key"/>; otherwise a clash is an error.</summary>
        public bool Replace { get; set; }

        /// <summary>How the file relates to the PDF; required by PDF/A-3.</summary>
        public PdfEmbeddedFileRelationship Relationship { get; set; } = PdfEmbeddedFileRelationship.Unspecified;
    }
}
