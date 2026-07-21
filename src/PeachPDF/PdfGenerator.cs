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

using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using System;
using System.Linq;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.PdfSharpCore.Pdf.Annotations;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PeachPDF
{
    /// <summary>
    /// Renders HTML (and optionally CSS) into a PDF document entirely in-process, with no external
    /// browser or process dependency. This is the main entry point for PeachPDF: use one of the
    /// <c>GeneratePdf</c> overloads to create a new <see cref="PeachPdfDocument"/>, or one of the
    /// <c>AddPdfPages</c> overloads to append rendered pages to an existing one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="PdfGenerator"/> instance is <b>not thread-safe</b>. Its brush/pen caches, font
    /// resolver, and network loader are owned exclusively by that instance, so calling its methods
    /// concurrently from multiple threads on the <i>same</i> instance (or reusing one instance across
    /// overlapping renders) is not supported and can corrupt its internal state. This includes every
    /// custom font registered on it — via <c>@font-face</c> or <see cref="AddFontFromStream(Stream)"/> — whose
    /// resolved glyph/metrics data lives in caches private to that instance, so two instances that
    /// register <i>different</i> bytes under the <i>same</i> font family name never collide.
    /// </para>
    /// <para>
    /// Using a <b>separate <see cref="PdfGenerator"/> instance per thread</b> — e.g. one per
    /// incoming web request, or one per work item in a parallel batch — is safe and is the intended
    /// way to generate PDFs concurrently. Pure system-font data (fonts already installed on the
    /// machine, discovered once at process startup) is deliberately the one exception: it's
    /// immutable and safely shared read-only across every instance, so resolving e.g. "Arial Bold"
    /// isn't repeated per instance.
    /// </para>
    /// </remarks>
    public class PdfGenerator
    {
        private readonly PdfSharpAdapter _pdfSharpAdapter = new();

        /// <summary>
        /// Adds a font mapping from <paramref name="fromFamily"/> to <paramref name="toFamily"/> iff the <paramref name="fromFamily"/> is not found.<br/>
        /// When the <paramref name="fromFamily"/> font is used in rendered html and is not found in existing 
        /// fonts (installed or added) it will be replaced by <paramref name="toFamily"/>.<br/>
        /// </summary>
        /// <remarks>
        /// This fonts mapping can be used as a fallback in case the requested font is not installed in the client system.
        /// </remarks>
        /// <param name="fromFamily">the font family to replace</param>
        /// <param name="toFamily">the font family to replace with</param>
        public void AddFontFamilyMapping(string fromFamily, string toFamily)
        {
            ArgChecker.AssertArgNotNullOrEmpty(fromFamily, "fromFamily");
            ArgChecker.AssertArgNotNullOrEmpty(toFamily, "toFamily");

            _pdfSharpAdapter.AddFontFamilyMapping(fromFamily, toFamily);
        }

        /// <summary>
        /// Add a font to be rendered
        /// </summary>
        /// <param name="stream">Font stream</param>
        public async Task AddFontFromStream(Stream stream)
        {
            await _pdfSharpAdapter.AddFont(stream, null);
        }

        /// <summary>
        /// Add a font to be rendered, restricting it to the given codepoint ranges - the programmatic
        /// equivalent of an <c>@font-face</c> <c>unicode-range</c> descriptor. Characters outside these
        /// ranges resolve to another registered font (per-codepoint font matching). Pass an empty list to
        /// register a font that is never selected by codepoint coverage.
        /// </summary>
        /// <param name="stream">Font stream</param>
        /// <param name="unicodeRanges">The codepoint ranges this font should be used for</param>
        public async Task AddFontFromStream(Stream stream, IReadOnlyList<RuneRange> unicodeRanges)
        {
            await _pdfSharpAdapter.AddFont(stream, null, weightOverride: null, isItalicOverride: null, stretchOverride: null, unicodeRanges);
        }

        /// <summary>
        /// Parses the given stylesheet into a reusable <see cref="PeachPdfCssContent"/> object.<br/>
        /// If <paramref name="combineWithDefault"/> is true the parsed css blocks are added to the
        /// default css data (as defined by the <see href="http://www.w3.org/TR/CSS21/sample.html">CSS 2.1 default stylesheet for HTML</see>), merged if class name already exists. If false only the data in the given stylesheet is returned.
        /// </summary>
        /// <param name="stylesheet">the stylesheet source to parse</param>
        /// <param name="combineWithDefault">true - combine the parsed css data with default css data, false - return only the parsed css data</param>
        /// <returns>the parsed css data</returns>
        public async Task<PeachPdfCssContent> ParseStyleSheet(string stylesheet, bool combineWithDefault = true)
        {
            var cssData = await CssData.Parse(_pdfSharpAdapter, stylesheet, combineWithDefault);
            return new PeachPdfCssContent(cssData);
        }

        /// <summary>
        /// Create PDF document from given HTML.<br/>
        /// </summary>
        /// <param name="html">HTML source to create PDF from</param>
        /// <param name="pageSize">the page size to use for each page in the generated pdf </param>
        /// <param name="margin">the margin to use between the HTML and the edges of each page</param>
        /// <param name="cssData">optional: the style to use for html rendering (default - use W3 default style)</param>
        /// <returns>the generated image of the html</returns>
        public async Task<PeachPdfDocument> GeneratePdf(string html, PageSize pageSize, int margin = 20, PeachPdfCssContent? cssData = null)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = pageSize
            };

            config.SetMargins(margin);

            return await GeneratePdf(html, config, cssData);
        }

        /// <summary>
        /// Create PDF document from given HTML.<br/>
        /// </summary>
        /// <param name="html">HTML source to create PDF from</param>
        /// <param name="config">the configuration to use for the PDF generation (page size/page orientation/margins/etc.)</param>
        /// <param name="cssData">optional: the style to use for html rendering (default - use W3 default style)</param>
        /// <returns>the generated image of the html</returns>
        public async Task<PeachPdfDocument> GeneratePdf(string? html, PdfGenerateConfig config, PeachPdfCssContent? cssData = null)
        {
            // create PDF document to render the HTML into
            var document = new PeachPdfDocument(new PdfDocument());

            // add rendered PDF pages to document
            await AddPdfPages(document, html, config, cssData);

            return document;
        }

        /// <summary>
        /// Create PDF pages from given HTML and appends them to the provided PDF document.<br/>
        /// </summary>
        /// <param name="document">PDF document to append pages to</param>
        /// <param name="html">HTML source to create PDF from</param>
        /// <param name="pageSize">the page size to use for each page in the generated pdf </param>
        /// <param name="margin">the margin to use between the HTML and the edges of each page</param>
        /// <param name="cssData">optional: the style to use for html rendering (default - use W3 default style)</param>
        /// <returns>the generated image of the html</returns>
        public async Task AddPdfPages(PeachPdfDocument document, string html, PageSize pageSize, int margin = 20, PeachPdfCssContent? cssData = null)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = pageSize
            };

            config.SetMargins(margin);

            await AddPdfPages(document, html, config, cssData);
        }

        /// <summary>
        /// Create PDF pages from given HTML and appends them to the provided PDF document.<br/>
        /// </summary>
        /// <param name="document">PDF document to append pages to</param>
        /// <param name="html">HTML source to create PDF from</param>
        /// <param name="config">the configuration to use for the PDF generation (page size/page orientation/margins/etc.)</param>
        /// <param name="cssData">optional: the style to use for html rendering (default - use W3 default style)</param>
        /// <returns>the generated image of the html</returns>
        public async Task AddPdfPages(PeachPdfDocument document, string? html, PdfGenerateConfig config, PeachPdfCssContent? cssData = null)
        {
            // get the size of each page to layout the HTML in
            var orgPageSize = config.PageSize != PageSize.Undefined ? PageSizeConverter.ToSize(config.PageSize) : new XSize(config.ManualPageWidth, config.ManualPageHeight);

            if (config.PageOrientation == PageOrientation.Landscape)
            {
                // invert pagesize for landscape
                orgPageSize = new XSize(orgPageSize.Height, orgPageSize.Width);
            }

            if (string.IsNullOrEmpty(html) && config.NetworkLoader is null) return;

            document.PdfDocument.Options.CompressContentStreams = config.CompressContentStreams;

            _pdfSharpAdapter.NetworkLoader = config.NetworkLoader ?? new DataUriNetworkLoader();
            _pdfSharpAdapter.PixelsPerPoint = config.PixelsPerInch / 72d;

            html ??= await _pdfSharpAdapter.NetworkLoader.GetPrimaryContents();

            using var container = new HtmlContainer(_pdfSharpAdapter);

            await SetContent(container, config, html, cssData, orgPageSize);

            // If CSS @page { size: ... } overrides the configured page size, re-apply with the CSS size
            if (container.CssPageSize.HasValue && container.CssPageSize.Value != orgPageSize)
            {
                orgPageSize = container.CssPageSize.Value;
                await SetContent(container, config, html, cssData, orgPageSize);
            }

            var measure = XGraphics.CreateMeasureContext(container.PageSize, XGraphicsUnit.Point, XPageDirection.Downwards);

            var basePixelsPerPoint = config.PixelsPerInch / 72d;
            var minPixelsPerPoint = config.MinContentWidth > 0 ? config.MinContentWidth / container.PageSize.Width : basePixelsPerPoint;
            var pixelsPerPoint = minPixelsPerPoint;

            if (config.ScaleToPageSize || config.ShrinkToFit)
            {
                container.MaxSize = new XSize(container.PageSize.Width, 0);
                await container.PerformLayout(measure);

                var actualWidth = container.ActualSize.Width;

                _pdfSharpAdapter.ClearFontCache();
                pixelsPerPoint *= (actualWidth / container.PageSize.Width);

                if (pixelsPerPoint < minPixelsPerPoint)
                {
                    pixelsPerPoint = minPixelsPerPoint;
                }

                _pdfSharpAdapter.PixelsPerPoint = (config.ShrinkToFit && pixelsPerPoint > 1) || config.ScaleToPageSize
                    ? pixelsPerPoint
                    : _pdfSharpAdapter.PixelsPerPoint;

                await SetContent(container, config, html, cssData, orgPageSize);

                measure?.Dispose();
                measure = XGraphics.CreateMeasureContext(container.PageSize, XGraphicsUnit.Point, XPageDirection.Downwards);
            }

            container.MaxSize = new XSize(container.PageSize.Width, 0);

            // layout the HTML with the page width restriction to know how many pages are required
            await container.PerformLayout(measure);

            ApplyDocumentMetadata(document.PdfDocument, container.DocumentMetadata);

            // Wired unconditionally (independent of tagged-PDF output) - a document's own /Lang is
            // useful metadata regardless, and DocumentLanguage is already resolved by SetContent
            // (either from <html lang> or PdfGenerateConfig.DefaultLanguage).
            if (!string.IsNullOrEmpty(container.HtmlContainerInt.DocumentLanguage))
            {
                document.PdfDocument.Language = container.HtmlContainerInt.DocumentLanguage;
            }

            // Only constructed when tagging is enabled - CssBox.PaintImp's tagging wrapper checks
            // HtmlContainerInt.StructureTagBuilder for null and skips all classification/bookkeeping
            // when it's not set, so this is the single point that gates the whole feature off by
            // default.
            var structureTagBuilder = config.EnableTaggedPdf ? new StructureTagBuilder(document.PdfDocument) : null;
            container.HtmlContainerInt.StructureTagBuilder = structureTagBuilder;

            // Per CSS2.1 §14.2, the "canvas" (here: every page) is filled with body's background if it
            // declares one, else html's - resolved once, up front, since which box (if either) is chosen
            // never changes page to page. Whichever box was chosen also gets its own normal background
            // paint pass suppressed (see SuppressOwnBackgroundPaint), so it isn't painted twice.
            var canvasBackgroundBox = ResolveCanvasBackground(container.HtmlContainerInt.Root);

            // Margin-box `content: url(...)` images (see MarginBoxRenderer.ResolveContentImage) are
            // cached by declaration text across the whole document, since the same margin rule - and
            // so the same image - repeats identically on every page; without this a multi-hundred-page
            // document would re-decode (or re-fetch, for a network image) the same logo once per page.
            var marginBoxImageCache = new Dictionary<string, CssImage?>();

            // Create a PDF page for each page-slot that would actually show something, per CSS Paged
            // Media Level 3 §3.2 ("User agents SHOULD avoid generating a large number of
            // content-empty pages") - e.g. Acid2's own "100em" margins on "#top"/".picture" are
            // intentionally huge, meant to be scrolled off-screen in a real, single-viewport browser;
            // without this, a paginated PDF would dutifully generate several genuinely blank pages to
            // walk through that margin before reaching the real content on the far side. Must run
            // after canvasBackgroundBox above, since GetPaginationSlots relies on
            // CssBox.SuppressOwnBackgroundPaint (set by ResolveCanvasBackground) to avoid treating the
            // whole-page canvas fill itself as "real content" spanning the entire document.
            var pageSlots = container.HtmlContainerInt.GetPaginationSlots();
            int pageNumber = 0;
            var totalPages = pageSlots.Count;
            foreach (var (slotIndex, slotTop) in pageSlots)
            {
                pageNumber++;
                var scrollOffset = -slotTop;

                // The single source of truth for this slot's margins and band: the same geometry
                // table layout paginated against, so paint can never disagree with layout about a
                // page's content band. Margins come out in true points (the space the clip/translate
                // below and MarginBoxRenderer use); Top/BandHeight are internal-pixel document space
                // (the space NamedPageElement Ys live in), which also fixes the historical
                // ShrinkToFit drift where pageY mixed a pixel-space slot top with a point-space
                // MarginTop for named-page attribution.
                var geom = container.HtmlContainerInt.PageGeometry.GetPage(slotIndex);
                var pageY = geom.Top;
                var applicableMargins = SelectApplicableMarginRules(
                    container.PageRules,
                    pageNumber,
                    container.NamedPageElements,
                    pageY,
                    geom.BandHeight);
                var applicablePageStyle = SelectApplicablePageStyle(
                    container.PageRules,
                    pageNumber,
                    container.NamedPageElements,
                    pageY,
                    geom.BandHeight);

                var (mL, mT, mR, mB) = (geom.MarginLeftPt, geom.MarginTopPt, geom.MarginRightPt, geom.MarginBottomPt);

                var page = document.PdfDocument.AddPage();
                page.Height = orgPageSize.Height;
                page.Width = orgPageSize.Width;

                structureTagBuilder?.BeginPage(page);

                using var g = XGraphics.FromPdfPage(page);

                if (canvasBackgroundBox != null)
                {
                    // Must paint before the content clip below is applied (page.304's IntersectClip),
                    // so the fill reaches the true full page bleed (including the margin-box area), not
                    // just the content rect.
                    using var canvasGraphics = new GraphicsAdapter(_pdfSharpAdapter, g, _pdfSharpAdapter.PixelsPerPoint);
                    canvasBackgroundBox.PaintCanvasBackground(canvasGraphics, new RRect(0, 0, page.Width * _pdfSharpAdapter.PixelsPerPoint, page.Height * _pdfSharpAdapter.PixelsPerPoint));
                }

                // Save state so the content transform can be undone for margin box rendering
                var preContentState = g.Save();

                g.IntersectClip(new XRect(mL, mT, page.Width - mL - mR, page.Height - mT - mB));

                var deltaX = mL - container.MarginLeft;
                var deltaY = mT - container.MarginTop;
                if (deltaX != 0 || deltaY != 0)
                    g.TranslateTransform(deltaX, deltaY);

                // scrollOffset is already in layout-pixel space (GetPaginationSlots walks the
                // internal PageSize.Height), so it must be assigned to the internal container
                // directly. The public HtmlContainer.ScrollOffset setter treats its input as
                // POINTS and multiplies by PixelsPerPoint to store pixels - feeding it a pixel
                // value scaled the offset twice whenever ShrinkToFit actually shrank content
                // (PixelsPerPoint > 1), sliding every page's content up by slot ×
                // (PixelsPerPoint - 1). The error compounded per page (~1.5pt/page on the SVG
                // showcase), so by page 4+ a box laid out flush at a page's top painted its
                // glyph tops back across the previous page's bottom clip edge - fragments must
                // start at the top of their own fragmentainer, not straddle the previous one
                // (CSS Fragmentation Level 3 §4; CSS2.1 §13.2's page box model).
                container.HtmlContainerInt.ScrollOffset = new RPoint(0, scrollOffset);

                // Same-units (internal pixel space) generalization of PageBoxRect's default paint
                // window: x/y adapt to this page's margin override via the delta translate above;
                // the width pins the window's right edge to the physical paper edge (identical to
                // PageBoxRect's PageSize.Width + MarginRight whenever mL equals the base left
                // margin, so non-overridden pages are unchanged). The height is this slot's own
                // content band from the geometry table — pagination itself ran on the same variable
                // bands, so a margin-0 page's window reclaims the full sheet height without ever
                // exposing a neighboring slot's content.
                container.HtmlContainerInt.PageClipOverride = new RRect(
                    container.HtmlContainerInt.MarginLeft,
                    container.HtmlContainerInt.MarginTop,
                    (page.Width - mL) * _pdfSharpAdapter.PixelsPerPoint,
                    geom.BandHeight);

                await container.PerformPaint(g);

                // Restore to pre-content state so margin boxes render in absolute page coordinates
                g.Restore(preContentState);

                if (applicableMargins.Count > 0)
                {
                    await MarginBoxRenderer.Render(
                        g,
                        orgPageSize,
                        mL,
                        mT,
                        mR,
                        mB,
                        applicableMargins,
                        pageNumber,
                        totalPages,
                        pageY,
                        geom.BandHeight,
                        container.NamedStrings,
                        _pdfSharpAdapter,
                        applicablePageStyle,
                        container.HtmlContainerInt,
                        marginBoxImageCache);
                }
            }

            foreach (var cachedImage in marginBoxImageCache.Values)
                cachedImage?.Dispose();

            // Finalizes /ParentTree page-keyed entries before HandleLinks (which, when tagging is
            // enabled, appends further annotation-keyed entries to the same tree - see
            // StructureTagBuilder.Finish and HandleLinks's own tagging-aware section below).
            structureTagBuilder?.Finish();

            // add web links and anchors
            HandleLinks(document.PdfDocument, container, orgPageSize, pageSlots, structureTagBuilder);

            measure?.Dispose();
        }

        #region Private/Protected methods

        /// <summary>
        /// Resolves which box's background (if any) should fill the whole page canvas, per CSS2.1 §14.2:
        /// <c>&lt;body&gt;</c>'s own background if it declares one, else <c>&lt;html&gt;</c>'s, else no
        /// canvas fill at all. The chosen box (if any) has <see cref="CssBox.SuppressOwnBackgroundPaint"/>
        /// set so its own normal paint pass doesn't also paint the same background a second time at its
        /// own (possibly much smaller than a page) laid-out rect.
        /// </summary>
        private static CssBox? ResolveCanvasBackground(CssBox? root)
        {
            var html = DomUtils.GetBoxByTagName(root, "html");
            var body = DomUtils.GetBoxByTagName(root, "body");

            var chosen = body is { HasOwnBackground: true } ? body : html is { HasOwnBackground: true } ? html : null;
            if (chosen != null)
                chosen.SuppressOwnBackgroundPaint = true;

            return chosen;
        }

        private static void ApplyDocumentMetadata(PdfDocument pdfDocument, HtmlDocumentMetadata? metadata)
        {
            var info = pdfDocument.Info;
            info.Producer = PeachPdfProductInfo.Generator;
            info.Creator  = metadata?.Generator ?? PeachPdfProductInfo.Generator;

            if (metadata is null) return;
            if (!string.IsNullOrEmpty(metadata.Title))    info.Title    = metadata.Title;
            if (!string.IsNullOrEmpty(metadata.Author))   info.Author   = metadata.Author;
            if (!string.IsNullOrEmpty(metadata.Subject))  info.Subject  = metadata.Subject;
            if (!string.IsNullOrEmpty(metadata.Keywords)) info.Keywords = metadata.Keywords;
            if (metadata.Date.HasValue)                   info.CreationDate = metadata.Date.Value;
        }

        internal static async Task SetContent(HtmlContainer container, PdfGenerateConfig config, string html, PeachPdfCssContent? cssData, XSize orgPageSize)
        {
            container.MarginBottom = config.MarginBottom;
            container.MarginLeft = config.MarginLeft;
            container.MarginRight = config.MarginRight;
            container.MarginTop = config.MarginTop;

            // Parse-time @page relative units (% / em, base rule and the captured PageLengthContext
            // alike) resolve against PageSize as it stands during SetHtml — carry the physical sheet
            // in so a percentage margin resolves against the page-box width (css-page-3 §7.1), not a
            // stale band from a previous pass or the unset 0 default on the first pass.
            container.PageSize = orgPageSize;

            await container.SetHtml(html, cssData?.CssData);

            // The document's own <html lang> always wins; config.DefaultLanguage only fills in when the
            // document declares none — PeachPDF itself never guesses a language on its own initiative.
            if (string.IsNullOrEmpty(container.HtmlContainerInt.DocumentLanguage) && !string.IsNullOrEmpty(config.DefaultLanguage))
            {
                container.HtmlContainerInt.DocumentLanguage = config.DefaultLanguage;
            }

            // Just in case @page rules got applied
            var pageSize = new XSize(orgPageSize.Width - container.MarginLeft - container.MarginRight, orgPageSize.Height - container.MarginTop - container.MarginBottom);
            container.PageSize = pageSize;
            container.Location = new XPoint(container.MarginLeft, container.MarginTop);
        }

        /// <summary>
        /// Handle HTML links by create PDF Documents link either to external URL or to another page in the document.
        /// <paramref name="structureTagBuilder"/> is non-null only when tagged PDF output is enabled -
        /// used to attach each created Link annotation's "/OBJR" back to its owning "/Link" structure
        /// element (see the tagging-aware section below), completing the bidirectional PDF/UA
        /// linkage between the annotation and the structure tree.
        /// </summary>
        private static void HandleLinks(PdfDocument document, HtmlContainer container, XSize orgPageSize, IReadOnlyList<(int SlotIndex, double SlotTop)> pageSlots, StructureTagBuilder? structureTagBuilder = null)
        {
            var inner = container.HtmlContainerInt;
            var ppp = container.PixelsPerPoint;

            // Materialized pages are the emitted pagination slots, which may skip content-empty
            // grid slots entirely (GetPaginationSlots) - so a document-space slot index and a
            // document.Pages index are NOT interchangeable. Build the slot -> page mapping once.
            var slotToPage = new Dictionary<int, int>(pageSlots.Count);
            for (var p = 0; p < pageSlots.Count; p++)
                slotToPage[pageSlots[p].SlotIndex] = p;

            var maxMappedSlot = slotToPage.Count > 0 ? slotToPage.Keys.Max() : -1;

            foreach (var link in container.GetLinks())
            {
                // Link rects are true points (the public GetLinks wrapper divides by PixelsPerPoint
                // and resolves relative hrefs against the document base URI); slot attribution runs
                // on the internal-pixel shifted grid (content starts at MarginTop, not 0) - the
                // historical raw Top/PageSize.Height attribution ignored that shift.
                var firstSlot = Math.Max(inner.PageIndexOf(link.Rectangle.Top * ppp), 0);
                for (var slot = firstSlot; inner.PageTopOf(slot) < link.Rectangle.Bottom * ppp; slot++)
                {
                    if (!slotToPage.TryGetValue(slot, out var pageIndex) || pageIndex >= document.Pages.Count)
                        continue;

                    // Page-local geometry in true points, matching the painted content's own per-page
                    // margins (the geometry table's slot margins are what the paint loop's
                    // clip/translate used); PDF rect y counts from the page bottom.
                    var slotGeom = inner.PageGeometry.GetPage(slot);
                    var topPt = slotGeom.MarginTopPt + (link.Rectangle.Top * ppp - inner.PageTopOf(slot)) / ppp;
                    var leftPt = slotGeom.MarginLeftPt + (link.Rectangle.Left * ppp - inner.MarginLeft) / ppp;
                    var xRect = new XRect(
                        leftPt,
                        orgPageSize.Height - (topPt + link.Rectangle.Height),
                        link.Rectangle.Width,
                        link.Rectangle.Height);

                    PdfLinkAnnotation annotation;

                    if (link.IsAnchor)
                    {
                        // create link to another page in the document
                        var anchorRect = container.GetElementRectangle(link.AnchorId);

                        if (!anchorRect.HasValue) continue;

                        var anchorSlot = inner.PageIndexOf(anchorRect.Value.Top * ppp + HtmlContainerInt.PageBoundaryEpsilon);
                        // An anchor inside a skipped (content-empty) slot attributes to the next
                        // materialized page - the nearest place a reader can actually land.
                        var anchorPage = -1;
                        for (var s = Math.Max(anchorSlot, 0); anchorPage < 0 && s <= maxMappedSlot; s++)
                            if (slotToPage.TryGetValue(s, out var found))
                                anchorPage = found;
                        if (anchorPage < 0) anchorPage = pageSlots.Count - 1;

                        var anchorTopPt = inner.PageGeometry.GetPage(anchorSlot).MarginTopPt
                            + (anchorRect.Value.Top * ppp - inner.PageTopOf(anchorSlot)) / ppp;

                        document.AddNamedDestination(link.AnchorId, anchorPage + 1, PdfNamedDestinationParameters.CreateFitVertically(anchorTopPt));
                        annotation = document.Pages[pageIndex].AddDocumentLink(new PdfRectangle(xRect), link.AnchorId);
                    }
                    else
                    {
                        // create link to URL
                        annotation = document.Pages[pageIndex].AddWebLink(new PdfRectangle(xRect), link.Href);
                    }

                    if (structureTagBuilder != null && link.SourceBox != null)
                    {
                        structureTagBuilder.LinkAnnotationToStructureElement(link.SourceBox, document.Pages[pageIndex], annotation);
                    }
                }
            }
        }

        /// <summary>
        /// Delegating shims over <see cref="PageRuleResolver"/> — the cascade implementation moved to
        /// Html/Core so layout-time page geometry (<c>PageGeometryTable</c>) and paint-time selection
        /// share one implementation. These preserve the historical signatures (name attribution via
        /// pageY/pageHeight, i.e. <see cref="PageRuleResolver.ActiveNameAtPageEnd"/> semantics).
        /// </summary>
        internal static PageRule? SelectPageRule(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight)
            => PageRuleResolver.SelectPageRule(rules, pageNumber,
                PageRuleResolver.ActiveNameAtPageEnd(namedPageElements, pageY, pageHeight));

        internal static IReadOnlyList<MarginStyleRule> SelectApplicableMarginRules(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight)
            => PageRuleResolver.SelectApplicableMarginRules(rules, pageNumber,
                PageRuleResolver.ActiveNameAtPageEnd(namedPageElements, pageY, pageHeight));

        internal static StyleDeclaration? SelectApplicablePageStyle(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight)
            => PageRuleResolver.SelectApplicablePageStyle(rules, pageNumber,
                PageRuleResolver.ActiveNameAtPageEnd(namedPageElements, pageY, pageHeight));

        internal static (double L, double T, double R, double B) ResolvePageMargins(
            PageRule? rule, double baseL, double baseT, double baseR, double baseB)
            => PageRuleResolver.ResolvePageMargins(rule, baseL, baseT, baseR, baseB);

        #endregion
    }
}