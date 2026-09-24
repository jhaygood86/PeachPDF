// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

#nullable enable

using PeachPDF.Network;
using System.Collections.Generic;

namespace PeachPDF
{
    /// <summary>
    /// The settings for generating PDF using <see cref="PdfGenerator"/>
    /// </summary>
    public sealed class PdfGenerateConfig
    {
        #region Fields/Consts

        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        private int _marginTop = 10;

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        private int _marginBottom = 10;

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        private int _marginLeft = 10;

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        private int _marginRight = 10;

        #endregion

        /// <summary>
        /// the amount of pixels per inch. We always render at 72 points per inch, so this is used to determine how many pixels a point is
        /// </summary>
        public double PixelsPerInch { get; set; } = 72d;

        /// <summary>
        /// When set to true, this renders the page and automatically scales PixelsPerInch to fit the page contents
        /// </summary>
        public bool ScaleToPageSize { get; set; }

        /// <summary>
        /// When set to true, sets the PixelsPerInch to fit the page contents only if the page contents is larger than the window
        /// </summary>
        public bool ShrinkToFit { get; set; }

        /// <summary>
        /// When set to a positive number, sets the PixelsPerInch to fit the minimum content width
        /// </summary>
        public double MinContentWidth { get; set; }

        /// <summary>
        /// The CSS media type the cascade matches <c>@media</c> rules against. Defaults to
        /// <c>"print"</c> (the appropriate media for paged output). Set to another value (e.g.
        /// <c>"screen"</c>) to render as that media type instead.
        /// </summary>
        public string Media { get; set; } = "print";

        /// <summary>
        /// The color scheme reported to <c>@media (prefers-color-scheme: ...)</c> queries.
        /// Defaults to <see cref="PdfColorScheme.Light"/>; set to <see cref="PdfColorScheme.Dark"/>
        /// to render the document's dark-mode styles.
        /// </summary>
        public PdfColorScheme PreferredColorScheme { get; set; } = PdfColorScheme.Light;

        /// <summary>
        /// When set to <c>true</c>, the document's own author style sheets (its <c>&lt;style&gt;</c>
        /// elements and <c>&lt;link rel="stylesheet"&gt;</c> references) are ignored — only the user-agent
        /// default styles and any caller-supplied stylesheet (the <c>cssData</c> argument) are applied.
        /// Inline <c>style</c> attributes are not affected. Defaults to <c>false</c>.
        /// </summary>
        public bool IgnoreAuthorStyleSheets { get; set; }

        /// <summary>
        /// Optional overrides for the generated PDF's document-information metadata (title, author,
        /// subject, keywords, creator). When non-null, each non-null field overrides the corresponding
        /// value extracted from the HTML source; when null (the default) all metadata comes from the HTML.
        /// </summary>
        public PdfDocumentMetadata? Metadata { get; set; } = null;

        /// <summary>
        /// the page size to use for each page in the generated pdf
        /// </summary>
        public PageSize PageSize { get; set; }

        /// <summary>
        /// if the page size is undefined this allows you to set manually the page width in points
        /// </summary>
        public double ManualPageWidth { get; set; }

        /// <summary>
        /// if the page size is undefined this allows you to set manually the page height in points
        /// </summary>
        public double ManualPageHeight { get; set; }

        /// <summary>
        /// the orientation of each page of the generated pdf
        /// </summary>
        public PageOrientation PageOrientation { get; set; }

        /// <summary>
        /// The resources to load network content for the renderer.
        /// If null is provided, then an implementation that loads only the default document and any resources with data: URIs is provided
        /// We ship with MimeKit (MHTML) and HttpClient based implementations that can be used instead.
        /// </summary>
        public RNetworkLoader? NetworkLoader { get; set; } = null;

        /// <summary>
        /// Whether the document may reach the local file system. Defaults to <c>true</c>, preserving the
        /// historical behavior in which <c>file:</c> references are read from disk and relative references
        /// resolve against the current working directory.
        /// <para>
        /// Set to <c>false</c> when rendering untrusted input, or in a host with no meaningful file system
        /// (a browser/WebAssembly app). Two things then change: every <c>file:</c> resource request resolves
        /// to "not found", and a <see cref="NetworkLoader"/> with no <see cref="RNetworkLoader.BaseUri"/> of
        /// its own (<see cref="DataUriNetworkLoader"/> and <see cref="MimeKitNetworkLoader"/> are both in
        /// this category) no longer inherits the working directory as a base — so a relative reference goes
        /// unresolved rather than being turned into a <c>file:</c> URI and read from disk.
        /// </para>
        /// <para>
        /// This covers every resource the document asks for — <c>&lt;img&gt;</c>,
        /// <c>&lt;link rel="stylesheet"&gt;</c>, CSS <c>url()</c>, SVG <c>&lt;image href&gt;</c>, and
        /// <c>@font-face src: url()</c> — because they all resolve through one place. A deny wins even over
        /// an explicitly configured <see cref="FileUriNetworkLoader"/>.
        /// </para>
        /// <para>
        /// It does not restrict the root document, which is an input rather than a fetch: an HTML string
        /// passed to <c>GeneratePdf</c>, or a file a <see cref="FileUriNetworkLoader"/> was explicitly
        /// constructed with, is still read.
        /// </para>
        /// </summary>
        public bool AllowLocalFileAccess { get; set; } = true;

        /// <summary>
        /// A fallback language (e.g. <c>"en-US"</c>) used for language-dependent rendering — currently
        /// <c>hyphens: auto</c> automatic hyphenation — only when the document itself declares none via
        /// <c>&lt;html lang="..."&gt;</c>. A document's own <c>lang</c> attribute always takes priority
        /// over this setting when present. Per the CSS Text spec, automatic hyphenation requires knowing
        /// the text's language; PeachPDF never guesses one on its own, so a document with no <c>lang</c>
        /// and no <see cref="DefaultLanguage"/> set will not be automatically hyphenated. Set this when
        /// you know your content's language out-of-band and want automatic hyphenation to apply anyway.
        /// </summary>
        public string? DefaultLanguage { get; set; } = null;

        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        public int MarginTop
        {
            get => _marginTop;
            set
            {
                if (value > -1)
                    _marginTop = value;
            }
        }

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        public int MarginBottom
        {
            get => _marginBottom;
            set
            {
                if (value > -1)
                    _marginBottom = value;
            }
        }

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        public int MarginLeft
        {
            get => _marginLeft;
            set
            {
                if (value > -1)
                    _marginLeft = value;
            }
        }

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        public int MarginRight
        {
            get => _marginRight;
            set
            {
                if (value > -1)
                    _marginRight = value;
            }
        }

        /// <summary>
        /// Gets or sets whether PDF content streams are compressed with FlateDecode.
        /// Defaults to <c>true</c>. Set to <c>false</c> to produce human-readable PDF streams,
        /// which is useful for testing or debugging.
        /// </summary>
        public bool CompressContentStreams { get; set; } = true;

        /// <summary>
        /// When set to <c>true</c>, PeachPDF emits a PDF/UA-style tagged structure tree
        /// (StructTreeRoot, MarkInfo, per-element structure elements and marked content)
        /// alongside the visual content, mapping the HTML element tree to standard PDF structure
        /// types (see the <c>-peachpdf-pdf-tag-type</c> CSS property for how the mapping is
        /// controlled). Defaults to <c>false</c> — tagging adds a real amount of extra
        /// object-model bookkeeping per page, so it is an explicit, informed opt-in.
        /// </summary>
        public bool EnableTaggedPdf { get; set; } = false;

        /// <summary>
        /// When set to <c>true</c>, PeachPDF emits real, fillable AcroForm fields (text, checkbox,
        /// radio, select) for form elements whose resolved <c>-peachpdf-pdf-form-field</c> value
        /// requests one, instead of the default static box rendering. Defaults to <c>false</c> - an
        /// explicit, informed opt-in, exactly like <see cref="EnableTaggedPdf"/>.
        /// </summary>
        public bool EnableInteractivePdfForms { get; set; } = false;

        /// <summary>
        /// The PDF/A (ISO 19005) conformance level to target. Defaults to <see cref="PeachPDF.PdfAConformance.None"/>
        /// - no PDF/A-specific work is done (see <see cref="PeachPDF.PdfAConformance.None"/> for exactly
        /// what that means). See <see cref="PeachPDF.PdfAConformance"/> for what each other level
        /// requires, including PDF/A-1's restriction on transparency and the accessible "A" levels'
        /// language requirement. Requesting any level other than <see cref="PeachPDF.PdfAConformance.None"/>
        /// implicitly enables an XMP metadata stream for that render regardless of
        /// <see cref="EnableXmpMetadata"/>.
        /// </summary>
        public PdfAConformance PdfAConformance { get; set; } = PdfAConformance.None;

        /// <summary>
        /// The PDF version to target. Defaults to <see cref="PeachPDF.PdfVersion.Pdf17"/> - PeachPDF's
        /// long-standing output version. Set to <see cref="PeachPDF.PdfVersion.Pdf20"/> to emit a real
        /// PDF 2.0 (ISO 32000-2) file header. Incompatible with requesting any
        /// <see cref="PdfAConformance"/> level other than <see cref="PeachPDF.PdfAConformance.None"/> -
        /// generation throws if both are set on the same call.
        /// </summary>
        public PdfVersion PdfVersion { get; set; } = PdfVersion.Pdf17;

        /// <summary>
        /// When set to <c>true</c>, PeachPDF emits an XMP metadata stream (the document catalog's
        /// <c>/Metadata</c> entry) alongside the classic Document Information dictionary - useful for
        /// digital-asset-management/archival pipelines that read XMP directly. This is independent of
        /// <see cref="PdfAConformance"/>: a caller can opt into an XMP stream without requesting PDF/A
        /// conformance at all. Defaults to <c>false</c>. <see cref="PdfAConformance"/> being anything
        /// other than <see cref="PeachPDF.PdfAConformance.None"/> forces an XMP stream to be written
        /// for that render regardless of this flag's value (PDF/A requires one), without mutating this
        /// config object.
        /// </summary>
        public bool EnableXmpMetadata { get; set; } = false;

        /// <summary>
        /// Files to embed in the generated PDF - for example the source data behind a report, or the
        /// supporting documents of an invoice. Each is listed in the PDF's attachments panel and indexed
        /// in the catalog's <c>/AF</c> array and <c>/Names /EmbeddedFiles</c> name tree. Empty by default,
        /// in which case nothing is embedded.
        /// </summary>
        /// <remarks>
        /// Embedding arbitrary files is what distinguishes PDF/A-3 from PDF/A-1 and PDF/A-2: generation
        /// throws when a file is attached together with <see cref="PdfAConformance"/> set to a PDF/A-1 or
        /// PDF/A-2 level (use one of the <c>PdfA3*</c> levels instead). With no PDF/A level requested,
        /// attachments are ordinary PDF attachments.
        /// Attachments are a whole-document property, like <see cref="PdfAConformance"/>: when several
        /// <c>AddPdfPages</c>/<c>AddPages</c> calls build one document, every call must specify the same
        /// set (the files are embedded once).
        /// </remarks>
        public ICollection<PdfAttachment> Attachments { get; } = [];

        /// <summary>
        /// When set, the generated PDF becomes a Factur-X / ZUGFeRD hybrid e-invoice: the supplied invoice XML is
        /// embedded and the document is marked as a Factur-X invoice in its XMP metadata. See
        /// <see cref="FacturXOptions"/> for what PeachPDF does and does not do, and its requirements (a PDF/A-3
        /// level in <see cref="PdfAConformance"/>). Defaults to <c>null</c> - not an e-invoice.
        /// </summary>
        /// <remarks>
        /// Like <see cref="Attachments"/> and <see cref="PdfAConformance"/> this is a whole-document
        /// property: every <c>AddPdfPages</c>/<c>AddPages</c> call on one document must specify the same
        /// invoice. It can be combined with <see cref="Attachments"/> for supporting documents; the invoice XML is
        /// always the first attachment.
        /// </remarks>
        public FacturXOptions? FacturX { get; set; }

        /// <summary>
        /// When set to <c>true</c> (the default), a raster image whose decoded pixel size is larger than
        /// its on-page display size is resized down before being embedded in the PDF, shrinking output
        /// file size with no visible quality loss. Set to <c>false</c> to always embed images at their
        /// full decoded resolution, as PeachPDF did before this option existed.
        /// </summary>
        public bool DownscaleImages { get; set; } = true;

        /// <summary>
        /// The JPEG quality (0-100) used when <see cref="DownscaleImages"/> actually resizes an image
        /// that has no real alpha channel (and so would be JPEG-encoded anyway). Has no effect on
        /// full-resolution JPEG embeds, and never applies to images with real transparency - those stay
        /// on the lossless embed path regardless of downscaling, just resized. Defaults to <c>70</c>.
        /// </summary>
        public int DownscaleQuality { get; set; } = 70;

        /// <summary>
        /// When <see cref="DownscaleImages"/> resizes an image, the resize target is the image's on-page
        /// display size multiplied by this value, clamped to never exceed the image's natural decoded
        /// size (downscaling never upscales). For example, an image displayed at 60x40 with a multiplier
        /// of <c>2.0</c> resizes to 120x80 - useful headroom for zooming or printing. Defaults to
        /// <c>1.0</c> (resize to exactly the display size).
        /// </summary>
        public double MaximumDownscaleMultiplier { get; set; } = 1.0;

        /// <summary>
        /// How an opaque PNG/BMP/GIF source is embedded - see <see cref="PeachPDF.ImageCompression"/> for
        /// what each level does. Defaults to <see cref="PeachPDF.ImageCompression.Auto"/>.
        /// </summary>
        public ImageCompression ImageCompression { get; set; } = ImageCompression.Auto;

        /// <summary>
        /// The resolution, in pixels per inch of <em>paper</em>, at which PeachPDF renders the effects a PDF cannot
        /// express as vector content (for example <c>filter: blur()</c>) into bitmaps before embedding them.
        /// Defaults to <c>300</c>, the print-quality convention: it stays sharp when a viewer is zoomed to several
        /// hundred percent or the file is printed. Valid values are 72 to 1200; anything else throws an
        /// <see cref="System.ArgumentOutOfRangeException"/> when generation starts.
        /// </summary>
        /// <remarks>
        /// The value is a physical resolution and is independent of <see cref="PixelsPerInch"/>: a bitmap is always
        /// placed at exactly the size of the content it replaces, so an inch of the page stays an inch, and only the
        /// number of pixels backing it changes. For example with <see cref="PixelsPerInch"/> of 96 and a value of
        /// 288, each CSS pixel is backed by 3 x 3 = 9 bitmap pixels. Higher values give sharper output and larger
        /// files; content that would exceed <see cref="MaxRasterPixels"/> is rendered at a lower resolution instead
        /// (its placed size never changes). Bitmaps are exempt from <see cref="DownscaleImages"/>.
        /// </remarks>
        public double RasterizationDpi { get; set; } = 300;

        /// <summary>
        /// The largest number of pixels a single rasterized region may have (see <see cref="RasterizationDpi"/>).
        /// A region that would exceed it is rendered at a lower resolution just large enough to fit. Defaults to
        /// 64 million pixels (about 256 MB of working memory for one region).
        /// </summary>
        public long MaxRasterPixels { get; set; } = 64_000_000;

        /// <summary>
        /// What happens when a document that targets PDF/A-1 or PDF/X-1a/X-3 (all of which forbid transparency) uses something that needs it.
        /// <see cref="TransparencyPolicy.Reject"/> (the default) fails generation with an error naming the construct;
        /// <see cref="TransparencyPolicy.Flatten"/> renders the affected region into an opaque bitmap at <see cref="RasterizationDpi"/> instead
        /// (embedded as DeviceCMYK under PDF/X-1a). Has no effect at any other conformance level, which permit transparency.
        /// </summary>
        public TransparencyPolicy TransparencyPolicy { get; set; } = TransparencyPolicy.Reject;

        /// <summary>
        /// The PDF/X (ISO 15930) print-production conformance level to target. Defaults to
        /// <see cref="PeachPDF.PdfXConformance.None"/> - no PDF/X-specific work is done. See
        /// <see cref="PeachPDF.PdfXConformance"/> for what each level requires, including the mandatory
        /// <see cref="ColorOptions.OutputIntentProfile"/> and X1a's CMYK-only content restriction.
        /// Mutually exclusive with <see cref="PdfAConformance"/> (archival and print-production are
        /// different documents) - generation throws if both are set to a non-<c>None</c> value on the
        /// same call.
        /// </summary>
        public PdfXConformance PdfXConformance { get; set; } = PdfXConformance.None;

        /// <summary>
        /// Print color-management options - ICC output intent, black generation, and real ICC
        /// device-to-device color conversion (<see cref="ColorOptions.ConversionMode"/> - see
        /// <see cref="PeachPDF.ColorOptions"/>'s remarks). <see langword="null"/> (the default) behaves as
        /// <see cref="ColorOptions.OutputIntentProfile"/> being unset - fine unless
        /// <see cref="PdfXConformance"/> requires one, in which case generation throws.
        /// </summary>
        public ColorOptions? ColorOptions { get; set; }

        /// <summary>
        /// Set all 4 margins to the given value.
        /// </summary>
        /// <param name="value"></param>
        public void SetMargins(int value)
        {
            if (value > -1)
                _marginBottom = _marginLeft = _marginTop = _marginRight = value;
        }
    }
}