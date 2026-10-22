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

using PeachBrackets;
using PeachBrackets.Building;
using PeachBrackets.Annotations;
using PeachPDF.Rendering;
using PeachDrawing.Text;
using PeachDrawing.PDF;
using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachBrackets.Html;
using PeachBrackets.Html.Annotations;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Handlers;
using PeachPDF.PdfFeatures;
using PeachBrackets.Html.Paint;
using PeachBrackets.Html.Pages;
using PeachBrackets.Html.Parse;
using System;
using System.Linq;
using PeachBrackets.Html.Utils;
using PeachPDF.Layout;
using PeachPDF.Network;
using PeachBrackets.Utilities;
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
            ArgumentException.ThrowIfNullOrEmpty(fromFamily);
            ArgumentException.ThrowIfNullOrEmpty(toFamily);

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
            return new PeachPdfCssContent(await StyleSheet.ParseAsync(_renderContext, stylesheet, combineWithDefault));
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
            return new PeachPdfCssContent(await StyleSheet.ParseAsync(_renderContext, stylesheet, combineWithDefault));
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
            var orgPageSize = config.PageSize != PageSize.Undefined ? PageSizeConverter.ToSize(config.PageSize) : new Size(config.ManualPageWidth, config.ManualPageHeight);

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

            var options = ToDocumentOptions(config, orgPageSize);
            options.StyleSheet = cssData?.StyleSheet;

            var htmlDocument = await HtmlDocument.LoadAsync(html, _renderContext, options);
            PdfCanvas? measure = null;

            try
            {
                // An @page { size } rule in the document corrects the page size while the document is loaded, so the content area is read back
                // from the document rather than derived from the configured size.
                var contentArea = htmlDocument.ContentArea;
                measure = PdfCanvas.CreateMeasuring(_renderContext, contentArea.Width, contentArea.Height);

                var basePixelsPerPoint = config.PixelsPerInch / 72d;
                var minPixelsPerPoint = config.MinContentWidth > 0 ? config.MinContentWidth / contentArea.Width : basePixelsPerPoint;
                var pixelsPerPoint = minPixelsPerPoint;

                // Whether the document has already been laid out at its final scale. A plain render lays out once, below. For
                // ShrinkToFit/ScaleToPageSize the measurement pass is itself a layout, and is kept when no rescale turns out to be needed.
                var laidOut = false;

                if (config.ScaleToPageSize || config.ShrinkToFit)
                {
                    await htmlDocument.LayoutAsync(measure);

                    var actualWidth = htmlDocument.ContentExtent.Width;

                    var candidatePixelsPerPoint = pixelsPerPoint * (actualWidth / contentArea.Width);

                    if (candidatePixelsPerPoint < minPixelsPerPoint)
                    {
                        candidatePixelsPerPoint = minPixelsPerPoint;
                    }

                    var effectivePixelsPerPoint = (config.ShrinkToFit && candidatePixelsPerPoint > 1) || config.ScaleToPageSize
                        ? candidatePixelsPerPoint
                        : _renderContext.PixelsPerPoint;

                    if (NeedsRescale(_renderContext.PixelsPerPoint, effectivePixelsPerPoint))
                    {
                        _renderContext.ClearFontCache();
                        _renderContext.PixelsPerPoint = effectivePixelsPerPoint;

                        htmlDocument.Dispose();
                        htmlDocument = await HtmlDocument.LoadAsync(html, _renderContext, options);

                        measure.Dispose();
                        contentArea = htmlDocument.ContentArea;
                        measure = PdfCanvas.CreateMeasuring(_renderContext, contentArea.Width, contentArea.Height);
                    }
                    else
                    {
                        laidOut = true;
                    }
                }

                if (!laidOut)
                {
                    await htmlDocument.LayoutAsync(measure);
                }

                await RenderPagesCore(document, htmlDocument, config);
            }
            finally
            {
                measure?.Dispose();
                htmlDocument.Dispose();
            }
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

            var buildContext = new DocumentBuildContext(_renderContext);

            if (documentBuilder.FileLayers.Count == 0)
            {
                foreach (var pageHandler in documentBuilder.PageHandlers)
                {
                    var (htmlDocument, measure) = await PrepareDeclarativePage(pageHandler, config, buildContext, documentBuilder.DocumentStylesheet);
                    using (htmlDocument)
                    using (measure)
                    {
                        await RenderPagesCore(document, htmlDocument, config);
                    }
                }

                return;
            }

            // A file layer's target pages can be written relative to the last page ("2-z"), so every page of the document has to be laid out
            // before the first is drawn. The layer files are read first: a bad file or range fails before any page work.
            var layers = documentBuilder.FileLayers.ConvertAll(l => l.Build());
            var prepared = new List<(HtmlDocument Document, PdfCanvas Measure)>();
            try
            {
                foreach (var pageHandler in documentBuilder.PageHandlers)
                {
                    prepared.Add(await PrepareDeclarativePage(pageHandler, config, buildContext, documentBuilder.DocumentStylesheet));
                }

                var totalPages = document.Composer.Pages.Count + prepared.Sum(p => p.Document.Pages.Count);
                var plan = new FileLayerPlan(layers, totalPages);
                foreach (var (htmlDocument, _) in prepared)
                {
                    await RenderPagesCore(document, htmlDocument, config, plan);
                }

                // The links, destinations and outline of the files drawn on the pages, now that every page they can lead to exists.
                plan.Finish(document.Composer, _renderContext);
            }
            finally
            {
                foreach (var (htmlDocument, measure) in prepared)
                {
                    htmlDocument.Dispose();
                    measure.Dispose();
                }
            }
        }

        /// <summary>
        /// Builds one declarative page's tree, lays it out and renders it. The tree is built in code (see <c>PeachBrackets.Building</c>), so there
        /// is no HTML to parse: the style sheet, the page's own margin boxes and the geometry its builder fixed explicitly go to the document as given.
        /// </summary>
        private async Task<(HtmlDocument Document, PdfCanvas Measure)> PrepareDeclarativePage(Action<IPageDescriptor> pageHandler, PdfGenerateConfig config, DocumentBuildContext context, PeachPdfCssContent? stylesheet)
        {
            var pageDescriptor = DocumentBuilder.BuildPage(pageHandler, context);
            var page = pageDescriptor.Page;

            var orgPageSize = pageDescriptor.PageSizeOverride
                ?? (config.PageSize != PageSize.Undefined
                    ? PageSizeConverter.ToSize(config.PageSize)
                    : new Size(config.ManualPageWidth, config.ManualPageHeight));

            if ((pageDescriptor.OrientationOverride ?? config.PageOrientation) == PageOrientation.Landscape)
            {
                orgPageSize = new Size(orgPageSize.Height, orgPageSize.Width);
            }

            _renderContext.NetworkLoader = config.NetworkLoader ?? new DataUriNetworkLoader();
            _renderContext.AllowLocalFileAccess = config.AllowLocalFileAccess;
            _renderContext.PixelsPerPoint = config.PixelsPerInch / 72d;
            ApplyRasterizationSettings(config);

            var options = new HtmlDocumentOptions
            {
                PageWidthPoints = orgPageSize.Width,
                PageHeightPoints = orgPageSize.Height,
                MarginTopPoints = pageDescriptor.MarginTopOverride ?? config.MarginTop,
                MarginBottomPoints = pageDescriptor.MarginBottomOverride ?? config.MarginBottom,
                MarginLeftPoints = pageDescriptor.MarginLeftOverride ?? config.MarginLeft,
                MarginRightPoints = pageDescriptor.MarginRightOverride ?? config.MarginRight,
                SnapBoxDecorationsToCssPixels = config.SnapBoxDecorationsToCssPixels,
                SnapBorderWidthsToCssPixels = config.SnapBorderWidthsToCssPixels,
                DefaultLanguage = config.DefaultLanguage,
                CollectFormFields = config.EnableInteractivePdfForms,
            };

            var htmlDocument = await HtmlDocument.CreateAsync(page, options, stylesheet?.StyleSheet);
            PdfCanvas? measure = null;
            try
            {
                var contentArea = htmlDocument.ContentArea;
                measure = PdfCanvas.CreateMeasuring(_renderContext, contentArea.Width, contentArea.Height);
                await htmlDocument.LayoutAsync(measure);
                return (htmlDocument, measure);
            }
            catch
            {
                measure?.Dispose();
                htmlDocument.Dispose();
                throw;
            }
        }

        internal static HtmlDocumentOptions ToDocumentOptions(PdfGenerateConfig config, Size sheet)
        {
            ValidateAnimationProgress(config);

            return new HtmlDocumentOptions
            {
                PageWidthPoints = sheet.Width,
                PageHeightPoints = sheet.Height,
                MarginTopPoints = config.MarginTop,
                MarginRightPoints = config.MarginRight,
                MarginBottomPoints = config.MarginBottom,
                MarginLeftPoints = config.MarginLeft,
                Media = string.IsNullOrEmpty(config.Media) ? "print" : config.Media,
                ColorScheme = config.PreferredColorScheme == PdfColorScheme.Dark ? ColorSchemePreference.Dark : ColorSchemePreference.Light,
                IgnoreAuthorStyleSheets = config.IgnoreAuthorStyleSheets,
                SnapBoxDecorationsToCssPixels = config.SnapBoxDecorationsToCssPixels,
                SnapBorderWidthsToCssPixels = config.SnapBorderWidthsToCssPixels,
                AnimationProgress = config.AnimationProgress,
                DefaultLanguage = string.IsNullOrEmpty(config.DefaultLanguage) ? null : config.DefaultLanguage,
                CollectFormFields = config.EnableInteractivePdfForms,
            };
        }

        /// <summary>
        /// The content-source-agnostic half of page generation: metadata/XMP/output-intent/tagging/form
        /// setup, one PDF page per fragmentainer (paint, footnote area, margin boxes), and the
        /// whole-document link/form-field/bookmark passes at the end. Reads only already-resolved
        /// <paramref name="htmlDocument"/> state (its fragment tree, page rules, document metadata/language) -
        /// nothing here is specific to how that container's content tree was built, so this is shared
        /// verbatim between the HTML path (<see cref="AddPdfPages(PeachPdfDocument, string?, PdfGenerateConfig, PeachPdfCssContent?)"/>,
        /// after its own <c>SetContent</c>/<c>ShrinkToFit</c> setup above) and the declarative
        /// document-building path that builds a <c>Html.Dom.CssBox</c> tree directly instead
        /// of parsing HTML. The caller owns its own measure context and disposes it after this returns.
        /// </summary>
        private async Task RenderPagesCore(PeachPdfDocument document, HtmlDocument htmlDocument, PdfGenerateConfig config, FileLayerPlan? fileLayers = null)
        {
            // Whole-document format, document information, and what the claimed conformance level requires of the file (the output
            // intent, the XMP packet, embedded files, the language) - established once, and checked against any earlier call on this
            // document. Everything that depends on this render's content (title, language, creation date) is known by now.
            var embeddings = PdfEmbeddingPlan.Create(config);
            var composerOptions = BuildComposerOptions(config, htmlDocument, embeddings);
            fileLayers?.ApplyDocumentInformation(composerOptions, canAttachFiles: config.PdfAConformance is PdfAConformance.None or PdfAConformance.PdfA3A or PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA4E or PdfAConformance.PdfA4F);
            document.Composer.Configure(composerOptions);

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
                ? new StructureTagBuilder(document.Composer.CreateTagRecorder(requireAlternateText: isAccessibleConformance))
                : null;
            htmlDocument.StructureTagSink = structureTagBuilder;

            // Same shape as structureTagBuilder above: only constructed when interactive PDF forms are
            // enabled - FragmentContentPainters.For and HandleFormFields (below) both check
            // HtmlContainerInt.FormFieldBuilder for null and skip all classification/bookkeeping when
            // it's not set, so this is the single point that gates the whole feature off by default.
            var formFieldBuilder = config.EnableInteractivePdfForms ? new FormFieldBuilder(document.Composer.CreateFormBuilder(_renderContext, "PeachPDFField")) : null;

            // One PDF page per page of the laid-out document, painted onto the canvas the PDF page hands out. The sheet size is each page's own
            // resolved physical size, in true PDF points - it already falls back to the document's base size whenever no @page rule overrides it.
            foreach (var htmlPage in htmlDocument.Pages)
            {
                var pageNumber = document.Composer.Pages.Count + 1;
                var page = document.Composer.AddPage(htmlPage.WidthPoints, htmlPage.HeightPoints);

                structureTagBuilder?.BeginPage(page);

                fileLayers?.Paint(page, pageNumber, underlay: true, structureTagBuilder?.Recorder);

                await htmlDocument.PaintPageAsync(htmlPage.Index, page.Canvas);

                fileLayers?.Paint(page, pageNumber, underlay: false, structureTagBuilder?.Recorder);

                page.Finish();
            }

            // Hand the painter's clip findings to the caller. Drained here, after the page loop, and
            // before `container` (a `using`) is disposed at the end of this method - it is the only
            // point where the whole render's collection exists and is still reachable.
            // Appended, not assigned. AddPdfPages is a repeatable public API - a caller adds more
            // pages to an existing document across several calls, each building its own container
            // and its own report - so replacing the property wholesale would silently discard the
            // findings of every earlier call. PeachPdfDocument.PageCount accumulates across the same
            // calls via the underlying PdfDocument; a report whose whole purpose is not to lose
            // information quietly should not be the one member that does.
            document.ClipReport.Append(htmlDocument.ClippedText.Select(w =>
                new ClippedWord(w.Text, w.DrawnWidth, w.VisibleWidth, w.DrawnHeight, w.VisibleHeight)));

            // Finalizes /ParentTree page-keyed entries before the links (which, when tagging is
            // enabled, append further annotation-keyed entries to the same tree: the document hands each
            // link it puts on a page's canvas to the structure builder - see StructureTagBuilder.LinkAdded).
            structureTagBuilder?.Finish();

            // The hyperlinks, the form controls and the outline, worked out from the laid-out pages in terms of pages and points. The
            // links go on the canvas of their page like any other drawing call (the PDF canvas writes them as link annotations); the
            // form fields and outline entries are made from them below.
            var annotations = htmlDocument.Annotations;
            foreach (var htmlPage in htmlDocument.Pages)
            {
                // Every page of this call is painted by now, which is when the links' places are known. The pages go in the composer in
                // order, after whatever an earlier call added.
                var composerIndex = document.Composer.Pages.Count - htmlDocument.Pages.Count + htmlPage.Index;
                htmlDocument.PaintLinks(htmlPage.Index, document.Composer.Pages[composerIndex].Canvas);
            }

            if (formFieldBuilder != null)
            {
                HandleFormFields(document.Composer, annotations, formFieldBuilder);
            }

            // PDF outline (bookmarks) - CSS-default-driven (h1-h6 default to bookmark-level 1-6 via the
            // UA stylesheet, everything else defaults to none), so this is unconditional: a document
            // with no headings simply collects zero bookmarks above and adds no outline.
            document.Composer.AddBookmarks(annotations.Bookmarks.Select(ToBookmark));
        }

        #region Private/Protected methods

        /// <summary>An outline entry the HTML core collected, as the PDF writer's own.</summary>
        private static PdfBookmark ToBookmark(Bookmark bookmark) => new(bookmark.Level, bookmark.Title, bookmark.IsOpen)
        {
            Uri = bookmark.Uri,
            PageIndex = bookmark.Destination?.PageIndex,
            TopPoints = bookmark.Destination?.Top ?? 0,
        };

        /// <summary>
        /// The options the document is configured with for one render call: the whole-document format settings from
        /// <paramref name="config"/>, and the document information - a non-empty field of <see cref="PdfGenerateConfig.Metadata"/> wins over
        /// the value extracted from the HTML, and a missing one falls back to it.
        /// </summary>
        private static PeachDrawing.PDF.PdfComposerOptions BuildComposerOptions(PdfGenerateConfig config, HtmlDocument htmlDocument, PdfEmbeddingPlan embeddings)
        {
            var metadata = htmlDocument.Metadata;
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
                Language = htmlDocument.Language,
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

        /// <summary>
        /// Creates AcroForm fields/widgets for the collected form controls, one per page.
        /// </summary>
        private static void HandleFormFields(PdfComposer composer, DocumentAnnotations annotations, FormFieldBuilder formFieldBuilder)
        {
            foreach (var field in annotations.FormFields)
            {
                if (field.PageIndex >= composer.Pages.Count)
                    continue;

                formFieldBuilder.AddField(composer.Pages[field.PageIndex], field);
            }
        }

        #endregion
    }
}