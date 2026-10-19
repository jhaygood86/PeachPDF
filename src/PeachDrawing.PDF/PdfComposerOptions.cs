#nullable enable

using System;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// What a <see cref="PdfComposer"/> writes into the file besides the pages: the document information, the PDF version and
    /// whether streams are compressed or the file encrypted. Read once, when the composer is created, so changing the options
    /// afterwards does not change a document in progress.
    /// </summary>
    public sealed class PdfComposerOptions
    {
        /// <summary>The document's title (shown in a reader's title bar), or <see langword="null"/> for none.</summary>
        public string? Title { get; set; }

        /// <summary>The name of the person who made the document, or <see langword="null"/> for none.</summary>
        public string? Author { get; set; }

        /// <summary>What the document is about, or <see langword="null"/> for none.</summary>
        public string? Subject { get; set; }

        /// <summary>Keywords for the document, separated as the author likes, or <see langword="null"/> for none.</summary>
        public string? Keywords { get; set; }

        /// <summary>The name of the program that made the content the file was written from, or <see langword="null"/> for none.</summary>
        public string? Creator { get; set; }

        /// <summary>When the document was made, or <see langword="null"/> to leave the date out so the same input always gives the same file.</summary>
        public DateTimeOffset? CreationDate { get; set; }

        /// <summary>The PDF version to write. Defaults to <see cref="PdfVersion.Pdf20"/>.</summary>
        public PdfVersion Version { get; set; } = PdfVersion.Pdf20;

        /// <summary>
        /// Whether page content streams are compressed. Defaults to <see langword="true"/>; turning it off gives a larger file whose
        /// drawing operators can be read in a text editor.
        /// </summary>
        public bool CompressContentStreams { get; set; } = true;

        /// <summary>
        /// Password protection and permissions for the file, or <see langword="null"/> (the default) to leave it unprotected.
        /// </summary>
        public PdfEncryptionOptions? Encryption { get; set; }
    }
}
