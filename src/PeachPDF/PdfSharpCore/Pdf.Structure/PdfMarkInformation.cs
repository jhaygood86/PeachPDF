namespace PeachPDF.PdfSharpCore.Pdf.Structure
{
    /// <summary>
    /// Represents a mark information dictionary ("/MarkInfo") - signals to a reader that the
    /// document conforms to Tagged PDF conventions. Ported from upstream PDFsharp's
    /// Pdf.Structure/PdfMarkInformation.cs.
    /// </summary>
    internal sealed class PdfMarkInformation : PdfDictionary
    {
        public PdfMarkInformation()
        { }

        public PdfMarkInformation(PdfDocument document)
            : base(document)
        { }

        /// <summary>
        /// Whether the document conforms to Tagged PDF conventions. Must be true for a tagged
        /// document to actually be recognized as such by a reader.
        /// </summary>
        public bool Marked
        {
            get { return Elements.GetBoolean(Keys.Marked); }
            set { Elements.SetBoolean(Keys.Marked, value); }
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : KeysBase
        {
            // Reference: ISO 32000-1 Table 321 - Entries in the mark information dictionary

            [KeyInfo(KeyType.Boolean | KeyType.Optional)]
            public const string Marked = "/Marked";

            [KeyInfo(KeyType.Boolean | KeyType.Optional)]
            public const string UserProperties = "/UserProperties";

            [KeyInfo(KeyType.Boolean | KeyType.Optional)]
            public const string Suspects = "/Suspects";

            public static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta _meta = null!;
        }

        internal override DictionaryMeta Meta => Keys.Meta;
    }
}
