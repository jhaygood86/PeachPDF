// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Represents the name dictionary.
/// </summary>
internal sealed class PdfNameDictionary : PdfDictionary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfNameDictionary"/> class.
    /// </summary>
    public PdfNameDictionary(PdfDocument document)
        : base(document)
    { }

    internal PdfNameDictionary(PdfDictionary dictionary)
        : base(dictionary)
    {
        var dests = Elements.GetDictionary(Keys.Dests);
        if (dests is not null)
        {
            _dests = new PdfNameTreeNode(dests);
        }

        var embeddedFiles = Elements.GetDictionary(Keys.EmbeddedFiles);
        if (embeddedFiles is not null)
        {
            _embeddedFiles = new PdfNameTreeNode(embeddedFiles);
        }
    }

    /// <summary>
    /// Gets the embedded-files name tree ("/EmbeddedFiles": file name -> file specification), or
    /// <c>null</c> when nothing has been embedded.
    /// </summary>
    public PdfNameTreeNode? EmbeddedFilesTree => _embeddedFiles;

    /// <summary>
    /// Registers <paramref name="fileSpecification"/> under <paramref name="name"/> in the
    /// "/EmbeddedFiles" name tree, creating the tree on first use. Only reached when a file is
    /// embedded - untouched, no "/EmbeddedFiles" entry is added at all.
    /// </summary>
    /// <remarks>
    /// The tree is one flat leaf ("/Names" only, no "/Kids"), which is valid at any size and so
    /// never needs the "/Limits" bookkeeping. The value is a real <see cref="PdfReference"/> - not a
    /// literal carrying an object number - because the writer renumbers every object at save time.
    /// </remarks>
    internal void AddEmbeddedFile(string name, PdfFileSpecification fileSpecification)
    {
        if (_embeddedFiles is null)
        {
            _embeddedFiles = new PdfNameTreeNode();
            Owner.Internals.AddObject(_embeddedFiles);
            Elements.SetReference(Keys.EmbeddedFiles, _embeddedFiles.RequiredReference);
        }

        Owner.Internals.AddObject(fileSpecification);
        _embeddedFiles.AddName(name, fileSpecification.RequiredReference);
    }

    /// <summary>
    /// Gets the named destinations
    /// </summary>
    public PdfNameTreeNode? NameTree => _dests;

    internal void AddNamedDestination(string destinationName, int destinationPage, PdfNamedDestinationParameters parameters)
    {
        if (_dests is null)
        {
            _dests = new PdfNameTreeNode();
            Owner.Internals.AddObject(_dests);
            Elements.SetReference(Keys.Dests, _dests.RequiredReference);
        }

        // destIndex > Owner.PageCount can happen when rendering pages using PDFsharp directly.
        int destIndex = destinationPage;
        if (destIndex > Owner.PageCount)
            destIndex = Owner.PageCount;
        destIndex--;
        PdfPage dest = Owner.Pages[destIndex];

        PdfArray destination = new(Owner,
            new PdfLiteral("{0} 0 R {1}", dest.ObjectNumber, parameters));
        _dests.AddName(destinationName, destination);
    }

    PdfNameTreeNode? _dests;
    PdfNameTreeNode? _embeddedFiles;

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys
    {
        // ReSharper disable InconsistentNaming

        /// <summary>
        /// (Optional; PDF 1.2) A name tree mapping name strings to destinations (see “Named Destinations” on page 583).
        /// </summary>
        public const string Dests = "/Dests";

        //[PdfKey("1.3", PdfKeyType.NameTree | PdfKeyType.Optional)]
        //public const string AP = "/AP";

        //[PdfKey("1.3", PdfKeyType.NameTree | PdfKeyType.Optional)]
        //public const string JavaScript = "/JavaScript";

        //[PdfKey("1.3", PdfKeyType.NameTree | PdfKeyType.Optional)]
        //public const string Pages = "/Pages";

        //[PdfKey("1.3", PdfKeyType.NameTree | PdfKeyType.Optional)]
        //public const string Templates = "/Templates";

        //[PdfKey("1.3", PdfKeyType.NameTree | PdfKeyType.Optional)]
        //public const string IDS = "/IDS";

        //[PdfKey("1.3", PdfKeyType.NameTree | PdfKeyType.Optional)]
        //public const string URLS = "/URLS";

        /// <summary>
        /// (Optional; PDF 1.4) A name tree mapping name strings to file specifications for embedded file streams
        /// (see Section 3.10.3, “Embedded File Streams”).
        /// </summary>
        public const string EmbeddedFiles = "/EmbeddedFiles";

        //[PdfKey("1.4", PdfKeyType.NameTree | PdfKeyType.Optional)]
        //public const string AlternatePresentations = "/AlternatePresentations";

        //[PdfKey("1.5", PdfKeyType.NameTree | PdfKeyType.Optional)]
        //public const string Renditions = "/Renditions";

        // ReSharper restore InconsistentNaming
    }

}
