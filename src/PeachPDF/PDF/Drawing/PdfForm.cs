// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using System.Numerics;
using PeachDrawing.Core;
using PeachPDF.PDF.Rendering;
using PeachPDF.PDF;
using PeachPDF.PDF.Advanced;
using PeachPDF.PDF.Filters;
using System;
using System.Diagnostics;

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// Represents a graphical object that can be used to render retained graphics on it.
/// In GDI+ it is represented by a Metafile, in WPF by a DrawingVisual, and in PDF by a Form XObjects.
/// </summary>
internal class PdfForm : PdfRasterImage, IContentStream
{
    internal enum FormState
    {
        /// <summary>
        /// The form is an imported PDF page.
        /// </summary>
        NotATemplate,

        /// <summary>
        /// The template is just created.
        /// </summary>
        Created,

        /// <summary>
        /// PdfGraphics.FromForm() was called.
        /// </summary>
        UnderConstruction,

        /// <summary>
        /// The form was drawn at least once and is 'frozen' now.
        /// </summary>
        Finished,
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfForm"/> class.
    /// </summary>
    protected PdfForm()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfForm"/> class that represents a page of a PDF document.
    /// </summary>
    /// <param name="document">The PDF document.</param>
    /// <param name="viewBox">The view box of the page.</param>
    public PdfForm(PdfDocument document, Rect viewBox)
    {
        if (viewBox.Width < 1 || viewBox.Height < 1)
            throw new ArgumentNullException("viewBox", "The size of the PdfForm is to small.");
        // I must tie the PdfForm to a document immediately, because otherwise I would have no place where
        // to store the resources.
        if (document is null)
            throw new ArgumentNullException("document", "An PdfForm template must be associated with a document at creation time.");

        _formState = FormState.Created;
        _document = document;
        _pdfForm = new PdfFormXObject(document, this);
        //_templateSize = size;
        _viewBox = viewBox;
        PdfRectangle rect = new PdfRectangle(viewBox);
        _pdfForm.Elements.SetRectangle(PdfFormXObject.Keys.BBox, rect);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfForm"/> class that represents a page of a PDF document.
    /// </summary>
    /// <param name="document">The PDF document.</param>
    /// <param name="size">The size of the page.</param>
    public PdfForm(PdfDocument document, Size size)
        : this(document, new Rect(0, 0, size.Width, size.Height))
    {
        ////if (size.width < 1 || size.height < 1)
        ////  throw new ArgumentNullException("size", "The size of the PdfForm is to small.");
        ////// I must tie the PdfForm to a document immediately, because otherwise I would have no place where
        ////// to store the resources.
        ////if (document == null)
        ////  throw new ArgumentNullException("document", "An PdfForm template must be associated with a document.");

        ////_formState = FormState.Created;
        ////_document = document;
        ////pdfForm = new PdfFormXObject(document, this);
        ////templateSize = size;
        ////PdfRectangle rect = new PdfRectangle(new PaintPoint(), size);
        ////pdfForm.Elements.SetRectangle(PdfFormXObject.Keys.BBox, rect);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfForm"/> class that represents a page of a PDF document.
    /// </summary>
    /// <param name="document">The PDF document.</param>
    /// <param name="width">The width of the page.</param>
    /// <param name="height">The height of the page</param>
    public PdfForm(PdfDocument document, double width, double height)
        : this(document, new Rect(0, 0, width, height))
    { }

    /// <summary>
    /// This function should be called when drawing the content of this form is finished.
    /// The PdfGraphics object used for drawing the content is disposed by this function and 
    /// cannot be used for any further drawing operations.
    /// PDFsharp automatically calls this function when this form was used the first time
    /// in a DrawImage function. 
    /// </summary>
    public void DrawingFinished()
    {
        if (_formState == FormState.Finished)
            return;

        if (_formState == FormState.NotATemplate)
            throw new InvalidOperationException("This object is an imported PDF page and you cannot finish drawing on it because you must not draw on it at all.");

        Finish();
    }

    /// <summary>
    /// Called from PdfGraphics constructor that creates an instance that work on this form.
    /// </summary>
    internal void AssociateGraphics(PdfGraphics gfx)
    {
        if (_formState == FormState.NotATemplate)
            throw new NotImplementedException("The current version of PDFsharp cannot draw on an imported page.");

        if (_formState == FormState.UnderConstruction)
            throw new InvalidOperationException("An PdfGraphics object already exists for this form.");

        if (_formState == FormState.Finished)
            throw new InvalidOperationException("After drawing a form it cannot be modified anymore.");

        Debug.Assert(_formState == FormState.Created);
        _formState = FormState.UnderConstruction;
        Gfx = gfx;
    }
    internal PdfGraphics Gfx = null!;

    /// <summary>
    /// Disposes this instance.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    /// <summary>
    /// Sets the form in the state FormState.Finished.
    /// </summary>
    internal virtual void Finish()
    {
        if (_formState == FormState.NotATemplate || _formState == FormState.Finished)
            return;

        if (!(_formState == FormState.Created || _formState == FormState.UnderConstruction))
        {
            throw new InvalidOperationException("Expected the form to be Created or UnderConstruction");
        }

        _formState = FormState.Finished;
        Gfx.Dispose();
        Gfx = null;

        if (PdfRenderer is not null)
        {
            //pdfForm.CreateStream(PdfEncoders.LatinOneByteEncoding.GetBytes(PdfRenderer.GetContent()));
            PdfRenderer.Close();

            if (_document.Options.CompressContentStreams)
            {
                _pdfForm.Stream.Value = StreamCompression.Encode(_document.Options, _pdfForm.Stream.Value, out var formFilter);
                _pdfForm.Elements["/Filter"] = new PdfName(formFilter);
            }
            int length = _pdfForm.Stream.Length;
            _pdfForm.Elements.SetInteger("/Length", length);
        }
    }

    /// <summary>
    /// Gets the owning document.
    /// </summary>
    internal PdfDocument Owner => _document;
    PdfDocument _document = null!;

    /// <summary>
    /// Gets the color model used in the underlying PDF document.
    /// </summary>
    internal PdfColorMode ColorMode
    {
        get
        {
            if (_document is null)
                return PdfColorMode.Undefined;
            return _document.Options.ColorMode;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this instance is a template.
    /// </summary>
    internal bool IsTemplate => _formState != FormState.NotATemplate;
    internal FormState _formState;

    /// <summary>
    /// Get the width in point of this image.
    /// </summary>
    public override double PointWidth
    {
        //get { return templateSize.width; }
        get => _viewBox.Width;
    }

    /// <summary>
    /// Get the height in point of this image.
    /// </summary>
    public override double PointHeight
    {
        //get { return templateSize.height; }
        get => _viewBox.Height;
    }

    /// <summary>
    /// Get the width of the page identified by the property PageNumber.
    /// </summary>
    public override int PixelWidth
    {
        //get { return (int)templateSize.width; }
        get => (int)_viewBox.Width;
    }

    /// <summary>
    /// Get the height of the page identified by the property PageNumber.
    /// </summary>
    public override int PixelHeight
    {
        //get { return (int)templateSize.height; }
        get => (int)_viewBox.Height;
    }

    /// <summary>
    /// Get the size of the page identified by the property PageNumber.
    /// </summary>
    public override Size Size
    {
        //get { return templateSize; }
        get => _viewBox.Size;
    }
    //Size templateSize;

    /// <summary>
    /// Gets the view box of the form.
    /// </summary>
    public Rect ViewBox => _viewBox;
    Rect _viewBox;

    /// <summary>
    /// Gets 72, the horizontal resolution by design of a form object.
    /// </summary>
    public override double HorizontalResolution => 72;

    /// <summary>
    /// Gets 72 always, the vertical resolution by design of a form object.
    /// </summary>
    public override double VerticalResolution => 72;

    /// <summary>
    /// Gets or sets the bounding box.
    /// </summary>
    public Rect BoundingBox
    {
        get => _boundingBox;
        set { _boundingBox = value; }  // TODO: pdfForm = null
    }
    Rect _boundingBox;

    /// <summary>
    /// Gets or sets the transformation matrix.
    /// </summary>
    public virtual Matrix3x2 Transform
    {
        get => _transform;
        set
        {
            if (_formState == FormState.Finished)
                throw new InvalidOperationException("After a PdfForm was once drawn it must not be modified.");
            _transform = value;
        }
    }
    internal Matrix3x2 _transform = Matrix3x2.Identity;

    internal PdfResources Resources
    {
        get
        {
            Debug.Assert(IsTemplate, "This function is for form templates only.");
            return FormXObject.Resources;
            //if (resources == null)
            //  resources = (PdfResources)pdfForm.Elements.GetValue(PdfFormXObject.Keys.Resources, ValueCreationFlags.Create); // ValueCreationFlags.CreateIndirect
            //return resources;
        }
    }
    //PdfResources resources;

    /// <summary>
    /// Implements the interface because the primary function is internal.
    /// </summary>
    PdfResources IContentStream.Resources
    {
        get => Resources;
    }

    /// <summary>
    /// Gets the resource name of the specified font within this form.
    /// </summary>
    internal string GetFontName(TypefaceFont font, out PdfFont pdfFont)
    {
        Debug.Assert(IsTemplate, "This function is for form templates only.");
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
    /// Tries to get the resource name of the specified font data within this form.
    /// Returns null if no such font exists.
    /// </summary>
    internal string TryGetFontName(string idName, out PdfFont pdfFont)
    {
        Debug.Assert(IsTemplate, "This function is for form templates only.");
        pdfFont = _document.FontTable.TryGetFont(idName);
        string name = null;
        if (pdfFont is not null)
            name = Resources.AddFont(pdfFont);
        return name;
    }

    /// <summary>
    /// Gets the resource name of the specified image within this form.
    /// </summary>
    internal string GetImageName(PdfRasterImage image, double width, double height)
    {
        Debug.Assert(IsTemplate, "This function is for form templates only.");
        global::PeachPDF.PDF.Advanced.PdfImage pdfImage = _document.ImageTable.GetImage(image, width, height);
        Debug.Assert(pdfImage is not null);
        string name = Resources.AddImage(pdfImage);
        return name;
    }

    /// <summary>
    /// Implements the interface because the primary function is internal.
    /// </summary>
    string IContentStream.GetImageName(PdfRasterImage image, double width, double height)
    {
        return GetImageName(image, width, height);
    }

    internal PdfFormXObject FormXObject
    {
        get
        {
            Debug.Assert(IsTemplate, "This function is for form templates only.");
            if (_pdfForm.Reference is null)
                _document._irefTable.Add(_pdfForm);
            return _pdfForm;
        }
    }

    /// <summary>
    /// Gets the resource name of the specified form within this form.
    /// </summary>
    internal string GetFormName(PdfForm form)
    {
        Debug.Assert(IsTemplate, "This function is for form templates only.");
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
        return GetFormName(form);
    }

    /// <summary>
    /// The PdfFormXObject gets invalid when PageNumber or transform changed. This is because a modification
    /// of an PdfForm must not change objects that are already been drawn.
    /// </summary>
    internal PdfFormXObject _pdfForm = null!;  // TODO: make private

    internal PdfGraphicsRenderer PdfRenderer = null!;

}
