#nullable disable warnings

using System.Text;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Represent a file stream embedded in the PDF document
    /// </summary>
    internal class PdfFileSpecification : PdfDictionary
    {
        private readonly PdfDictionary embeddedFileDictionary;

        public PdfFileSpecification(PdfDocument document)
          : base(document)
        {
            this.embeddedFileDictionary = new PdfDictionary();

            Elements.SetName(Keys.Type, "/Filespec");
            Elements.SetObject(Keys.EF, embeddedFileDictionary);
        }

        public PdfFileSpecification(PdfDocument document, string fileName, PdfEmbeddedFile embeddedFile)
            : this(document)
        {
            this.FileName = fileName;
            this.EmbeddedFile = embeddedFile;
        }

        public string FileName
        {
            get { return Elements.GetString(Keys.F); }
            set { Elements.SetString(Keys.F, value); }
        }

        /// <summary>
        /// The file name as a Unicode text string ("/UF", PDF 1.7). ISO 19005-3 wants it alongside
        /// "/F", which stays the plain-ASCII fallback for older readers.
        /// </summary>
        public string UnicodeFileName
        {
            get { return Elements.GetString(Keys.UF); }
            set { Elements.SetString(Keys.UF, value, PdfStringEncoding.Unicode); }
        }

        /// <summary>
        /// A human-readable description of the file ("/Desc"). Written as a plain string when it is
        /// pure ASCII (the common case), and as a UTF-16BE text string otherwise.
        /// </summary>
        public string Description
        {
            get { return Elements.GetString(Keys.Desc); }
            set
            {
                if (Ascii.IsValid(value))
                    Elements.SetString(Keys.Desc, value);
                else
                    Elements.SetString(Keys.Desc, value, PdfStringEncoding.Unicode);
            }
        }

        public PdfEmbeddedFile EmbeddedFile
        {
            get
            {
                var reference = embeddedFileDictionary.Elements.GetReference(Keys.F);

                return reference?.Value as PdfEmbeddedFile;
            }
            set
            {
                if (value == null)
                {
                    embeddedFileDictionary.Elements.Remove(Keys.F);
                    embeddedFileDictionary.Elements.Remove(Keys.UF);
                }
                else
                {
                    if (!value.IsIndirect)
                        Owner._irefTable.Add(value);

                    // Both keys name the same stream: "/F" for legacy readers, "/UF" because a PDF/A-3
                    // validator (and the Factur-X specification's structure diagram) expects it too.
                    embeddedFileDictionary.Elements.SetReference(Keys.F, value);
                    embeddedFileDictionary.Elements.SetReference(Keys.UF, value);
                }
            }
        }

        /// <summary>
        /// The relationship between this file specification and the object it's associated with
        /// ("/AFRelationship", PDF 2.0 / ISO 32000-2 §14.13) - e.g. "/Supplement" for a &lt;math&gt;
        /// element's original MathML markup attached to its "Formula" structure element, or "/Source"
        /// for the LaTeX/TeX an equation was authored from.
        /// </summary>
        public string AssociatedFileRelationship
        {
            get { return Elements.GetName(Keys.AFRelationship); }
            set { Elements.SetName(Keys.AFRelationship, value); }
        }

        /// <summary>
        /// Predefined keys of this embedded file.
        /// </summary>
        internal class Keys : KeysBase
        {
            /// <summary>
            /// (Required if an EF or RF entry is present; recommended always) 
            /// The type of PDF object that this dictionary describes; must be Filespec 
            /// for a file specification dictionary (see implementation note 45 in Appendix H).
            /// </summary>
            [KeyInfo(KeyType.Name | KeyType.Optional, FixedValue = "Filespec")]
            public const string Type = "/Type";

            /// <summary>
            /// (Required if the DOS, Mac, and Unix entries are all absent; amended with the UF 
            /// entry for PDF 1.7) A file specification string of the form described in Section 
            /// 3.10.1, “File Specification Strings,” or (if the file system is URL) a uniform 
            /// resource locator, as described in Section 3.10.4, “URL Specifications.”
            /// 
            /// Note: It is recommended that the UF entry be used in addition to the F entry.
            /// The UF entry provides cross-platform and cross-language compatibility and the F 
            /// entry provides backwards compatibility
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string F = "/F";

            /// <summary>
            /// (Required if RF is present; PDF 1.3; amended to include the UF key in PDF 1.7) 
            /// A dictionary containing a subset of the keys F, UF, DOS, Mac, and Unix, 
            /// corresponding to the entries by those names in the file specification dictionary. 
            /// The value of each such key is an embedded file stream (see Section 3.10.3, 
            /// “Embedded File Streams”) containing the corresponding file. If this entry is 
            /// present, the Type entry is required and the file specification dictionary must 
            /// be indirectly referenced. (See implementation note 46in Appendix H.)
            /// 
            /// Note: It is recommended that the F and UF entries be used in place of the DOS, 
            /// Mac, or Unix entries.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string EF = "/EF";

            /// <summary>
            /// (Optional, but recommended alongside F; PDF 1.7) A Unicode text string that provides a
            /// file specification of the form described in the PDF specification - the cross-platform,
            /// cross-language counterpart of F.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Optional)]
            public const string UF = "/UF";

            /// <summary>
            /// (Optional; PDF 1.6) A text string describing the file.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Optional)]
            public const string Desc = "/Desc";

            /// <summary>
            /// (Optional; PDF 2.0) The relationship between this file specification's file and the
            /// object it's associated with - one of /Source, /Data, /Alternative, /Supplement,
            /// /EncryptedPayload, /FormData, /Schema, or /Unspecified (ISO 32000-2 Table 43).
            /// </summary>
            [KeyInfo(KeyType.Name | KeyType.Optional)]
            public const string AFRelationship = "/AFRelationship";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            internal static DictionaryMeta Meta
            {
                get { return meta ??= CreateMeta(typeof(Keys)); }
            }
            static DictionaryMeta meta = null!;
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