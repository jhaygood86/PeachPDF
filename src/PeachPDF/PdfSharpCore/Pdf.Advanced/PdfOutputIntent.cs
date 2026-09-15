#nullable enable

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// A PDF output intent (ISO 19005 §6.2.2 / ISO 15930 / PDF 32000-1 §14.11.5) - the mechanism that
    /// makes an otherwise device-dependent <c>DeviceRGB</c>/<c>DeviceGray</c>/<c>DeviceCMYK</c> content
    /// stream (see <see cref="PeachPDF.PdfSharpCore.Drawing.Pdf.XGraphicsPdfRenderer"/>) legal under
    /// PDF/A or PDF/X: it names a device-independent ICC profile the whole document's color is defined
    /// relative to, without requiring every color operator to be rewritten as <c>/ICCBased</c>.
    /// </summary>
    /// <remarks>
    /// Two constructors, for the two conformance families this project supports: <see cref="PdfOutputIntent(PdfDocument, byte[])"/>
    /// is used only for the sRGB profile PeachPDF bundles for PDF/A (see <see cref="PdfAResources"/>) -
    /// PeachPDF never generates whole-document CMYK output today (<see cref="PdfDocumentOptions.ColorMode"/>
    /// stays <c>Undefined</c>/mixed - see <see cref="PeachPDF.PdfGenerator"/>), so PDF/A's output intent is
    /// always the bundled sRGB one. <see cref="PdfOutputIntent(PdfDocument, byte[], string, string, string, int?)"/>
    /// is the general PDF/X form: the caller supplies the ICC profile bytes (PeachPDF bundles no default
    /// press profile - see <see cref="ColorOptions.OutputIntentProfile"/>) and the <c>/S</c>/condition
    /// identifier for the requested <see cref="PeachPDF.PdfXConformance"/> level.
    /// </remarks>
    internal sealed class PdfOutputIntent : PdfDictionary
    {
        /// <summary>
        /// Creates the PDF/A output intent and its embedded <c>/DestOutputProfile</c> ICC stream (added
        /// to <paramref name="document"/> as its own indirect object - required for a stream). Always the
        /// bundled sRGB profile - see this class's remarks.
        /// </summary>
        public PdfOutputIntent(PdfDocument document, byte[] iccProfileBytes)
            // "/GTS_PDFA1" is the correct /S value for every ISO 19005 part (1, 2, and 3 all reuse
            // the original PDF/A-1 identifier for backward compatibility - not a mistake). /N 3: the
            // bundled profile is always sRGB (3 channels).
            : this(document, iccProfileBytes, "/GTS_PDFA1", "sRGB IEC61966-2.1", "sRGB IEC61966-2.1", explicitN: 3)
        {
        }

        /// <summary>
        /// Creates a general output intent and its embedded <c>/DestOutputProfile</c> ICC stream (added
        /// to <paramref name="document"/> as its own indirect object - required for a stream).
        /// </summary>
        /// <param name="document">The owning document.</param>
        /// <param name="iccProfileBytes">The raw ICC profile bytes to embed.</param>
        /// <param name="gtsIdentifier">
        /// The output intent subtype, e.g. <c>"/GTS_PDFA1"</c> or <c>"/GTS_PDFX"</c>.
        /// </param>
        /// <param name="outputConditionIdentifier">
        /// A string identifying the intended output device/production condition (<c>/OutputConditionIdentifier</c>).
        /// </param>
        /// <param name="info">A human-readable description of the output intent (<c>/Info</c>).</param>
        /// <param name="explicitN">
        /// The ICC profile's channel count (<c>/N</c> on the embedded profile stream). When
        /// <see langword="null"/>, derived from <paramref name="iccProfileBytes"/> itself via
        /// <see cref="PeachImage.IccColorProfile.TryCreate"/> (already a referenced package - reused
        /// rather than hand-rolling ICC header parsing a second time), falling back to <c>4</c> (CMYK, the
        /// only case PeachPDF's own callers pass unparsed bytes for) if parsing fails.
        /// </param>
        public PdfOutputIntent(PdfDocument document, byte[] iccProfileBytes, string gtsIdentifier,
            string outputConditionIdentifier, string info, int? explicitN)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/OutputIntent");
            Elements.SetName(Keys.S, gtsIdentifier);
            Elements.SetString(Keys.OutputConditionIdentifier, outputConditionIdentifier);
            Elements.SetString(Keys.Info, info);

            var profileStream = new PdfDictionary(document);
            document.Internals.AddObject(profileStream);
            profileStream.Elements.SetInteger("/N", explicitN ?? ResolveChannelCount(iccProfileBytes));
            profileStream.Stream = new PdfStream(iccProfileBytes, profileStream);
            profileStream.Elements[PdfStream.Keys.Length] = new PdfInteger(iccProfileBytes.Length);

            Elements.SetReference(Keys.DestOutputProfile, profileStream);
        }

        private static int ResolveChannelCount(byte[] iccProfileBytes)
        {
            return PeachImage.IccColorProfile.TryCreate(iccProfileBytes, out var profile) && profile is not null
                ? profile.ChannelCount
                : 4;
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : KeysBase
        {
            /// <summary>(Required) Must be OutputIntent for an output intent dictionary.</summary>
            [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "OutputIntent")]
            public const string Type = "/Type";

            /// <summary>
            /// (Required) The output intent subtype - GTS_PDFA1 identifies this as a PDF/A output
            /// intent (reused, unchanged, across ISO 19005-1/2/3).
            /// </summary>
            [KeyInfo(KeyType.Name | KeyType.Required)]
            public const string S = "/S";

            /// <summary>
            /// (Required) A string identifying the intended output device or production condition -
            /// for a registered characterization, the name registered with ICC; PeachPDF always names
            /// the well-known "sRGB IEC61966-2.1" condition.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Required)]
            public const string OutputConditionIdentifier = "/OutputConditionIdentifier";

            /// <summary>
            /// (Required if OutputConditionIdentifier does not identify a standard characterization,
            /// but PeachPDF always writes it) A human-readable description of the output intent.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Optional)]
            public const string Info = "/Info";

            /// <summary>
            /// (Required for PDF/A; must be an indirect reference) An ICC profile stream defining the
            /// transformation from source color space to a device-independent color space.
            /// </summary>
            [KeyInfo(KeyType.Stream | KeyType.Optional | KeyType.MustBeIndirect)]
            public const string DestOutputProfile = "/DestOutputProfile";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            public static DictionaryMeta Meta
            {
                get { return _meta ??= CreateMeta(typeof(Keys)); }
            }
            static DictionaryMeta _meta = null!;
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta
        {
            get { return Keys.Meta; }
        }
    }
}
