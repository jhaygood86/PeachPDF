#nullable enable

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// A PDF/A output intent (ISO 19005 §6.2.2 / PDF 32000-1 §14.11.5) - the mechanism that makes an
    /// otherwise device-dependent <c>DeviceRGB</c>/<c>DeviceGray</c> content stream (which is all
    /// PeachPDF ever emits - see <see cref="PeachPDF.PdfSharpCore.Drawing.Pdf.XGraphicsPdfRenderer"/>) legal under PDF/A: it
    /// names a device-independent ICC profile the whole document's color is defined relative to,
    /// without requiring every color operator to be rewritten as <c>/ICCBased</c>.
    /// </summary>
    /// <remarks>
    /// Only ever constructed for the sRGB profile PeachPDF embeds (see <see cref="PdfAResources"/>) - the
    /// library only ever generates <c>PdfColorMode.Rgb</c> output (<see cref="PdfDocumentOptions.ColorMode"/>
    /// is never exposed as CMYK through the public <see cref="PeachPDF.PdfGenerateConfig"/> surface),
    /// so a CMYK output intent is never needed.
    /// </remarks>
    internal sealed class PdfOutputIntent : PdfDictionary
    {
        /// <summary>
        /// Creates the output intent and its embedded <c>/DestOutputProfile</c> ICC stream (added to
        /// <paramref name="document"/> as its own indirect object - required for a stream).
        /// </summary>
        public PdfOutputIntent(PdfDocument document, byte[] iccProfileBytes)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/OutputIntent");
            // "/GTS_PDFA1" is the correct /S value for every ISO 19005 part (1, 2, and 3 all reuse
            // the original PDF/A-1 identifier for backward compatibility - not a mistake).
            Elements.SetName(Keys.S, "/GTS_PDFA1");
            Elements.SetString(Keys.OutputConditionIdentifier, "sRGB IEC61966-2.1");
            Elements.SetString(Keys.Info, "sRGB IEC61966-2.1");

            var profileStream = new PdfDictionary(document);
            document.Internals.AddObject(profileStream);
            profileStream.Elements.SetInteger("/N", 3);
            profileStream.Stream = new PdfStream(iccProfileBytes, profileStream);
            profileStream.Elements[PdfStream.Keys.Length] = new PdfInteger(iccProfileBytes.Length);

            Elements.SetReference(Keys.DestOutputProfile, profileStream);
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
