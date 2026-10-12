// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using PeachPDF.PDF.Drawing;
using System;
using System.Diagnostics;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents a base class for dictionaries with a content stream.
/// Implement IContentStream for use with a content writer.
/// </summary>
internal abstract class PdfDictionaryWithContentStream : PdfDictionary, IContentStream
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDictionaryWithContentStream"/> class.
    /// </summary>
    public PdfDictionaryWithContentStream()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDictionaryWithContentStream"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    public PdfDictionaryWithContentStream(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Initializes a new instance from an existing dictionary. Used for object type transformation.
    /// </summary>
    protected PdfDictionaryWithContentStream(PdfDictionary dict)
        : base(dict)
    { }

    /// <summary>
    /// Gets the resources dictionary of this dictionary. If no such dictionary exists, it is created.
    /// </summary>
    internal PdfResources Resources
    {
        get
        {
            if (_resources is null)
                _resources = Elements.GetOrCreate<PdfResources>(Keys.Resources, ValueCreationFlags.Create);
            return _resources;
        }
    }
    PdfResources _resources = null!;

    /// <summary>
    /// Implements the interface because the primary function is internal.
    /// </summary>
    PdfResources IContentStream.Resources
    {
        get => Resources;
    }

    internal string GetFontName(TypefaceFont font, out PdfFont pdfFont)
    {
        pdfFont = _document.FontTable.GetFont(font);
        Debug.Assert(pdfFont is not null);
        string name = Resources.AddFont(pdfFont);
        return name;
    }

    string IContentStream.GetFontName(TypefaceFont font, out PdfFont pdfFont)
    {
        return GetFontName(font, out pdfFont);
    }

    /// <summary>
    /// Gets the resource name of the specified image within this dictionary.
    /// </summary>
    internal string GetImageName(PdfRasterImage image, double width, double height)
    {
        PdfImage pdfImage = _document.ImageTable.GetImage(image, width, height);
        Debug.Assert(pdfImage is not null);
        string name = Resources.AddImage(pdfImage);
        return name;
    }

    /// <summary>
    /// Implements the interface because the primary function is internal.
    /// </summary>
    string IContentStream.GetImageName(PdfRasterImage image, double width, double height)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the resource name of the specified form within this dictionary.
    /// </summary>
    internal string GetFormName(PdfForm form)
    {
        PdfFormXObject pdfForm = _document.FormTable.GetForm(form);
        Debug.Assert(pdfForm is not null);
        string name = Resources.AddForm(pdfForm);
        return name;
    }

    /// <summary>
    /// Implements the interface because the primary function is internal.
    /// </summary>
    string IContentStream.GetFormName(PdfForm form)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal class Keys : PdfDictionary.PdfStream.Keys
    {
        /// <summary>
        /// (Optional but strongly recommended; PDF 1.2) A dictionary specifying any
        /// resources (such as fonts and images) required by the form XObject.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional, typeof(PdfResources))]
        public const string Resources = "/Resources";
    }
}
