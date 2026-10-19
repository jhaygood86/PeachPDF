#nullable enable

using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// What a <see cref="PdfComposer"/> writes into the file besides the pages: the document information, the PDF version and
    /// conformance level, colour management, encryption, embedded files and how streams and images are compressed.
    /// </summary>
    /// <remarks>
    /// The options are read when they are applied - by the <see cref="PdfComposer"/> constructor, or by
    /// <see cref="PdfComposer.Configure"/> - so changing the object afterwards does not change a document in progress. A document's
    /// format settings (version, conformance, colour, encryption, embedded files) are fixed by the first application: applying options
    /// that ask for different ones to the same composer is rejected, rather than leaving a file whose header disagrees with how some of
    /// its pages were written. The document information may change from one application to the next.
    /// </remarks>
    public sealed class PdfComposerOptions
    {
        // ---- Document information ----------------------------------------------------------------------------------------

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

        /// <summary>
        /// The name of the program that converted the content to PDF, or <see langword="null"/> for the writer's own
        /// (<c>PeachDrawing.PDF</c> and its version).
        /// </summary>
        public string? Producer { get; set; }

        /// <summary>
        /// When the document was made, or <see langword="null"/> to leave the date out so the same input always gives the same file.
        /// An XMP metadata packet (see <see cref="WriteXmpMetadata"/>) requires one.
        /// </summary>
        public DateTimeOffset? CreationDate { get; set; }

        /// <summary>
        /// The document's natural language as a BCP 47 tag (<c>en-US</c>), written as the catalog's <c>/Lang</c>, or
        /// <see langword="null"/> for none. The accessible PDF/A levels (<see cref="PdfAConformance.PdfA1A"/>,
        /// <see cref="PdfAConformance.PdfA2A"/>, <see cref="PdfAConformance.PdfA3A"/>) require one.
        /// </summary>
        public string? Language { get; set; }

        // ---- Format -------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The PDF version to write, or <see langword="null"/> (the default) to let the writer choose: <see cref="PdfVersion.Pdf20"/>,
        /// except for the PDF/A and PDF/X levels defined against PDF 1.x, which get <see cref="PdfVersion.Pdf17"/>.
        /// A version that contradicts the conformance level is rejected.
        /// </summary>
        public PdfVersion? Version { get; set; }

        /// <summary>The PDF/A level the file claims. Defaults to <see cref="PdfAConformance.None"/>. Cannot be combined with <see cref="XConformance"/> or <see cref="Encryption"/>.</summary>
        public PdfAConformance Conformance { get; set; }

        /// <summary>The PDF/X level the file claims. Defaults to <see cref="PdfXConformance.None"/>. Needs a <see cref="ColorOptions"/> output intent.</summary>
        public PdfXConformance XConformance { get; set; }

        /// <summary>
        /// Colour management: the output intent PDF/X requires, and the colour conversions to apply. <see langword="null"/> (the
        /// default) writes every colour in the space it was made in, and PDF/A files get the bundled sRGB output intent.
        /// </summary>
        public ColorOptions? ColorOptions { get; set; }

        /// <summary>
        /// Whether a document-wide XMP metadata packet is written. Always written when a PDF/A or PDF/X level is claimed or
        /// <see cref="CustomXmpProperties"/> is not empty. Requires <see cref="CreationDate"/>.
        /// </summary>
        public bool WriteXmpMetadata { get; set; }

        /// <summary>
        /// Extra elements to add to the XMP packet's <c>rdf:Description</c>, for properties of schemas the writer does not know.
        /// Each element must be in the namespace of its schema, and the schema must be declared the way PDF/A requires.
        /// </summary>
        public ICollection<XElement> CustomXmpProperties { get; } = [];

        /// <summary>
        /// Files to embed in the document. Requires PDF/A-3 (or no PDF/A level) and no PDF/X level; each file name must be unique.
        /// </summary>
        public IList<PdfEmbeddedFile> EmbeddedFiles { get; } = [];

        /// <summary>
        /// Marks the document as a Factur-X / ZUGFeRD e-invoice in its XMP metadata, or <see langword="null"/> (the default) for
        /// none. The invoice XML itself is one of <see cref="EmbeddedFiles"/>; the document must be PDF/A-3.
        /// </summary>
        public PdfFacturXInfo? FacturX { get; set; }

        /// <summary>
        /// Password protection and permissions for the file, or <see langword="null"/> (the default) to leave it unprotected.
        /// Cannot be combined with a PDF/A or PDF/X level.
        /// </summary>
        public PdfEncryptionOptions? Encryption { get; set; }

        // ---- Compression -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Whether page content streams are compressed. Defaults to <see langword="true"/>; turning it off gives a larger file whose
        /// drawing operators can be read in a text editor.
        /// </summary>
        public bool CompressContentStreams { get; set; } = true;

        /// <summary>
        /// Whether page content, fonts, embedded files and raster image data are compressed with Brotli (the PDF 2.0
        /// <c>/BrotliDecode</c> filter) instead of Flate: typically smaller output. Defaults to <see langword="false"/>, because the
        /// filter is not read by every PDF reader yet (PDFium, the engine behind Chrome's and Edge's viewers, and Acrobat show such a
        /// file as blank), so enable it only when you control the readers. Only applies to a <see cref="PdfVersion.Pdf20"/> file, and
        /// not to PDF/A-4 or PDF/X-6, which allow only the standard filters. Brotli comes from .NET's own encoder, or from the encoder
        /// a host registers with <c>PeachDrawing.Text.Compression.BrotliCompression.SetCompressor</c>; with neither, the writer falls
        /// back to Flate silently.
        /// </summary>
        public bool BrotliCompression { get; set; }

        /// <summary>
        /// <b>Experimental.</b> Whether an opaque, upright, non-animated gray or RGB JPEG XL image is embedded as its original bytes
        /// with the <c>/JXLDecode</c> filter instead of being decoded and recompressed. Defaults to <see langword="false"/>. There is
        /// no published PDF filter for JPEG XL yet: the name and layout follow the only open-source design so far (an experimental
        /// PDFium decoder), so no shipping PDF reader opens such a file today. A JPEG XL with alpha, animation or CMYK always takes the
        /// normal raster path, as does any image under <see cref="PeachDrawing.PDF.ImageCompression.Lossy"/>. Same version
        /// restrictions as <see cref="BrotliCompression"/>.
        /// </summary>
        public bool JxlPassthrough { get; set; }

        // ---- Images ------------------------------------------------------------------------------------------------------

        /// <summary>
        /// How lossless raster images are embedded. Defaults to <see cref="PeachDrawing.PDF.ImageCompression.Auto"/>.
        /// </summary>
        public ImageCompression ImageCompression { get; set; } = ImageCompression.Auto;

        /// <summary>
        /// Whether an image drawn smaller than its natural size is resampled down to the size it is drawn at. Defaults to
        /// <see langword="true"/>.
        /// </summary>
        public bool DownscaleImages { get; set; } = true;

        /// <summary>The JPEG quality (1 to 100) of a downscaled image that is re-encoded as JPEG. Defaults to 70.</summary>
        public int DownscaleQuality { get; set; } = 70;

        /// <summary>
        /// How many times larger than the size it is drawn at a downscaled image may stay, to keep detail for zooming in. Defaults to
        /// 1.0: exactly the drawn size.
        /// </summary>
        public double MaximumDownscaleMultiplier { get; set; } = 1.0;

        // ---- Effects -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Whether content that needs PDF transparency is flattened to an opaque bitmap when the claimed level (PDF/A-1, PDF/X-1a,
        /// PDF/X-3) forbids transparency, instead of being rejected. Defaults to <see langword="false"/>.
        /// </summary>
        public bool FlattenTransparency { get; set; }

        /// <summary>
        /// Whether per-channel colour effects (brightness, contrast, invert and the like) are written as blend-mode layers every PDF
        /// reader composites itself, instead of as a PDF transfer function (an ExtGState <c>/TR</c>). Defaults to
        /// <see langword="false"/>. The transfer function is exact for any content but some readers ignore it (MuPDF, and so the viewers
        /// built on it, draw the element unfiltered); the blend-mode layers filter the element in every reader, but are exact only where
        /// the content is opaque, and need a reader that supports PDF transparency (so they are rejected under PDF/A-1 and
        /// PDF/X-1a/X-3, like the default is).
        /// </summary>
        public bool PortableColorFilters { get; set; }
    }
}
