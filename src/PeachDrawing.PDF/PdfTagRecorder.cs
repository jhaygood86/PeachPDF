#nullable enable

using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Annotations;
using PeachDrawing.PDF.Engine.Structure;
using System;
using System.Collections.Generic;
using System.Text;
using EngineEmbeddedFile = PeachDrawing.PDF.Engine.Advanced.PdfEmbeddedFile;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// Records the logical structure of a document - headings, paragraphs, lists, figures, links - while its pages are drawn, so the
    /// file is a tagged PDF: a reader can navigate it by structure, and assistive technology can read it in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Get one from <see cref="PdfComposer.CreateTagRecorder"/>, call <see cref="BeginPage"/> as each page is added, and wrap the drawing of
    /// each piece of content in the scope an <c>Open</c> method returns: <see cref="OpenContentElement"/> tags what is drawn inside it
    /// (a paragraph's text), <see cref="OpenGroupingElement"/> nests the elements opened inside it (a list around its items), and
    /// <see cref="OpenArtifact"/> marks decoration that is not part of the structure. Call <see cref="Finish"/> after the last page and
    /// before adding links that should be tied to structure elements.
    /// </para>
    /// <para>
    /// Elements are identified by a key of the caller's choosing - any object with value or reference equality will do - so the same
    /// piece of content drawn again (on the next page, when it spans a page break) lands in the same structure element.
    /// </para>
    /// </remarks>
    public sealed class PdfTagRecorder
    {
        readonly PdfDocument _document;
        readonly PdfStructureTreeRoot _root;
        readonly PdfStructureElement _documentElement;
        readonly Dictionary<object, PdfStructureElement> _elementsByBox = new();

        // A tagged <li> box needs TWO distinct structure elements of its own ("/LI" itself and its
        // "/LBody" - see OpenListItemBodyElement) - a second cache, keyed by the same node, avoids
        // colliding with the box's "/LI" entry in _elementsByBox.
        readonly Dictionary<object, PdfStructureElement> _lbodyElementsByBox = new();

        readonly Stack<PdfStructureElement> _parentStack = new();

        // Guards AttachMathMlSource against attaching the same <math> element's source more than once -
        // OpenContentElement's own element cache already returns the same PdfStructureElement for a
        // box painted again (e.g. a formula whose line spans a page break via CssRectMath's own
        // /MCR handling), but nothing else stops this method's own caller from running per-occurrence.
        readonly HashSet<object> _mathMlAttached = new();

        // Per-StructParents-key (i.e. per page) ordered list of parent-tree entries, index == MCID, for the pages drawn since the last Finish.
        // Keys come from the structure tree root's running counter, which annotation keys share, so recording can resume after a Finish.
        readonly Dictionary<int, List<PdfItem>> _parentTreeEntriesByKey = new();

        PdfPage? _currentPage;
        int _currentPageStructParentsKey;
        int _nextMcidOnCurrentPage;
        readonly bool _requireAltText;

        /// <summary>
        /// How many marked-content sequences (a content element's BDC, an artifact's BMC) this builder
        /// has opened and not yet closed.
        /// </summary>
        int _openSequences;

        internal PdfTagRecorder(PdfDocument document, bool requireAltText)
        {
            _document = document;
            _requireAltText = requireAltText;

            var catalog = document.Catalog;
            catalog.MarkInfo.Marked = true;

            _root = catalog.StructureTreeRoot;

            _documentElement = new PdfStructureElement(document) { StructureType = "/Document" };
            document.Internals.AddObject(_documentElement);
            _documentElement.Parent = _root;
            _root.SetRootKid(_documentElement);
        }

        /// <summary>
        /// Starts recording a page. Call it once per page, right after the page is added and before it is drawn.
        /// </summary>
        /// <param name="page">the page about to be drawn</param>
        public void BeginPage(PdfComposerPage page)
        {
            ArgumentNullException.ThrowIfNull(page);

            _currentPage = page.Page;
            _nextMcidOnCurrentPage = 0;
            _currentPageStructParentsKey = _root.ParentTreeNextKey;
            _root.ParentTreeNextKey = _currentPageStructParentsKey + 1;
            _parentTreeEntriesByKey[_currentPageStructParentsKey] = new List<PdfItem>();
            page.Page.StructParents = _currentPageStructParentsKey;
        }

        /// <summary>
        /// Opens a grouping structure element for <paramref name="key"/> (a list, a section, a table row): creates it the first time the
        /// key is seen, links it to the current parent, and makes it the parent of every element opened inside the returned scope. It
        /// carries no content of its own.
        /// </summary>
        /// <param name="key">identifies the element; the same key opens the same element again</param>
        /// <param name="structureType">the structure type, such as <c>L</c>, <c>Sect</c> or <c>Table</c> (without the slash)</param>
        /// <returns>a scope that closes the element when disposed</returns>
        public IDisposable OpenGroupingElement(object key, string structureType)
        {
            var element = GetOrCreateElement(_elementsByBox, key, structureType);
            _parentStack.Push(element);
            return new PopParentScope(_parentStack);
        }

        /// <summary>
        /// Re-opens the structure elements of ancestors that were never drawn as boxes of their own but still belong in the structure
        /// (outermost first), so an element opened inside nests under them. The elements are cached by key like any other, so siblings
        /// share one.
        /// </summary>
        /// <param name="ancestors">the ancestors, each with its key and structure type</param>
        /// <returns>a scope that closes them when disposed</returns>
        public IDisposable OpenPhantomAncestors(IReadOnlyList<(object Key, string StructureType)> ancestors)
        {
            ArgumentNullException.ThrowIfNull(ancestors);

            var opened = 0;

            foreach (var ancestor in ancestors)
            {
                _parentStack.Push(GetOrCreateElement(_elementsByBox, ancestor.Key, ancestor.StructureType));
                opened++;

                if (ancestor.StructureType == "LI")
                {
                    _parentStack.Push(GetOrCreateElement(_lbodyElementsByBox, ancestor.Key, "LBody"));
                    opened++;
                }
            }

            return opened == 0 ? NullScope.Instance : new PopParentsScope(_parentStack, opened);
        }

        /// <summary>
        /// Opens the body element of a list item, inside the list item element opened with the same key. It is a grouping element with
        /// its own cache, because the list item itself already owns a distinct element under that key.
        /// </summary>
        /// <param name="key">the key of the list item</param>
        /// <returns>a scope that closes the element when disposed</returns>
        public IDisposable OpenListItemBodyElement(object key)
        {
            var element = GetOrCreateElement(_lbodyElementsByBox, key, "LBody");
            _parentStack.Push(element);
            return new PopParentScope(_parentStack);
        }

        /// <summary>
        /// Opens a content structure element for <paramref name="key"/>: creates it the first time the key is seen, allocates a
        /// marked-content id, and marks everything drawn on <paramref name="canvas"/> until the returned scope is disposed as that
        /// element's content. Content that spans a page break is recorded on each page it lands on. A content element is a leaf: it does not
        /// become the parent of elements opened inside it.
        /// </summary>
        /// <param name="canvas">the canvas the content is drawn on</param>
        /// <param name="key">identifies the element; the same key opens the same element again</param>
        /// <param name="structureType">the structure type, such as <c>P</c>, <c>H1</c>, <c>Span</c> or <c>Figure</c> (without the slash)</param>
        /// <param name="altText">
        /// the alternative text of a figure or formula, or <see langword="null"/>. A <c>Figure</c> with none still gets an empty
        /// alternative text when the recorder was created to require it (the accessible PDF/A levels), which marks it decorative.
        /// </param>
        /// <returns>a scope that ends the marked content when disposed</returns>
        public IDisposable OpenContentElement(Canvas canvas, object key, string structureType, string? altText = null)
        {
            var g = canvas;
            var element = GetOrCreateElement(_elementsByBox, key, structureType);

            if (!string.IsNullOrEmpty(altText))
                element.AlternateText = altText;
            else if (_requireAltText && structureType == "Figure")
                // PDF/A-a (and PDF/UA) validation requires a /Figure element to carry an /Alt entry -
                // present-but-empty is valid (marks it decorative), absent is not. Ordinary tagged-PDF
                // output (EnableTaggedPdf alone, _requireAltText false) is unaffected.
                element.AlternateText = string.Empty;

            if (g.IsOffscreenTile)
                // Struct element still created above (keeps the tree shape well-formed), but no
                // MCID/BDC for tile-painted content in v1 - see Canvas.IsOffscreenTile.
                return NullScope.Instance;

            var mcid = _nextMcidOnCurrentPage++;
            AddMarkedContentToStructureElement(element, mcid);

            // The BDC operator's tag operand must be a PDF name (always slash-prefixed) - use the
            // struct element's own already-prefixed /S value rather than the bare structureType
            // parameter, so both agree exactly (mirrors upstream, whose BDC line is built from
            // reading the struct element's own /S name back out, not from the caller's raw tag text).
            g.BeginMarkedContent(element.StructureType, mcid);
            _openSequences++;
            return new EndMarkedContentScope(g, this);
        }

        /// <summary>
        /// Attaches MathML markup to a formula's structure element as a PDF 2.0 associated file (ISO 32000-2 §14.13,
        /// <c>/AFRelationship /Supplement</c>), so assistive technology can read the formula, and indexes it in the document-level
        /// <c>/AF</c> array. Does nothing if the key never opened a content element, or if its source was attached already (a formula that
        /// spans a page break is drawn more than once).
        /// </summary>
        /// <param name="key">the key of the formula's content element</param>
        /// <param name="mathMlSource">the MathML markup</param>
        public void AttachMathMlSource(object key, string mathMlSource)
        {
            if (!_mathMlAttached.Add(key))
                return;

            if (!_elementsByBox.TryGetValue(key, out var element))
                return;

            var bytes = Encoding.UTF8.GetBytes(mathMlSource);
            var embeddedFile = new EngineEmbeddedFile(_document, bytes) { MimeType = "application/mathml+xml" };
            var fileSpec = new PdfFileSpecification(_document, "formula.mml", embeddedFile)
            {
                AssociatedFileRelationship = "/Supplement",
            };

            element.AppendAssociatedFile(fileSpec);
            _document.Catalog.AddAssociatedFile(fileSpec);
        }

        /// <summary>
        /// Whether what is drawn now lands inside a marked-content sequence already - a content element's or an artifact's. Marked content
        /// cannot nest, so a caller about to open an artifact for drawing that may be reached from inside another element's content checks this
        /// first.
        /// </summary>
        public bool IsInMarkedContent => _openSequences > 0;

        /// <summary>
        /// Marks everything drawn on <paramref name="canvas"/> until the returned scope is disposed as an artifact: decoration that is not
        /// part of the document's logical structure (a rule, a background).
        /// </summary>
        /// <param name="canvas">the canvas the decoration is drawn on</param>
        /// <returns>a scope that ends the artifact when disposed</returns>
        public IDisposable OpenArtifact(Canvas canvas)
        {
            var g = canvas;
            if (g.IsOffscreenTile)
                return NullScope.Instance;

            g.BeginArtifact();
            _openSequences++;
            return new EndMarkedContentScope(g, this);
        }

        /// <summary>The structure element created for <paramref name="key"/>, if any.</summary>
        internal PdfStructureElement? TryGetStructureElement(object key)
        {
            return _elementsByBox.TryGetValue(key, out var element) ? element : null;
        }

        /// <summary>
        /// Ties a link to the structure element of the content it covers, so a reader can reach one from the other (PDF/UA): the element
        /// gets a reference to the link in its content, and the link points back at the element. Call it after <see cref="Finish"/>, once
        /// per link. Does nothing if the key never opened a <c>Link</c> element (tagging was suppressed for it).
        /// </summary>
        /// <param name="key">the key of the link's structure element</param>
        /// <param name="link">the link, as returned when it was added to its page</param>
        public void LinkAnnotation(object key, PdfLink link)
        {
            ArgumentNullException.ThrowIfNull(link);

            var element = TryGetStructureElement(key);
            if (element == null)
                return;

            var page = link.Page.Page;
            var annotation = link.Annotation;

            var objectReference = new PdfObjectReference(_document) { Page = page, Object = annotation };
            _document.Internals.AddObject(objectReference);
            element.AppendKid(objectReference);

            var structParentKey = _root.ParentTreeNextKey;
            _root.ParentTreeNextKey = structParentKey + 1;
            annotation.StructParent = structParentKey;

            var array = new PdfArray(_document);
            array.Elements.Add(element.RequiredReference);
            _root.ParentTree.AddNumber(structParentKey, array);

            // PDF/UA requires tab order to follow structure order on any page that has an
            // annotation linked into the structure tree.
            page.Tabs = "S";
        }

        /// <summary>
        /// Completes the structure tree after the last page: writes each page's entry in the parent tree and, when the document has a
        /// title, asks readers to show it (required once a document is tagged). Call it before <see cref="LinkAnnotation"/>.
        /// </summary>
        public void Finish()
        {
            var parentTree = _root.ParentTree;
            foreach (var (key, entries) in _parentTreeEntriesByKey)
            {
                var array = new PdfArray(_document);
                foreach (var item in entries)
                    array.Elements.Add(item);
                parentTree.AddNumber(key, array);
            }

            _parentTreeEntriesByKey.Clear();

            if (!string.IsNullOrEmpty(_document.Info.Title))
                _document.Catalog.ViewerPreferences.DisplayDocTitle = true;
        }

        PdfStructureElement GetOrCreateElement(Dictionary<object, PdfStructureElement> cache, object key, string structureType)
        {
            if (cache.TryGetValue(key, out var existing))
                return existing;

            var parent = _parentStack.Count > 0 ? _parentStack.Peek() : _documentElement;

            var element = new PdfStructureElement(_document) { StructureType = "/" + structureType };
            _document.Internals.AddObject(element);
            element.Parent = parent;
            parent.AppendKid(element);

            cache[key] = element;
            return element;
        }

        /// <summary>
        /// Adds the marked content with the given MCID on the current page to the given structure
        /// element, and records the corresponding /ParentTree entry - mirrors upstream's
        /// AddMarkedContentToStructureElement/AddToParentTree.
        /// </summary>
        void AddMarkedContentToStructureElement(PdfStructureElement element, int mcid)
        {
            if (element.Page == null)
            {
                // First content this element has ever received - claim the current page as its own.
                element.Page = _currentPage!;
                element.AppendKid(new PdfInteger(mcid));
            }
            else if (ReferenceEquals(element.Page, _currentPage))
            {
                element.AppendKid(new PdfInteger(mcid));
            }
            else
            {
                // Same logical box painted again on a later page (e.g. a paragraph whose lines span
                // a page break) - the element's own primary /Pg stays on its first page, so this
                // occurrence needs a full /MCR pointing at the actual page it's really on.
                var mcr = new PdfMarkedContentReference(_document) { Page = _currentPage!, Mcid = mcid };
                _document.Internals.AddObject(mcr);
                element.AppendKid(mcr);
            }

            var entries = _parentTreeEntriesByKey[_currentPageStructParentsKey];
            System.Diagnostics.Debug.Assert(mcid == entries.Count, "MCIDs must be allocated contiguously per page.");
            entries.Add(element.RequiredReference);
        }

        sealed class PopParentScope : IDisposable
        {
            readonly Stack<PdfStructureElement> _stack;
            public PopParentScope(Stack<PdfStructureElement> stack) => _stack = stack;
            public void Dispose() => _stack.Pop();
        }

        sealed class PopParentsScope(Stack<PdfStructureElement> stack, int count) : IDisposable
        {
            public void Dispose()
            {
                for (var i = 0; i < count; i++) stack.Pop();
            }
        }

        sealed class EndMarkedContentScope(Canvas g, PdfTagRecorder builder) : IDisposable
        {
            public void Dispose()
            {
                g.EndMarkedContent();
                builder._openSequences--;
            }
        }

        sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
