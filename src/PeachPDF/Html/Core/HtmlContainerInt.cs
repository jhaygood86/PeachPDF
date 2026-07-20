// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// Low level handling of Html Renderer logic.<br/>
    /// Allows html layout and rendering without association to actual control, those allowing to handle html rendering on any graphics object.<br/>
    /// Using this class will require the client to handle all propagation's of mouse/keyboard events, layout/paint calls, scrolling offset, 
    /// location/size/rectangle handling and UI refresh requests.<br/>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MaxSize and ActualSize:</b><br/>
    /// The max width and height of the rendered html.<br/>
    /// The max width will effect the html layout wrapping lines, resize images and tables where possible.<br/>
    /// The max height does NOT effect layout, but will not render outside it (clip).<br/>
    /// <see cref="ActualSize"/> can be exceed the max size by layout restrictions (unwrap-able line, set image size, etc.).<br/>
    /// Set zero for unlimited (width/height separately).<br/>
    /// </para>
    /// <para>
    /// <b>ScrollOffset:</b><br/>
    /// This will adjust the rendered html by the given offset so the content will be "scrolled".<br/>
    /// Element that is rendered at location (50,100) with offset of (0,200) will not be rendered
    /// at -100, therefore outside the client rectangle.
    /// </para>
    /// </remarks>
    internal sealed class HtmlContainerInt : IDisposable
    {
        #region Fields and Consts


        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        private double _marginTop;

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        private double _marginBottom;

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        private double _marginLeft;

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        private double _marginRight;

        /// <summary>
        /// Document-level named string storage for CSS GCPM string-set property.
        /// Stores named strings in document order to support first/last retrieval.
        /// </summary>
        private readonly List<NamedString> _namedStrings = new();

        private readonly List<NamedPageElement> _namedPageElements = new();

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        public HtmlContainerInt(RAdapter adapter)
        {
            ArgumentNullException.ThrowIfNull(adapter);

            Adapter = adapter;
            CssParser = new CssParser(adapter, this);
        }

        /// <summary>
        /// 
        /// </summary>
        internal RAdapter Adapter { get; }

        /// <summary>
        /// parser for CSS data
        /// </summary>
        internal CssParser CssParser { get; }

        /// <summary>
        /// the parsed stylesheet data used for handling the html
        /// </summary>
        public CssData? CssData { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating if anti-aliasing should be avoided for geometry like backgrounds and borders (default - false).
        /// </summary>
        public bool AvoidGeometryAntialias { get; set; }

        /// <summary>
        /// Gets the document-level named strings in document order.
        /// Used by CSS GCPM string-set and string() functions.
        /// </summary>
        internal IReadOnlyList<NamedString> NamedStrings => _namedStrings;

        /// <summary>
        /// Registers a named string at the document level in document order.
        /// Used by CSS GCPM string-set property.
        /// </summary>
        /// <param name="namedString">The named string to register</param>
        internal void RegisterNamedString(NamedString namedString)
        {
            _namedStrings.Add(namedString);
        }

        /// <summary>
        /// Clears all document-level named strings.
        /// </summary>
        internal void ClearNamedStrings()
        {
            _namedStrings.Clear();
        }

        internal IReadOnlyList<NamedPageElement> NamedPageElements => _namedPageElements;

        /// <summary>
        /// The most recently registered explicit <c>page</c> name in document/flow order (or
        /// <see cref="string.Empty"/> if none has been registered yet at this point in layout) - the
        /// "currently active" named page a box with no <c>page</c> value of its own would carry
        /// forward, per CSS2.1 §13.2. Used by <see cref="Dom.CssBox.PerformLayoutImp"/> to detect a
        /// forced page break when an element's own explicit <c>page</c> value differs from it.
        /// </summary>
        internal string ActivePageName => _namedPageElements.Count > 0 ? _namedPageElements[^1].Name : string.Empty;

        internal NamedPageElement RegisterNamedPageElement(string name, double y)
        {
            var element = new NamedPageElement(name, y);
            _namedPageElements.Add(element);
            return element;
        }

        internal void ClearNamedPageElements() => _namedPageElements.Clear();

        /// <summary>
        /// The scroll offset of the html.<br/>
        /// This will adjust the rendered html by the given offset so the content will be "scrolled".<br/>
        /// </summary>
        /// <example>
        /// Element that is rendered at location (50,100) with offset of (0,200) will not be rendered as it
        /// will be at -100 therefore outside the client rectangle.
        /// </example>
        public RPoint ScrollOffset { get; set; }

        /// <summary>
        /// The top-left most location of the rendered html.<br/>
        /// This will offset the top-left corner of the rendered html.
        /// </summary>
        public RPoint Location { get; set; }

        /// <summary>
        /// The max width and height of the rendered html.<br/>
        /// The max width will effect the html layout wrapping lines, resize images and tables where possible.<br/>
        /// The max height does NOT effect layout, but will not render outside it (clip).<br/>
        /// <see cref="ActualSize"/> can be exceed the max size by layout restrictions (unwrapable line, set image size, etc.).<br/>
        /// Set zero for unlimited (width\height separately).<br/>
        /// </summary>
        public RSize MaxSize { get; set; }

        /// <summary>
        /// The actual size of the rendered html (after layout)
        /// </summary>
        public RSize ActualSize { get; set; }

        /// <summary>
        /// Whether any box in the current document has <c>float: left/right</c>. Computed once per
        /// <see cref="PerformLayout"/> call and used to let float-intersection lookups
        /// (<see cref="Utils.DomUtils.GetFirstIntersectingFloatBox"/>) skip their tree walk entirely for
        /// the common case of a document with no floated content at all.
        /// </summary>
        internal bool HasFloatedBoxes { get; private set; }

        /// <summary>
        /// Whether any box in the current document is out-of-flow (floated, absolutely positioned, or
        /// fixed). Computed alongside <see cref="HasFloatedBoxes"/> and used by
        /// <see cref="CssBox.Paint"/> to decide whether Bounds-based page-visibility pruning is safe (an
        /// out-of-flow descendant's visual position can fall outside its "invisible" ancestor's own
        /// Bounds, so that pruning is only safe with none anywhere in the document).
        /// </summary>
        internal bool HasOutOfFlowBoxes { get; private set; }

        /// <summary>
        /// Whether any non-root box in the current document either is out-of-flow or establishes its own
        /// stacking context (see <see cref="Utils.DomUtils.IsStackingContextBox"/> - position+z-index,
        /// fixed/sticky, a flex item with z-index, opacity &lt; 1, or a non-identity transform). Computed
        /// alongside <see cref="HasFloatedBoxes"/>/<see cref="HasOutOfFlowBoxes"/> and used by
        /// <see cref="Utils.DomUtils.FlattenStackingContext"/> to skip searching for stacking-context
        /// participants to hoist past normal-flow wrapper boxes entirely when there's nothing to hoist.
        /// </summary>
        internal bool HasStackingHoistCandidates { get; private set; }

        public RSize PageSize { get; set; }

        /// <summary>
        /// Page size (width × height) in PDF points derived from the CSS @page { size: ... } rule.
        /// Null when no size rule is present. Stored in PDF points, not internal pixel units.
        /// </summary>
        public XSize? CssPageSize { get; set; }

        /// <summary>
        /// All @page rules parsed from the document's stylesheets, in cascade order.
        /// </summary>
        public IReadOnlyList<PageRule> PageRules { get; internal set; } = [];

        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        public double MarginTop
        {
            get => _marginTop;
            set
            {
                if (value > -1)
                    _marginTop = value;
            }
        }

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        public double MarginBottom
        {
            get => _marginBottom;
            set
            {
                if (value > -1)
                    _marginBottom = value;
            }
        }

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        public double MarginLeft
        {
            get => _marginLeft;
            set
            {
                if (value > -1)
                    _marginLeft = value;
            }
        }

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        public double MarginRight
        {
            get => _marginRight;
            set
            {
                if (value > -1)
                    _marginRight = value;
            }
        }

        /// <summary>
        /// the root css box of the parsed html
        /// </summary>
        internal CssBox? Root { get; private set; }

        /// <summary>
        /// Metadata extracted from the HTML head elements.
        /// </summary>
        internal HtmlDocumentMetadata? DocumentMetadata { get; private set; }

        /// <summary>
        /// The document's language, from the root <c>&lt;html lang="..."&gt;</c> attribute — <c>null</c>
        /// if the document declares none. Used by <c>hyphens: auto</c> (per the CSS Text spec, automatic
        /// hyphenation requires knowing the language; when unknown, a spec-compliant renderer doesn't
        /// hyphenate). <see cref="PdfGenerateConfig.DefaultLanguage"/> can supply an app-level fallback
        /// when a document declares none, applied by the caller (see <see cref="PdfGenerator"/>) — this
        /// property itself only ever reflects what the document actually declared.
        /// </summary>
        internal string? DocumentLanguage { get; set; }

        /// <summary>
        /// Orchestrates tagged-PDF structure-tree/MCID bookkeeping during painting. Set by
        /// <c>PdfGenerator</c> before the page-render loop only when
        /// <c>PdfGenerateConfig.EnableTaggedPdf</c> is set; null (the default) means tagging is
        /// disabled and <c>CssBox.PaintImp</c>'s tagging wrapper is a no-op.
        /// </summary>
        internal Handlers.StructureTagBuilder? StructureTagBuilder { get; set; }

        /// <summary>
        /// Init with optional document and stylesheet.
        /// </summary>
        /// <param name="htmlSource">the html to init with, init empty if not given</param>
        /// <param name="baseCssData">optional: the stylesheet to init with, init default if not given</param>
        public async Task SetHtml(string htmlSource, CssData? baseCssData = null)
        {
            Clear();
            if (string.IsNullOrEmpty(htmlSource)) return;

            CssData = baseCssData ?? await Adapter.GetDefaultCssData();

            DomParser parser = new(CssParser);
            (Root, CssData, DocumentMetadata) = await parser.GenerateCssTree(htmlSource, this, CssData);
        }

        /// <summary>
        /// Clear the content of the HTML container releasing any resources used to render previously existing content.
        /// </summary>
        public void Clear()
        {
            if (Root == null) return;

            Root.Dispose();
            Root = null;
            DocumentLanguage = null;
            ClearNamedStrings();
            ClearNamedPageElements();
        }

        /// <summary>
        /// Get all the links in the HTML with the element rectangle and href data.
        /// </summary>
        /// <returns>collection of all the links in the HTML</returns>
        public List<LinkElementData<RRect>> GetLinks()
        {
            var linkBoxes = new List<CssBox>();
            DomUtils.GetAllLinkBoxes(Root, linkBoxes);

            var linkElements = new List<LinkElementData<RRect>>();
            foreach (var box in linkBoxes)
            {
                linkElements.Add(new LinkElementData<RRect>(box.GetAttribute("id"), box.GetAttribute("href"), CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds), box));
            }

            var svgLinks = new List<(RRect Rect, string Href)>();
            DomUtils.GetAllSvgLinks(Root, svgLinks);

            foreach (var (rect, href) in svgLinks)
            {
                linkElements.Add(new LinkElementData<RRect>(string.Empty, href, rect));
            }

            return linkElements;
        }

        /// <summary>
        /// Get the rectangle of html element as calculated by html layout.<br/>
        /// Element if found by id (id attribute on the html element).<br/>
        /// Note: to get the screen rectangle you need to adjust by the hosting control.<br/>
        /// </summary>
        /// <param name="elementId">the id of the element to get its rectangle</param>
        /// <returns>the rectangle of the element or null if not found</returns>
        public RRect? GetElementRectangle(string elementId)
        {
            ArgChecker.AssertArgNotNullOrEmpty(elementId, "elementId");

            var box = DomUtils.GetBoxById(Root, elementId.ToLower());
            return box != null ? CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds) : (RRect?)null;
        }

        /// <summary>
        /// Measures the bounds of box and children, recursively.
        /// </summary>
        /// <param name="g">Device context to draw</param>
        public async ValueTask PerformLayout(RGraphics g)
        {
            ArgumentNullException.ThrowIfNull(g);

            ActualSize = RSize.Empty;
            if (Root is null) return;

            // includeStackingHoistCandidates: false here - IsStackingContextBox reads IsTransformed,
            // which lazily computes and permanently caches ActualTransformMatrix against this box's own
            // border-box size on first access. Before layout, every box's size is still unset/default,
            // so triggering that computation this early would cache a wrong transform matrix forever
            // (this box never gets asked for its transform again once the cache is populated) - see the
            // "actualTransformComputed" cache in CssBoxProperties.ActualTransformMatrix.
            (HasFloatedBoxes, HasOutOfFlowBoxes, _) = ComputeFlowFlags(Root, includeStackingHoistCandidates: false);

            // if width is not restricted we set it to large value to get the actual later
            Root.Size = new RSize(MaxSize.Width > 0 ? MaxSize.Width : PageSize.Width, 0);
            Root.Location = Location;
            await Root.PerformLayout(g);

            if (MaxSize.Width <= 0.1)
            {
                // in case the width is not restricted we need to double layout, first will find the width so second can layout by it (center alignment)
                Root.Size = new RSize((int)Math.Ceiling(ActualSize.Width), 0);
                ActualSize = RSize.Empty;
                await Root.PerformLayout(g);
            }

            // After layout, re-apply content to pseudo-elements now that named strings are set
            ReapplyPseudoElementContent(Root);

            // Recompute after layout in case pseudo-element reapplication added any out-of-flow boxes.
            // Every box's size is final now, so it's safe to also compute HasStackingHoistCandidates.
            (HasFloatedBoxes, HasOutOfFlowBoxes, HasStackingHoistCandidates) =
                ComputeFlowFlags(Root, includeStackingHoistCandidates: true);
        }

        /// <summary>
        /// Recursively checks whether any box in the tree is floated, out-of-flow, and/or (when
        /// <paramref name="includeStackingHoistCandidates"/> is true, and excluding the root itself,
        /// which trivially always establishes a stacking context) needs hoisting for stacking-context
        /// purposes, short-circuiting once every requested flag has been confirmed true.
        /// </summary>
        private static (bool HasFloated, bool HasOutOfFlow, bool HasStackingHoistCandidates) ComputeFlowFlags(
            CssBox box, bool includeStackingHoistCandidates)
        {
            var hasFloated = false;
            var hasOutOfFlow = false;
            var hasStackingHoistCandidates = false;
            ComputeFlowFlags(box, isRoot: true, includeStackingHoistCandidates,
                ref hasFloated, ref hasOutOfFlow, ref hasStackingHoistCandidates);
            return (hasFloated, hasOutOfFlow, hasStackingHoistCandidates);
        }

        private static void ComputeFlowFlags(CssBox box, bool isRoot, bool includeStackingHoistCandidates,
            ref bool hasFloated, ref bool hasOutOfFlow, ref bool hasStackingHoistCandidates)
        {
            if (box.IsFloated) hasFloated = true;
            if (box.IsOutOfFlow) hasOutOfFlow = true;
            if (includeStackingHoistCandidates && !isRoot && DomUtils.NeedsStackingHoist(box))
                hasStackingHoistCandidates = true;

            foreach (var childBox in box.Boxes)
            {
                if (hasFloated && hasOutOfFlow && (!includeStackingHoistCandidates || hasStackingHoistCandidates))
                    return;
                ComputeFlowFlags(childBox, false, includeStackingHoistCandidates,
                    ref hasFloated, ref hasOutOfFlow, ref hasStackingHoistCandidates);
            }
        }

        /// <summary>
        /// Recursively re-applies content to pseudo-elements after layout completes.
        /// This ensures pseudo-elements can access named strings set during layout.
        /// </summary>
        private void ReapplyPseudoElementContent(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                if (childBox.IsPseudoElement && !string.IsNullOrEmpty(childBox.Content) && childBox.Content != CssConstants.None && childBox.Content != CssConstants.Normal)
                {
                    // Check if content contains string() function
                    if (childBox.Content.Contains("string("))
                    {
                        CssContentEngine.ApplyContent(childBox);
                        // Re-parse words after content changes
                        if (!string.IsNullOrEmpty(childBox.Text))
                        {
                            childBox.ParseToWords();
                        }
                    }
                }
                ReapplyPseudoElementContent(childBox);
            }
        }

        /// <summary>
        /// The page/viewport rect, in the same per-page paint-time coordinate space every box's
        /// painted <c>Rectangles</c> use (i.e. independent of <see cref="ScrollOffset"/>, exactly like
        /// a <c>position: fixed</c> box's own offset override) - the same rect <see cref="PerformPaint"/>
        /// pushes as the top-level page clip, also used as the background positioning area for a
        /// <c>background-attachment: fixed</c> layer (CSS Backgrounds 3 §3.9).
        /// </summary>
        /// <summary>
        /// Per-page paint-window override set by <c>PdfGenerator.AddPdfPages</c>'s page loop (the
        /// same per-page mutation pattern as <see cref="ScrollOffset"/>) so a page whose margins
        /// are overridden by a per-page <c>@page</c> rule (e.g. <c>:first { margin: 0 }</c>) gets a
        /// window matching its own margins instead of the base-margin <see cref="PageBoxRect"/>.
        /// <see cref="PerformPaint"/> falls back to <see cref="PageBoxRect"/> when unset.
        /// </summary>
        internal RRect? PageClipOverride { get; set; }

        internal RRect PageBoxRect => MaxSize.Height > 0
            ? new RRect(Location.X, Location.Y, Math.Min(MaxSize.Width, PageSize.Width),
                Math.Min(MaxSize.Height, PageSize.Height))
            : new RRect(MarginLeft, MarginTop, PageSize.Width + MarginRight, PageSize.Height);

        /// <summary>
        /// The zero-based pagination-slot index containing document Y-coordinate <paramref name="y"/>,
        /// on the "shifted grid" every page-boundary decision needs to agree on: slot <c>k</c> occupies
        /// <c>[k·PageSize.Height + MarginTop, (k+1)·PageSize.Height + MarginTop)</c>. This is the
        /// convention the painter's own per-page clip/translation (<c>PdfGenerator.AddPdfPages</c>) and
        /// <see cref="GetPaginationSlots"/> already use - <see cref="PageSize"/>'s <c>Height</c> is
        /// already margin-free (<c>PdfGenerator.SetContent</c> subtracts both margins from the raw page
        /// height up front), so every page's real content band starts <see cref="MarginTop"/> past each
        /// raw multiple of <see cref="PageSize"/>'s height, not at the raw multiple itself. Callers must
        /// only invoke this when <see cref="PageSize"/>'s <c>Height</c> is a real, finite, positive
        /// value - unpaginated/measurement passes use a <c>double.MaxValue</c> sentinel and must guard
        /// around this the same way existing raw-<c>PageSize.Height</c> call sites already do.
        /// </summary>
        internal int PageIndexOf(double y) => (int)((y - MarginTop) / PageSize.Height);

        /// <summary>
        /// The document Y-coordinate of the content-top of pagination slot <paramref name="pageIndex"/>,
        /// per the same shifted-grid convention as <see cref="PageIndexOf"/> - the inverse operation.
        /// </summary>
        internal double PageTopOf(int pageIndex) => pageIndex * PageSize.Height + MarginTop;

        /// <summary>
        /// The page-relative Y ("<c>pageY</c>" in <c>PdfGenerator.AddPdfPages</c>'s own terms - i.e.
        /// <c>-scrollOffset + MarginTop</c>) of every page-slot that should actually be materialized
        /// as a real PDF page, per CSS Paged Media Level 3 §3.2: "User agents SHOULD avoid generating
        /// a large number of content-empty pages". A slot is skipped when no box's own laid-out
        /// range (see <see cref="DomUtils.CollectPrintableContentRanges"/>) overlaps it - this is what
        /// lets a document with huge, purely-decorative margins (e.g. Acid2's own "100em" margins on
        /// "#top"/".picture", meant to be scrolled off-screen in a real, single-viewport browser) skip
        /// straight past those gaps instead of paginating through several blank pages to reach the
        /// real content on the far side. Non-destructive: no content is discarded or repositioned,
        /// only which page-slots get a <c>PdfPage</c> materialized for them changes.
        /// </summary>
        internal IReadOnlyList<double> GetPaginationSlots()
        {
            var slots = new List<double>();

            if (ActualSize.Height <= 0 || Root is null)
                return slots;

            var printableRanges = DomUtils.CollectPrintableContentRanges(Root);
            var pageHeight = PageSize.Height;
            var rangeIndex = 0;

            for (var pageTop = 0.0; pageTop < ActualSize.Height; pageTop += pageHeight)
            {
                var pageBottom = pageTop + pageHeight;

                while (rangeIndex < printableRanges.Count && printableRanges[rangeIndex].Bottom < pageTop)
                    rangeIndex++;

                if (rangeIndex < printableRanges.Count && printableRanges[rangeIndex].Top < pageBottom)
                    slots.Add(pageTop);
            }

            // Never emit a 0-page PDF for a document that genuinely laid out some non-zero height -
            // if literally nothing qualified as "printable" (e.g. every box is a whole-page canvas
            // background, or the printable-content heuristic is simply too conservative for some
            // edge case), fall back to the first slot rather than producing nothing at all.
            if (slots.Count == 0)
                slots.Add(0.0);

            return slots;
        }

        /// <summary>
        /// Render the html using the given device.
        /// </summary>
        /// <param name="g">the device to use to render</param>
        public async ValueTask PerformPaint(RGraphics g)
        {
            ArgumentNullException.ThrowIfNull(g);

            g.PushClip(PageClipOverride ?? PageBoxRect);

            if (Root is not null)
            {
                Root.ResetPaint();
                await Root.Paint(g);
            }

            g.PopClip();
        }

        /// <summary>
        /// Given the list of available media types, returns the "best" one
        /// </summary>
        /// <param name="mediaTypesAvailable"></param>
        /// <returns></returns>
        internal string GetCssMediaType(IEnumerable<string> mediaTypesAvailable)
        {
            return Adapter.GetCssMediaType(mediaTypesAvailable);
        }

        /// <summary>
        /// Report error in html render process.
        /// </summary>
        /// <param name="type">the type of error to report</param>
        /// <param name="message">the error message</param>
        /// <param name="exception">optional: the exception that occured</param>
        [DoesNotReturn]
        internal void ReportError(HtmlRenderErrorType type, string message, Exception? exception = null)
        {
            throw new HtmlRenderException(message, type, exception);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            Dispose(true);
        }


        #region Private methods

        /// <summary>
        /// Adjust the offset of the given location by the current scroll offset.
        /// </summary>
        /// <param name="location">the location to adjust</param>
        /// <returns>the adjusted location</returns>
        private RPoint OffsetByScroll(RPoint location)
        {
            return new RPoint(location.X - ScrollOffset.X, location.Y - ScrollOffset.Y);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        private void Dispose(bool all)
        {
            try
            {
                CssData = null;
                Root?.Dispose();
                Root = null;
            }
            catch
            { }
        }

        #endregion
    }
}