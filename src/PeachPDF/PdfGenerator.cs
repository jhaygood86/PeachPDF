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
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Html.Core.Parse;
using System;
using System.Linq;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.Utilities;
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
            return new PeachPdfCssContent(cssData, _pdfSharpAdapter);
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
            _pdfSharpAdapter.AllowLocalFileAccess = config.AllowLocalFileAccess;
            _pdfSharpAdapter.PixelsPerPoint = config.PixelsPerInch / 72d;

            html ??= await _pdfSharpAdapter.NetworkLoader.GetPrimaryContents();

            using var container = new HtmlContainer(_pdfSharpAdapter);

            await SetContent(container, config, html, cssData, orgPageSize);

            // DomParser.CascadeApplyPageStyles (run inside SetContent's call to SetHtml, above) already
            // corrects the container's own PageSize/margins in place when the document's @page { size }
            // differs from orgPageSize (issue #582 - it runs before the expensive cascade/correction
            // passes within the same parse, so nothing needs to be discarded and re-parsed). Keep this
            // local variable in sync with that correction, since it - not container.PageSize - is what
            // the measure/rescale pass below and each page's dimensions (page.Width/page.Height,
            // MarginBoxRenderer, HandleLinks) use.
            if (container.CssPageSize.HasValue)
            {
                orgPageSize = container.CssPageSize.Value;
            }

            var measure = XGraphics.CreateMeasureContext(container.PageSize, XGraphicsUnit.Point, XPageDirection.Downwards);

            var basePixelsPerPoint = config.PixelsPerInch / 72d;
            var minPixelsPerPoint = config.MinContentWidth > 0 ? config.MinContentWidth / container.PageSize.Width : basePixelsPerPoint;
            var pixelsPerPoint = minPixelsPerPoint;

            // Tracks whether the final layout pass below still needs to run: it always does for a
            // plain (non-shrink) render, since it's the only layout pass in that case. For
            // ShrinkToFit/ScaleToPageSize it's set to false when the measurement pass already
            // determined no rescale is needed, so the font-cache clear, the full HTML/CSS re-parse,
            // and this pass aren't repeated against output that would come out byte-for-byte
            // identical to what the measurement pass already produced (see NeedsRescale below).
            var needsRescale = true;

            if (config.ScaleToPageSize || config.ShrinkToFit)
            {
                container.MaxSize = new XSize(container.PageSize.Width, 0);
                await container.PerformLayout(measure);

                var actualWidth = container.ActualSize.Width;

                var candidatePixelsPerPoint = pixelsPerPoint * (actualWidth / container.PageSize.Width);

                if (candidatePixelsPerPoint < minPixelsPerPoint)
                {
                    candidatePixelsPerPoint = minPixelsPerPoint;
                }

                var effectivePixelsPerPoint = (config.ShrinkToFit && candidatePixelsPerPoint > 1) || config.ScaleToPageSize
                    ? candidatePixelsPerPoint
                    : _pdfSharpAdapter.PixelsPerPoint;

                needsRescale = NeedsRescale(_pdfSharpAdapter.PixelsPerPoint, effectivePixelsPerPoint);

                if (needsRescale)
                {
                    _pdfSharpAdapter.ClearFontCache();
                    _pdfSharpAdapter.PixelsPerPoint = effectivePixelsPerPoint;

                    await SetContent(container, config, html, cssData, orgPageSize);

                    measure?.Dispose();
                    measure = XGraphics.CreateMeasureContext(container.PageSize, XGraphicsUnit.Point, XPageDirection.Downwards);
                }
            }

            if (needsRescale)
            {
                container.MaxSize = new XSize(container.PageSize.Width, 0);

                // layout the HTML with the page width restriction to know how many pages are required
                await container.PerformLayout(measure);
            }

            ApplyDocumentMetadata(document.PdfDocument, container.DocumentMetadata, config.Metadata);

            // Wired unconditionally (independent of tagged-PDF output) - a document's own /Lang is
            // useful metadata regardless, and DocumentLanguage is already resolved by SetContent
            // (the document's own <html lang> takes priority, else PdfGenerateConfig.DefaultLanguage -
            // so an explicit DefaultLanguage still reaches /Lang when the document declares none).
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

            // Same shape as structureTagBuilder above: only constructed when interactive PDF forms are
            // enabled - FragmentContentPainters.For and HandleFormFields (below) both check
            // HtmlContainerInt.FormFieldBuilder for null and skip all classification/bookkeeping when
            // it's not set, so this is the single point that gates the whole feature off by default.
            var formFieldBuilder = config.EnableInteractivePdfForms ? new FormFieldBuilder(document.PdfDocument, container.HtmlContainerInt.Adapter) : null;
            container.HtmlContainerInt.FormFieldBuilder = formFieldBuilder;

            // Per CSS2.1 §14.2 the "canvas" (here: every page) is filled with body's background if it
            // declares one, else html's. Resolved during layout, since the fragment tree's own
            // page-materialization rule depends on it.
            var canvasBackgroundBox = container.HtmlContainerInt.CanvasBackgroundBox;

            // Margin-box `content: url(...)` images (see MarginBoxRenderer.ResolveContentImage) are
            // cached by declaration text across the whole document, since the same margin rule - and
            // so the same image - repeats identically on every page; without this a multi-hundred-page
            // document would re-decode (or re-fetch, for a network image) the same logo once per page.
            var marginBoxImageCache = new Dictionary<string, CssImage?>();

            // One PDF page per fragmentainer. The fragment tree is layout's own output, so which
            // pages exist is a structural fact rather than a geometric rediscovery: a page-slot that
            // no printable fragment landed in was never built, per CSS Paged Media Level 3 §3.2
            // ("User agents SHOULD avoid generating a large number of content-empty pages") - e.g.
            // Acid2's own "100em" margins on "#top"/".picture" are intentionally huge, meant to be
            // scrolled off-screen in a real, single-viewport browser; without this a paginated PDF
            // would dutifully emit several genuinely blank pages to walk through that margin before
            // reaching the real content on the far side.
            var fragmentainers = container.HtmlContainerInt.FragmentTree?.Fragmentainers ?? [];
            int pageNumber = 0;
            var totalPages = fragmentainers.Count;
            foreach (var fragmentainer in fragmentainers)
            {
                pageNumber++;
                var slotIndex = fragmentainer.SlotIndex;
                var scrollOffset = -fragmentainer.LocalOriginY;

                // The single source of truth for this slot's margins and band: the same geometry
                // table layout paginated against, carried on the fragmentainer itself, so paint can
                // never disagree with layout about a page's content band. Margins come out in true
                // points (the space the clip/translate below and MarginBoxRenderer use);
                // Top/BandHeight are internal-pixel document space (the space NamedPageElement Ys
                // live in), which also fixes the historical ShrinkToFit drift where pageY mixed a
                // pixel-space slot top with a point-space MarginTop for named-page attribution.
                var geom = fragmentainer.Geometry;
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
                    FragmentPainter.PaintCanvasBackground(canvasGraphics, canvasBackgroundBox,
                        new RRect(0, 0, page.Width * _pdfSharpAdapter.PixelsPerPoint, page.Height * _pdfSharpAdapter.PixelsPerPoint));
                }

                // Save state so the content transform can be undone for margin box rendering
                var preContentState = g.Save();

                g.IntersectClip(new XRect(mL, mT, page.Width - mL - mR, page.Height - mT - mB));

                var deltaX = mL - container.MarginLeft;
                var deltaY = mT - container.MarginTop;
                if (deltaX != 0 || deltaY != 0)
                    g.TranslateTransform(deltaX, deltaY);

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

                container.PerformPaint(g, fragmentainer);

                // The footnote area's own geometry (AttachFootnoteAreas) is built the same
                // fragmentainer-local way every other content fragment's is - content stays anchored at
                // the base MarginLeft/MarginTop in layout space, with this page's own deltaX/deltaY
                // translate (above) the only thing that maps it onto a page whose own @page margins
                // differ from the base. It must therefore paint here, inside that transform, alongside
                // ordinary content - not after the restore below, which is what a true page-absolute rect
                // (a plain string/counter/element() margin box's own rect, computed directly from this
                // page's own margins) needs instead.
                if (fragmentainer.FootnoteArea is { } footnoteArea)
                {
                    PaintFootnoteArea(g, _pdfSharpAdapter, container.HtmlContainerInt, footnoteArea);
                }

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
                        container.NamedStrings,
                        _pdfSharpAdapter,
                        applicablePageStyle,
                        container.HtmlContainerInt,
                        marginBoxImageCache);
                }

                if (fragmentainer.MarginBoxes.Count > 0)
                {
                    PaintElementMarginBoxes(g, _pdfSharpAdapter, container.HtmlContainerInt, fragmentainer.MarginBoxes);
                }
            }

            foreach (var cachedImage in marginBoxImageCache.Values)
                cachedImage?.Dispose();

            // Finalizes /ParentTree page-keyed entries before HandleLinks (which, when tagging is
            // enabled, appends further annotation-keyed entries to the same tree - see
            // StructureTagBuilder.Finish and HandleLinks's own tagging-aware section below).
            structureTagBuilder?.Finish();

            // add web links and anchors. bookmarkBoxes rides along on the same full-tree walk
            // GetLinks() already performs (DomUtils.GetAllLinkAndBookmarkBoxes), so building the PDF
            // outline afterwards adds zero net full-tree traversals.
            var bookmarkBoxes = new List<CssBox>();
            HandleLinks(document.PdfDocument, container, orgPageSize, fragmentainers, bookmarkBoxes, structureTagBuilder);

            if (formFieldBuilder != null)
            {
                HandleFormFields(document.PdfDocument, container, orgPageSize, fragmentainers, formFieldBuilder);
            }

            // PDF outline (bookmarks) - CSS-default-driven (h1-h6 default to bookmark-level 1-6 via the
            // UA stylesheet, everything else defaults to none), so this is unconditional: a document
            // with no headings simply collects zero bookmark boxes above and adds no outline.
            BookmarkOutlineBuilder.Build(document.PdfDocument, container, fragmentainers, bookmarkBoxes);

            measure?.Dispose();
        }

        #region Private/Protected methods

        private static void ApplyDocumentMetadata(PdfDocument pdfDocument, HtmlDocumentMetadata? metadata, PdfDocumentMetadata? overrides)
        {
            var info = pdfDocument.Info;
            info.Producer = PeachPdfProductInfo.Generator;
            info.Creator  = overrides?.Creator ?? metadata?.Generator ?? PeachPdfProductInfo.Generator;

            // A non-null field on the caller-supplied overrides wins over the HTML-extracted value; a
            // null field falls back to the HTML metadata exactly as before (byte-identical when no
            // overrides are supplied).
            if (!string.IsNullOrEmpty(overrides?.Title))         info.Title    = overrides.Title;
            else if (!string.IsNullOrEmpty(metadata?.Title))     info.Title    = metadata.Title;

            if (!string.IsNullOrEmpty(overrides?.Author))        info.Author   = overrides.Author;
            else if (!string.IsNullOrEmpty(metadata?.Author))    info.Author   = metadata.Author;

            if (!string.IsNullOrEmpty(overrides?.Subject))       info.Subject  = overrides.Subject;
            else if (!string.IsNullOrEmpty(metadata?.Subject))   info.Subject  = metadata.Subject;

            if (!string.IsNullOrEmpty(overrides?.Keywords))      info.Keywords = overrides.Keywords;
            else if (!string.IsNullOrEmpty(metadata?.Keywords))  info.Keywords = metadata.Keywords;

            if (metadata?.Date.HasValue == true)                 info.CreationDate = metadata.Date.Value;
        }

        /// <summary>
        /// Whether a <c>ScaleToPageSize</c>/<c>ShrinkToFit</c> render needs its font-cache clear,
        /// full HTML/CSS re-parse, and second layout pass, or whether the measurement pass already
        /// used the correct <see cref="PdfSharpAdapter.PixelsPerPoint"/> (the common case: ordinary
        /// non-overflowing content already fits the page, so no rescale is needed). The comparison
        /// is exact rather than epsilon-based - <paramref name="effectivePixelsPerPoint"/> is either
        /// <paramref name="currentPixelsPerPoint"/> read back unchanged (no rescale needed) or a
        /// freshly computed, genuinely different value, never a value that merely rounds close to it.
        /// </summary>
        internal static bool NeedsRescale(double currentPixelsPerPoint, double effectivePixelsPerPoint) =>
            effectivePixelsPerPoint != currentPixelsPerPoint;

        internal static async Task SetContent(HtmlContainer container, PdfGenerateConfig config, string html, PeachPdfCssContent? cssData, XSize orgPageSize)
        {
            container.MarginBottom = config.MarginBottom;
            container.MarginLeft = config.MarginLeft;
            container.MarginRight = config.MarginRight;
            container.MarginTop = config.MarginTop;

            // Must be set before SetHtml below, which generates the DOM/CSS tree (DomParser) - both feed
            // into that generation: Media selects which @media rules match, IgnoreAuthorStyleSheets gates
            // whether the document's own <style>/<link> author sheets are collected at all.
            container.HtmlContainerInt.Media = string.IsNullOrEmpty(config.Media) ? "print" : config.Media;
            container.HtmlContainerInt.PreferredColorScheme = config.PreferredColorScheme;
            container.HtmlContainerInt.IgnoreAuthorStyleSheets = config.IgnoreAuthorStyleSheets;

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

            // Just in case @page rules got applied. SetContent is now only ever called once per render
            // with the ORIGINAL orgPageSize parameter (DomParser.CascadeApplyPageStyles corrects
            // PageSize/margins against the CSS @page size in place, inside SetHtml above, rather than a
            // caller re-invoking SetContent a second time with the corrected size - issue #582) - so this
            // must subtract margins from the CSS-resolved sheet size (falling back to orgPageSize when
            // there is no @page { size } rule), or it would clobber that in-place correction with the
            // stale, originally-configured size.
            var sheetSize = container.CssPageSize ?? orgPageSize;
            var pageSize = new XSize(sheetSize.Width - container.MarginLeft - container.MarginRight, sheetSize.Height - container.MarginTop - container.MarginBottom);
            container.PageSize = pageSize;
            container.Location = new XPoint(container.MarginLeft, container.MarginTop);
        }

        /// <summary>
        /// Paints one page's css-gcpm-3 <c>content: element()</c> margin-box content - real, laid-out
        /// <see cref="CssBox"/> subtrees (<see cref="FragmentainerFragment.MarginBoxes"/>), unlike the
        /// plain string/counter/image content <see cref="MarginBoxRenderer.Render"/> still draws directly.
        /// Reuses <see cref="FragmentPainter.PaintFragment"/> completely unmodified - the same call every
        /// other <see cref="BoxFragment"/> in the document goes through - so real formatting/descendant
        /// elements (backgrounds, borders, nested inline styling) paint correctly for free, and (per
        /// <c>FragmentPainter.PaintFragment</c>'s own doc comment, "the single choke point all tagging
        /// flows through") so tagged-PDF structure attaches automatically when enabled, keyed to whichever
        /// page's <c>structureTagBuilder.BeginPage</c> is currently open - the precondition this call site,
        /// immediately after the same page's main-content paint, satisfies.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="MarginBoxRenderer"/>'s own <c>PaintImage</c> precedent for going from a
        /// point-space margin-box rect to a fresh, scoped <see cref="GraphicsAdapter"/> - <see cref="MarginBoxFragment.Content"/>'s
        /// coordinates are already in that same pixel space (<see cref="RunningElementLayout.LayoutRunningElementFor"/>
        /// laid it out directly against the pixel-converted rect), so no further conversion happens here.
        /// </remarks>
        private static void PaintElementMarginBoxes(XGraphics g, RAdapter adapter, HtmlContainerInt htmlContainer, IReadOnlyList<MarginBoxFragment> marginBoxes)
        {
            var pixelsPerPoint = (adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            using var graphicsAdapter = new GraphicsAdapter(adapter, g, pixelsPerPoint);
            var painter = new FragmentPainter(htmlContainer);

            foreach (var marginBox in marginBoxes)
            {
                painter.PaintFragment(graphicsAdapter, marginBox.Content);
            }
        }

        /// <summary>
        /// Paints one page's css-gcpm-3 <c>float: footnote</c> note area - a hard-coded UA divider rule
        /// (no author styling of the area itself in this version; see docs/html-css-support.md's
        /// "Footnotes" section), then each footnote body. Mirrors <see cref="PaintElementMarginBoxes"/>
        /// exactly for the bodies (real, laid-out <see cref="CssBox"/> subtrees reused unmodified through
        /// <see cref="FragmentPainter.PaintFragment"/> - backgrounds/borders/nested styling and tagged-PDF
        /// structure attach for free, same reasoning) - the divider alone is drawn directly, the same
        /// simple filled-rectangle primitive <see cref="PeachPDF.Html.Core.Paint.Content.HrFragmentPainter"/>
        /// uses for <c>&lt;hr&gt;</c>.
        /// </summary>
        private static void PaintFootnoteArea(XGraphics g, RAdapter adapter, HtmlContainerInt htmlContainer, FootnoteAreaFragment footnoteArea)
        {
            var pixelsPerPoint = (adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            using var graphicsAdapter = new GraphicsAdapter(adapter, g, pixelsPerPoint);

            var rect = footnoteArea.DividerRect;
            graphicsAdapter.DrawRectangle(graphicsAdapter.GetSolidBrush(RColor.Black), rect.X, rect.Y, rect.Width, rect.Height);

            var painter = new FragmentPainter(htmlContainer);
            foreach (var body in footnoteArea.Bodies)
            {
                painter.PaintFragment(graphicsAdapter, body);
            }
        }

        /// <summary>
        /// Handle HTML links by create PDF Documents link either to external URL or to another page in the document.
        /// <paramref name="bookmarkBoxes"/> collects every bookmark-candidate box found along the way
        /// (see <see cref="Html.Core.Utils.DomUtils.GetAllLinkAndBookmarkBoxes"/>), for
        /// <see cref="BookmarkOutlineBuilder"/> to consume afterwards without a second full-tree walk.
        /// <paramref name="structureTagBuilder"/> is non-null only when tagged PDF output is enabled -
        /// used to attach each created Link annotation's "/OBJR" back to its owning "/Link" structure
        /// element (see the tagging-aware section below), completing the bidirectional PDF/UA
        /// linkage between the annotation and the structure tree.
        /// </summary>
        private static void HandleLinks(PdfDocument document, HtmlContainer container, XSize orgPageSize, IReadOnlyList<FragmentainerFragment> fragmentainers, List<CssBox> bookmarkBoxes, StructureTagBuilder? structureTagBuilder = null)
        {
            var inner = container.HtmlContainerInt;
            var ppp = container.PixelsPerPoint;

            var (slotToPage, maxMappedSlot) = PageAnchorResolver.BuildSlotToPageMap(fragmentainers);

            // An anchor inside a skipped (content-empty) slot attributes to the next materialized
            // page - the nearest place a reader can actually land. Shared by both link sources below
            // (the main document tree and, per §5.1, css-gcpm-3 running-element content) so they can
            // never disagree about which page an anchor lands on.
            (int PageIndex, double TopPt)? ResolveAnchorTarget(string anchorId)
            {
                // href="#" (an empty fragment, commonly used as a JS-driven no-op link) reaches here
                // as an empty anchorId - there is no element to target, so treat it the same as an
                // anchor whose target simply wasn't found rather than let the elementId-not-null-or-empty
                // guard inside GetElementRectangle throw.
                if (anchorId.Length == 0) return null;

                var anchorRect = container.GetElementRectangle(anchorId);
                return anchorRect.HasValue
                    ? PageAnchorResolver.ResolveRectToPage(inner, ppp, slotToPage, maxMappedSlot, fragmentainers.Count, anchorRect.Value)
                    : null;
            }

            foreach (var link in container.GetLinks(bookmarkBoxes))
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
                        var target = ResolveAnchorTarget(link.AnchorId);
                        if (target is not { } t) continue;

                        // /FitH top fits page width and positions vertically at top - the "jump down
                        // to this Y" behavior an anchor link destination actually wants; CreateFitVertically
                        // would instead pass TopPt as /FitV's horizontal left parameter. TopPt itself is
                        // top-down (0 at the page's own top margin, increasing downward, matching every
                        // other rect this pass works with - see the xRect construction below), but /FitH's
                        // own "top" parameter is PDF default user space, which is bottom-up (0 at the page's
                        // bottom edge) - flipping via orgPageSize.Height is required, exactly like the xRect
                        // construction below already does for the same reason.
                        document.AddNamedDestination(link.AnchorId, t.PageIndex + 1, PdfNamedDestinationParameters.CreateFitHorizontally(orgPageSize.Height - t.TopPt));
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

            HandleRunningElementLinks(document, container, orgPageSize, fragmentainers, ResolveAnchorTarget, structureTagBuilder);
        }

        /// <summary>
        /// Creates AcroForm fields/widgets for every classified &lt;input&gt;/&lt;select&gt; box, one
        /// per page. Mirrors <see cref="HandleLinks"/>'s own per-box rect-to-page resolution (matching
        /// its exact math, since both convert a post-layout box rect to page-space PDF points against
        /// the same slot geometry) rather than <see cref="HandleLinks"/>'s multi-slot loop, which
        /// exists only because a link's box can span a page break - a form-control box never can
        /// (see <c>MonolithicContent.IsReplaced</c>'s <c>CssBoxFormField</c> arm), so each
        /// field resolves to exactly one page and one rect.
        /// </summary>
        private static void HandleFormFields(PdfDocument document, HtmlContainer container, XSize orgPageSize, IReadOnlyList<FragmentainerFragment> fragmentainers, FormFieldBuilder formFieldBuilder)
        {
            var inner = container.HtmlContainerInt;
            var ppp = container.PixelsPerPoint;
            var (slotToPage, _) = PageAnchorResolver.BuildSlotToPageMap(fragmentainers);

            var fieldBoxes = new List<CssBox>();
            DomUtils.GetAllFormFieldBoxes(inner.Root, fieldBoxes);

            foreach (var box in fieldBoxes)
            {
                var classification = FormFieldMapper.Classify(box);
                if (classification.Kind == FormFieldKind.None)
                    continue;

                var pixelRect = CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds);

                var slot = Math.Max(inner.PageIndexOf(pixelRect.Top), 0);
                if (!slotToPage.TryGetValue(slot, out var pageIndex) || pageIndex >= document.Pages.Count)
                    continue;

                var slotGeom = inner.PageGeometry.GetPage(slot);
                var topPt = slotGeom.MarginTopPt + (pixelRect.Top - inner.PageTopOf(slot)) / ppp;
                var leftPt = slotGeom.MarginLeftPt + (pixelRect.Left - inner.MarginLeft) / ppp;
                var widthPt = pixelRect.Width / ppp;
                var heightPt = pixelRect.Height / ppp;

                if (widthPt <= 0 || heightPt <= 0)
                    continue;

                var xRect = new XRect(leftPt, orgPageSize.Height - (topPt + heightPt), widthPt, heightPt);
                formFieldBuilder.AddField(document.Pages[pageIndex], new PdfRectangle(xRect), box, classification);
            }
        }

        /// <summary>
        /// The second, per-page link source §5.1 of the implementation plan calls for: a link inside a
        /// css-gcpm-3 running element's content. <see cref="HandleLinks"/>'s own main loop is driven by
        /// <c>container.GetLinks()</c>, which walks only the live, main-tree <see cref="CssBox"/>
        /// geometry - a running box's own <c>&lt;a&gt;</c> is never flowed there
        /// (<see cref="CssBox.IsRunningPositioned"/> excludes it from the main pass entirely, see
        /// <c>CssBox.LayoutBlockChildren</c>), and the same <c>&lt;a&gt;</c> can legitimately produce a
        /// <i>different</i> annotation on every page its running element was selected onto (a different
        /// rect each time, since <c>RunningElementLayout</c> genuinely re-lays it out per page) - the
        /// single-Y slot-attribution model the main loop uses doesn't fit that at all. Each page's own
        /// captured <see cref="MarginBoxFragment.Content"/> already names exactly one page, with an
        /// already-final rect, so no slot-attribution is needed here - only the anchor-target resolution
        /// is shared, via <paramref name="resolveAnchorTarget"/>.
        /// </summary>
        private static void HandleRunningElementLinks(
            PdfDocument document,
            HtmlContainer container,
            XSize orgPageSize,
            IReadOnlyList<FragmentainerFragment> fragmentainers,
            Func<string, (int PageIndex, double TopPt)?> resolveAnchorTarget,
            StructureTagBuilder? structureTagBuilder)
        {
            var ppp = container.PixelsPerPoint;

            for (var pageIndex = 0; pageIndex < fragmentainers.Count && pageIndex < document.Pages.Count; pageIndex++)
            {
                var marginBoxes = fragmentainers[pageIndex].MarginBoxes;
                if (marginBoxes.Count == 0) continue;

                foreach (var marginBox in marginBoxes)
                {
                    foreach (var (fragment, box) in FindLinkFragments(marginBox.Content))
                    {
                        var href = box.HtmlTag?.TryGetAttribute("href") ?? "";
                        if (href.Length == 0) continue;

                        var rectPt = Utils.Convert(fragment.Rect, ppp);
                        var xRect = new XRect(rectPt.X, orgPageSize.Height - (rectPt.Y + rectPt.Height), rectPt.Width, rectPt.Height);

                        PdfLinkAnnotation annotation;

                        if (href[0] == '#')
                        {
                            var anchorId = href[1..];
                            var target = resolveAnchorTarget(anchorId);
                            if (target is not { } t) continue;

                            // See the same flip's comment in HandleLinks above - TopPt is top-down, /FitH's
                            // own top parameter needs bottom-up PDF default user space.
                            document.AddNamedDestination(anchorId, t.PageIndex + 1, PdfNamedDestinationParameters.CreateFitHorizontally(orgPageSize.Height - t.TopPt));
                            annotation = document.Pages[pageIndex].AddDocumentLink(new PdfRectangle(xRect), anchorId);
                        }
                        else
                        {
                            var baseElement = DomUtils.GetBoxByTagName(container.HtmlContainerInt.Root, "base");
                            var baseUrl = baseElement?.HtmlTag?.TryGetAttribute("href", "");
                            var baseUri = string.IsNullOrWhiteSpace(baseUrl) ? container.HtmlContainerInt.Adapter.BaseUri : new RUri(baseUrl);
                            var resolvedHref = baseUri is null ? href : new RUri(baseUri, href).AbsoluteUri;

                            annotation = document.Pages[pageIndex].AddWebLink(new PdfRectangle(xRect), resolvedHref);
                        }

                        // The same source box can legitimately gain more than one annotation here (once
                        // per page its running element was selected onto) - LinkAnnotationToStructureElement
                        // is already called once per annotation in the main loop above too, so a repeat
                        // call for the same box is an existing, exercised shape, not a new one.
                        if (structureTagBuilder != null)
                        {
                            structureTagBuilder.LinkAnnotationToStructureElement(box, document.Pages[pageIndex], annotation);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively collects every clickable, visible box fragment in <paramref name="fragment"/>'s
        /// subtree - the fragment-tree analog of <see cref="DomUtils.GetAllLinkBoxes"/>, which walks the
        /// live <see cref="CssBox"/> tree the same way but is unusable here since a running element's
        /// content never appears in that tree with meaningful geometry (see <see cref="HandleRunningElementLinks"/>).
        /// </summary>
        private static IEnumerable<(BoxFragment Fragment, CssBox Box)> FindLinkFragments(BoxFragment fragment)
        {
            if (fragment.Box is { IsClickable: true, Visibility.Value: Visibility.Visible })
            {
                yield return (fragment, fragment.Box);
            }

            foreach (var child in fragment.Children)
            {
                foreach (var found in FindLinkFragments(child))
                {
                    yield return found;
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