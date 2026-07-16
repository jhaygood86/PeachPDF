namespace PeachPDF.PdfSharpCore.Pdf.Structure
{
    /// <summary>
    /// Represents the root of a tagged-PDF logical structure tree ("/StructTreeRoot").
    /// Ported from upstream PDFsharp's Pdf.Structure/PdfStructureTreeRoot.cs, adapted to this
    /// fork's PdfDictionary/DictionaryElements API.
    /// </summary>
    internal sealed class PdfStructureTreeRoot : PdfDictionary
    {
        public PdfStructureTreeRoot()
        {
            Elements.SetName(Keys.Type, "/StructTreeRoot");
        }

        public PdfStructureTreeRoot(PdfDocument document)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/StructTreeRoot");
        }

        /// <summary>
        /// The root's sole child - a synthetic "/Document" structure element.
        /// </summary>
        public void SetRootKid(PdfStructureElement documentElement)
        {
            Elements.SetReference(Keys.K, documentElement);
        }

        /// <summary>
        /// The number tree mapping page /StructParents (and annotation /StructParent) keys to
        /// their owning structure elements.
        /// </summary>
        public PdfNumberTreeNode ParentTree
        {
            get { return _parentTree ??= (PdfNumberTreeNode)Elements.GetValue(Keys.ParentTree, VCF.CreateIndirect); }
        }
        PdfNumberTreeNode _parentTree = null!;

        /// <summary>
        /// An integer greater than any key currently in the parent tree, used as the key for the
        /// next entry added to it. Shared by both page-keyed (/StructParents) and
        /// annotation-keyed (/StructParent) entries, so both coexist without key collisions.
        /// </summary>
        public int ParentTreeNextKey
        {
            get { return Elements.GetInteger(Keys.ParentTreeNextKey); }
            set { Elements.SetInteger(Keys.ParentTreeNextKey, value); }
        }

        internal override void PrepareForSave()
        {
            foreach (var k in PdfStructureElement.GetKids(Elements))
                k.PrepareForSave();
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : KeysBase
        {
            // Reference: ISO 32000-1 Table 322 - Entries in the structure tree root

            [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "StructTreeRoot")]
            public const string Type = "/Type";

            [KeyInfo(KeyType.ArrayOrDictionary | KeyType.Optional)]
            public const string K = "/K";

            [KeyInfo(KeyType.Optional)]
            public const string IDTree = "/IDTree";

            [KeyInfo(KeyType.NumberTree | KeyType.Optional, typeof(PdfNumberTreeNode))]
            public const string ParentTree = "/ParentTree";

            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string ParentTreeNextKey = "/ParentTreeNextKey";

            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string RoleMap = "/RoleMap";

            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string ClassMap = "/ClassMap";

            public static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta _meta = null!;
        }

        internal override DictionaryMeta Meta => Keys.Meta;
    }
}
