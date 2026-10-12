using PeachPDF.PDF.Annotations;
using PeachPDF.PDF.Internal;

namespace PeachPDF.PDF.AcroForms;

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
        get => Elements.GetName(Keys.FT);
        set { Elements.SetName(Keys.FT, value); }
    }

    /// <summary>Bit 1 (ISO 32000-1 Table 221) - the user may not change the field's value. Shared by every field type.</summary>
    internal const int ReadOnlyFlag = 1 << 0;

    /// <summary>Bit 2 (ISO 32000-1 Table 221) - the field shall have a value when the form is submitted. Shared by every field type.</summary>
    internal const int RequiredFlag = 1 << 1;

    /// <summary>Bit 3 (ISO 32000-1 Table 221) - the field shall not be exported when the form is submitted. Shared by every field type.</summary>
    internal const int NoExportFlag = 1 << 2;

    /// <summary>The field's partial name ("/T") - this field's <c>name</c> attribute, or a generated fallback.</summary>
    public string PartialFieldName
    {
        get => Elements.GetString(PdfAnnotation.Keys.T);
        set { Elements.SetString(PdfAnnotation.Keys.T, value, PdfStringEncoding.WinAnsiEncoding); }
    }

    /// <summary>The field flags ("/Ff") - see the FieldFlags constants on each concrete subclass. Distinct from the inherited annotation-level <see cref="PdfAnnotation.Flags"/> ("/F"), a different key entirely.</summary>
    public int FieldFlags
    {
        get => Elements.GetInteger(Keys.Ff);
        set { Elements.SetInteger(Keys.Ff, value); }
    }

    /// <summary>
    /// The field's alternate name ("/TU", ISO 32000-1 Table 220) - the human-readable description
    /// a reader shows as the field's tooltip, and the accessible name assistive technology reads
    /// in place of the machine-oriented <see cref="PartialFieldName"/>.
    /// </summary>
    public string AlternateFieldName
    {
        get => Elements.GetString(Keys.TU);
        set { Elements.SetString(Keys.TU, value, PdfStringEncoding.Unicode); }
    }

    /// <summary>The default appearance string ("/DA") - font/size/color operators for generating a text-like appearance.</summary>
    public string DefaultAppearance
    {
        get => Elements.GetString(Keys.DA);
        set { Elements.SetString(Keys.DA, value, PdfStringEncoding.WinAnsiEncoding); }
    }

    /// <summary>The appearance state ("/AS") selecting which subdictionary of "/AP" applies - e.g. "/Yes" or "/Off".</summary>
    public string AppearanceState
    {
        get => Elements.GetName(PdfAnnotation.Keys.AS);
        set { Elements.SetName(PdfAnnotation.Keys.AS, value); }
    }

    /// <summary>
    /// Predefined keys of this dictionary, extending <see cref="PdfAnnotation.Keys"/> with the
    /// field-dictionary entries a merged field/widget object also carries (ISO 32000-1 Table 220).
    /// </summary>
    internal new class Keys : PdfAnnotation.Keys
    {
        [PdfKey(PdfKeyType.Name | PdfKeyType.Optional)]
        public const string FT = "/FT";

        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string Parent = "/Parent";

        [PdfKey(PdfKeyType.Array | PdfKeyType.Optional)]
        public const string Kids = "/Kids";

        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string TU = "/TU";

        [PdfKey(PdfKeyType.Integer | PdfKeyType.Optional)]
        public const string Ff = "/Ff";

        [PdfKey(PdfKeyType.Various | PdfKeyType.Optional)]
        public const string V = "/V";

        [PdfKey(PdfKeyType.Various | PdfKeyType.Optional)]
        public const string DV = "/DV";

        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string DA = "/DA";

        [PdfKey(PdfKeyType.Integer | PdfKeyType.Optional)]
        public const string MaxLen = "/MaxLen";

        [PdfKey(PdfKeyType.Array | PdfKeyType.Optional)]
        public const string Opt = "/Opt";

        public static PdfDictionaryMetadata Meta { get; } = CreateMeta(typeof(Keys));

    }

    internal override PdfDictionaryMetadata Meta => Keys.Meta;
}
