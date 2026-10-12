namespace PeachPDF.PDF.Structure
{
    /// <summary>
    /// Represents a marked-content reference ("/MCR") - used when a structure element's marked
    /// content lives on a page other than the element's own primary "/Pg" (e.g. content spanning
    /// a page break). Ported from upstream PDFsharp's Pdf.Structure/PdfMarkedContentReference.cs.
    /// </summary>
    internal sealed class PdfMarkedContentReference : PdfDictionary
    {
        public PdfMarkedContentReference()
        {
            Elements.SetName(Keys.Type, "/MCR");
        }

        public PdfMarkedContentReference(PdfDocument document)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/MCR");
        }

        /// <summary>
        /// The page the referenced marked-content sequence is rendered on.
        /// </summary>
        public PdfPage Page
        {
            get { return (PdfPage)Elements.GetDictionary(Keys.Pg); }
            set { Elements.SetReference(Keys.Pg, value); }
        }

        /// <summary>
        /// The marked-content identifier of the sequence within its page's content stream.
        /// </summary>
        public int Mcid
        {
            get { return Elements.GetInteger(Keys.MCID); }
            set { Elements.SetInteger(Keys.MCID, value); }
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : KeysBase
        {
            // Reference: ISO 32000-1 Table 324 - Entries in a marked-content reference dictionary

            [PdfKey(PdfKeyType.Name | PdfKeyType.Required, FixedValue = "MCR")]
            public const string Type = "/Type";

            [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional)]
            public const string Pg = "/Pg";

            [PdfKey(PdfKeyType.Stream | PdfKeyType.Optional)]
            public const string Stm = "/Stm";

            [PdfKey(PdfKeyType.Optional)]
            public const string StmOwn = "/StmOwn";

            [PdfKey(PdfKeyType.Integer | PdfKeyType.Required)]
            public const string MCID = "/MCID";

            public static PdfDictionaryMetadata Meta => _meta ??= CreateMeta(typeof(Keys));

            static PdfDictionaryMetadata _meta = null!;
        }

        internal override PdfDictionaryMetadata Meta => Keys.Meta;
    }
}
