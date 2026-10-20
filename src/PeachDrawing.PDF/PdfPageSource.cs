using System;
using System.Collections.Generic;
using System.IO;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// An existing PDF opened so its pages can be painted onto any <see cref="Canvas"/>: a <see cref="PdfCanvas"/> to stamp
    /// a page of it over or under a document being written, a raster canvas to render it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Painting re-draws the page's content rather than copying the file's objects, so it works the same on every canvas.
    /// On a <see cref="PdfCanvas"/> the text, paths, clips, colour and alpha come out as ordinary content of the new
    /// document, with fonts re-embedded and text keeping the text map of the original.
    /// </para>
    /// <para>
    /// What the importer cannot draw yet (JPEG with a mask, JPEG 2000, CCITT and JBIG2 images, mesh shadings, tiling patterns, soft masks, blend modes, fonts without an embedded TrueType or
    /// OpenType program, text render modes that stroke) is skipped, and <see cref="Paint"/> names each such feature in its
    /// result. Encrypted files are refused.
    /// </para>
    /// </remarks>
    public sealed class PdfPageSource
    {
        private readonly PdfSourceDocument _document;

        private PdfPageSource(PdfSourceDocument document)
        {
            _document = document;
        }

        /// <summary>Opens a PDF held in memory.</summary>
        /// <param name="pdf">the bytes of the PDF file</param>
        /// <returns>the opened file</returns>
        /// <exception cref="InvalidDataException">the data is not a readable PDF</exception>
        /// <exception cref="NotSupportedException">the PDF is encrypted</exception>
        public static PdfPageSource Open(byte[] pdf)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            return new PdfPageSource(PdfSourceDocument.Open(pdf));
        }

        /// <summary>Opens a PDF read from <paramref name="stream"/>, which is read to the end and not closed.</summary>
        /// <param name="stream">a stream positioned at the start of the PDF file</param>
        /// <returns>the opened file</returns>
        /// <exception cref="InvalidDataException">the data is not a readable PDF</exception>
        /// <exception cref="NotSupportedException">the PDF is encrypted</exception>
        public static PdfPageSource Open(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return Open(copy.ToArray());
        }

        /// <summary>The number of pages in the file.</summary>
        public int PageCount => _document.Pages.Count;

        /// <summary>The size of a page as a viewer shows it (after its rotation), in points.</summary>
        /// <param name="pageIndex">the zero-based page number</param>
        /// <returns>the width and height in points</returns>
        public (double Width, double Height) GetPageSize(int pageIndex) =>
            PdfPagePainter.GetPageSize(_document, Page(pageIndex));

        /// <summary>
        /// Paints a page onto <paramref name="canvas"/>, whose origin is the top-left of the area the page is to occupy and whose
        /// units are points. The page is clipped to its own box, so content never spills beyond it.
        /// </summary>
        /// <param name="pageIndex">the zero-based page number</param>
        /// <param name="canvas">the canvas to draw on</param>
        /// <returns>the features of the page that could not be drawn, each named once; empty when all of it was</returns>
        public IReadOnlyCollection<string> Paint(int pageIndex, Canvas canvas)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            return PdfPagePainter.Paint(_document, Page(pageIndex), canvas);
        }

        private PdfDictionary Page(int pageIndex)
        {
            if ((uint)pageIndex >= (uint)_document.Pages.Count)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "The file has " + _document.Pages.Count + " pages.");
            return _document.Pages[pageIndex];
        }
    }
}
