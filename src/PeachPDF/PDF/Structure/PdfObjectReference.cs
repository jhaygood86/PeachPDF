using System.Diagnostics.CodeAnalysis;
using PeachPDF.PDF.Annotations;

namespace PeachPDF.PDF.Structure;

/// <summary>
/// Represents an object reference ("/OBJR") - links a structure element (e.g. "/Link") to a
/// non-marked-content PDF object such as an annotation. Ported from upstream PDFsharp's
/// Pdf.Structure/PdfObjectReference.cs.
/// </summary>
internal sealed class PdfObjectReference : PdfDictionary
{
    public PdfObjectReference()
    {
        Elements.SetName(Keys.Type, "/OBJR");
    }

    public PdfObjectReference(PdfDocument document)
        : base(document)
    {
        Elements.SetName(Keys.Type, "/OBJR");
    }

    /// <summary>
    /// The page the referenced object is rendered on.
    /// </summary>
    [DisallowNull]
    public PdfPage? Page
    {
        get => (PdfPage?)Elements.GetDictionary(Keys.Pg);
        set { Elements.SetReference(Keys.Pg, value); }
    }

    /// <summary>
    /// The referenced object (e.g. a link annotation).
    /// </summary>
    [DisallowNull]
    public PdfAnnotation? Object
    {
        get => (PdfAnnotation?)Elements.GetDictionary(Keys.Obj);
        set { Elements.SetReference(Keys.Obj, value); }
    }

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys : KeysBase
    {
        // Reference: ISO 32000-1 Table 325 - Entries in an object reference dictionary

        [PdfKey(PdfKeyType.Name | PdfKeyType.Required, FixedValue = "OBJR")]
        public const string Type = "/Type";

        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string Pg = "/Pg";

        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Required)]
        public const string Obj = "/Obj";

        public static PdfDictionaryMetadata Meta { get; } = CreateMeta(typeof(Keys));

    }

    internal override PdfDictionaryMetadata Meta => Keys.Meta;
}
