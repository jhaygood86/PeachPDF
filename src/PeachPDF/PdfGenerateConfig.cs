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