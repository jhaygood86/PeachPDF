// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.Internal;
using PeachPDF.PDF.IO;
using System;
using System.Diagnostics;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents a PDF trailer dictionary. Even though trailers are dictionaries they never have a cross
/// reference entry in PdfReferenceTable.
/// </summary>
internal class PdfTrailer : PdfDictionary  // Reference: 3.4.4  File Trailer / Page 96
{
    /// <summary>
    /// Initializes a new instance of PdfTrailer.
    /// </summary>
    public PdfTrailer(PdfDocument document)
        : base(document)
    {
        _document = document;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfTrailer"/> class from a <see cref="PdfCrossReferenceStream"/>.
    /// </summary>
    public PdfTrailer(PdfCrossReferenceStream trailer)
        : base(trailer._document)
    {
        _document = trailer._document;

        // /ID [<09F877EBF282E9408ED1882A9A21D9F2><2A4938E896006F499AC1C2EA7BFB08E4>]
        // /Info 7 0 R
        // /Root 1 0 R
        // /Size 10

        PdfReference? iref = trailer.Elements.GetReference(Keys.Info);
        if (iref is not null)
            Elements.SetReference(Keys.Info, iref);

        Elements.SetReference(Keys.Root, trailer.Elements.GetReference(Keys.Root)!);

        Elements.SetInteger(Keys.Size, trailer.Elements.GetInteger(Keys.Size));

        PdfArray? id = trailer.Elements.GetArray(Keys.ID);
        if (id is not null)
            Elements.SetValue(Keys.ID, id);
    }

    public int Size
    {
        get => Elements.GetInteger(Keys.Size);
        set { Elements.SetInteger(Keys.Size, value); }
    }

    // TODO: needed when linearized...
    //public int Prev
    //{
    //  get {return Elements.GetInteger(Keys.Prev);}
    //}

    public PdfDocumentInformation Info => Elements.GetOrCreate<PdfDocumentInformation>(Keys.Info, ValueCreationFlags.CreateIndirect);

    /// <summary>
    /// (Required; must be an indirect reference)
    /// The catalog dictionary for the PDF document contained in the file.
    /// </summary>
    public PdfCatalog Root => Elements.GetOrCreate<PdfCatalog>(PdfTrailer.Keys.Root, ValueCreationFlags.CreateIndirect);

    /// <summary>
    /// Gets the first or second document identifier.
    /// </summary>
    public string GetDocumentId(int index)
    {
        if (index < 0 || index > 1)
            throw new ArgumentOutOfRangeException("index", index, "Index must be 0 or 1.");

        PdfArray? array = Elements[Keys.ID] as PdfArray;
        if (array is null || array.Elements.Count < 2)
            return "";
        PdfItem item = array.Elements[index];
        if (item is PdfString)
            return ((PdfString)item).Value;
        return "";
    }

    /// <summary>
    /// Sets the first or second document identifier.
    /// </summary>
    public void SetDocumentId(int index, string value)
    {
        if (index < 0 || index > 1)
            throw new ArgumentOutOfRangeException("index", index, "Index must be 0 or 1.");

        PdfArray? array = Elements[Keys.ID] as PdfArray;
        if (array is null || array.Elements.Count < 2)
            array = CreateNewDocumentIds();
        array.Elements[index] = new PdfString(value, PdfStringFlags.HexLiteral);
    }

    /// <summary>
    /// Creates and sets two identical new document IDs.
    /// </summary>
    internal PdfArray CreateNewDocumentIds()
    {
        PdfArray array = new PdfArray(_document);
        byte[] documentId = Guid.NewGuid().ToByteArray();
        string id = PdfEncoders.LatinOneByteEncoding.GetString(documentId, 0, documentId.Length);
        array.Elements.Add(new PdfString(id, PdfStringFlags.HexLiteral));
        array.Elements.Add(new PdfString(id, PdfStringFlags.HexLiteral));
        Elements[Keys.ID] = array;
        return array;
    }

    internal override void WriteObject(PdfWriter writer)
    {
        // Delete /XRefStm entry, if any.
        _elements.Remove(Keys.XRefStm);

        base.WriteObject(writer);
    }

    /// <summary>
    /// Replace temporary irefs by their correct counterparts from the iref table.
    /// </summary>
    internal void Finish()
    {
        // /Root
        PdfReference? iref = _document._trailer.Elements[Keys.Root] as PdfReference;
        if (iref is not null && iref.Value is null)
        {
            iref = _document._irefTable[iref.ObjectId]!;
            Debug.Assert(iref.Value is not null);
            _document._trailer.Elements[Keys.Root] = iref;
        }

        // /Info
        iref = _document._trailer.Elements[PdfTrailer.Keys.Info] as PdfReference;
        if (iref is not null && iref.Value is null)
        {
            iref = _document._irefTable[iref.ObjectId]!;
            Debug.Assert(iref.Value is not null);
            _document._trailer.Elements[Keys.Info] = iref;
        }

        Elements.Remove(Keys.Prev);

        Debug.Assert(_document._irefTable.IsUnderConstruction == false);
        _document._irefTable.IsUnderConstruction = false;
    }

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal class Keys : KeysBase  // Reference: TABLE 3.13  Entries in the file trailer dictionary / Page 97
    {
        /// <summary>
        /// (Required; must not be an indirect reference) The total number of entries in the file's 
        /// cross-reference table, as defined by the combination of the original section and all
        /// update sections. Equivalently, this value is 1 greater than the highest object number
        /// used in the file.
        /// Note: Any object in a cross-reference section whose number is greater than this value is
        /// ignored and considered missing.
        /// </summary>
        [PdfKey(PdfKeyType.Integer | PdfKeyType.Required)]
        public const string Size = "/Size";

        /// <summary>
        /// (Present only if the file has more than one cross-reference section; must not be an indirect
        /// reference) The byte offset from the beginning of the file to the beginning of the previous 
        /// cross-reference section.
        /// </summary>
        [PdfKey(PdfKeyType.Integer | PdfKeyType.Optional)]
        public const string Prev = "/Prev";

        /// <summary>
        /// (Required; must be an indirect reference) The catalog dictionary for the PDF document
        /// contained in the file.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Required, typeof(PdfCatalog))]
        public const string Root = "/Root";

        /// <summary>
        /// (Required if document is encrypted; PDF 1.1) The document's encryption dictionary.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string Encrypt = "/Encrypt";

        /// <summary>
        /// (Optional; must be an indirect reference) The document's information dictionary.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfDocumentInformation))]
        public const string Info = "/Info";

        /// <summary>
        /// (Optional, but strongly recommended; PDF 1.1) An array of two strings constituting
        /// a file identifier for the file. Although this entry is optional, 
        /// its absence might prevent the file from functioning in some workflows
        /// that depend on files being uniquely identified.
        /// </summary>
        [PdfKey(PdfKeyType.Array | PdfKeyType.Optional)]
        public const string ID = "/ID";

        /// <summary>
        /// (Optional) The byte offset from the beginning of the file of a cross-reference stream.
        /// </summary>
        [PdfKey(PdfKeyType.Integer | PdfKeyType.Optional)]
        public const string XRefStm = "/XRefStm";

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
