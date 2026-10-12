// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Numerics;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Core;
using PeachPDF.PDF.Rendering;
using PeachPDF.PDF;
using PeachPDF.PDF.Advanced;
using System;

#pragma warning disable 1587
// ReSharper disable UseNullPropagation
// ReSharper disable RedundantNameQualifier
// ReSharper disable UseNameofExpression

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// Represents a drawing surface for a fixed size page.
/// </summary>
internal sealed partial class PdfGraphics : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the PdfGraphics class for drawing on a PDF page.
    /// </summary>
    PdfGraphics(PdfPage page, PdfPageAppendMode options)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Owner is null)
            throw new ArgumentException("You cannot draw on a page that is not owned by a PdfDocument object.", "page");

        if (page.RenderContent is not null)
            throw new InvalidOperationException("An PdfGraphics object already exists for this page and must be disposed before a new one can be created.");

        if (page.Owner.IsReadOnly)
            throw new InvalidOperationException("Cannot create PdfGraphics for a page of a document that cannot be modified. Use PdfDocumentOpenMode.Modify.");

        _gsStack = new GraphicsStateStack(this);
        if (options == PdfPageAppendMode.Replace)
            page.Contents.Elements.Clear();

        page.RenderContent = options == PdfPageAppendMode.Prepend
            ? page.Contents.PrependContent()
            : page.Contents.AppendContent();
        _renderer = new PdfGraphicsRenderer(page, this, options);
        _pageSizePoints = new Size(page.Width, page.Height);
        _pageSize = new Size(page.Width, page.Height);

        Initialize();
    }

    PdfGraphics(Size size)
    {
        _gsStack = new GraphicsStateStack(this);
        _pageSizePoints = new Size(size.Width, size.Height);
        _pageSize = new Size(size.Width, size.Height);

        Initialize();
    }

    /// <summary>
    /// Initializes a new instance of the PdfGraphics class used for drawing on a form.
    /// </summary>
    PdfGraphics(PdfForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        _form = form;
        form.AssociateGraphics(this);

        _gsStack = new GraphicsStateStack(this);
        _drawGraphics = false;
        if (form.Owner is not null)
            _renderer = new PdfGraphicsRenderer(form, this);
        _pageSize = form.Size;
        Initialize();
    }

    /// <summary>
    /// Creates the measure context. This is a graphics context created only for querying measures of text.
    /// Drawing on a measure context has no effect.
    /// </summary>
    public static PdfGraphics CreateMeasureContext(Size size)
    {
        return new PdfGraphics(size);
    }

    /// <summary>
    /// Creates a new instance of the PdfGraphics class from a PeachPDF.PDF.PdfPage object.
    /// </summary>
    public static PdfGraphics FromPdfPage(PdfPage page)
    {
        return new PdfGraphics(page, PdfPageAppendMode.Append);
    }

    /// <summary>
    /// Creates a new instance of the PdfGraphics class from a PeachPDF.PDF.PdfPage object.
    /// </summary>
    public static PdfGraphics FromPdfPage(PdfPage page, PdfPageAppendMode options)
    {
        return new PdfGraphics(page, options);
    }

    /// <summary>
    /// Creates a new instance of the PdfGraphics class from a PeachPDF.PDF.Drawing.PdfForm object.
    /// </summary>
    public static PdfGraphics FromForm(PdfForm form)
    {
        if (form.Gfx is not null)
            return form.Gfx;

        return new PdfGraphics(form);
    }

    /// <summary>
    /// Internal setup.
    /// </summary>
    void Initialize()
    {
        _pageOrigin = new PaintPoint();

        double pageHeight = _pageSize.Height;
        PdfPage? targetPage = PdfPage;
        PaintPoint trimOffset = new PaintPoint();
        if (targetPage is not null && targetPage.TrimMargins.AreSet)
        {
            pageHeight += targetPage.TrimMargins.Top + targetPage.TrimMargins.Bottom;
            trimOffset = new PaintPoint(targetPage.TrimMargins.Left, targetPage.TrimMargins.Top);
        }

        Matrix3x2 matrix = Matrix3x2.Identity;
        if (trimOffset != new PaintPoint())
            matrix = PdfMatrix.Translation(trimOffset.X, -trimOffset.Y) * matrix;

        DefaultViewMatrix = matrix;
        _transform = Matrix3x2.Identity;
    }

    /// <summary>
    /// Releases all resources used by this object.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
    }

    void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_form is not null)
                _form.Finish();

            _drawGraphics = false;

            if (_renderer is not null)
            {
                _renderer.Close();
                _renderer = null!;
            }
        }
    }

    bool _disposed;

    /// <summary>
    /// Gets the current page origin. Setting the origin is not yet implemented.
    /// </summary>
    public PaintPoint PageOrigin
    {
        get => _pageOrigin;
        set
        {
            // Is there really anybody who needes to set the page origin?
            if (value != new PaintPoint())
                throw new NotImplementedException("PageOrigin cannot be modified in current implementation.");
        }
    }

    PaintPoint _pageOrigin;

    /// <summary>
    /// Gets the current size of the page.
    /// </summary>
    public Size PageSize
    {
        get => _pageSize;
    }

    Size _pageSize;

    Size _pageSizePoints;

    // ----- DrawString ---------------------------------------------------------------------------

    /// <summary>
    /// Draws the specified text string.
    /// </summary>
    /// <summary>
    /// When true, <see cref="DrawString(string, TypefaceFont, Brush, Rect, PdfStringFormat, double, FontPalette?, ShapeSettings?, string?)"/>
    /// shows text in PDF text rendering mode 3 (neither filled nor stroked): the glyphs are laid out, embedded and
    /// extractable exactly as visible text is, but paint nothing. Used to keep text selectable and searchable over content
    /// that was drawn as a bitmap. Text decorations and colour-glyph artwork are not painted either.
    /// </summary>
    public bool InvisibleText { get; set; }

    /// <summary>What draws the SVG documents of a font's glyphs (OpenType SVG) on this graphics, or <see langword="null"/> when nothing does.</summary>
    internal PeachDrawing.Core.ISvgGlyphPainter? PdfSvgGlyphPainter { get; set; }

    /// <summary>The render context pixel canvases are built from when a glyph is drawn through the raster backend, or <see langword="null"/> when there is none.</summary>
    internal PeachDrawing.Core.RenderContext? RasterContext { get; set; }

    /// <summary>
    /// Gets the current transformation matrix.
    /// The transformation matrix canot be set. Insted use Save/Restore or BeginContainer/EndContainer to
    /// save the state before Transform is called and later restore to the previous transform.
    /// </summary>
    public Matrix3x2 Transform => _transform;

    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// The transformation matrix from the PdfGraphics page space to the Graphics world space.
    /// (The name 'default view matrix' comes from Microsoft OS/2 Presentation Manager. I choose
    /// this name because I have no better one.)
    /// </summary>
    internal Matrix3x2 DefaultViewMatrix = Matrix3x2.Identity;

    /// <summary>
    /// Indicates whether to send drawing operations to _gfx or _dc.
    /// </summary>
    bool _drawGraphics;

    readonly PdfForm _form = null!;

    /// <summary>
    /// Interface to an (optional) renderer. Currently it is the PdfGraphicsRenderer, if defined.
    /// </summary>
    PdfGraphicsRenderer _renderer = null!;

    /// <summary>
    /// The transformation matrix from PdfGraphics world space to page unit space.
    /// </summary>
    Matrix3x2 _transform = Matrix3x2.Identity;

    /// <summary>
    /// The graphics state stack.
    /// </summary>
    readonly GraphicsStateStack _gsStack;

    /// <summary>
    /// Gets the PDF page that serves as drawing surface if PDF is rendered,
    /// or null, if no such object exists.
    /// </summary>
    public PdfPage? PdfPage
    {
        get
        {
            PdfGraphicsRenderer? renderer = _renderer as PdfGraphicsRenderer;
            return renderer is not null ? renderer._page : null;
        }
    }

    /// <summary>
    /// The owning <see cref="PdfDocument"/>, whether this <see cref="PdfGraphics"/> is bound to a real
    /// page (<see cref="PdfPage"/>) or to a Form XObject (<c>FromForm</c> - as a <c>CreateTile</c>
    /// tile is). Falling back to the form's own owner is what lets a tile be created recursively from
    /// inside another tile (e.g. nested CSS/SVG group <c>opacity</c>, or opacity inside a mask/pattern
    /// tile) - <see cref="PdfPage"/> alone is null for a form-bound instance.
    /// </summary>
    internal PdfDocument? Owner => PdfPage?.Owner ?? _form?.Owner;


}
