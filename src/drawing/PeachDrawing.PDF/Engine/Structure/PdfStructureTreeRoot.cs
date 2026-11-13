namespace PeachDrawing.PDF.Engine.Structure;

/// <summary>
/// Represents the root of a tagged-PDF logical structure tree ("/StructTreeRoot").
/// Ported from upstream PDFsharp's Pdf.Structure/PdfStructureTreeRoot.cs, adapted to this
/// fork's PdfDictionary/DictionaryElements API.
/// </summary>
internal sealed class PdfStructureTreeRoot : PdfDictionary, IPdfDictionaryValue<PdfStructureTreeRoot>
{
    static PdfStructureTreeRoot IPdfDictionaryValue<PdfStructureTreeRoot>.Create(PdfDocument document) => new(document);
    static PdfStructureTreeRoot IPdfDictionaryValue<PdfStructureTreeRoot>.Wrap(PdfDictionary existing) => new(existing);

    private PdfStructureTreeRoot(PdfDictionary dict)
        : base(dict)
    { }

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
    public PdfNumberTreeNode ParentTree => _parentTree ??= Elements.GetOrCreate<PdfNumberTreeNode>(Keys.ParentTree, ValueCreationFlags.CreateIndirect);
    PdfNumberTreeNode _parentTree = null!;

    /// <summary>
    /// An integer greater than any key currently in the parent tree, used as the key for the
    /// next entry added to it. Shared by both page-keyed (/StructParents) and
    /// annotation-keyed (/StructParent) entries, so both coexist without key collisions.
    /// </summary>
    public int ParentTreeNextKey
    {
        get => Elements.GetInteger(Keys.ParentTreeNextKey);
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
    internal sealed class Keys
    {
        // Reference: ISO 32000-1 Table 322 - Entries in the structure tree root

        public const string Type = "/Type";

        public const string K = "/K";

        public const string IDTree = "/IDTree";

        public const string ParentTree = "/ParentTree";

        public const string ParentTreeNextKey = "/ParentTreeNextKey";

        public const string RoleMap = "/RoleMap";

        public const string ClassMap = "/ClassMap";


    }

}
