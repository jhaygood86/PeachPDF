#nullable enable

using System;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// A file to embed in the PDF, added through <see cref="PdfComposerOptions.EmbeddedFiles"/>.
    /// The file shows up in a viewer's attachments panel and is indexed in the document catalog's
    /// <c>/AF</c> (Associated Files) array and <c>/Names /EmbeddedFiles</c> name tree.
    /// </summary>
    /// <remarks>
    /// Embedding arbitrary files is what separates PDF/A-3 from PDF/A-1 and PDF/A-2, so asking for an embedded file
    /// together with a <see cref="PdfAConformance"/> level below PDF/A-3 is rejected when the options are applied.
    /// Without any PDF/A level embedded files are simply ordinary PDF attachments.
    /// </remarks>
    public sealed class PdfEmbeddedFile
    {
        /// <summary>
        /// The file name shown to the reader (and used as the file's key). Required, unique among the
        /// document's embedded files, and limited to characters a PDF name-tree key can carry (Latin-1); a
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
        /// How the file relates to the PDF's content. Defaults to <see cref="PdfEmbeddedFileRelationship.Unspecified"/>.
        /// </summary>
        public PdfEmbeddedFileRelationship Relationship { get; set; } = PdfEmbeddedFileRelationship.Unspecified;

        /// <summary>An optional human-readable description of the file.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// The file's last-modification date. When left <see langword="null"/>, the document's creation date
        /// is used (so output stays deterministic) and, when there is none, the current time.
        /// </summary>
        public DateTimeOffset? ModificationDate { get; set; }
    }

    /// <summary>
    /// How an embedded file (<see cref="PdfEmbeddedFile"/>) relates to the PDF document it is embedded in -
    /// the PDF <c>/AFRelationship</c> entry (ISO 19005-3, ISO 32000-2 §14.13).
    /// </summary>
    /// <remarks>
    /// There are no technical consequences inside the PDF file from the value chosen; it tells a reader
    /// how to understand the role of the embedded data.
    /// </remarks>
    public enum PdfEmbeddedFileRelationship
    {
        /// <summary>
        /// None of the other relationships applies, or it is unknown. Default.
        /// </summary>
        Unspecified = 0,

        /// <summary>
        /// The file is the source the visual representation was derived from - for example the XML a PDF
        /// was produced from by a transformation.
        /// </summary>
        Source,

        /// <summary>
        /// The file holds data used for the visual representation - for example the numbers behind a table
        /// or chart. It may hold less than the PDF shows.
        /// </summary>
        Data,

        /// <summary>
        /// The file is an alternative representation of the PDF's content: both carry the same information
        /// and either can stand in for the other.
        /// </summary>
        Alternative,

        /// <summary>
        /// The file is neither the source nor an alternative, but adds information - for example
        /// supporting documents.
        /// </summary>
        Supplement,
    }

    /// <summary>
    /// Marks the document as a Factur-X / ZUGFeRD e-invoice in its XMP metadata: which of the embedded files is the invoice XML,
    /// and the conformance level of the profile it follows. Set through <see cref="PdfComposerOptions.FacturX"/>.
    /// </summary>
    /// <remarks>
    /// The writer only records the marking; building and validating the invoice XML, and choosing its file name, is the caller's job.
    /// The document must be PDF/A-3 conformant for the marking to be valid.
    /// </remarks>
    public sealed class PdfFacturXInfo
    {
        /// <summary>Creates the marking.</summary>
        /// <param name="fileName">the <see cref="PdfEmbeddedFile.FileName"/> of the embedded invoice XML (<c>factur-x.xml</c>, say)</param>
        /// <param name="conformanceLevel">the <c>fx:ConformanceLevel</c> value of the profile, such as <c>EN 16931</c> or <c>BASIC WL</c></param>
        public PdfFacturXInfo(string fileName, string conformanceLevel)
        {
            ArgumentException.ThrowIfNullOrEmpty(fileName);
            ArgumentException.ThrowIfNullOrEmpty(conformanceLevel);
            FileName = fileName;
            ConformanceLevel = conformanceLevel;
        }

        /// <summary>The file name of the embedded invoice XML.</summary>
        public string FileName { get; }

        /// <summary>The <c>fx:ConformanceLevel</c> value.</summary>
        public string ConformanceLevel { get; }
    }
}
