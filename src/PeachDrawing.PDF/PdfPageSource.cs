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
    /// What the importer cannot draw yet (fonts without an embedded TrueType, OpenType, CFF or
    /// Type 1 program) is skipped, and <see cref="Paint(int, Canvas)"/> names each such feature in its
    /// result. A file protected with AES and the empty user password is read; one that needs a password, or that uses RC4 (which .NET does not provide), is refused.
    /// </para>
    /// </remarks>
    public sealed class PdfPageSource
    {
        private readonly PdfSourceDocument _document;
        private readonly PdfSourceNavigation _navigation;
        private readonly PdfSourceForms _forms;
        private readonly PdfSourceAnnotations _annotations;

        private PdfPageSource(PdfSourceDocument document)
        {
            _document = document;
            _navigation = new PdfSourceNavigation(document);
            _forms = new PdfSourceForms(document);
            _annotations = new PdfSourceAnnotations(document);
        }

        /// <summary>Opens a PDF held in memory.</summary>
        /// <param name="pdf">the bytes of the PDF file</param>
        /// <returns>the opened file</returns>
        /// <exception cref="InvalidDataException">the data is not a readable PDF</exception>
        /// <exception cref="NotSupportedException">the PDF needs a password to open, uses RC4, or uses an unknown security handler</exception>
        public static PdfPageSource Open(byte[] pdf)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            return new PdfPageSource(PdfSourceDocument.Open(pdf));
        }

        /// <summary>Opens a PDF read from <paramref name="stream"/>, which is read to the end and not closed.</summary>
        /// <param name="stream">a stream positioned at the start of the PDF file</param>
        /// <returns>the opened file</returns>
        /// <exception cref="InvalidDataException">the data is not a readable PDF</exception>
        /// <exception cref="NotSupportedException">the PDF needs a password to open, uses RC4, or uses an unknown security handler</exception>
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

        /// <summary>The links of a page that open a web address or lead to another place in the file; other kinds of link are left out.</summary>
        /// <param name="pageIndex">the zero-based page number</param>
        /// <returns>the links, with their rectangles in the same terms <see cref="Paint(int, Canvas)"/> draws the page in</returns>
        public IReadOnlyList<PdfSourceLink> GetLinks(int pageIndex) => _navigation.Links(Page(pageIndex));

        /// <summary>The document outline (the bookmarks sidebar of a reader) flattened in reading order; an entry's <see cref="PdfBookmark.PageIndex"/> is a page of this file.</summary>
        /// <returns>the entries, each with its nesting level</returns>
        public IReadOnlyList<PdfBookmark> GetOutline() => _navigation.Outline();

        /// <summary>The destinations the file gives a name, which another document can link to.</summary>
        /// <returns>each name with the place it leads to</returns>
        public IReadOnlyDictionary<string, PdfSourceDestination> GetNamedDestinations() => _navigation.NamedDestinations();

        /// <summary>The title, author and the other document information of the file.</summary>
        /// <returns>the information the file gives</returns>
        public PdfSourceMetadata GetMetadata() => _navigation.Metadata();

        /// <summary>The files attached to the PDF.</summary>
        /// <returns>one entry per attachment whose data could be read</returns>
        public IReadOnlyList<PdfEmbeddedFile> GetAttachments() => _navigation.Attachments();

        /// <summary>
        /// Paints a page like <see cref="Paint(int, Canvas)"/> and then the appearance of its annotations, as a viewer shows them (a highlight, a
        /// stamp, a note, a drawing, a filled-in field).
        /// </summary>
        /// <param name="pageIndex">the zero-based page number</param>
        /// <param name="canvas">the canvas to draw on</param>
        /// <param name="annotations">which annotations to draw</param>
        /// <param name="tags">
        /// the tagging of a document being tagged (from <see cref="CreateTagSession"/>), or <see langword="null"/>. With one, the logical structure the file
        /// gave the page's content (headings, paragraphs, lists, tables, figures with their alternative text, languages and attributes) is recorded under
        /// the recorder's current parent in the order of the file's structure tree, and everything else that is drawn is marked as an artifact, so the page
        /// adds no untagged content to a tagged document. The recorder must have had <see cref="PdfTagRecorder.BeginPage"/> called for the page being drawn on.
        /// </param>
        /// <returns>the features of the page that could not be drawn, each named once; empty when all of it was</returns>
        public IReadOnlyCollection<string> Paint(int pageIndex, Canvas canvas, PdfAnnotationPaint annotations, PdfTagSession? tags = null)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            return PdfPagePainter.Paint(_document, Page(pageIndex), canvas, annotations, tags?.Binding);
        }

        /// <summary>
        /// The interactive form fields (text fields, check boxes, radio buttons and choice fields) with widgets on a page, ready to add to a
        /// page of a document with <see cref="PdfFormBuilder.AddField"/>. Each field is drawn by the appearance stream of its own widget, so it looks as
        /// it did in the file; a push button or signature field has no definition here and is drawn by
        /// <see cref="Paint(int, Canvas, PdfAnnotationPaint, PdfTagSession)"/> with <see cref="PdfAnnotationPaint.ExceptFormFields"/> instead.
        /// </summary>
        /// <param name="pageIndex">the zero-based page number</param>
        /// <param name="pixelsPerPoint">the layout units per point of the canvas the fields' appearances are drawn at</param>
        /// <param name="rename">
        /// maps a field's name in the file to the name it is to have, for when the same page is added more than once; it is called once per widget
        /// with the field's full name, so the buttons of a radio group, which share a name, must be given the same answer
        /// </param>
        /// <returns>the fields, with positions in points from the top-left corner of the page as it is shown</returns>
        public IReadOnlyList<PdfFormFieldDefinition> GetFormFields(int pageIndex, double pixelsPerPoint = 1, Func<string, string>? rename = null) =>
            _forms.Fields(Page(pageIndex), pixelsPerPoint, rename);

        /// <summary>The PDF/A and PDF/X levels the file claims for itself.</summary>
        /// <returns>the levels, <c>None</c> where it claims none</returns>
        public PdfSourceConformance GetConformance() => _navigation.Conformance();

        /// <summary>The file's output intent, the condition its colours are meant for, when it has one with a readable ICC profile.</summary>
        /// <returns>the output intent, or <see langword="null"/></returns>
        public PdfSourceOutputIntent? GetOutputIntent() => _navigation.OutputIntent();

        /// <summary>
        /// The annotations of a page that are neither links nor form fields (highlights, notes, stamps, drawings...) and have an appearance, ready to
        /// add to a page of a document with <see cref="PdfAnnotationBuilder.AddAnnotation"/>. Each is drawn by the appearance stream it had in the file.
        /// An annotation with no appearance stream, one marked hidden, and pop-up windows have no definition here.
        /// </summary>
        /// <param name="pageIndex">the zero-based page number</param>
        /// <param name="pixelsPerPoint">the layout units per point of the canvas the appearances are drawn at</param>
        /// <returns>the annotations, with positions in points from the top-left corner of the page as it is shown</returns>
        public IReadOnlyList<PdfAnnotationDefinition> GetAnnotations(int pageIndex, double pixelsPerPoint = 1) =>
            _annotations.Annotations(Page(pageIndex), pixelsPerPoint);

        /// <summary>
        /// Starts the tagging of one drawing of a page of this file onto a document being tagged. Pass the result to <see cref="Paint(int, Canvas, PdfAnnotationPaint, PdfTagSession)"/>,
        /// and to <see cref="TieLinkToStructure"/> for the links of the page; use a new one each time a page is drawn, so each drawing gets structure elements of its own.
        /// </summary>
        /// <param name="recorder">the recorder of the document</param>
        /// <returns>the tagging of one drawing</returns>
        public PdfTagSession CreateTagSession(PdfTagRecorder recorder)
        {
            ArgumentNullException.ThrowIfNull(recorder);
            return new PdfTagSession(new PdfTagBinding(_document, recorder));
        }

        /// <summary>
        /// Ties a link that was added to a document to the structure element the file gave the link, when that element was recorded by <paramref name="tags"/>,
        /// so a reader can reach one from the other. Call it after <see cref="PdfTagRecorder.Finish"/>. Does nothing for a link whose file had no structure for it.
        /// </summary>
        /// <param name="tags">the tagging of the drawing of the page the link is on</param>
        /// <param name="link">the link as read from the file</param>
        /// <param name="added">the link as added to the document's page</param>
        public void TieLinkToStructure(PdfTagSession tags, PdfSourceLink link, PdfLink added)
        {
            ArgumentNullException.ThrowIfNull(tags);
            ArgumentNullException.ThrowIfNull(link);
            ArgumentNullException.ThrowIfNull(added);

            if (link.Annotation is { } annotation && tags.Binding.KeyOfAnnotation(annotation) is { } key)
                tags.Binding.Recorder.LinkAnnotation(key, added);
        }

        private PdfDictionary Page(int pageIndex)
        {
            if ((uint)pageIndex >= (uint)_document.Pages.Count)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "The file has " + _document.Pages.Count + " pages.");
            return _document.Pages[pageIndex];
        }
    }
}
