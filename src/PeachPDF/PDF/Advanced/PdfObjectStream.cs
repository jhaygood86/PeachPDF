// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents an object stream that contains compressed objects.
/// PDF 1.5.
/// </summary>
internal class PdfObjectStream : PdfDictionary
{
    // Reference: 3.4.6  Object Streams / Page 100

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfObjectStream"/> class.
    /// </summary>
    public PdfObjectStream(PdfDocument document)
        : base(document)
    {
    }

    /// <summary>
    /// Initializes a new instance from an existing dictionary. Used for object type transformation.
    /// </summary>

    /// <summary>
    /// Predefined keys common to all font dictionaries.
    /// </summary>
    internal class Keys : PdfStream.Keys
    {
        // Reference: TABLE 3.14  Additional entries specific to an object stream dictionary / Page 101

        /// <summary>
        /// (Required) The type of PDF object that this dictionary describes;
        /// must be ObjStmfor an object stream.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required, FixedValue = "ObjStm")]
        public const string Type = "/Type";

        /// <summary>
        /// (Required) The number of compressed objects in the stream.
        /// </summary>
        [PdfKey(PdfKeyType.Integer | PdfKeyType.Required)]
        public const string N = "/N";

        /// <summary>
        /// (Required) The byte offset (in the decoded stream) of the first
        /// compressed object.
        /// </summary>
        [PdfKey(PdfKeyType.Integer | PdfKeyType.Required)]
        public const string First = "/First";

        /// <summary>
        /// (Optional) A reference to an object stream, of which the current object
        /// stream is considered an extension. Both streams are considered part of
        /// a collection of object streams (see below). A given collection consists
        /// of a set of streams whose Extendslinks form a directed acyclic graph.
        /// </summary>
        [PdfKey(PdfKeyType.Stream | PdfKeyType.Optional)]
        public const string Extends = "/Extends";
    }
}
