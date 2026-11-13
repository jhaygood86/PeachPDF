namespace PeachDrawing.PDF.Engine.Structure;

/// <summary>
/// Represents a mark information dictionary ("/MarkInfo") - signals to a reader that the
/// document conforms to Tagged PDF conventions. Ported from upstream PDFsharp's
/// Pdf.Structure/PdfMarkInformation.cs.
/// </summary>
internal sealed class PdfMarkInformation : PdfDictionary, IPdfDictionaryValue<PdfMarkInformation>
{
    static PdfMarkInformation IPdfDictionaryValue<PdfMarkInformation>.Create(PdfDocument document) => new(document);
    static PdfMarkInformation IPdfDictionaryValue<PdfMarkInformation>.Wrap(PdfDictionary existing) => new(existing);

    private PdfMarkInformation(PdfDictionary dict)
        : base(dict)
    { }

    public PdfMarkInformation()
    { }

    public PdfMarkInformation(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Whether the document conforms to Tagged PDF conventions. Must be true for a tagged
    /// document to actually be recognized as such by a reader.
    /// </summary>
    public bool Marked
    {
        get => Elements.GetBoolean(Keys.Marked);
        set { Elements.SetBoolean(Keys.Marked, value); }
    }

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys
    {
        // Reference: ISO 32000-1 Table 321 - Entries in the mark information dictionary

        /// <summary>(Optional)</summary>
        public const string Marked = "/Marked";

        /// <summary>(Optional)</summary>
        public const string UserProperties = "/UserProperties";

        /// <summary>(Optional)</summary>
        public const string Suspects = "/Suspects";


    }

}
