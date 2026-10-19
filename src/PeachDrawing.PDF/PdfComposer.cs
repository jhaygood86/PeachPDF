#nullable enable

using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
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
        private readonly List<PdfComposerPage> _pages = [];
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

        /// <summary>The engine's document, for the rest of the package.</summary>
        internal PdfDocument Document => _document;

        /// <summary>The number of pages added so far.</summary>
        public int PageCount => _pages.Count;

        /// <summary>The pages added so far, in order.</summary>
        public IReadOnlyList<PdfComposerPage> Pages => _pages;

        /// <summary>
        /// Adds a page at the end of the document.
        /// </summary>
        /// <param name="widthPoints">the page's width in points (1/72 inch); 595 by 842 is A4</param>
        /// <param name="heightPoints">the page's height in points</param>
        /// <returns>the page; its <see cref="PdfComposerPage.Canvas"/> draws it</returns>
        /// <exception cref="ArgumentOutOfRangeException">a dimension is negative or not a finite number</exception>
        public PdfComposerPage AddPage(double widthPoints, double heightPoints)
        {
            // Zero is allowed: a page with no area is an empty page, which a caller laying content out against a size it has not been
            // given yet (an HTML document with no page size) can end up with.
            if (!(widthPoints >= 0) || double.IsInfinity(widthPoints))
                throw new ArgumentOutOfRangeException(nameof(widthPoints), widthPoints, "A page needs a non-negative, finite width.");
            if (!(heightPoints >= 0) || double.IsInfinity(heightPoints))
                throw new ArgumentOutOfRangeException(nameof(heightPoints), heightPoints, "A page needs a non-negative, finite height.");

            EnsureConfigured();

            var page = _document.AddPage();
            page.Width = widthPoints;
            page.Height = heightPoints;

            var composerPage = new PdfComposerPage(this, page, _pages.Count, _renderContext);
            _pages.Add(composerPage);
            return composerPage;
        }

        /// <summary>
        /// Starts recording the document's logical structure, which makes the file a tagged PDF. A recorder replaces the structure tree
        /// any earlier one wrote.
        /// </summary>
        /// <param name="requireAlternateText">
        /// whether a figure with no alternative text gets an empty one (which marks it decorative) instead of none: required by the
        /// accessible PDF/A levels
        /// </param>
        /// <returns>the recorder to open structure elements with while pages are drawn</returns>
        public PdfTagRecorder CreateTagRecorder(bool requireAlternateText = false)
        {
            EnsureConfigured();
            return new PdfTagRecorder(_document, requireAlternateText);
        }

        /// <summary>
        /// Starts adding interactive form fields to the document's pages.
        /// </summary>
        /// <param name="renderContext">the context whose fonts and images the fields' appearances are drawn with</param>
        /// <param name="generatedNamePrefix">the prefix of the names generated for fields that have none</param>
        /// <returns>the builder to add fields with</returns>
        public PdfFormBuilder CreateFormBuilder(PdfRenderContext renderContext, string generatedNamePrefix = "Field")
        {
            ArgumentNullException.ThrowIfNull(renderContext);
            ArgumentNullException.ThrowIfNull(generatedNamePrefix);

            EnsureConfigured();
            return new PdfFormBuilder(_document, renderContext, generatedNamePrefix);
        }

        /// <summary>
        /// Names a place in the document, so links (<see cref="PdfComposerPage.AddDestinationLink"/>) and readers can jump to it. The reader
        /// scrolls to put the place at the top of the window, at the width of the page.
        /// </summary>
        /// <param name="name">the destination's name; naming one twice keeps the last</param>
        /// <param name="pageIndex">the zero-based index of the page the place is on</param>
        /// <param name="topPoints">how far below the top edge of that page the place is, in points</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of the document</exception>
        public void AddNamedDestination(string name, int pageIndex, double topPoints)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            if (pageIndex < 0 || pageIndex >= _pages.Count)
                throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, "There is no such page.");

            // /FitH's top is PDF default user space, which is bottom-up (0 at the bottom edge), so the offset down from the top is
            // flipped against the TARGET page's own height - a link can jump between differently sized pages.
            _document.AddNamedDestination(name, pageIndex + 1, PdfNamedDestinationParameters.CreateFitHorizontally(_pages[pageIndex].HeightPoints - topPoints));
        }

        /// <summary>
        /// Adds entries to the document outline (the bookmarks sidebar of a reader). Each entry nests under the nearest preceding entry
        /// of the same batch with a shallower level, or sits at the top if there is none - the conventional heading-outline shape.
        /// </summary>
        /// <param name="bookmarks">the entries, in document order</param>
        public void AddBookmarks(IEnumerable<PdfBookmark> bookmarks)
        {
            ArgumentNullException.ThrowIfNull(bookmarks);

            var stack = new Stack<(int Level, PdfOutline Outline)>();
            foreach (var bookmark in bookmarks)
            {
                while (stack.Count > 0 && stack.Peek().Level >= bookmark.Level)
                    stack.Pop();

                var outline = new PdfOutline
                {
                    Title = bookmark.Title,
                    Opened = bookmark.IsOpen,
                };

                if (bookmark.Uri is { } uri)
                {
                    outline.Url = uri;
                }
                else if (bookmark.PageIndex is { } pageIndex && pageIndex >= 0 && pageIndex < _pages.Count)
                {
                    outline.DestinationPage = _pages[pageIndex].Page;
                    // /FitH top fits the page width and positions vertically at the top - not /FitV's horizontal left. The offset is top
                    // down, but /FitH's own top is PDF default user space, bottom-up, so it is flipped against the destination page.
                    outline.PageDestinationType = PdfPageDestinationType.FitH;
                    outline.Top = outline.DestinationPage.Height - bookmark.TopPoints;
                }

                if (stack.Count > 0)
                    stack.Peek().Outline.Outlines.Add(outline);
                else
                    _document.Outlines.Add(outline);

                stack.Push((bookmark.Level, outline));
            }
        }

        /// <summary>
        /// Merges embedded raster images that encode to byte-identical PDF image objects, keeping one copy and pointing every other
        /// reference at it. Call it after all pages are drawn and before <see cref="Save"/>: the saved file then drops the duplicates.
        /// </summary>
        /// <remarks>
        /// Repeated references to the same image source within one canvas's work already share one embedded image. This is for what that
        /// does not cover: the same image drawn on separate pages by separate work, or two sources that happen to encode identically. It is
        /// not run automatically, since hashing every embedded image is not a cost most documents should pay on every save.
        /// </remarks>
        public void ConsolidateImages()
        {
            foreach (var page in _pages)
                page.Finish();

            _document.ConsolidateImages();
        }

        /// <summary>
        /// Finishes the pages and writes the file. Pages can be added and the document saved again: each save writes everything so far.
        /// </summary>
        /// <param name="stream">where to write; it is not closed</param>
        /// <exception cref="InvalidOperationException">the document has no pages</exception>
        public void Save(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);

            if (_document.PageCount == 0)
                throw new InvalidOperationException("A PDF file needs at least one page; call AddPage first.");

            EnsureConfigured();
            FinishPages();
            _document.Save(stream);
        }

        /// <summary>Releases the document. Canvases returned by <see cref="AddPage"/> are not usable afterwards.</summary>
        public void Dispose()
        {
            FinishPages();
            _document.Dispose();
        }

        private void FinishPages()
        {
            foreach (var page in _pages)
                page.Finish();
        }
    }
}
