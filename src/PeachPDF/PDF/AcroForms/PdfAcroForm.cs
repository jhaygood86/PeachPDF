using PeachPDF.PDF.Internal;

namespace PeachPDF.PDF.AcroForms;

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
    public PdfArray Fields => _fields ??= (PdfArray)Elements.GetValue(Keys.Fields, ValueCreationFlags.CreateIndirect);
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
        get => Elements.GetString(Keys.DA);
        set { Elements.SetString(Keys.DA, value, PdfStringEncoding.WinAnsiEncoding); }
    }

    /// <summary>The default resource dictionary ("/DR") - the shared Helvetica font resource every field's "/DA" and generated appearance stream references.</summary>
    public PdfDictionary DefaultResources => _defaultResources ??= (PdfDictionary)Elements.GetValue(Keys.DR, ValueCreationFlags.Create);
    PdfDictionary _defaultResources = null!;

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys : KeysBase
    {
        [PdfKey(PdfKeyType.Array | PdfKeyType.Required | PdfKeyType.MustBeIndirect)]
        public const string Fields = "/Fields";

        [PdfKey(PdfKeyType.Boolean | PdfKeyType.Optional)]
        public const string NeedAppearances = "/NeedAppearances";

        [PdfKey(PdfKeyType.Integer | PdfKeyType.Optional)]
        public const string SigFlags = "/SigFlags";

        [PdfKey(PdfKeyType.Array | PdfKeyType.Optional)]
        public const string CO = "/CO";

        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string DR = "/DR";

        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string DA = "/DA";

        [PdfKey(PdfKeyType.Integer | PdfKeyType.Optional)]
        public const string Q = "/Q";

        public static PdfDictionaryMetadata Meta { get; } = CreateMeta(typeof(Keys));

    }

    internal override PdfDictionaryMetadata Meta => Keys.Meta;
}
