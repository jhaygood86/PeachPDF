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
    public sealed class PdfComposer : IDisposable
    {
        private readonly PdfDocument _document;
        private readonly PdfRenderContext _renderContext;
        private readonly List<(PdfCanvas Canvas, PdfGraphics Graphics)> _open = [];
        private bool _saved;

        /// <summary>
        /// Starts a document.
        /// </summary>
        /// <param name="renderContext">the fonts and images the pages draw with; its <see cref="PdfRenderContext.PixelsPerPoint"/> sets the unit canvases draw in</param>
        /// <param name="options">what to write besides the pages, or <see langword="null"/> for the defaults of <see cref="PdfComposerOptions"/></param>
        public PdfComposer(PdfRenderContext renderContext, PdfComposerOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(renderContext);

            options ??= new PdfComposerOptions();
            _renderContext = renderContext;
            _document = new PdfDocument();

            var settings = _document.Options;
            settings.CompressContentStreams = options.CompressContentStreams;
            // Each colour is written in the space it carries (RGB as /DeviceRGB, CMYK as /DeviceCMYK) rather than collapsed to RGB.
            settings.ColorMode = PdfColorMode.Undefined;
            settings.PdfAConformance = PdfAConformance.None;
            settings.PdfAConformanceEstablished = true;
            settings.PdfXConformance = PdfXConformance.None;
            settings.PdfXConformanceEstablished = true;
            settings.PdfVersion = options.Version;
            settings.PdfVersionEstablished = true;
            if (options.Version == PdfVersion.Pdf20)
                _document.Version = 20;

            if (options.Encryption is { } encryption)
            {
                var snapshot = encryption.Clone();
                settings.Encryption = snapshot;
                // AES-256 (revision 6) is a PDF 2.0 feature that PDF 1.7 readers also accept; a file that would carry an older header is raised.
                if (_document.Version < 17)
                    _document.Version = 17;
            }

            settings.EncryptionEstablished = true;

            var info = _document.Info;
            info.Producer = PdfComposerProductInfo.Generator;
            if (!string.IsNullOrEmpty(options.Title)) info.Title = options.Title;
            if (!string.IsNullOrEmpty(options.Author)) info.Author = options.Author;
            if (!string.IsNullOrEmpty(options.Subject)) info.Subject = options.Subject;
            if (!string.IsNullOrEmpty(options.Keywords)) info.Keywords = options.Keywords;
            if (!string.IsNullOrEmpty(options.Creator)) info.Creator = options.Creator;
            // Converted to local time first: the date is written with the machine's offset, which would mislabel any other instant's.
            if (options.CreationDate is { } created) info.CreationDate = created.LocalDateTime;
        }

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
