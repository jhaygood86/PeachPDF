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

        internal void RegisterNamedPageElement(string name, double y) =>
            _namedPageElements.Add(new NamedPageElement(name, y));

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
        /// <see cref="Utils.DomUtils.FlattenStackingContext"/> to skip hoisting out-of-flow descendants
        /// past normal-flow wrapper boxes entirely when there's nothing to hoist.
        /// </summary>
        internal bool HasOutOfFlowBoxes { get; private set; }

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
                linkElements.Add(new LinkElementData<RRect>(box.GetAttribute("id"), box.GetAttribute("href"), CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds)));
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

            (HasFloatedBoxes, HasOutOfFlowBoxes) = ComputeFlowFlags(Root);

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
            (HasFloatedBoxes, HasOutOfFlowBoxes) = ComputeFlowFlags(Root);
        }

        /// <summary>
        /// Recursively checks whether any box in the tree is floated and/or out-of-flow, short-circuiting
        /// once both have been confirmed true.
        /// </summary>
        private static (bool HasFloated, bool HasOutOfFlow) ComputeFlowFlags(CssBox box)
        {
            var hasFloated = false;
            var hasOutOfFlow = false;
            ComputeFlowFlags(box, ref hasFloated, ref hasOutOfFlow);
            return (hasFloated, hasOutOfFlow);
        }

        private static void ComputeFlowFlags(CssBox box, ref bool hasFloated, ref bool hasOutOfFlow)
        {
            if (box.IsFloated) hasFloated = true;
            if (box.IsOutOfFlow) hasOutOfFlow = true;

            foreach (var childBox in box.Boxes)
            {
                if (hasFloated && hasOutOfFlow) return;
                ComputeFlowFlags(childBox, ref hasFloated, ref hasOutOfFlow);
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
        /// Render the html using the given device.
        /// </summary>
        /// <param name="g">the device to use to render</param>
        public async ValueTask PerformPaint(RGraphics g)
        {
            ArgumentNullException.ThrowIfNull(g);

            g.PushClip(MaxSize.Height > 0
                ? new RRect(Location.X, Location.Y, Math.Min(MaxSize.Width, PageSize.Width),
                    Math.Min(MaxSize.Height, PageSize.Height))
                : new RRect(MarginLeft, MarginTop, PageSize.Width + MarginRight, PageSize.Height));

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