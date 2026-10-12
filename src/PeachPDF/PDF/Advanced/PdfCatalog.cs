// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.AcroForms;
using PeachPDF.PDF.IO;
using PeachPDF.PDF.Structure;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents the catalog dictionary.
/// </summary>
internal sealed class PdfCatalog : PdfDictionary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfCatalog"/> class.
    /// </summary>
    public PdfCatalog(PdfDocument document)
        : base(document)
    {
        Elements.SetName(Keys.Type, "/Catalog");

        _version = "1.4";  // HACK in PdfCatalog
    }

    internal PdfCatalog(PdfDictionary dictionary)
        : base(dictionary)
    { }

    /// <summary>
    /// Gets the version of the PDF specification to which the document conforms.
    /// </summary>
    public string Version => _version;
    readonly string _version = "1.3";

    /// <summary>
    /// Gets the pages collection of this document.
    /// </summary>
    public PdfPages Pages
    {
        get
        {
            _pages ??= Elements.GetOrCreate<PdfPages>(Keys.Pages, ValueCreationFlags.CreateIndirect);
            return _pages;
        }
    }
    PdfPages _pages = null!;

    /// <summary>
    /// Implementation of PdfDocument.PageLayout.
    /// </summary>
    internal PdfPageLayout PageLayout
    {
        get => Elements.GetEnumFromName(Keys.PageLayout, PdfPageLayout.SinglePage);
        set { Elements.SetEnumAsName(Keys.PageLayout, value); }
    }

    /// <summary>
    /// Implementation of PdfDocument.PageMode.
    /// </summary>
    internal PdfPageMode PageMode
    {
        get => Elements.GetEnumFromName(Keys.PageMode, PdfPageMode.UseNone);
        set { Elements.SetEnumAsName(Keys.PageMode, value); }
    }

    /// <summary>
    /// Implementation of PdfDocument.ViewerPreferences.
    /// </summary>
    internal PdfViewerPreferences ViewerPreferences => _viewerPreferences ??= Elements.GetOrCreate<PdfViewerPreferences>(Keys.ViewerPreferences, ValueCreationFlags.CreateIndirect);
    PdfViewerPreferences _viewerPreferences = null!;

    /// <summary>
    /// Implementation of PdfDocument.Outlines.
    /// </summary>
    internal PdfOutlineCollection Outlines
    {
        get
        {
            _outline ??= Elements.GetOrCreate<PdfOutline>(Keys.Outlines, ValueCreationFlags.CreateIndirect);
            return _outline.Outlines;
        }
    }
    PdfOutline _outline = null!;

    /// <summary>
    /// Gets the name dictionary of this document.
    /// </summary>
    public PdfNameDictionary Names
    {
        get
        {
            if (_names is null)
            {
                var dict = Elements.GetDictionary(Keys.Names);
                if (dict is not null)
                    _names = new PdfNameDictionary(dict);
                else
                {
                    _names = new PdfNameDictionary(Owner);
                    Owner.Internals.AddObject(_names);
                    Elements.SetReference(Keys.Names, _names.RequiredReference);
                }
            }
            return _names;
        }
    }
    PdfNameDictionary? _names;

    /// <summary>
    /// Gets or sets the language identifier specifying the natural language for all text in the document.
    /// Sample values are 'en-US' for 'English United States' or 'de-DE' for 'deutsch Deutschland' (i.e. 'German Germany').
    /// </summary>
    public string Language
    {
        get => Elements.GetString(Keys.Lang);
        set
        {
            if (value is null)
                Elements.Remove(Keys.Lang);
            else
                Elements.SetString(Keys.Lang, value);
        }
    }

    /// <summary>
    /// Gets the structure tree root ("/StructTreeRoot"), lazily creating it on first access.
    /// Only ever touched when tagged PDF output is enabled - untouched, this stays null and no
    /// indirect object is allocated for it.
    /// </summary>
    internal PdfStructureTreeRoot StructureTreeRoot => _structureTreeRoot ??= Elements.GetOrCreate<PdfStructureTreeRoot>(Keys.StructTreeRoot, ValueCreationFlags.CreateIndirect);
    PdfStructureTreeRoot _structureTreeRoot = null!;

    /// <summary>
    /// Gets the interactive form dictionary ("/AcroForm"), lazily creating it on first access.
    /// Only ever touched when interactive PDF forms output is enabled - untouched, this stays
    /// null and no indirect object is allocated for it.
    /// </summary>
    internal PdfAcroForm AcroForm => _acroForm ??= Elements.GetOrCreate<PdfAcroForm>(Keys.AcroForm, ValueCreationFlags.CreateIndirect);
    PdfAcroForm _acroForm = null!;

    /// <summary>
    /// Gets the mark information dictionary ("/MarkInfo"), lazily creating it on first access.
    /// </summary>
    internal PdfMarkInformation MarkInfo => _markInfo ??= Elements.GetOrCreate<PdfMarkInformation>(Keys.MarkInfo, ValueCreationFlags.CreateIndirect);
    PdfMarkInformation _markInfo = null!;

    /// <summary>
    /// Sets the document's single PDF/A output intent (see <see cref="PdfOutputIntent"/>) - a
    /// one-element <c>/OutputIntents</c> array, per <see cref="PeachPDF.PdfGenerateConfig.PdfAConformance"/>.
    /// Only ever called when PDF/A conformance is requested - untouched, no <c>/OutputIntents</c>
    /// entry is added at all.
    /// </summary>
    internal void SetOutputIntent(PdfOutputIntent outputIntent)
    {
        // Registered as its own indirect object (matching SetMetadata's own pattern just below,
        // and common real-world PDF/A-writer practice) rather than embedded inline in the array -
        // the bare PDF spec permits either, but this is the safer, more broadly-compatible choice.
        Owner.Internals.AddObject(outputIntent);
        var array = new PdfArray(Owner);
        array.Elements.Add(outputIntent.RequiredReference);
        Elements[Keys.OutputIntents] = array;
    }

    /// <summary>
    /// Appends a file specification to the document-level <c>/AF</c> (Associated Files) array
    /// (ISO 32000-2 §14.13), creating the array on first call - the catalog-level index of every
    /// file embedded anywhere in the document (e.g. MathML source attached to a <c>Formula</c>
    /// structure element - see <see cref="PdfStructureElement.AppendAssociatedFile"/> for the
    /// structure-element-level counterpart). Only ever called when such a file is embedded -
    /// untouched, no <c>/AF</c> entry is added at all.
    /// </summary>
    internal void AddAssociatedFile(PdfFileSpecification fileSpecification)
    {
        Owner.Internals.AddObject(fileSpecification);

        var array = Elements.GetArray(Keys.AF);
        if (array is null)
        {
            array = new PdfArray(Owner);
            Elements[Keys.AF] = array;
        }
        array.Elements.Add(fileSpecification.RequiredReference);
    }

    /// <summary>
    /// Embeds a file document-wide: indexes its file specification in the catalog's <c>/AF</c> array
    /// (see <see cref="AddAssociatedFile"/>) <em>and</em> in the <c>/Names /EmbeddedFiles</c> name tree
    /// under <paramref name="name"/>. PDF/A-3 wants the former, and viewers (and the Factur-X
    /// specification, which asks for both) list attachments from the latter.
    /// </summary>
    internal void AddAttachment(string name, PdfFileSpecification fileSpecification)
    {
        AddAssociatedFile(fileSpecification);
        Names.AddEmbeddedFile(name, fileSpecification);
    }

    /// <summary>
    /// Sets the document's XMP metadata stream (<c>/Metadata</c>, see <see cref="PdfMetadataStream"/>) -
    /// only ever called when <see cref="PeachPDF.PdfGenerateConfig.EnableXmpMetadata"/> or
    /// <see cref="PeachPDF.PdfGenerateConfig.PdfAConformance"/> requests one. Untouched, no
    /// <c>/Metadata</c> entry is added at all.
    /// </summary>
    internal void SetMetadata(PdfMetadataStream metadata)
    {
        Owner.Internals.AddObject(metadata);
        Elements.SetReference(Keys.Metadata, metadata);
    }

    /// <summary>
    /// Dispatches PrepareForSave to the objects that need it.
    /// </summary>
    internal override void PrepareForSave()
    {
        _pages?.PrepareForSave();

        _structureTreeRoot?.PrepareForSave();
        _acroForm?.PrepareForSave();

        if (_outline is null || _outline.Outlines.Count <= 0) return;

        if (Elements[Keys.PageMode] is null)
            PageMode = PdfPageMode.UseOutlines;
        _outline.PrepareForSave();
    }

    internal override void WriteObject(PdfWriter writer)
    {
        if (_outline is not null && _outline.Outlines.Count > 0)
        {
            if (Elements[Keys.PageMode] is null)
                PageMode = PdfPageMode.UseOutlines;
        }
        base.WriteObject(writer);
    }

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys : KeysBase
    {
        /// <summary>
        /// (Required) The type of PDF object that this dictionary describes; 
        /// must be Catalog for the catalog dictionary.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required, FixedValue = "Catalog")]
        public const string Type = "/Type";

        /// <summary>
        /// (Optional; PDF 1.4) The version of the PDF specification to which the document
        /// conforms (for example, 1.4) if later than the version specified in the file's header.
        /// If the header specifies a later version, or if this entry is absent, the document 
        /// conforms to the version specified in the header. This entry enables a PDF producer 
        /// application to update the version using an incremental update.
        /// </summary>
        [PdfKey("1.4", PdfKeyType.Name | PdfKeyType.Optional)]
        public const string Version = "/Version";

        /// <summary>
        /// (Required; must be an indirect reference) The page tree node that is the root of 
        /// the document's page tree.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Required | PdfKeyType.MustBeIndirect, typeof(PdfPages))]
        public const string Pages = "/Pages";

        /// <summary>
        /// (Optional; PDF 1.3) A number tree defining the page labeling for the document. 
        /// The keys in this tree are page indices; the corresponding values are page label dictionaries.
        /// Each page index denotes the first page in a labeling range to which the specified page 
        /// label dictionary applies. The tree must include a value for pageindex 0.
        /// </summary>
        [PdfKey("1.3", PdfKeyType.NumberTree | PdfKeyType.Optional)]
        public const string PageLabels = "/PageLabels";

        [PdfKey("1.2", PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string Names = "/Names";

        [PdfKey("1.1", PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string Dests = "/Dests";

        [PdfKey("1.2", PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfViewerPreferences))]
        public const string ViewerPreferences = "/ViewerPreferences";

        [PdfKey(PdfKeyType.Name | PdfKeyType.Optional)]
        public const string PageLayout = "/PageLayout";

        [PdfKey(PdfKeyType.Name | PdfKeyType.Optional)]
        public const string PageMode = "/PageMode";

        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfOutline))]
        public const string Outlines = "/Outlines";

        [PdfKey("1.1", PdfKeyType.Array | PdfKeyType.Optional)]
        public const string Threads = "/Threads";

        [PdfKey("1.1", PdfKeyType.ArrayOrDictionary | PdfKeyType.Optional)]
        public const string OpenAction = "/OpenAction";

        [PdfKey("1.4", PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string AA = "/AA";

        [PdfKey("1.1", PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string URI = "/URI";

        /// <summary>
        /// (Optional; PDF 1.4; must be an indirect reference) A metadata stream 
        /// containing metadata  for the document.
        /// </summary>
        [PdfKey("1.4", PdfKeyType.Dictionary | PdfKeyType.Optional | PdfKeyType.MustBeIndirect)]
        public const string Metadata = "/Metadata";

        /// <summary>
        /// (Optional; PDF 1.3) The document's structure tree root dictionary.
        /// </summary>
        [PdfKey("1.3", PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfStructureTreeRoot))]
        public const string StructTreeRoot = "/StructTreeRoot";

        /// <summary>
        /// (Optional; must be an indirect reference) The document's interactive form (AcroForm)
        /// dictionary.
        /// </summary>
        [PdfKey("1.2", PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfAcroForm))]
        public const string AcroForm = "/AcroForm";

        /// <summary>
        /// (Optional; PDF 1.4) A mark information dictionary containing information
        /// about the document's usage of Tagged PDF conventions.
        /// </summary>
        [PdfKey("1.4", PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfMarkInformation))]
        public const string MarkInfo = "/MarkInfo";

        /// <summary>
        /// (Optional; PDF 1.4) A language identifier specifying the natural language for all 
        /// text in the document except where overridden by language specifications for structure 
        /// elements or marked content. If this entry is absent, the language is considered unknown.
        /// </summary>
        [PdfKey("1.4", PdfKeyType.String | PdfKeyType.Optional)]
        public const string Lang = "/Lang";

        /// <summary>
        /// (Optional; PDF 1.3) A Web Capture information dictionary containing state information
        /// used by the Acrobat Web Capture (AcroSpider) plugin extension.
        /// </summary>
        [PdfKey("1.3", PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string SpiderInfo = "/SpiderInfo";

        /// <summary>
        /// (Optional; PDF 1.4) An array of output intent dictionaries describing the color 
        /// characteristics of output devices on which the document might be rendered.
        /// </summary>
        [PdfKey("1.4", PdfKeyType.Array | PdfKeyType.Optional)]
        public const string OutputIntents = "/OutputIntents";

        /// <summary>
        /// (Optional; PDF 1.4) A page-piece dictionary associated with the document.
        /// </summary>
        [PdfKey("1.4", PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string PieceInfo = "/PieceInfo";

        /// <summary>
        /// (Optional; PDF 1.5; required if a document contains optional content) The document's 
        /// optional content properties dictionary.
        /// </summary>
        [PdfKey("1.5", PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string OCProperties = "/OCProperties";

        /// <summary>
        /// (Optional; PDF 1.5) A permissions dictionary that specifies user access permissions 
        /// for the document.
        /// </summary>
        [PdfKey("1.5", PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string Perms = "/Perms";

        /// <summary>
        /// (Optional; PDF 1.5) A dictionary containing attestations regarding the content of a
        /// PDF document, as it relates to the legality of digital signatures.
        /// </summary>
        [PdfKey("1.5", PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string Legal = "/Legal";

        /// <summary>
        /// (Optional; PDF 2.0) An array of file specification dictionaries denoting the associated
        /// files for the entire document (ISO 32000-2 §14.13, "Associated files").
        /// </summary>
        [PdfKey("2.0", PdfKeyType.Array | PdfKeyType.Optional)]
        public const string AF = "/AF";

        /// <summary>
        /// Gets the KeysMeta for these keys.
        /// </summary>
        public static PdfDictionaryMetadata Meta { get; } = CreateMeta(typeof(Keys));
    }

    /// <summary>
    /// Gets the KeysMeta of this dictionary type.
    /// </summary>
    internal override PdfDictionaryMetadata Meta => Keys.Meta;
}
