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
    /// A <see cref="PdfGenerator"/> instance is <b>not thread-safe</b>. Every font/brush/pen cache,
    /// font resolver, and network loader it uses is owned exclusively by that instance, so calling
    /// its methods concurrently from multiple threads on the <i>same</i> instance (or reusing one
    /// instance across overlapping renders) is not supported and can corrupt its internal state.
    /// </para>
    /// <para>
    /// Using a <b>separate <see cref="PdfGenerator"/> instance per thread</b> — e.g. one per
    /// incoming web request, or one per work item in a parallel batch — is safe and is the intended
    /// way to generate PDFs concurrently. Any state PeachPDF shares across instances (such as
    /// system font discovery) is synchronized internally for exactly this usage pattern.
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

            // while there is un-rendered HTML, create another PDF page and render with proper offset for the next page
            double scrollOffset = 0;
            int pageNumber = 0;
            var totalPages = (int)Math.Ceiling(container.ActualSize.Height / container.PageSize.Height);
            while (scrollOffset > -container.ActualSize.Height)
            {
                pageNumber++;

                // Content's own coordinate system starts at container.MarginTop, not 0 (see
                // container.Location = new XPoint(MarginLeft, MarginTop) in SetContent), so a page's
                // true content range is [pageY+MarginTop, pageY+MarginTop+PageSize.Height), not
                // [pageY, pageY+PageSize.Height) - omitting +MarginTop here misattributes content
                // landing in that leading margin-sized band to this page when it's still visually on
                // the previous one (only visible via multi-column's atomic "may overrun its nominal
                // row boundary" placement, since ordinary block flow never lands content there).
                var pageY = -scrollOffset + container.MarginTop;
                var applicableRule = SelectPageRule(
                    container.PageRules,
                    pageNumber,
                    container.NamedPageElements,
                    pageY,
                    container.PageSize.Height);
                var applicableMargins = SelectApplicableMarginRules(
                    container.PageRules,
                    pageNumber,
                    container.NamedPageElements,
                    pageY,
                    container.PageSize.Height);
                var applicablePageStyle = SelectApplicablePageStyle(
                    container.PageRules,
                    pageNumber,
                    container.NamedPageElements,
                    pageY,
                    container.PageSize.Height);

                var (mL, mT, mR, mB) = ResolvePageMargins(
                    applicableRule,
                    container.MarginLeft,
                    container.MarginTop,
                    container.MarginRight,
                    container.MarginBottom);

                var page = document.PdfDocument.AddPage();
                page.Height = orgPageSize.Height;
                page.Width = orgPageSize.Width;

                structureTagBuilder?.BeginPage(page);

                using var g = XGraphics.FromPdfPage(page);

                // Save state so the content transform can be undone for margin box rendering
                var preContentState = g.Save();

                g.IntersectClip(new XRect(mL, mT, page.Width - mL - mR, page.Height - mT - mB));

                var deltaX = mL - container.MarginLeft;
                var deltaY = mT - container.MarginTop;
                if (deltaX != 0 || deltaY != 0)
                    g.TranslateTransform(deltaX, deltaY);

                container.ScrollOffset = new XPoint(0, scrollOffset);
                await container.PerformPaint(g);

                // Restore to pre-content state so margin boxes render in absolute page coordinates
                g.Restore(preContentState);

                if (applicableMargins.Count > 0)
                {
                    MarginBoxRenderer.Render(
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
                        applicablePageStyle);
                }

                scrollOffset -= container.PageSize.Height;
            }

            // Finalizes /ParentTree page-keyed entries before HandleLinks (which, when tagging is
            // enabled, appends further annotation-keyed entries to the same tree - see
            // StructureTagBuilder.Finish and HandleLinks's own tagging-aware section below).
            structureTagBuilder?.Finish();

            // add web links and anchors
            HandleLinks(document.PdfDocument, container, orgPageSize, container.PageSize, structureTagBuilder);

            measure?.Dispose();
        }

        #region Private/Protected methods

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
        private static void HandleLinks(PdfDocument document, HtmlContainer container, XSize orgPageSize, XSize pageSize, StructureTagBuilder? structureTagBuilder = null)
        {
            foreach (var link in container.GetLinks())
            {
                var i = (int)(link.Rectangle.Top / pageSize.Height);
                for (; i < document.Pages.Count && pageSize.Height * i < link.Rectangle.Bottom; i++)
                {
                    var offset = pageSize.Height * i;

                    // position is from the bottom of the page
                    var xRect = new XRect(link.Rectangle.Left, orgPageSize.Height - (link.Rectangle.Height + link.Rectangle.Top - offset), link.Rectangle.Width, link.Rectangle.Height);

                    PdfLinkAnnotation annotation;

                    if (link.IsAnchor)
                    {
                        // create link to another page in the document
                        var anchorRect = container.GetElementRectangle(link.AnchorId);

                        if (!anchorRect.HasValue) continue;

                        var anchorPageNumber = 1;
                        var top = anchorRect.Value.Top;

                        while (top > pageSize.Height)
                        {
                            top -= pageSize.Height;
                            anchorPageNumber++;
                        }

                        document.AddNamedDestination(link.AnchorId, anchorPageNumber, PdfNamedDestinationParameters.CreateFitVertically(top));
                        annotation = document.Pages[i].AddDocumentLink(new PdfRectangle(xRect), link.AnchorId);
                    }
                    else
                    {
                        // create link to URL
                        annotation = document.Pages[i].AddWebLink(new PdfRectangle(xRect), link.Href);
                    }

                    if (structureTagBuilder != null && link.SourceBox != null)
                    {
                        structureTagBuilder.LinkAnnotationToStructureElement(link.SourceBox, document.Pages[i], annotation);
                    }
                }
            }
        }

        /// <summary>
        /// Selects the most specific @page rule for the given page.
        /// Priority (last wins): base → named page → :right/:left → :first.
        /// </summary>
        internal static PageRule? SelectPageRule(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight)
        {
            var ordered = GetOrderedApplicableRules(rules, pageNumber, namedPageElements, pageY, pageHeight);
            return ordered.Count > 0 ? ordered[^1] : null;
        }

        /// <summary>
        /// Resolves the effective set of margin-box declarations (<c>@top-left</c>, <c>@bottom-right</c>,
        /// etc.) for the given page — the CSS cascade for <c>@page</c> rules is per-declaration, not
        /// per-rule, so a page can (and, in css4.pub's real dictionary CSS, does) simultaneously match a
        /// low-specificity base named-page rule that defines <c>@top-left/@top-center/@top-right</c> AND
        /// a higher-specificity compound <c>name:left</c>/<c>name:right</c> rule that only defines
        /// <c>@bottom-left</c>/<c>@bottom-right</c>/<c>@right-top</c> — both sets of margin boxes must
        /// render together (merged by box name, with a more specific rule's own definition of a given
        /// box name winning over a less specific rule's), not just whichever single rule
        /// <see cref="SelectPageRule"/> would pick as "the" applicable one for page-level properties like
        /// <c>margin</c>/<c>size</c>.
        /// </summary>
        internal static IReadOnlyList<MarginStyleRule> SelectApplicableMarginRules(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight)
        {
            var ordered = GetOrderedApplicableRules(rules, pageNumber, namedPageElements, pageY, pageHeight);
            var merged = new Dictionary<string, MarginStyleRule>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in ordered)
            {
                foreach (var margin in rule.Margins)
                {
                    var name = margin.Selector?.Text?.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(name)) continue;

                    if (!merged.TryGetValue(name, out var mergedRule))
                    {
                        mergedRule = new MarginStyleRule(margin.Parser) { Selector = margin.Selector };
                        merged[name] = mergedRule;
                    }

                    // Per-declaration merge, not whole-rule replacement: a later (higher-precedence)
                    // rule's own properties win, but properties it doesn't redeclare survive from an
                    // earlier, less-specific rule for the same box name - matching real CSS cascade
                    // (and Prince), which resolves @page per-declaration like any other stylesheet rule.
                    MergeDeclarationsInto(mergedRule.Style, margin.Style);
                }
            }

            return merged.Values.ToList();
        }

        /// <summary>
        /// Resolves the effective, per-declaration-merged page-context style (the properties declared
        /// directly on matching <c>@page</c> rules themselves, not inside any margin-box block) for the
        /// given page. Per CSS Paged Media, margin boxes inherit these when they don't declare a property
        /// themselves - see <see cref="MarginBoxRenderer.Render"/>'s <c>pageStyle</c> parameter. Uses the
        /// same ascending-precedence merge as <see cref="SelectApplicableMarginRules"/>, independently of
        /// <see cref="SelectPageRule"/>'s single-winner selection (still used, unchanged, for page-level
        /// <c>margin</c>/<c>size</c> via <see cref="ResolvePageMargins"/>).
        /// </summary>
        internal static StyleDeclaration? SelectApplicablePageStyle(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight)
        {
            var ordered = GetOrderedApplicableRules(rules, pageNumber, namedPageElements, pageY, pageHeight);
            StyleDeclaration? merged = null;

            foreach (var rule in ordered)
            {
                if (rule.Style is null) continue;

                merged ??= new StyleDeclaration(rule.Parser);
                MergeDeclarationsInto(merged, rule.Style);
            }

            return merged;
        }

        /// <summary>
        /// Copies every declared property from <paramref name="source"/> into <paramref name="target"/>,
        /// overwriting same-named properties already present - the shared per-declaration merge step for
        /// <see cref="SelectApplicableMarginRules"/> and <see cref="SelectApplicablePageStyle"/>.
        /// </summary>
        private static void MergeDeclarationsInto(StyleDeclaration target, StyleDeclaration source)
        {
            foreach (var property in source.Declarations)
            {
                target.SetProperty(property);
            }
        }

        /// <summary>
        /// Every <c>@page</c> rule that applies to this page, in ascending cascade precedence (base rule
        /// first if present, then named/pseudo matches from lowest to highest specificity score —
        /// preserving declaration order among equal scores, so a later-declared rule still wins ties —
        /// then <c>:first</c> last, since it always outranks everything else per spec). Shared by
        /// <see cref="SelectPageRule"/> (single-winner page-level properties) and
        /// <see cref="SelectApplicableMarginRules"/> (per-margin-box-name cascade merge).
        /// </summary>
        private static List<PageRule> GetOrderedApplicableRules(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight)
        {
            var result = new List<PageRule>();
            if (rules.Count == 0)
                return result;

            PageRule? baseRule = null;
            PageRule? firstRule = null;
            var matches = new List<(PageRule Rule, int Score)>();

            // The CSS "page" property propagates forward through the normal flow until a later element
            // sets a different one — it isn't a one-page-only tag. So the name in effect for this page
            // is whichever named-page assignment most recently took effect at or before this page's end
            // (the highest Y that's still < pageY + pageHeight), not just an assignment whose own Y
            // happens to fall inside this specific page's range. The small epsilon guards against an
            // element's Y and the page boundary being computed via independent accumulation paths that
            // can differ by a hairline of floating-point noise (see MarginBoxRenderer.PageBoundaryEpsilon).
            var activeNamedPage = namedPageElements
                .Where(e => e.Y < pageY + pageHeight - MarginBoxRenderer.PageBoundaryEpsilon)
                .OrderByDescending(e => e.Y)
                .Select(e => e.Name)
                .FirstOrDefault();

            foreach (var rule in rules)
            {
                var entries = (rule.Selector as PageSelector)?.Entries;

                if (entries is not { Count: > 0 })
                {
                    baseRule = rule;
                    continue;
                }

                foreach (var entry in entries)
                {
                    // Page names are case-sensitive CSS custom-idents; pseudo-class keywords
                    // (first/left/right) are matched case-insensitively.
                    var nameMatches = entry.Name is null || entry.Name == activeNamedPage;
                    var pseudo = entry.Pseudo?.ToLowerInvariant();
                    var isFirst = pseudo == "first";

                    // ":first" (optionally combined with a matching name) always outranks every other
                    // selector shape, regardless of declaration order — per the CSS Paged Media spec,
                    // this is a special case, not part of the additive name/pseudo specificity score
                    // below (a compound "chapter1:first" still requires the name to match; a bare
                    // ":first" applies unconditionally on page 1).
                    if (isFirst)
                    {
                        if (nameMatches && pageNumber == 1)
                            firstRule = rule;
                        continue;
                    }

                    var pseudoMatches = pseudo switch
                    {
                        null => true,
                        "left" => pageNumber % 2 == 0,
                        "right" => pageNumber % 2 != 0,
                        _ => false
                    };

                    if (!nameMatches || !pseudoMatches) continue;

                    // Specificity: name+pseudo(left/right) > name-alone > pseudo(left/right)-alone.
                    var score = (entry.Name != null ? 2 : 0) + (entry.Pseudo != null ? 1 : 0);
                    matches.Add((rule, score));
                }
            }

            if (baseRule != null) result.Add(baseRule);
            // OrderBy is a stable sort — equal-score matches keep their original (declaration) order, so
            // the later-declared one still ends up last (highest precedence), matching the prior single-
            // winner behavior's ">=" tie-break.
            result.AddRange(matches.OrderBy(m => m.Score).Select(m => m.Rule));
            if (firstRule != null) result.Add(firstRule);

            return result;
        }

        private static (double L, double T, double R, double B) ResolvePageMargins(
            PageRule? rule, double baseL, double baseT, double baseR, double baseB)
        {
            if (rule == null) return (baseL, baseT, baseR, baseB);
            var s = rule.Style;
            return (
                DomParser.ParseLengthToPdfPoints(s.MarginLeft)   ?? baseL,
                DomParser.ParseLengthToPdfPoints(s.MarginTop)    ?? baseT,
                DomParser.ParseLengthToPdfPoints(s.MarginRight)  ?? baseR,
                DomParser.ParseLengthToPdfPoints(s.MarginBottom) ?? baseB
            );
        }

        #endregion
    }
}