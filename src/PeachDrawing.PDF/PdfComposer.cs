#nullable enable

using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Drawing;
using System;
using System.Collections.Generic;
using System.IO;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// Writes a PDF file: add pages, draw on each one through a <see cref="PdfCanvas"/>, then <see cref="Save"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything is drawn through the <c>PeachDrawing.Core</c> drawing surface, so code written against
    /// <see cref="PeachDrawing.Core.Canvas"/> draws a PDF page the same way it draws a bitmap with PeachDrawing's raster canvas.
    /// Fonts are embedded and subset; only the glyphs a document draws end up in the file.
    /// </para>
    /// <para>
    /// A canvas is in layout units, which are points unless the <see cref="PdfRenderContext"/> was told otherwise
    /// (<see cref="PdfRenderContext.PixelsPerPoint"/>): its origin is the top-left corner of the page and y grows downward. A page is
    /// finished when the composer is saved, so a canvas can be kept and drawn on until then. A composer writes one file; make another
    /// for the next. It is not thread safe, and neither are its canvases.
    /// </para>
    /// </remarks>
    public sealed partial class PdfComposer : IDisposable
    {
        private readonly PdfDocument _document;
        private readonly PdfRenderContext _renderContext;
        private readonly List<(PdfCanvas Canvas, PdfGraphics Graphics)> _open = [];
        private bool _saved;
        private bool _configured;

        /// <summary>
        /// Starts a document.
        /// </summary>
        /// <param name="renderContext">the fonts and images the pages draw with; its <see cref="PdfRenderContext.PixelsPerPoint"/> sets the unit canvases draw in</param>
        /// <param name="options">what to write besides the pages, applied with <see cref="Configure"/>, or <see langword="null"/> for the defaults of <see cref="PdfComposerOptions"/> (applied when the first page is added)</param>
        public PdfComposer(PdfRenderContext renderContext, PdfComposerOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(renderContext);

            _renderContext = renderContext;
            _document = new PdfDocument();

            if (options is not null)
                Configure(options);
        }

        /// <summary>The engine's document, for the rest of the package and the temporary bridge to PeachPDF.</summary>
        internal PdfDocument Document => _document;

        /// <summary>The number of pages added so far.</summary>
        public int PageCount => _document.PageCount;

        /// <summary>
        /// Adds a page at the end of the document and returns the canvas to draw it with.
        /// </summary>
        /// <param name="widthPoints">the page's width in points (1/72 inch); 595 by 842 is A4</param>
        /// <param name="heightPoints">the page's height in points</param>
        /// <returns>a canvas for the page, valid until the composer is saved or disposed</returns>
        /// <exception cref="ArgumentOutOfRangeException">a dimension is not a positive number</exception>
        /// <exception cref="InvalidOperationException">the composer has already been saved</exception>
        public PdfCanvas AddPage(double widthPoints, double heightPoints)
        {
            if (!(widthPoints > 0) || double.IsInfinity(widthPoints))
                throw new ArgumentOutOfRangeException(nameof(widthPoints), widthPoints, "A page needs a positive, finite width.");
            if (!(heightPoints > 0) || double.IsInfinity(heightPoints))
                throw new ArgumentOutOfRangeException(nameof(heightPoints), heightPoints, "A page needs a positive, finite height.");
            if (_saved)
                throw new InvalidOperationException("The document has been saved; start a new composer for another file.");

            EnsureConfigured();

            var page = _document.AddPage();
            page.Width = widthPoints;
            page.Height = heightPoints;

            var graphics = PdfGraphics.FromPdfPage(page);
            var canvas = new PdfCanvas(_renderContext, graphics, _renderContext.PixelsPerPoint);
            _open.Add((canvas, graphics));
            return canvas;
        }

        /// <summary>
        /// Finishes the pages and writes the file. The composer cannot be added to afterwards.
        /// </summary>
        /// <param name="stream">where to write; it is not closed</param>
        /// <exception cref="InvalidOperationException">the document has no pages, or it has already been saved</exception>
        public void Save(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            if (_saved)
                throw new InvalidOperationException("The document has already been saved.");
            if (_document.PageCount == 0)
                throw new InvalidOperationException("A PDF file needs at least one page; call AddPage first.");

            EnsureConfigured();
            FinishPages();
            _document.Save(stream);
            _saved = true;
        }

        /// <summary>Releases the document. Canvases returned by <see cref="AddPage"/> are not usable afterwards.</summary>
        public void Dispose()
        {
            FinishPages();
            _document.Dispose();
        }

        private void FinishPages()
        {
            foreach (var (canvas, graphics) in _open)
            {
                canvas.Dispose();
                graphics.Dispose();
            }

            _open.Clear();
        }
    }
}
