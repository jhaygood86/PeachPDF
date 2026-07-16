using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Annotations;
using PeachPDF.PdfSharpCore.Pdf.Structure;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Orchestrates tagged-PDF structure-tree/MCID/ParentTree bookkeeping, driven live from the
    /// box-tree paint walk (see <see cref="CssBox.PaintImp"/>) rather than an event-interception
    /// layer around raw drawing calls - PeachPDF already fully controls its own paint walk, unlike
    /// upstream PDFsharp's XGraphics-event-driven UAManager/StructureBuilder this reimplements the
    /// same underlying algorithm (AddMarkedContentToStructureElement/AddToParentTree) for.
    /// Only ever constructed when <see cref="PdfGenerateConfig.EnableTaggedPdf"/> is set.
    /// </summary>
    internal sealed class StructureTagBuilder
    {
        readonly PdfDocument _document;
        readonly PdfStructureTreeRoot _root;
        readonly PdfStructureElement _documentElement;
        readonly Dictionary<CssBox, PdfStructureElement> _elementsByBox = new();

        // A tagged <li> box needs TWO distinct structure elements of its own ("/LI" itself and its
        // "/LBody" - see OpenListItemBodyElement) - a second cache, keyed by the same CssBox, avoids
        // colliding with the box's "/LI" entry in _elementsByBox.
        readonly Dictionary<CssBox, PdfStructureElement> _lbodyElementsByBox = new();

        readonly Stack<PdfStructureElement> _parentStack = new();

        // Per-StructParents-key (i.e. per page) ordered list of parent-tree entries, index == MCID.
        readonly List<List<PdfItem>> _parentTreeEntriesByKey = new();

        PdfPage? _currentPage;
        int _currentPageStructParentsKey;
        int _nextMcidOnCurrentPage;

        public StructureTagBuilder(PdfDocument document)
        {
            _document = document;

            var catalog = document.Catalog;
            catalog.MarkInfo.Marked = true;

            _root = catalog.StructureTreeRoot;

            _documentElement = new PdfStructureElement(document) { StructureType = "/Document" };
            document.Internals.AddObject(_documentElement);
            _documentElement.Parent = _root;
            _root.SetRootKid(_documentElement);
        }

        /// <summary>
        /// Called once per page, right after the page is created, before it is painted.
        /// </summary>
        public void BeginPage(PdfPage page)
        {
            _currentPage = page;
            _nextMcidOnCurrentPage = 0;
            _currentPageStructParentsKey = _parentTreeEntriesByKey.Count;
            _parentTreeEntriesByKey.Add(new List<PdfItem>());
            page.StructParents = _currentPageStructParentsKey;
        }

        /// <summary>
        /// Opens a pure-grouping structure element for <paramref name="box"/>: creates/caches its
        /// <see cref="PdfStructureElement"/>, links it to the current parent, and pushes it as the
        /// parent for any nested Open* calls until disposed. No MCID, no marked content - matches
        /// upstream's GroupingItem, which never opens a marked-content sequence of its own.
        /// </summary>
        public IDisposable OpenGroupingElement(CssBox box, string structureType)
        {
            var element = GetOrCreateElement(_elementsByBox, box, structureType);
            _parentStack.Push(element);
            return new PopParentScope(_parentStack);
        }

        /// <summary>
        /// Opens the "/LBody" grouping element for a tagged &lt;li&gt;'s body content - see
        /// <see cref="CssBox.PaintListItem"/>. A dedicated cache/overload (rather than
        /// <see cref="OpenGroupingElement"/>) because the same <paramref name="box"/> (the &lt;li&gt;
        /// itself) already owns a distinct "/LI" element in <see cref="_elementsByBox"/>.
        /// </summary>
        public IDisposable OpenListItemBodyElement(CssBox box)
        {
            var element = GetOrCreateElement(_lbodyElementsByBox, box, "LBody");
            _parentStack.Push(element);
            return new PopParentScope(_parentStack);
        }

        /// <summary>
        /// Opens a content structure element for <paramref name="box"/>: creates/caches its
        /// <see cref="PdfStructureElement"/>, allocates an MCID, emits a BDC around the caller's own
        /// paint calls, and records the MCID (or, for content spanning a page break, a
        /// <see cref="PdfMarkedContentReference"/>) both on the element's own "/K" and in the
        /// document's /ParentTree. Does not push itself as a parent - content elements are always
        /// leaves in this engine (see StructureTagMapper for why real elements' own text is always
        /// carried by an anonymous child box, never the element's own box).
        /// </summary>
        public IDisposable OpenContentElement(RGraphics g, CssBox box, string structureType, string? altText = null)
        {
            var element = GetOrCreateElement(_elementsByBox, box, structureType);

            if (!string.IsNullOrEmpty(altText))
                element.AlternateText = altText;

            if (g.IsOffscreenTile)
                // Struct element still created above (keeps the tree shape well-formed), but no
                // MCID/BDC for tile-painted content in v1 - see RGraphics.IsOffscreenTile.
                return NullScope.Instance;

            var mcid = _nextMcidOnCurrentPage++;
            AddMarkedContentToStructureElement(element, mcid);

            // The BDC operator's tag operand must be a PDF name (always slash-prefixed) - use the
            // struct element's own already-prefixed /S value rather than the bare structureType
            // parameter, so both agree exactly (mirrors upstream, whose BDC line is built from
            // reading the struct element's own /S name back out, not from the caller's raw tag text).
            g.BeginMarkedContent(element.StructureType, mcid);
            return new EndMarkedContentScope(g);
        }

        /// <summary>
        /// Opens an artifact marked-content sequence around the caller's own paint calls - no
        /// struct element, not part of the logical structure tree (e.g. a decorative &lt;hr&gt;).
        /// </summary>
        public IDisposable OpenArtifact(RGraphics g)
        {
            if (g.IsOffscreenTile)
                return NullScope.Instance;

            g.BeginArtifact();
            return new EndMarkedContentScope(g);
        }

        /// <summary>
        /// Looks up the structure element created for <paramref name="box"/>, if any - used by
        /// <c>PdfGenerator.HandleLinks</c> to attach an "/OBJR" from a Link annotation back to its
        /// owning structure element.
        /// </summary>
        public PdfStructureElement? TryGetStructureElement(CssBox box)
        {
            return _elementsByBox.TryGetValue(box, out var element) ? element : null;
        }

        /// <summary>
        /// Completes the bidirectional PDF/UA link between a Link annotation and its owning
        /// "/Link" structure element - called from <c>PdfGenerator.HandleLinks</c> once per Link
        /// annotation it creates, after painting (and this builder's own <see cref="Finish"/>)
        /// have already run. Appends a <see cref="PdfObjectReference"/> ("/OBJR") to the struct
        /// element's "/K" (so a reader walking the structure tree can reach the annotation), and
        /// gives the annotation its own "/StructParent"-keyed <see cref="PdfNumberTreeNode"/>
        /// entry pointing back at the struct element (so a reader starting from the annotation -
        /// e.g. via the page's own annotation array - can reach the structure tree). Both entries
        /// draw from the same <see cref="PdfStructureTreeRoot.ParentTreeNextKey"/> counter
        /// <see cref="Finish"/> already advanced past every page-keyed MCID entry, so annotation
        /// keys never collide with page/MCID keys. No-op if <paramref name="box"/> was never
        /// tagged "/Link" (tagging disabled, or the box's classification resolved to something
        /// else, e.g. an author suppressed it with <c>-peachpdf-pdf-tag-type: none</c>).
        /// </summary>
        public void LinkAnnotationToStructureElement(CssBox box, PdfPage page, PdfAnnotation annotation)
        {
            var element = TryGetStructureElement(box);
            if (element == null)
                return;

            var objectReference = new PdfObjectReference(_document) { Page = page, Object = annotation };
            _document.Internals.AddObject(objectReference);
            element.AppendKid(objectReference);

            var structParentKey = _root.ParentTreeNextKey;
            _root.ParentTreeNextKey = structParentKey + 1;
            annotation.StructParent = structParentKey;

            var array = new PdfArray(_document);
            array.Elements.Add(element.Reference);
            _root.ParentTree.AddNumber(structParentKey, array);

            // PDF/UA requires tab order to follow structure order on any page that has an
            // annotation linked into the structure tree.
            page.Tabs = "S";
        }

        /// <summary>
        /// Finalizes the structure tree after the whole page-render loop: builds each page's
        /// /ParentTree /Nums entry and sets /ViewerPreferences/DisplayDocTitle when a title is
        /// present (required once a document is tagged). Per-annotation ("/OBJR") ParentTree
        /// entries (see PdfGenerator.HandleLinks) are appended after this call, from the same
        /// underlying /ParentTree/ParentTreeNextKey sequence - Finish() itself only needs to have
        /// already assigned all page-keyed entries so annotation keys allocate after them.
        /// </summary>
        public void Finish()
        {
            var parentTree = _root.ParentTree;
            for (var key = 0; key < _parentTreeEntriesByKey.Count; key++)
            {
                var array = new PdfArray(_document);
                foreach (var item in _parentTreeEntriesByKey[key])
                    array.Elements.Add(item);
                parentTree.AddNumber(key, array);
            }

            _root.ParentTreeNextKey = _parentTreeEntriesByKey.Count;

            if (!string.IsNullOrEmpty(_document.Info.Title))
                _document.Catalog.ViewerPreferences.DisplayDocTitle = true;
        }

        PdfStructureElement GetOrCreateElement(Dictionary<CssBox, PdfStructureElement> cache, CssBox box, string structureType)
        {
            if (cache.TryGetValue(box, out var existing))
                return existing;

            var parent = _parentStack.Count > 0 ? _parentStack.Peek() : _documentElement;

            var element = new PdfStructureElement(_document) { StructureType = "/" + structureType };
            _document.Internals.AddObject(element);
            element.Parent = parent;
            parent.AppendKid(element);

            cache[box] = element;
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
            entries.Add(element.Reference);
        }

        sealed class PopParentScope : IDisposable
        {
            readonly Stack<PdfStructureElement> _stack;
            public PopParentScope(Stack<PdfStructureElement> stack) => _stack = stack;
            public void Dispose() => _stack.Pop();
        }

        sealed class EndMarkedContentScope : IDisposable
        {
            readonly RGraphics _g;
            public EndMarkedContentScope(RGraphics g) => _g = g;
            public void Dispose() => _g.EndMarkedContent();
        }

        sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
