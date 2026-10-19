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

using PeachPDF.Rendering;
using PeachDrawing.Text;
using PeachDrawing.PDF;
using PeachPDF.CSS;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Annotations;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.PdfFeatures;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Html.Core.Parse;
using System;
using System.Linq;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Layout;
using PeachPDF.Network;
using PeachPDF.Utilities;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Annotations;
using System.Collections.Generic;
using System.Numerics;
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
        private readonly HtmlRenderContext _renderContext = new();

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

            _renderContext.AddFontFamilyMapping(fromFamily, toFamily);
        }

        /// <summary>
        /// Add a font to be rendered
        /// </summary>
        /// <param name="stream">Font stream</param>
        public async Task AddFontFromStream(Stream stream)
        {
            await _renderContext.AddFont(stream, null);
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
            // A caller from code that is not nullable-aware may still pass null, which has always meant "no restriction".
            List<RuneInterval>? ranges = null;
            if (unicodeRanges is not null)
            {
                ranges = new List<RuneInterval>(unicodeRanges.Count);
                foreach (var range in unicodeRanges)
                    ranges.Add(new RuneInterval(range.Start, range.End));
            }

            await _renderContext.AddFont(stream, null, default, ranges);
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
            var cssData = await CssData.Parse(_renderContext, stylesheet, combineWithDefault);
            return new PeachPdfCssContent(cssData, _renderContext);
        }

        /// <summary>
        /// Genuinely zero-copy counterpart of <see cref="ParseStyleSheet(string, bool)"/> - see
        /// the parser's own remarks
        /// for why <see cref="ReadOnlyMemory{T}"/>, not <see cref="ReadOnlySpan{T}"/>, is what makes this
        /// possible.
        /// </summary>
        /// <param name="stylesheet">the stylesheet source to parse</param>
        /// <param name="combineWithDefault">true - combine the parsed css data with default css data, false - return only the parsed css data</param>
        /// <returns>the parsed css data</returns>
        public async Task<PeachPdfCssContent> ParseStyleSheet(ReadOnlyMemory<char> stylesheet, bool combineWithDefault = true)
        {
            var cssData = await CssData.Parse(_renderContext, stylesheet, combineWithDefault);
            return new PeachPdfCssContent(cssData, _renderContext);
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
            var document = new PeachPdfDocument(new PdfComposer(_renderContext));

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
        /// Validates the raster backend's settings and hands them to the adapter every <see cref="Canvas"/> reads
        /// them from. Called wherever <see cref="PdfGenerateConfig.PixelsPerInch"/> is applied, so the two always agree.
        /// </summary>
        private void ApplyRasterizationSettings(PdfGenerateConfig config)
        {
            if (!(config.RasterizationDpi >= 72 && config.RasterizationDpi <= 1200))
            {
                throw new ArgumentOutOfRangeException(nameof(config), config.RasterizationDpi,
                    "PdfGenerateConfig.RasterizationDpi must be between 72 and 1200 pixels per inch.");
            }

            if (config.MaxRasterPixels < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(config), config.MaxRasterPixels,
                    "PdfGenerateConfig.MaxRasterPixels must be at least 1.");
            }

            if (!Enum.IsDefined(config.TextHinting))
            {
                throw new ArgumentOutOfRangeException(nameof(config), config.TextHinting,
                    "PdfGenerateConfig.TextHinting is not a defined TextHinting value.");
            }

            _renderContext.RasterizationDpi = config.RasterizationDpi;
            _renderContext.MaxRasterPixels = config.MaxRasterPixels;
            _renderContext.TextHinting = config.TextHinting;
            _renderContext.TextStemDarkening = config.TextStemDarkening;
            _renderContext.RasterAntiAliasing = config.RasterAntiAliasing;
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
            // First, so a bad value changes nothing: not the document's options, not the network, and not an
            // empty-html call that would otherwise return before reaching it.
            ValidateAnimationProgress(config);

            // get the size of each page to layout the HTML in
            var orgPageSize = config.PageSize != PageSize.Undefined ? PageSizeConverter.ToSize(config.PageSize.ToEngine()) : new Size(config.ManualPageWidth, config.ManualPageHeight);

            if (config.PageOrientation == PageOrientation.Landscape)
            {
                // invert pagesize for landscape
                orgPageSize = new Size(orgPageSize.Height, orgPageSize.Width);
            }

            if (string.IsNullOrEmpty(html) && config.NetworkLoader is null) return;

            _renderContext.NetworkLoader = config.NetworkLoader ?? new DataUriNetworkLoader();
            _renderContext.AllowLocalFileAccess = config.AllowLocalFileAccess;
            _renderContext.PixelsPerPoint = config.PixelsPerInch / 72d;
            ApplyRasterizationSettings(config);

            html ??= await _renderContext.NetworkLoader.GetPrimaryContents();

            using var container = new HtmlContainer(_renderContext);

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

            var measure = PdfGraphics.CreateMeasureContext(container.PageSize);

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
                container.MaxSize = new Size(container.PageSize.Width, 0);
                await container.PerformLayout(measure);

                var actualWidth = container.ActualSize.Width;

                var candidatePixelsPerPoint = pixelsPerPoint * (actualWidth / container.PageSize.Width);

                if (candidatePixelsPerPoint < minPixelsPerPoint)
                {
                    candidatePixelsPerPoint = minPixelsPerPoint;
                }

                var effectivePixelsPerPoint = (config.ShrinkToFit && candidatePixelsPerPoint > 1) || config.ScaleToPageSize
                    ? candidatePixelsPerPoint
                    : _renderContext.PixelsPerPoint;

                needsRescale = NeedsRescale(_renderContext.PixelsPerPoint, effectivePixelsPerPoint);

                if (needsRescale)
                {
                    _renderContext.ClearFontCache();
                    _renderContext.PixelsPerPoint = effectivePixelsPerPoint;

                    await SetContent(container, config, html, cssData, orgPageSize);

                    measure?.Dispose();
                    measure = PdfGraphics.CreateMeasureContext(container.PageSize);
                }
            }

            if (needsRescale)
            {
                container.MaxSize = new Size(container.PageSize.Width, 0);

                // layout the HTML with the page width restriction to know how many pages are required
                await container.PerformLayout(measure);
            }

            await RenderPagesCore(document, container, config);

            measure?.Dispose();
        }

        /// <summary>
        /// Creates a PDF document by building PeachPDF's own internal box tree directly in C#, via
        /// <paramref name="handler"/>, instead of parsing HTML/CSS - a QuestPDF-style declarative
        /// alternative to <see cref="GeneratePdf(string?,PdfGenerateConfig,PeachPdfCssContent?)"/> for a
        /// caller that wants a code-first API. <paramref name="handler"/> never receives, generates, or
        /// parses any HTML/CSS text; every style is set directly as a typed value
        /// (<see cref="PdfLength"/>/<see cref="PdfColor"/>/etc.).
        /// </summary>
        /// <param name="handler">Builds the document's pages via the given <see cref="IDocumentBuilder"/>.</param>
        /// <param name="config">
        /// The configuration to use for generation (page size/orientation/margins/etc. - see
        /// <see cref="PdfGenerateConfig"/>). A page built by <see cref="IPageDescriptor"/> that never
        /// calls <c>.Size(...)</c>/<c>.Margin*(...)</c> falls back to this config's own page geometry,
        /// exactly like an HTML document's own <c>@page</c> rule falls back to it. Defaults to A4 with
        /// 20pt margins when omitted - unlike <see cref="PdfGenerateConfig"/>'s own bare field defaults
        /// (<see cref="PageSize.Undefined"/>, a 0pt page with no margins), which exist so the HTML path's
        /// <c>@page</c> rule can tell "unset" from "explicitly zero"; a declarative document has no
        /// <c>@page</c> rule to defer to, so <c>CreateDocument(handler)</c> with no config at all needs a
        /// usable page size on its own to be a genuinely code-first, zero-config entry point.
        /// </param>
        public async Task<PeachPdfDocument> CreateDocument(Action<IDocumentBuilder> handler, PdfGenerateConfig? config = null)
        {
            var document = new PeachPdfDocument(new PdfComposer(_renderContext));
            await AddPages(document, handler, config);
            return document;
        }

        /// <summary>
        /// Builds a declarative document's pages via <paramref name="handler"/> (see
        /// <see cref="CreateDocument"/>) and appends them to <paramref name="document"/> - the
        /// declarative counterpart of <see cref="AddPdfPages(PeachPdfDocument, string?, PdfGenerateConfig, PeachPdfCssContent?)"/>,
        /// callable more than once to add further declaratively-built pages to the same document.
        /// </summary>
        public async Task AddPages(PeachPdfDocument document, Action<IDocumentBuilder> handler, PdfGenerateConfig? config = null)
        {
            ArgumentNullException.ThrowIfNull(handler);
            config ??= new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                MarginTop = 20,
                MarginBottom = 20,
                MarginLeft = 20,
                MarginRight = 20
            };

            ValidateAnimationProgress(config);

            // Collected synchronously first (the builder callback itself is synchronous, matching
            // QuestPDF's own declarative-composition-then-execution shape), then each page's own tree is
            // built/laid out/rendered in turn below, since that part is genuinely asynchronous (image
            // loading, layout).
            var documentBuilder = new DocumentBuilder();
            handler(documentBuilder);

            var properties = new CssPropertyFactory(_renderContext);

            foreach (var pageHandler in documentBuilder.PageHandlers)
            {
                await AddDeclarativePage(document, pageHandler, config, properties, documentBuilder.DocumentStylesheet);
            }
        }

        /// <summary>
        /// Builds, lays out and renders exactly one <see cref="IDocumentBuilder.Page"/> call's own content
        /// as one or more physical PDF pages appended to <paramref name="document"/> - mirrors
        /// <see cref="AddPdfPages(PeachPdfDocument, string?, PdfGenerateConfig, PeachPdfCssContent?)"/>'s
        /// own shape (resolve page geometry, populate the container, lay out, then
        /// <see cref="RenderPagesCore"/>), but populates the container from an already-built
        /// <see cref="CssBox"/> tree (<see cref="HtmlContainer.SetDeclarativeRoot"/>) instead of parsing
        /// HTML. Each <see cref="IDocumentBuilder.Page"/> call is independent - its own page size/margins,
        /// its own <see cref="HtmlContainer"/>, its own layout pass - exactly like a separate
        /// <see cref="AddPdfPages(PeachPdfDocument, string?, PdfGenerateConfig, PeachPdfCssContent?)"/>
        /// call appending to the same document would be.
        /// </summary>
        private async Task AddDeclarativePage(PeachPdfDocument document, Action<IPageDescriptor> pageHandler, PdfGenerateConfig config, CssPropertyFactory properties, PeachPdfCssContent? stylesheet)
        {
            // The factory is shared by every page of the document, and the fragments this page's builder
            // callback splices deposit their display: contents shells on it - only this page's, so the
            // previous page's (which belong to a container already disposed) must not come along.
            properties.DisplayContentsShells.Clear();

            var pageDescriptor = DocumentBuilder.BuildPage(pageHandler, properties);
            ResolvePendingAutoDirections(pageDescriptor.RootBox, properties);

            var orgPageSize = pageDescriptor.PageSizeOverride
                ?? (config.PageSize != PageSize.Undefined
                    ? PageSizeConverter.ToSize(config.PageSize.ToEngine())
                    : new Size(config.ManualPageWidth, config.ManualPageHeight));

            if ((pageDescriptor.OrientationOverride ?? config.PageOrientation) == PageOrientation.Landscape)
            {
                orgPageSize = new Size(orgPageSize.Height, orgPageSize.Width);
            }

            _renderContext.NetworkLoader = config.NetworkLoader ?? new DataUriNetworkLoader();
            _renderContext.AllowLocalFileAccess = config.AllowLocalFileAccess;
            _renderContext.PixelsPerPoint = config.PixelsPerInch / 72d;
            ApplyRasterizationSettings(config);

            using var container = new HtmlContainer(_renderContext);

            container.MarginTop = pageDescriptor.MarginTopOverride ?? config.MarginTop;
            container.MarginBottom = pageDescriptor.MarginBottomOverride ?? config.MarginBottom;
            container.MarginLeft = pageDescriptor.MarginLeftOverride ?? config.MarginLeft;
            container.MarginRight = pageDescriptor.MarginRightOverride ?? config.MarginRight;
            container.HtmlContainerInt.PageRules = DomParser.BuildDeclarativePageRules(
                pageDescriptor.PageRules, stylesheet?.CssData,
                marginLeftIsExplicit: pageDescriptor.MarginLeftOverride is not null,
                marginTopIsExplicit: pageDescriptor.MarginTopOverride is not null,
                marginRightIsExplicit: pageDescriptor.MarginRightOverride is not null,
                marginBottomIsExplicit: pageDescriptor.MarginBottomOverride is not null,
                sizeIsExplicit: pageDescriptor.PageSizeOverride is not null);

            container.PageSize = orgPageSize;
            container.HtmlContainerInt.SnapBoxDecorationsToCssPixels = config.SnapBoxDecorationsToCssPixels;
            container.HtmlContainerInt.SnapBorderWidthsToCssPixels = config.SnapBorderWidthsToCssPixels;
            await container.SetDeclarativeRoot(pageDescriptor.RootBox, config.DefaultLanguage, stylesheet, properties.DisplayContentsShells);

            if (stylesheet is not null)
            {
                // Track A: a document-level stylesheet's own base @page rule may still adjust whole-page
                // geometry, but only for an edge/size the page builder itself left unset - see
                // IDocumentBuilder.Stylesheet's own precedence doc comment. Run after SetDeclarativeRoot
                // (needs root.HtmlContainer set, for em/rem-relative margin resolution via
                // CssBox.GetEmHeight/GetRemHeight).
                DomParser.CascadeApplyPageStyles(container.HtmlContainerInt, pageDescriptor.RootBox, stylesheet.CssData,
                    allowMarginLeft: pageDescriptor.MarginLeftOverride is null,
                    allowMarginTop: pageDescriptor.MarginTopOverride is null,
                    allowMarginRight: pageDescriptor.MarginRightOverride is null,
                    allowMarginBottom: pageDescriptor.MarginBottomOverride is null,
                    allowSize: pageDescriptor.PageSizeOverride is null);

                // Keep the local sheet-size variable in sync with any in-place correction the call above just
                // made, mirroring AddPdfPages's own identical "orgPageSize = container.CssPageSize.Value"
                // sync after SetContent/SetHtml - everything below this point (the content-area computation,
                // the measure context) must see the corrected size, not the originally-configured one.
                if (container.CssPageSize.HasValue)
                {
                    orgPageSize = container.CssPageSize.Value;
                }
            }

            // Mirrors SetContent's own tail: the content page size is the sheet less its margins, and
            // the document origin sits at the top-left of that band.
            var contentPageSize = new Size(
                orgPageSize.Width - container.MarginLeft - container.MarginRight,
                orgPageSize.Height - container.MarginTop - container.MarginBottom);
            container.PageSize = contentPageSize;
            container.Location = new PaintPoint(container.MarginLeft, container.MarginTop);

            using var measure = PdfGraphics.CreateMeasureContext(orgPageSize);
            container.MaxSize = new Size(container.PageSize.Width, 0);
            await container.PerformLayout(measure);

            await RenderPagesCore(document, container, config);
        }

        /// <summary>
        /// Resolves every <see cref="CssBox.PendingAutoDirection"/> box under <paramref name="root"/> to a
        /// literal <c>ltr</c>/<c>rtl</c> <c>direction</c>, once over the whole page's already-built tree -
        /// mirrors <see cref="DomParser"/> resolving the HTML path's own <c>dir="auto"</c> once over a
        /// whole freshly-parsed document (<see cref="DomParser.GenerateCssTree"/>'s own call to its
        /// <c>ResolveAutoDirectionality</c>), just triggered here instead of from the parser, since a
        /// declaratively-built tree has no parse step of its own to hook. Unlike that HTML path (which
        /// writes a literal <c>dir</c> attribute back onto the element and lets the UA stylesheet's
        /// <c>[dir]</c> attribute-selector apply <c>direction</c> through the normal cascade), this sets the
        /// resolved value directly via <paramref name="properties"/> instead - runs before
        /// <see cref="HtmlContainer.SetDeclarativeRoot"/>'s own optional stylesheet-matching pass (see
        /// <see cref="IDocumentBuilder.Stylesheet"/>), so the result is tracked in
        /// <see cref="CssBox.BuilderSetProperties"/> and a plain (non-<c>!important</c>) stylesheet
        /// <c>direction</c> rule cannot silently override it, the same as any other builder-set property.
        /// </summary>
        internal static void ResolvePendingAutoDirections(CssBox root, CssPropertyFactory properties)
        {
            if (root.PendingAutoDirection)
            {
                properties.Set(root, "direction", BidiDirectionalityResolver.ScanForStrongDirection(root) ?? Keywords.Ltr);
                root.PendingAutoDirection = false;
            }

            foreach (var child in root.Boxes)
            {
                ResolvePendingAutoDirections(child, properties);
            }
        }

        /// <summary>
        /// The content-source-agnostic half of page generation: metadata/XMP/output-intent/tagging/form
        /// setup, one PDF page per fragmentainer (paint, footnote area, margin boxes), and the
        /// whole-document link/form-field/bookmark passes at the end. Reads only already-resolved
        /// <paramref name="container"/> state (its fragment tree, page rules, document metadata/language) -
        /// nothing here is specific to how that container's content tree was built, so this is shared
        /// verbatim between the HTML path (<see cref="AddPdfPages(PeachPdfDocument, string?, PdfGenerateConfig, PeachPdfCssContent?)"/>,
        /// after its own <see cref="SetContent"/>/<c>ShrinkToFit</c> setup above) and the declarative
        /// document-building path that builds a <see cref="Html.Core.Dom.CssBox"/> tree directly instead
        /// of parsing HTML. The caller owns its own measure context and disposes it after this returns.
        /// </summary>
        private async Task RenderPagesCore(PeachPdfDocument document, HtmlContainer container, PdfGenerateConfig config)
        {
            // Whole-document format, document information, and what the claimed conformance level requires of the file (the output
            // intent, the XMP packet, embedded files, the language) - established once, and checked against any earlier call on this
            // document. Everything that depends on this render's content (title, language, creation date) is known by now.
            var embeddings = PdfEmbeddingPlan.Create(config);
            document.Composer.Configure(BuildComposerOptions(config, container, embeddings));

            // PDF/A-1a/2a/3a build on tagged-PDF output (StructureTagBuilder below) and additionally require a real document language,
            // which Configure has already checked.
            var isAccessibleConformance = config.PdfAConformance is
                PdfAConformance.PdfA1A or PdfAConformance.PdfA2A or PdfAConformance.PdfA3A;

            // Only constructed when tagging is enabled - CssBox.PaintImp's tagging wrapper checks
            // HtmlContainerInt.StructureTagBuilder for null and skips all classification/bookkeeping
            // when it's not set, so this is the single point that gates the whole feature off by
            // default. An accessible PdfAConformance level forces this on even if EnableTaggedPdf was
            // left false - config itself is never mutated.
            var structureTagBuilder = (config.EnableTaggedPdf || isAccessibleConformance)
                ? new StructureTagBuilder(document.PdfDocument, requireAltText: isAccessibleConformance)
                : null;
            container.HtmlContainerInt.StructureTagBuilder = structureTagBuilder;

            // Same shape as structureTagBuilder above: only constructed when interactive PDF forms are
            // enabled - FragmentContentPainters.For and HandleFormFields (below) both check
            // HtmlContainerInt.FormFieldBuilder for null and skip all classification/bookkeeping when
            // it's not set, so this is the single point that gates the whole feature off by default.
            var formFieldBuilder = config.EnableInteractivePdfForms ? new FormFieldBuilder(document.PdfDocument, container.HtmlContainerInt.RenderContext) : null;
            container.HtmlContainerInt.InteractiveForms = formFieldBuilder != null;

            // Per CSS2.1 §14.2 the "canvas" (here: every page) is filled with body's background if it
            // declares one, else html's. Resolved during layout, since the fragment tree's own
            // page-materialization rule depends on it.
            var canvasBackgroundBox = container.HtmlContainerInt.CanvasBackgroundBox;

            // Margin-box `content: url(...)` images (see MarginBoxRenderer.ResolveContentImage) are
            // cached by declaration text across the whole document, since the same margin rule - and
            // so the same image - repeats identically on every page; without this a multi-hundred-page
            // document would re-decode (or re-fetch, for a network image) the same logo once per page.
            var marginBoxImageCache = new Dictionary<string, CssImage?>();

            // @page box background image layers (LayeredBackgroundPainter) and margin-box background
            // image layers share this same per-document cache shape/rationale - the same declaration
            // (the same image) repeats identically on every page/every margin-box instance.
            var pageBackgroundImageCache = new Dictionary<string, IReadOnlyList<CssImage>?>();
            var marginBoxBackgroundImageCache = new Dictionary<string, IReadOnlyList<CssImage>?>();

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
                var scrollOffset = -fragmentainer.LocalOriginY;

                // The single source of truth for this slot's margins and band: the same geometry
                // table layout paginated against, carried on the fragmentainer itself (already
                // corrected against this slot's materialized page number, when a content-empty gap
                // skipped earlier made that differ from its raw grid number - see
                // HtmlContainerInt.LayoutMarginBoxes and issue #148), so paint can never disagree with
                // layout about a page's content band. Margins come out in true points (the space the
                // clip/translate below and MarginBoxRenderer use); Top/BandHeight are internal-pixel
                // document space (the space NamedPageElement Ys live in), which also fixes the
                // historical ShrinkToFit drift where pageY mixed a pixel-space slot top with a
                // point-space MarginTop for named-page attribution.
                var geom = fragmentainer.Geometry;
                var pageY = geom.Top;
                var isBlankPage = container.HtmlContainerInt.IsReservedBlankSlot(fragmentainer.SlotIndex);
                var applicableMargins = SelectApplicableMarginRules(
                    container.PageRules,
                    pageNumber,
                    container.NamedPageElements,
                    pageY,
                    geom.BandHeight,
                    isBlankPage);
                var applicablePageStyle = SelectApplicablePageStyle(
                    container.PageRules,
                    pageNumber,
                    container.NamedPageElements,
                    pageY,
                    geom.BandHeight,
                    isBlankPage);

                var (mL, mT, mR, mB) = (geom.MarginLeftPt, geom.MarginTopPt, geom.MarginRightPt, geom.MarginBottomPt);
                // The page box's own resolved border/padding (issue #1147) - all zero for a page with no
                // @page border/padding declared, which is exactly the pre-#1147 behavior.
                var (bL, bT, bR, bB) = (geom.BorderLeftPt, geom.BorderTopPt, geom.BorderRightPt, geom.BorderBottomPt);
                var (pL, pT, pR, pB) = (geom.PaddingLeftPt, geom.PaddingTopPt, geom.PaddingRightPt, geom.PaddingBottomPt);

                var page = document.PdfDocument.AddPage();
                // This slot's own resolved physical sheet size, in true PDF points - already falls back
                // to the document's base/configured size (orgPageSize) whenever no @page rule overrides
                // `size` for this slot, so every existing (non-mixed-size) document is unaffected.
                page.Height = geom.SheetHeightPt;
                page.Width = geom.SheetWidthPt;

                structureTagBuilder?.BeginPage(page);

                using var g = PdfGraphics.FromPdfPage(page);

                var sheetRect = new Rect(0, 0,
                    page.Width * _renderContext.PixelsPerPoint, page.Height * _renderContext.PixelsPerPoint);

                // The page box's own border-box/padding-box/content-box rects (css-page-3 §3's box
                // model), resolved in true PDF points then converted to px - the same border-box ->
                // padding-box -> content-box shrink chain MarginBoxRenderer.PaintBackgroundAndBorder
                // already uses for a page-MARGIN-box's own background/border, reused here via the same
                // MarginBoxRenderer.Shrink primitive rather than re-deriving the arithmetic (issue
                // #1147). border-box is the sheet minus this page's own margin only - background/border
                // never extend into the margin area (margin never participates in background painting
                // for ANY CSS box), unlike the canvas fill below, which deliberately does.
                var sheetRectPt = new Rect(0, 0, geom.SheetWidthPt, geom.SheetHeightPt);
                var borderBoxRectPt = MarginBoxRenderer.Shrink(sheetRectPt, mL, mT, mR, mB);
                var paddingBoxRectPt = MarginBoxRenderer.Shrink(borderBoxRectPt, bL, bT, bR, bB);
                var contentBoxRectPt = MarginBoxRenderer.Shrink(paddingBoxRectPt, pL, pT, pR, pB);

                var borderBoxRect = Utils.ToPixels(borderBoxRectPt, _renderContext.PixelsPerPoint);
                var paddingBoxRect = Utils.ToPixels(paddingBoxRectPt, _renderContext.PixelsPerPoint);
                var contentBoxRect = Utils.ToPixels(contentBoxRectPt, _renderContext.PixelsPerPoint);

                Rect ResolvePagePositioningRect(string value) => value switch
                {
                    Keywords.ContentBox => contentBoxRect,
                    Keywords.BorderBox => borderBoxRect,
                    // padding-box is the initial value (CSS Backgrounds 3 §3.9/§3.10) and what an
                    // unrecognized/empty value falls back to - matching MarginBoxRenderer's own
                    // ResolvePositioningRect for a page-margin box's background.
                    _ => paddingBoxRect,
                };

                // Same "no real inheritance chain" em-basis convention MarginBoxRenderer.ResolveFontSizePt's
                // own StyleDeclaration overload uses - falls back to DefaultFontResolver.FontSize, never
                // null, so a gradient/border-width with no @page font-size resolves against the same
                // page-box-appropriate basis a margin-box's own would, not the document root's own
                // (possibly quite different) actual font-size. Shared between the page background
                // (gradient em-sizing) and the page border (below) so both agree on the same basis.
                var pageFontSizeStr = applicablePageStyle?.FontSize;
                var pageEmSizePt = string.IsNullOrEmpty(pageFontSizeStr)
                    ? DefaultFontResolver.FontSize
                    : MarginBoxRenderer.ResolveFontSizePt(pageFontSizeStr);
                var pageRemPt = container.HtmlContainerInt.PageLengthContext?.RemPt ?? DefaultFontResolver.FontSize;

                // css-page-3 §3.1's page layer paint order is (bottommost first): page background,
                // document canvas, page borders, document contents, page-margin boxes. This paints the
                // @page box's own background - the bottommost layer - one step before the canvas fill
                // below, so an opaque canvas background naturally occludes it (and a transparent/absent
                // one lets it show through) with no extra precedence logic needed. background-origin/
                // -clip now (issue #1147) genuinely distinguish border-box/padding-box/content-box via
                // this page's own resolved border/padding, mirroring how a page-margin box's own
                // background already does.
                if (container.HtmlContainerInt.Root is { } rootBoxForPageBackground && applicablePageStyle is not null)
                {
                    using var pageBackgroundGraphics = new PdfCanvas(_renderContext, g, _renderContext.PixelsPerPoint);

                    await LayeredBackgroundPainter.PaintAsync(
                        pageBackgroundGraphics, applicablePageStyle, _renderContext, container.HtmlContainerInt,
                        rootBoxForPageBackground, pageEmSizePt,
                        resolvePositioningRect: ResolvePagePositioningRect,
                        viewportRect: sheetRect,
                        pageBackgroundImageCache);
                }

                if (canvasBackgroundBox != null)
                {
                    // Must paint before the content clip below is applied (page.304's IntersectClip),
                    // so the fill reaches the true full page bleed (including the margin-box area), not
                    // just the content rect. The canvas fill is a distinct paint step from the page box's
                    // own background above - CSS2.1 §14.2's canvas propagation always fills the whole
                    // sheet regardless of the page box's own border/padding, exactly as before #1147.
                    using var canvasGraphics = new PdfCanvas(_renderContext, g, _renderContext.PixelsPerPoint);
                    FragmentPainter.PaintCanvasBackground(canvasGraphics, canvasBackgroundBox, sheetRect);
                }

                // css-page-3 §3.1's third paint layer: the page box's OWN border (issue #1147) - a
                // distinct layer from a page-MARGIN-box's own border (MarginBoxRenderer.PaintBorder,
                // painted later alongside the rest of that margin box's own content), sitting between
                // the document canvas and the document's own content. Painted at borderBoxRect (the
                // sheet minus this page's own margin only) using the same non-mitred four-independent-
                // edge approach a margin box's border already uses (PaintBorder itself) - a physical page
                // never fragments/shares a corner with a neighboring page either.
                if (applicablePageStyle is not null && (bL > 0 || bT > 0 || bR > 0 || bB > 0))
                {
                    using var pageBorderGraphics = new PdfCanvas(_renderContext, g, _renderContext.PixelsPerPoint);
                    MarginBoxRenderer.PaintBorder(
                        pageBorderGraphics, borderBoxRect, applicablePageStyle,
                        pageEmSizePt, pageRemPt, _renderContext.PixelsPerPoint, _renderContext);
                }

                // Save state so the content transform can be undone for margin box rendering
                var preContentState = g.Save();

                // No content-area clip pushed here (an earlier version of this method intersected one
                // directly on the raw PdfGraphics, ahead of FragmentPainter.Paint's own push below) - a raw
                // PdfGraphics.IntersectClip call made here is invisible to the Canvas abstraction's own
                // clip-stack bookkeeping, which is what Canvas.SuspendClipping walks to let a
                // position: fixed box's own paint reach back out to the full page box (margins included,
                // its actual containing block per CSS2.1 §10.1). Baking the content clip in here put it
                // permanently out of that call's reach, silently discarding every fixed box's own geometry
                // that legitimately fell inside the page's margins. FragmentPainter.Paint pushes the
                // equivalent content-area clip itself now (HtmlContainerInt.PageClipOverride, set below) -
                // PaintFootnoteArea, which paints outside that wrapper, pushes its own.

                // Content is anchored at the base MarginLeft/MarginTop in layout space; this delta maps
                // that anchor onto THIS page's own physical content-box origin - margin, and (since
                // issue #1147) the page box's own border/padding too, all three of which sit between the
                // sheet edge and the content that pagination itself already reserved this room for
                // (geom.BandWidth/BandHeight).
                var deltaX = mL + bL + pL - container.MarginLeft;
                var deltaY = mT + bT + pT - container.MarginTop;
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
                container.HtmlContainerInt.PageClipOverride = new Rect(
                    container.HtmlContainerInt.MarginLeft,
                    container.HtmlContainerInt.MarginTop,
                    (page.Width - mL) * _renderContext.PixelsPerPoint,
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
                // A page can carry more than one: its own note area, plus one per column any
                // float-reference: column call landed in. Each already knows its own rect, so nothing
                // here needs to distinguish them.
                foreach (var footnoteArea in fragmentainer.FootnoteAreas ?? [])
                {
                    PaintFootnoteArea(g, _renderContext, container.HtmlContainerInt, footnoteArea);
                }

                // Restore to pre-content state so margin boxes render in absolute page coordinates
                g.Restore(preContentState);

                if (applicableMargins.Count > 0)
                {
                    using var marginBoxCanvas = new PdfCanvas(_renderContext, g, _renderContext.PixelsPerPoint);
                    await MarginBoxRenderer.Render(
                        marginBoxCanvas,
                        new Size(page.Width, page.Height),
                        mL,
                        mT,
                        mR,
                        mB,
                        applicableMargins,
                        pageNumber,
                        totalPages,
                        pageY,
                        container.NamedStrings,
                        _renderContext,
                        applicablePageStyle,
                        container.HtmlContainerInt,
                        marginBoxImageCache,
                        marginBoxBackgroundImageCache);
                }

                // A rule can be content:element(...) with no matching position:running(...) anywhere (yet)
                // - HtmlContainerInt.LayoutMarginBoxes then creates no MarginBoxFragment for it, but its
                // own background/border must still paint (PaintElementMarginBoxes' own second loop), so
                // this can't gate on fragmentainer.MarginBoxes alone.
                var hasElementMarginBoxes = fragmentainer.MarginBoxes.Count > 0 || applicableMargins.Any(m =>
                    !string.IsNullOrEmpty(m.Style.Content) && MarginBoxRenderer.TryParseElementFunction(m.Style.Content, out _, out _));
                if (hasElementMarginBoxes)
                {
                    await PaintElementMarginBoxes(g, _renderContext, container.HtmlContainerInt, fragmentainer.MarginBoxes,
                        new Size(page.Width, page.Height), mL, mT, mR, mB, applicableMargins, applicablePageStyle,
                        marginBoxBackgroundImageCache);
                }
            }

            foreach (var cachedImage in pageBackgroundImageCache.Values)
            {
                if (cachedImage is null) continue;
                foreach (var image in cachedImage) image.Dispose();
            }

            foreach (var cachedImage in marginBoxBackgroundImageCache.Values)
            {
                if (cachedImage is null) continue;
                foreach (var image in cachedImage) image.Dispose();
            }

            foreach (var cachedImage in marginBoxImageCache.Values)
                cachedImage?.Dispose();

            // Hand the painter's clip findings to the caller. Drained here, after the page loop, and
            // before `container` (a `using`) is disposed at the end of this method - it is the only
            // point where the whole render's collection exists and is still reachable.
            // Appended, not assigned. AddPdfPages is a repeatable public API - a caller adds more
            // pages to an existing document across several calls, each building its own container
            // and its own report - so replacing the property wholesale would silently discard the
            // findings of every earlier call. PeachPdfDocument.PageCount accumulates across the same
            // calls via the underlying PdfDocument; a report whose whole purpose is not to lose
            // information quietly should not be the one member that does.
            document.ClipReport.Append(container.HtmlContainerInt.ClipReport);

            // Finalizes /ParentTree page-keyed entries before HandleLinks (which, when tagging is
            // enabled, appends further annotation-keyed entries to the same tree - see
            // StructureTagBuilder.Finish and HandleLinks's own tagging-aware section below).
            structureTagBuilder?.Finish();

            // The hyperlinks and the outline, worked out from the laid-out pages in terms of pages and points; the PDF
            // annotations and outline entries are made from them below.
            var annotations = DocumentAnnotationCollector.Collect(container.HtmlContainerInt, fragmentainers);
            HandleLinks(document.PdfDocument, annotations, structureTagBuilder);

            if (formFieldBuilder != null)
            {
                HandleFormFields(document.PdfDocument, annotations, formFieldBuilder);
            }

            // PDF outline (bookmarks) - CSS-default-driven (h1-h6 default to bookmark-level 1-6 via the
            // UA stylesheet, everything else defaults to none), so this is unconditional: a document
            // with no headings simply collects zero bookmarks above and adds no outline.
            BookmarkOutlineBuilder.Build(document.PdfDocument, annotations.Bookmarks);
        }

        #region Private/Protected methods

        /// <summary>
        /// The options the document is configured with for one render call: the whole-document format settings from
        /// <paramref name="config"/>, and the document information - a non-empty field of <see cref="PdfGenerateConfig.Metadata"/> wins over
        /// the value extracted from the HTML, and a missing one falls back to it.
        /// </summary>
        private static PeachDrawing.PDF.PdfComposerOptions BuildComposerOptions(PdfGenerateConfig config, HtmlContainer container, PdfEmbeddingPlan embeddings)
        {
            var metadata = container.DocumentMetadata;
            var overrides = config.Metadata;

            var options = new PeachDrawing.PDF.PdfComposerOptions
            {
                Title = !string.IsNullOrEmpty(overrides?.Title) ? overrides.Title : metadata?.Title,
                Author = !string.IsNullOrEmpty(overrides?.Author) ? overrides.Author : metadata?.Author,
                Subject = !string.IsNullOrEmpty(overrides?.Subject) ? overrides.Subject : metadata?.Subject,
                Keywords = !string.IsNullOrEmpty(overrides?.Keywords) ? overrides.Keywords : metadata?.Keywords,
                Creator = overrides?.Creator ?? metadata?.Generator ?? PeachPdfProductInfo.Generator,
                Producer = PeachPdfProductInfo.Generator,
                // An HTML-extracted date (<meta name="date" content="2024-03-15">) carries no timezone of its own, so it is read as local
                // time, which is also how the PDF date it is written as is interpreted.
                CreationDate = overrides?.CreationDate
                    ?? (metadata?.Date is { } htmlDate ? new DateTimeOffset(DateTime.SpecifyKind(htmlDate, DateTimeKind.Local)) : null),
                // Resolved by SetContent: the document's own <html lang> takes priority, else PdfGenerateConfig.DefaultLanguage.
                Language = container.HtmlContainerInt.DocumentLanguage,
                Version = config.PdfVersionExplicit ? config.PdfVersion.ToEngine() : null,
                Conformance = config.PdfAConformance.ToEngine(),
                XConformance = config.PdfXConformance.ToEngine(),
                ColorOptions = config.ColorOptions.ToEngine(),
                WriteXmpMetadata = config.EnableXmpMetadata,
                FacturX = embeddings.FacturX,
                Encryption = config.Encryption.ToEngine(),
                CompressContentStreams = config.CompressContentStreams,
                BrotliCompression = config.BrotliCompression,
                JxlPassthrough = config.JxlPassthrough,
                ImageCompression = config.ImageCompression.ToEngine(),
                DownscaleImages = config.DownscaleImages,
                DownscaleQuality = config.DownscaleQuality,
                MaximumDownscaleMultiplier = config.MaximumDownscaleMultiplier,
                FlattenTransparency = config.TransparencyPolicy == TransparencyPolicy.Flatten,
                PortableColorFilters = config.PortableColorFilters,
            };

            foreach (var property in overrides?.CustomXmpProperties ?? [])
                options.CustomXmpProperties.Add(property);

            foreach (var file in embeddings.Files)
                options.EmbeddedFiles.Add(file);

            return options;
        }

        /// <summary>
        /// Whether a <c>ScaleToPageSize</c>/<c>ShrinkToFit</c> render needs its font-cache clear,
        /// full HTML/CSS re-parse, and second layout pass, or whether the measurement pass already
        /// used the correct <c>HtmlRenderContext.PixelsPerPoint</c> (the common case: ordinary
        /// non-overflowing content already fits the page, so no rescale is needed). The comparison
        /// is exact rather than epsilon-based - <paramref name="effectivePixelsPerPoint"/> is either
        /// <paramref name="currentPixelsPerPoint"/> read back unchanged (no rescale needed) or a
        /// freshly computed, genuinely different value, never a value that merely rounds close to it.
        /// </summary>
        internal static bool NeedsRescale(double currentPixelsPerPoint, double effectivePixelsPerPoint) =>
            effectivePixelsPerPoint != currentPixelsPerPoint;

        /// <summary>
        /// Rejects an <see cref="PdfGenerateConfig.AnimationProgress"/> that is not a number from 0 to 1 (NaN, the
        /// infinities, negative and above 1), so every entry point - HTML and declarative alike - treats it alike.
        /// </summary>
        internal static void ValidateAnimationProgress(PdfGenerateConfig config)
        {
            if (config.AnimationProgress is { } animationProgress && !(animationProgress >= 0 && animationProgress <= 1))
            {
                throw new ArgumentOutOfRangeException(nameof(config), animationProgress,
                    "PdfGenerateConfig.AnimationProgress must be a number from 0 to 1.");
            }
        }

        internal static async Task SetContent(HtmlContainer container, PdfGenerateConfig config, string html, PeachPdfCssContent? cssData, Size orgPageSize)
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
            container.HtmlContainerInt.SnapBoxDecorationsToCssPixels = config.SnapBoxDecorationsToCssPixels;
            container.HtmlContainerInt.SnapBorderWidthsToCssPixels = config.SnapBorderWidthsToCssPixels;

            ValidateAnimationProgress(config);
            container.HtmlContainerInt.AnimationProgress = config.AnimationProgress;

            // Read while the DOM tree is generated, when every text box is cut into words: hyphens: auto and the
            // language-dependent line-break tailorings need the language then, not after SetHtml returns.
            container.HtmlContainerInt.DefaultLanguage = string.IsNullOrEmpty(config.DefaultLanguage) ? null : config.DefaultLanguage;

            // Parse-time @page relative units (% / em, base rule and the captured PageLengthContext
            // alike) resolve against PageSize as it stands during SetHtml — carry the physical sheet
            // in so a percentage margin resolves against the page-box width (css-page-3 §7.1), not a
            // stale band from a previous pass or the unset 0 default on the first pass.
            container.PageSize = orgPageSize;

            // The document's own <html lang> always wins (DomParser); config.DefaultLanguage, set above, only fills
            // in when the document declares none — PeachPDF itself never guesses a language on its own initiative.
            await container.SetHtml(html, cssData?.CssData);

            // Just in case @page rules got applied. SetContent is now only ever called once per render
            // with the ORIGINAL orgPageSize parameter (DomParser.CascadeApplyPageStyles corrects
            // PageSize/margins against the CSS @page size in place, inside SetHtml above, rather than a
            // caller re-invoking SetContent a second time with the corrected size - issue #582) - so this
            // must subtract margins from the CSS-resolved sheet size (falling back to orgPageSize when
            // there is no @page { size } rule), or it would clobber that in-place correction with the
            // stale, originally-configured size.
            var sheetSize = container.CssPageSize ?? orgPageSize;
            var pageSize = new Size(sheetSize.Width - container.MarginLeft - container.MarginRight, sheetSize.Height - container.MarginTop - container.MarginBottom);
            container.PageSize = pageSize;
            container.Location = new PaintPoint(container.MarginLeft, container.MarginTop);
        }

        /// <summary>
        /// Paints one page's css-gcpm-3 <c>content: element()</c> margin-box content - real, laid-out
        /// <see cref="CssBox"/> subtrees (<see cref="FragmentainerFragment.MarginBoxes"/>), unlike the
        /// plain string/counter/image content <see cref="MarginBoxRenderer.Render"/> still draws directly.
        /// Reuses <see cref="FragmentPainter.PaintFragment"/>'s paint through <see cref="FragmentPainter.PaintDetached"/>
        /// (the tree is not part of the page's own, so a decoration needs it as the root to find where its
        /// line was truncated) - the same paint every other <see cref="BoxFragment"/> in the document goes through - so real formatting/descendant
        /// elements (backgrounds, borders, nested inline styling) paint correctly for free, and (per
        /// <c>FragmentPainter.PaintFragment</c>'s own doc comment, "the single choke point all tagging
        /// flows through") so tagged-PDF structure attaches automatically when enabled, keyed to whichever
        /// page's <c>structureTagBuilder.BeginPage</c> is currently open - the precondition this call site,
        /// immediately after the same page's main-content paint, satisfies.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="MarginBoxRenderer"/>'s own <c>PaintImage</c> precedent for going from a
        /// point-space margin-box rect to a fresh, scoped <see cref="PdfCanvas"/> - <see cref="MarginBoxFragment.Content"/>'s
        /// coordinates are already in that same pixel space (<see cref="RunningElementLayout.LayoutRunningElementFor"/>
        /// laid it out directly against the pixel-converted rect), so no further conversion happens here.
        /// <para>
        /// The running element's own background/border (a real, laid-out <see cref="CssBox"/>) paints for
        /// free through <c>PaintFragment</c> above - what doesn't is the margin-box RULE's own
        /// background/border (e.g. <c>@top-center { content: element(chapter-title); background-color:
        /// #ccc; }</c>), painted here via <see cref="MarginBoxRenderer.PaintBackgroundAndBorder"/> at the
        /// same rect <see cref="HtmlContainerInt.LayoutMarginBoxes"/> derived for layout, recomputed
        /// (<see cref="MarginBoxRenderer.FindMargin"/> by <see cref="MarginBoxFragment.BoxName"/>) since
        /// it isn't retained on <see cref="MarginBoxFragment"/> itself - the same recomputation-not-
        /// caching convention <see cref="MarginBoxRenderer.Render"/>'s own text path already follows.
        /// </para>
        /// <para>
        /// A second pass then covers every <c>element(...)</c>-content rule that has NO
        /// <see cref="MarginBoxFragment"/> at all - a page where the named running element hasn't been
        /// reached yet (or never exists, e.g. a misspelled name) - since <c>LayoutMarginBoxes</c> only
        /// creates a fragment once its running-element lookup actually resolves. That rule's own
        /// background/border must still paint independent of content, the same as an ordinary
        /// <c>content: none</c>/empty text-path box already does in <c>MarginBoxRenderer.Render</c> -
        /// otherwise a margin box's background/border would silently depend on whether its content
        /// happens to have resolved yet, which is not what "paints unconditionally" is supposed to mean.
        /// </para>
        /// </remarks>
        private static async Task PaintElementMarginBoxes(
            PdfGraphics g, RenderContext renderContext, HtmlContainerInt htmlContainer, IReadOnlyList<MarginBoxFragment> marginBoxes,
            Size pageSize, double marginLeft, double marginTop, double marginRight, double marginBottom,
            IReadOnlyList<MarginStyleRule> margins, StyleDeclaration? pageStyle,
            Dictionary<string, IReadOnlyList<CssImage>?> backgroundImageCache)
        {
            var pixelsPerPoint = renderContext.LayoutUnitsPerPoint;
            using var pdfCanvas = new PdfCanvas(renderContext, g, pixelsPerPoint);
            var painter = new FragmentPainter(htmlContainer);
            var remPt = htmlContainer.PageLengthContext?.RemPt ?? DefaultFontResolver.FontSize;
            var resolvedBoxNames = marginBoxes.Count > 0
                ? new HashSet<string>(marginBoxes.Select(m => m.BoxName), StringComparer.OrdinalIgnoreCase)
                : [];

            async Task PaintRuleOwnBackgroundAndBorder(string boxName, MarginStyleRule rule)
            {
                var outerRect = MarginBoxRenderer.GetMarginBoxRect(boxName, pageSize,
                    marginLeft, marginTop, marginRight, marginBottom, margins, pageStyle, remPt);
                var cbWidth = MarginBoxRenderer.MarginAreaWidth(boxName, pageSize, marginLeft, marginRight);
                var cbHeight = MarginBoxRenderer.MarginAreaHeight(boxName, pageSize, marginTop, marginBottom);

                await MarginBoxRenderer.PaintBackgroundAndBorder(pdfCanvas, outerRect, pageSize, rule, pageStyle, remPt,
                    cbWidth, cbHeight, renderContext, htmlContainer, backgroundImageCache);
            }

            foreach (var marginBox in marginBoxes)
            {
                if (MarginBoxRenderer.FindMargin(margins, marginBox.BoxName) is { } rule)
                    await PaintRuleOwnBackgroundAndBorder(marginBox.BoxName, rule);

                painter.PaintDetached(pdfCanvas, marginBox.Content);
            }

            foreach (var rule in margins)
            {
                var boxName = rule.Selector?.Text?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(boxName) || resolvedBoxNames.Contains(boxName))
                    continue;

                var contentValue = rule.Style.Content;
                if (string.IsNullOrEmpty(contentValue) || !MarginBoxRenderer.TryParseElementFunction(contentValue, out _, out _))
                    continue;

                await PaintRuleOwnBackgroundAndBorder(boxName, rule);
            }
        }

        /// <summary>
        /// Paints one page's css-gcpm-3 <c>float: footnote</c> note area - a divider rule above the
        /// stacked footnote bodies, then each body. The divider's own geometry and color were already
        /// resolved at layout time (<see cref="HtmlContainerInt.ResolveFootnoteAreaRule"/>, via
        /// <see cref="FootnoteAreaFragment.DividerRect"/>/<see cref="FootnoteAreaFragment.DividerColor"/>)
        /// - this reads that resolved data rather than re-resolving <c>@footnote</c>'s cascade itself, the
        /// same "paint consumes only the fragment tree" division of labor every other paint code in this
        /// file follows. Mirrors <see cref="PaintElementMarginBoxes"/> exactly for the bodies (real,
        /// laid-out <see cref="CssBox"/> subtrees reused unmodified through
        /// <see cref="FragmentPainter.PaintDetached"/> - backgrounds/borders/nested styling and tagged-PDF
        /// structure attach for free, same reasoning) - the divider alone is drawn directly, as a simple
        /// filled rectangle: it is a UA-drawn separator with no box of its own, not an <c>&lt;hr&gt;</c>
        /// (which is an ordinary box and paints its rule through the normal border path).
        /// </summary>
        private static void PaintFootnoteArea(PdfGraphics g, RenderContext renderContext, HtmlContainerInt htmlContainer, FootnoteAreaFragment footnoteArea)
        {
            var pixelsPerPoint = renderContext.LayoutUnitsPerPoint;
            using var pdfCanvas = new PdfCanvas(renderContext, g, pixelsPerPoint);

            PaintFootnoteArea(pdfCanvas, renderContext, htmlContainer, footnoteArea);
        }

        /// <summary>
        /// The <see cref="Canvas"/>-level half of <see cref="PaintFootnoteArea(PdfGraphics, RenderContext, HtmlContainerInt, FootnoteAreaFragment)"/>,
        /// split out so a test can drive it with a recording <see cref="Canvas"/> and assert the actual
        /// draw call sequence (which calls, in what order) rather than only that a full PDF pipeline
        /// completes without error or that a fragment holds the right data - see this repo's own testing
        /// conventions on why a token/page-count check alone is not proof a paint path isn't a no-op.
        /// </summary>
        internal static void PaintFootnoteArea(Canvas pdfCanvas, RenderContext renderContext, HtmlContainerInt htmlContainer, FootnoteAreaFragment footnoteArea)
        {
            // Bypasses FragmentPainter.Paint's own clip push (this is called directly, not through that
            // wrapper), so it needs its own content-area bound - previously provided for free by a raw
            // PdfGraphics.IntersectClip AddPdfPages applied ahead of this call, removed because it also
            // (wrongly) bounded position: fixed content the same way; see AddPdfPages's own remarks.
            pdfCanvas.PushClip(htmlContainer.PageClipOverride ?? htmlContainer.PageBoxRect);

            var rect = footnoteArea.DividerRect;
            if (rect.Height > 0)
            {
                var dividerColor = ResolveFootnoteDividerColor(footnoteArea.DividerColor, renderContext);
                pdfCanvas.DrawRectangle(pdfCanvas.GetSolidBrush(dividerColor), rect.X, rect.Y, rect.Width, rect.Height);
            }

            var painter = new FragmentPainter(htmlContainer);
            foreach (var body in footnoteArea.Bodies)
            {
                painter.PaintDetached(pdfCanvas, body);
            }

            pdfCanvas.PopClip();
        }

        /// <summary>
        /// The <c>@footnote</c> divider's resolved paint color - black when <paramref name="declaredColor"/>
        /// is null (no <c>border-top</c> declared, see <see cref="HtmlContainerInt.ResolveFootnoteAreaRule"/>)
        /// or resolves to no real color of its own (<c>currentcolor</c>/<c>initial</c> - the divider is a
        /// UA-drawn separator with no element of its own to inherit a text color from), otherwise the
        /// declared color parsed the same way any other CSS color value is.
        /// </summary>
        internal static PaintColor ResolveFootnoteDividerColor(string? declaredColor, RenderContext renderContext) =>
            string.IsNullOrWhiteSpace(declaredColor) ||
            declaredColor.Equals(Keywords.CurrentColor, StringComparison.OrdinalIgnoreCase) ||
            declaredColor.Equals(Keywords.Initial, StringComparison.OrdinalIgnoreCase)
                ? PaintColor.Black
                : new CssValueParser(renderContext).GetActualColor(declaredColor);

        /// <summary>
        /// Writes the collected hyperlinks as PDF link annotations, either to an external URL or to another place in the document.
        /// <paramref name="structureTagBuilder"/> is non-null only when tagged PDF output is enabled - used to attach each created
        /// Link annotation's "/OBJR" back to its owning "/Link" structure element, completing the bidirectional PDF/UA linkage
        /// between the annotation and the structure tree.
        /// </summary>
        private static void HandleLinks(PdfDocument document, DocumentAnnotations annotations, StructureTagBuilder? structureTagBuilder = null)
        {
            foreach (var link in annotations.Links)
            {
                if (link.PageIndex >= document.Pages.Count)
                    continue;

                var page = document.Pages[link.PageIndex];

                // The collected geometry is measured down from the page's top-left corner; a PDF rect counts from the page bottom.
                var xRect = new Rect(link.Bounds.X, page.Height - (link.Bounds.Y + link.Bounds.Height), link.Bounds.Width, link.Bounds.Height);

                PdfLinkAnnotation annotation;

                if (link.Anchor is { } anchorId && link.Destination is { } destination)
                {
                    // /FitH top fits page width and positions vertically at top - the "jump down to this Y" behavior an anchor link
                    // destination actually wants; CreateFitVertically would instead pass the offset as /FitV's horizontal left
                    // parameter. The offset is top-down, but /FitH's own "top" is PDF default user space, which is bottom-up -
                    // flipping against the TARGET page's own height is required (not this link's page: a link can jump between
                    // differently-sized pages).
                    document.AddNamedDestination(anchorId, destination.PageIndex + 1, PdfNamedDestinationParameters.CreateFitHorizontally(document.Pages[destination.PageIndex].Height - destination.Top));
                    annotation = page.AddDocumentLink(new PdfRectangle(xRect), anchorId);
                }
                else
                {
                    annotation = page.AddWebLink(new PdfRectangle(xRect), link.Uri ?? string.Empty);
                }

                // A link over a transformed element also carries the drawn quadrilateral, which PDF has no key for before 1.6.
                if (link.Quad is { } quad && document.Version >= 16)
                    annotation.SetQuadPoints(quad.Select(corner => new PaintPoint(corner.X, page.Height - corner.Y)).ToArray());

                // The same source box can legitimately gain more than one annotation (once per page its running element was selected
                // onto) - LinkAnnotationToStructureElement is exercised for a repeated box already.
                if (structureTagBuilder != null && link.Source is { } source)
                {
                    structureTagBuilder.LinkAnnotationToStructureElement(source, page, annotation);
                }
            }
        }

        /// <summary>
        /// Creates AcroForm fields/widgets for the collected form controls, one per page.
        /// </summary>
        private static void HandleFormFields(PdfDocument document, DocumentAnnotations annotations, FormFieldBuilder formFieldBuilder)
        {
            foreach (var field in annotations.FormFields)
            {
                if (field.PageIndex >= document.Pages.Count)
                    continue;

                var page = document.Pages[field.PageIndex];

                // The collected geometry is measured down from the page's top-left corner; a PDF rect counts from the page bottom.
                var bounds = field.Bounds;
                var rect = new Rect(bounds.X, page.Height - (bounds.Y + bounds.Height), bounds.Width, bounds.Height);

                // The appearance stream's own space is y up too, so the y-down map is mirrored across the x axis.
                Matrix3x2? appearanceMatrix = field.AppearanceTransform is { } m ? new Matrix3x2(m.M11, -m.M12, -m.M21, m.M22, 0, 0) : null;

                formFieldBuilder.AddField(page, new PdfRectangle(rect), field, appearanceMatrix);
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
            double pageHeight,
            bool isBlank = false)
            => PageRuleResolver.SelectPageRule(rules, pageNumber,
                PageRuleResolver.ActiveNameAtPageEnd(namedPageElements, pageY, pageHeight), isBlank);

        internal static IReadOnlyList<MarginStyleRule> SelectApplicableMarginRules(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight,
            bool isBlank = false)
            => PageRuleResolver.SelectApplicableMarginRules(rules, pageNumber,
                PageRuleResolver.ActiveNameAtPageEnd(namedPageElements, pageY, pageHeight), isBlank);

        internal static StyleDeclaration? SelectApplicablePageStyle(
            IReadOnlyList<PageRule> rules,
            int pageNumber,
            IReadOnlyList<NamedPageElement> namedPageElements,
            double pageY,
            double pageHeight,
            bool isBlank = false)
            => PageRuleResolver.SelectApplicablePageStyle(rules, pageNumber,
                PageRuleResolver.ActiveNameAtPageEnd(namedPageElements, pageY, pageHeight), isBlank);

        internal static (double L, double T, double R, double B) ResolvePageMargins(
            PageRule? rule, double baseL, double baseT, double baseR, double baseB)
            => PageRuleResolver.ResolvePageMargins(rule, baseL, baseT, baseR, baseB);

        #endregion
    }
}