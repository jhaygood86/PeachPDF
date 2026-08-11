using PeachPDF.PdfSharpCore.Pdf.Internal;

namespace PeachPDF.PdfSharpCore.Pdf.AcroForms
{
    /// <summary>
    /// The document's interactive form dictionary ("/AcroForm", ISO 32000-1 §12.7.2). Lazily created
    /// by <see cref="Advanced.PdfCatalog.AcroForm"/> only when interactive PDF forms output is
    /// enabled - see that property's own doc comment.
    /// </summary>
    internal sealed class PdfAcroForm : PdfDictionary
    {
        public PdfAcroForm(PdfDocument document)
            : base(document)
        {
        }

        /// <summary>The document's top-level fields ("/Fields") - one entry per text/checkbox/radio-group/combo field; a radio group's own widget kids are not listed here.</summary>
        public PdfArray Fields
        {
            get { return _fields ??= (PdfArray)Elements.GetValue(Keys.Fields, VCF.CreateIndirect); }
        }
        PdfArray _fields = null!;

        /// <summary>Appends a top-level field.</summary>
        public void AddField(PdfAcroField field)
        {
            Owner.Internals.AddObject(field);
            Fields.Elements.Add(field.Reference);
        }

        /// <summary>The default appearance string ("/DA") every field's own "/DA" (absent) falls back to.</summary>
        public string DefaultAppearance
        {
            get { return Elements.GetString(Keys.DA); }
            set { Elements.SetString(Keys.DA, value, PdfStringEncoding.WinAnsiEncoding); }
        }

        /// <summary>The default resource dictionary ("/DR") - the shared Helvetica font resource every field's "/DA" and generated appearance stream references.</summary>
        public PdfDictionary DefaultResources
        {
            get { return _defaultResources ??= (PdfDictionary)Elements.GetValue(Keys.DR, VCF.Create); }
        }
        PdfDictionary _defaultResources = null!;

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : KeysBase
        {
            [KeyInfo(KeyType.Array | KeyType.Required | KeyType.MustBeIndirect)]
            public const string Fields = "/Fields";

            [KeyInfo(KeyType.Boolean | KeyType.Optional)]
            public const string NeedAppearances = "/NeedAppearances";

            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string SigFlags = "/SigFlags";

            [KeyInfo(KeyType.Array | KeyType.Optional)]
            public const string CO = "/CO";

            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string DR = "/DR";

            [KeyInfo(KeyType.String | KeyType.Optional)]
            public const string DA = "/DA";

            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string Q = "/Q";

            public static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta _meta = null!;
        }

        internal override DictionaryMeta Meta => Keys.Meta;
    }
}
