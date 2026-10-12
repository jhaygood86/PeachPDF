// Derived from PDFsharp (MIT) - see LICENSE.md

using System;

namespace PeachPDF.PDF;

/// <summary>
/// Represents the PDF document information dictionary.
/// </summary>
internal sealed class PdfDocumentInformation : PdfDictionary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDocumentInformation"/> class.
    /// </summary>
    public PdfDocumentInformation(PdfDocument document)
        : base(document)
    { }

    internal PdfDocumentInformation(PdfDictionary dict)
        : base(dict)
    { }

    /// <summary>
    /// Gets or sets the document's title.
    /// </summary>
    public string Title
    {
        get => Elements.GetString(Keys.Title);
        set { Elements.SetString(Keys.Title, value, PdfStringEncoding.Unicode); }
    }

    /// <summary>
    /// Gets or sets the name of the person who created the document.
    /// </summary>
    public string Author
    {
        get => Elements.GetString(Keys.Author);
        set { Elements.SetString(Keys.Author, value, PdfStringEncoding.Unicode); }
    }

    /// <summary>
    /// Gets or sets the name of the subject of the document.
    /// </summary>
    public string Subject
    {
        get => Elements.GetString(Keys.Subject);
        set { Elements.SetString(Keys.Subject, value, PdfStringEncoding.Unicode); }
    }

    /// <summary>
    /// Gets or sets keywords associated with the document.
    /// </summary>
    public string Keywords
    {
        get => Elements.GetString(Keys.Keywords);
        set { Elements.SetString(Keys.Keywords, value, PdfStringEncoding.Unicode); }
    }

    /// <summary>
    /// Gets or sets the name of the application (for example, MigraDoc) that created the document.
    /// </summary>
    public string Creator
    {
        get => Elements.GetString(Keys.Creator);
        set { Elements.SetString(Keys.Creator, value, PdfStringEncoding.Unicode); }
    }

    /// <summary>
    /// Gets or sets the producer application (for example, PDFsharp).
    /// </summary>
    public string Producer
    {
        get => Elements.GetString(Keys.Producer);
        internal set { Elements.SetString(Keys.Producer, value, PdfStringEncoding.Unicode); }
    }

    /// <summary>
    /// Gets or sets the creation date of the document.
    /// Breaking Change: If the date is not set in a PDF file DateTime.MinValue is returned.
    /// </summary>
    public DateTime CreationDate
    {
        get => Elements.GetDateTime(Keys.CreationDate, DateTime.MinValue);
        set { Elements.SetDateTime(Keys.CreationDate, value); }
    }

    /// <summary>
    /// Gets or sets the modification date of the document.
    /// Breaking Change: If the date is not set in a PDF file DateTime.MinValue is returned.
    /// </summary>
    public DateTime ModificationDate
    {
        get => Elements.GetDateTime(Keys.ModDate, DateTime.MinValue);
        set { Elements.SetDateTime(Keys.ModDate, value); }
    }

    // TODO CustomProperties and meta data

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys : KeysBase
    {
        /// <summary>
        /// (Optional; PDF 1.1) The document's title.
        /// </summary>
        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string Title = "/Title";

        /// <summary>
        /// (Optional) The name of the person who created the document.
        /// </summary>
        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string Author = "/Author";

        /// <summary>
        /// (Optional; PDF 1.1) The subject of the document.
        /// </summary>
        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string Subject = "/Subject";

        /// <summary>
        /// (Optional; PDF 1.1) Keywords associated with the document.
        /// </summary>
        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string Keywords = "/Keywords";

        /// <summary>
        /// (Optional) If the document was converted to PDF from another format,
        /// the name of the application (for example, empira MigraDoc) that created the
        /// original document from which it was converted.
        /// </summary>
        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string Creator = "/Creator";

        /// <summary>
        /// (Optional) If the document was converted to PDF from another format,
        /// the name of the application (for example, this library) that converted it to PDF.
        /// </summary>
        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string Producer = "/Producer";

        /// <summary>
        /// (Optional) The date and time the document was created, in human-readable form.
        /// </summary>
        [PdfKey(PdfKeyType.Date | PdfKeyType.Optional)]
        public const string CreationDate = "/CreationDate";

        /// <summary>
        /// (Required if PieceInfo is present in the document catalog; otherwise optional; PDF 1.1)
        /// The date and time the document was most recently modified, in human-readable form.
        /// </summary>
        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string ModDate = "/ModDate";

        /// <summary>
        /// (Optional; PDF 1.3) A name object indicating whether the document has been modified 
        /// to include trapping information.
        /// </summary>
        [PdfKey("1.3", PdfKeyType.Name | PdfKeyType.Optional)]
        public const string Trapped = "/Trapped";

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
