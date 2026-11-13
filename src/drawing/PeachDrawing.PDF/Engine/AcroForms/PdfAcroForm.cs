using PeachDrawing.PDF.Engine.Internal;

namespace PeachDrawing.PDF.Engine.AcroForms;

/// <summary>
/// The document's interactive form dictionary ("/AcroForm", ISO 32000-1 §12.7.2). Lazily created
/// by <see cref="Advanced.PdfCatalog.AcroForm"/> only when interactive PDF forms output is
/// enabled - see that property's own doc comment.
/// </summary>
internal sealed class PdfAcroForm : PdfDictionary, IPdfDictionaryValue<PdfAcroForm>
{
    static PdfAcroForm IPdfDictionaryValue<PdfAcroForm>.Create(PdfDocument document) => new(document);
    static PdfAcroForm IPdfDictionaryValue<PdfAcroForm>.Wrap(PdfDictionary existing) => new(existing);

    private PdfAcroForm(PdfDictionary dict)
        : base(dict)
    { }

    public PdfAcroForm(PdfDocument document)
        : base(document)
    {
    }

    /// <summary>The document's top-level fields ("/Fields") - one entry per text/checkbox/radio-group/combo field; a radio group's own widget kids are not listed here.</summary>
    public PdfArray Fields => _fields ??= Elements.GetOrCreateArray<PdfArray>(Keys.Fields, ValueCreationFlags.CreateIndirect);
    PdfArray _fields = null!;

    /// <summary>Appends a top-level field.</summary>
    public void AddField(PdfAcroField field)
    {
        Owner.Internals.AddObject(field);
        Fields.Elements.Add(field.RequiredReference);
    }

    /// <summary>The default appearance string ("/DA") every field's own "/DA" (absent) falls back to.</summary>
    public string DefaultAppearance
    {
        get => Elements.GetString(Keys.DA);
        set { Elements.SetString(Keys.DA, value, PdfStringEncoding.WinAnsiEncoding); }
    }

    /// <summary>The default resource dictionary ("/DR") - the shared Helvetica font resource every field's "/DA" and generated appearance stream references.</summary>
    public PdfDictionary DefaultResources => _defaultResources ??= Elements.GetOrCreate<PdfDictionary>(Keys.DR, ValueCreationFlags.Create);
    PdfDictionary _defaultResources = null!;

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys
    {
        public const string Fields = "/Fields";

        public const string NeedAppearances = "/NeedAppearances";

        public const string SigFlags = "/SigFlags";

        public const string CO = "/CO";

        public const string DR = "/DR";

        public const string DA = "/DA";

        public const string Q = "/Q";


    }

}
