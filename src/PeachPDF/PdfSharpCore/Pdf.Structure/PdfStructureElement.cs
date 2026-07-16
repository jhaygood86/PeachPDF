using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System.Collections.Generic;

namespace PeachPDF.PdfSharpCore.Pdf.Structure
{
    /// <summary>
    /// Represents a node in a tagged-PDF logical structure tree (a "/StructElem" dictionary).
    /// Ported from upstream PDFsharp's Pdf.Structure/PdfStructureElement.cs, adapted to this
    /// fork's PdfDictionary/DictionaryElements API.
    /// </summary>
    internal sealed class PdfStructureElement : PdfDictionary
    {
        public PdfStructureElement()
        {
            Elements.SetName(Keys.Type, "/StructElem");
        }

        public PdfStructureElement(PdfDocument document)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/StructElem");
        }

        internal PdfStructureElement(PdfDictionary dict)
            : base(dict)
        {
            Elements.SetName(Keys.Type, "/StructElem");
        }

        /// <summary>
        /// The structure type ("/S"), e.g. "/H1", "/P", "/Table" - written without validation,
        /// exactly as resolved by StructureTagMapper.
        /// </summary>
        public string StructureType
        {
            get { return Elements.GetName(Keys.S); }
            set { Elements.SetName(Keys.S, value); }
        }

        /// <summary>
        /// The parent structure element (or the document's PdfStructureTreeRoot's synthetic root
        /// /Document element).
        /// </summary>
        public PdfDictionary Parent
        {
            get { return Elements.GetDictionary(Keys.P); }
            set { Elements.SetReference(Keys.P, value); }
        }

        /// <summary>
        /// The page most of this element's marked content lives on (set once, on first content).
        /// </summary>
        public PdfPage Page
        {
            get { return (PdfPage)Elements.GetDictionary(Keys.Pg); }
            set { Elements.SetReference(Keys.Pg, value); }
        }

        /// <summary>
        /// Alternate description text ("/Alt"), e.g. an image's alt text.
        /// </summary>
        public string AlternateText
        {
            get { return Elements.GetString(Keys.Alt); }
            set { Elements.SetString(Keys.Alt, value); }
        }

        /// <summary>
        /// Appends a kid (a child PdfStructureElement, a bare MCID integer, a
        /// PdfMarkedContentReference, or a PdfObjectReference) to this element's "/K" array.
        /// </summary>
        public void AppendKid(PdfItem kid)
        {
            var array = Elements.GetArray(Keys.K);
            if (array == null)
            {
                var existing = Elements.GetObject(Keys.K);
                array = new PdfArray(Owner);
                if (existing != null)
                    array.Elements.Add(existing);
                Elements.SetObject(Keys.K, array);
            }

            array.Elements.Add(kid);
        }

        internal override void PrepareForSave()
        {
            SimplifyKidsArray();

            foreach (var k in GetKids(Elements))
                k.PrepareForSave();
        }

        /// <summary>
        /// Returns all PdfDictionaries directly held (not wrapped) in the "/K" key.
        /// </summary>
        internal static IEnumerable<PdfDictionary> GetKids(DictionaryElements elements)
        {
            // GetObject (not GetValue) deliberately - "/K" has no single fixed KeyInfo type (it can
            // hold a struct elem, an MCID integer, an /MCR, or an /OBJR), and GetValue's type
            // transformation logic throws for a KeyType.Various key with no declared type.
            var k = elements.GetObject(Keys.K);

            if (k is PdfArray array)
            {
                foreach (var item in array.Elements)
                {
                    var dict = GetPdfDictionary(item);
                    if (dict != null)
                        yield return dict;
                }
            }
            else
            {
                var dict = GetPdfDictionary(k);
                if (dict != null)
                    yield return dict;
            }
        }

        static PdfDictionary? GetPdfDictionary(PdfItem item)
        {
            if (item is PdfReference r)
                return r.Value as PdfDictionary;
            return item as PdfDictionary;
        }

        /// <summary>
        /// Removes the "/K" array and directly stores its first item if there is only one item -
        /// per spec, a single-kid "/K" need not be wrapped in an array.
        /// </summary>
        void SimplifyKidsArray()
        {
            if (Elements.GetArray(Keys.K) is { } k && k.Elements.Count == 1)
            {
                Elements[Keys.K] = k.Elements[0];
            }
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : KeysBase
        {
            // Reference: ISO 32000-1 Table 323 - Entries in a structure element dictionary

            [KeyInfo(KeyType.Name | KeyType.Optional, FixedValue = "StructElem")]
            public const string Type = "/Type";

            [KeyInfo(KeyType.Name | KeyType.Required)]
            public const string S = "/S";

            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public const string P = "/P";

            [KeyInfo(KeyType.ByteString | KeyType.Optional)]
            public const string ID = "/ID";

            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string Pg = "/Pg";

            [KeyInfo(KeyType.Various | KeyType.Optional)]
            public const string K = "/K";

            [KeyInfo(KeyType.TextString | KeyType.Optional)]
            public const string Lang = "/Lang";

            [KeyInfo(KeyType.TextString | KeyType.Optional)]
            public const string Alt = "/Alt";

            [KeyInfo(KeyType.TextString | KeyType.Optional)]
            public const string ActualText = "/ActualText";

            public static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta _meta = null!;
        }

        internal override DictionaryMeta Meta => Keys.Meta;
    }
}
