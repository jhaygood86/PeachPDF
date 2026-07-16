using PeachPDF.PdfSharpCore.Pdf.Annotations;

namespace PeachPDF.PdfSharpCore.Pdf.Structure
{
    /// <summary>
    /// Represents an object reference ("/OBJR") - links a structure element (e.g. "/Link") to a
    /// non-marked-content PDF object such as an annotation. Ported from upstream PDFsharp's
    /// Pdf.Structure/PdfObjectReference.cs.
    /// </summary>
    internal sealed class PdfObjectReference : PdfDictionary
    {
        public PdfObjectReference()
        {
            Elements.SetName(Keys.Type, "/OBJR");
        }

        public PdfObjectReference(PdfDocument document)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/OBJR");
        }

        /// <summary>
        /// The page the referenced object is rendered on.
        /// </summary>
        public PdfPage Page
        {
            get { return (PdfPage)Elements.GetDictionary(Keys.Pg); }
            set { Elements.SetReference(Keys.Pg, value); }
        }

        /// <summary>
        /// The referenced object (e.g. a link annotation).
        /// </summary>
        public PdfAnnotation Object
        {
            get { return (PdfAnnotation)Elements.GetDictionary(Keys.Obj); }
            set { Elements.SetReference(Keys.Obj, value); }
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : KeysBase
        {
            // Reference: ISO 32000-1 Table 325 - Entries in an object reference dictionary

            [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "OBJR")]
            public const string Type = "/Type";

            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string Pg = "/Pg";

            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public const string Obj = "/Obj";

            public static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta _meta = null!;
        }

        internal override DictionaryMeta Meta => Keys.Meta;
    }
}
