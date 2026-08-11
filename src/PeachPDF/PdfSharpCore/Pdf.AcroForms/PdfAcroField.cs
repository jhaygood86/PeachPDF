using PeachPDF.PdfSharpCore.Pdf.Annotations;
using PeachPDF.PdfSharpCore.Pdf.Internal;

namespace PeachPDF.PdfSharpCore.Pdf.AcroForms
{
    /// <summary>
    /// Base class for an AcroForm field (ISO 32000-1 §12.7.3/12.7.4). New PeachPDF-original code
    /// (this fork's snapshot of upstream PDFsharp shipped no interactive-forms support at all - see
    /// docs/architecture.md - so there is no upstream file to port). Derives from
    /// <see cref="PdfAnnotation"/> because a terminal, single-widget field (text/checkbox/combo)
    /// merges the field dictionary and its one widget annotation into a single object per §12.7.3.1,
    /// exactly the shape <see cref="PdfLinkAnnotation"/> already uses for its own dictionary-is-the-
    /// annotation pattern. <see cref="PdfRadioButtonField"/> is the one non-terminal exception - see
    /// its own doc comment.
    /// </summary>
    internal abstract class PdfAcroField : PdfAnnotation
    {
        protected PdfAcroField(PdfDocument document)
            : base(document)
        {
        }

        /// <summary>The field type ("/FT") - "/Tx", "/Btn" or "/Ch".</summary>
        public string FieldType
        {
            get { return Elements.GetName(Keys.FT); }
            set { Elements.SetName(Keys.FT, value); }
        }

        /// <summary>The field's partial name ("/T") - this field's <c>name</c> attribute, or a generated fallback.</summary>
        public string PartialFieldName
        {
            get { return Elements.GetString(PdfAnnotation.Keys.T); }
            set { Elements.SetString(PdfAnnotation.Keys.T, value, PdfStringEncoding.WinAnsiEncoding); }
        }

        /// <summary>The field flags ("/Ff") - see the FieldFlags constants on each concrete subclass. Distinct from the inherited annotation-level <see cref="PdfAnnotation.Flags"/> ("/F"), a different key entirely.</summary>
        public int FieldFlags
        {
            get { return Elements.GetInteger(Keys.Ff); }
            set { Elements.SetInteger(Keys.Ff, value); }
        }

        /// <summary>The default appearance string ("/DA") - font/size/color operators for generating a text-like appearance.</summary>
        public string DefaultAppearance
        {
            get { return Elements.GetString(Keys.DA); }
            set { Elements.SetString(Keys.DA, value, PdfStringEncoding.WinAnsiEncoding); }
        }

        /// <summary>The appearance state ("/AS") selecting which subdictionary of "/AP" applies - e.g. "/Yes" or "/Off".</summary>
        public string AppearanceState
        {
            get { return Elements.GetName(PdfAnnotation.Keys.AS); }
            set { Elements.SetName(PdfAnnotation.Keys.AS, value); }
        }

        /// <summary>
        /// Predefined keys of this dictionary, extending <see cref="PdfAnnotation.Keys"/> with the
        /// field-dictionary entries a merged field/widget object also carries (ISO 32000-1 Table 220).
        /// </summary>
        internal new class Keys : PdfAnnotation.Keys
        {
            [KeyInfo(KeyType.Name | KeyType.Optional)]
            public const string FT = "/FT";

            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string Parent = "/Parent";

            [KeyInfo(KeyType.Array | KeyType.Optional)]
            public const string Kids = "/Kids";

            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string Ff = "/Ff";

            [KeyInfo(KeyType.Various | KeyType.Optional)]
            public const string V = "/V";

            [KeyInfo(KeyType.Various | KeyType.Optional)]
            public const string DV = "/DV";

            [KeyInfo(KeyType.String | KeyType.Optional)]
            public const string DA = "/DA";

            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string MaxLen = "/MaxLen";

            [KeyInfo(KeyType.Array | KeyType.Optional)]
            public const string Opt = "/Opt";

            public static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta _meta = null!;
        }

        internal override DictionaryMeta Meta => Keys.Meta;
    }
}
