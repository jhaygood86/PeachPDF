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
using PeachPDF.Layout;
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

            EstablishDocumentOptions(document, config);

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

            await RenderPagesCore(document, container, config);

            measure?.Dispose();
        }

        /// <summary>
        /// Establishes every whole-document <see cref="PdfDocumentOptions"/> property from <paramref name="config"/>
        /// - color mode, downscaling, PDF/A and PDF/X conformance (plus their "first call on this document
        /// wins, a later mismatched call throws" cross-call consistency guards, since both
        /// <see cref="AddPdfPages(PeachPdfDocument,string?,PdfGenerateConfig,PeachPdfCssContent?)"/>
        /// and <see cref="AddPages"/> are repeatable APIs that can append further content to an existing
        /// <see cref="PeachPdfDocument"/>), the PDF version header, and <see cref="ColorOptions"/>. Shared
        /// verbatim between the HTML path (<see cref="AddPdfPages(PeachPdfDocument,string?,PdfGenerateConfig,PeachPdfCssContent?)"/>)
        /// and the declarative document-building path (<see cref="AddPages"/>) - <see cref="RenderPagesCore"/> reads <paramref name="config"/>
        /// directly for its own validation/XMP/OutputIntent writing, but the paint-time *enforcement* of
        /// PDF/A/PDF/X construct restrictions (<see cref="PdfSharpCore.Drawing.Pdf.PdfGraphicsState"/>'s
        /// color-mode resolution, <see cref="PdfSharpCore.Pdf.Advanced.PdfXColorSpaceGuard"/>,
        /// <see cref="PdfSharpCore.Pdf.Advanced.PdfColorConversionGuard"/>, <see cref="PdfSharpCore.Pdf.Advanced.PdfATransparencyGuard"/>)
        /// all read <c>document.PdfDocument.Options.*</c> instead, so a caller that skipped this method
        /// would get a document whose <c>/OutputIntents</c>/XMP conformance claim is real but whose actual
        /// content is completely unenforced against it - exactly the bug this method's extraction fixes.
        /// </summary>
        private static void EstablishDocumentOptions(PeachPdfDocument document, PdfGenerateConfig config)
        {
            document.PdfDocument.Options.CompressContentStreams = config.CompressContentStreams;
            // Undefined (not the PdfSharpCore-internal default of Rgb) lets each color write in
            // whichever space it actually carries - RGB-authored colors as /DeviceRGB, device-cmyk()
            // -authored colors as real /DeviceCMYK operators (see PdfEncoders.ToString's per-color branch
            // under Undefined) - rather than every CMYK-tagged XColor being force-collapsed back to a
            // lossy RGB round-trip by ColorSpaceHelper.EnsureColorMode's Rgb branch. For an all-RGB
            // document this is byte-identical to Rgb mode (every color's ColorSpace is already Rgb).
            document.PdfDocument.Options.ColorMode = PdfColorMode.Undefined;
            document.PdfDocument.Options.DownscaleImages = config.DownscaleImages;
            document.PdfDocument.Options.DownscaleQuality = config.DownscaleQuality;
            document.PdfDocument.Options.MaximumDownscaleMultiplier = config.MaximumDownscaleMultiplier;
            document.PdfDocument.Options.ImageCompression = config.ImageCompression;
            // PDF/A conformance is a whole-document property, but AddPdfPages/AddPages are repeatable
            // public APIs (a caller can append more pages to an existing PeachPdfDocument) - a second call
            // requesting a different level than the first would otherwise silently leave the document's
            // already-written /OutputIntents/XMP conformance claim disagreeing with how some of its
            // pages were actually painted (earlier pages painted under a different transparency-guard
            // regime than the level the file now claims). Reject that outright rather than produce a
            // self-contradictory document; the same level requested again across multiple calls is fine.
            if (document.PdfDocument.Options.PdfAConformanceEstablished
                && document.PdfDocument.Options.PdfAConformance != config.PdfAConformance)
            {
                throw new InvalidOperationException(
                    $"PdfGenerateConfig.PdfAConformance must be the same on every AddPdfPages/AddPages call " +
                    $"for a given document - this document was already established as '{document.PdfDocument.Options.PdfAConformance}' " +
                    $"by an earlier call, and this call specifies '{config.PdfAConformance}'. A single PDF " +
                    "document can only claim one PDF/A conformance level (or none) as a whole.");
            }

            document.PdfDocument.Options.PdfAConformance = config.PdfAConformance;
            document.PdfDocument.Options.PdfAConformanceEstablished = true;

            // ISO 19005-2/3 (PDF/A-2/3) are defined in terms of PDF 1.7/ISO 32000-1. ISO 19005-1
            // (PDF/A-1) is defined in terms of PDF 1.4 - the version PeachPDF already always emits -
            // so PdfA1B/PdfA1A need no version change at all.
            if (config.PdfAConformance is PdfAConformance.PdfA2B or PdfAConformance.PdfA2U or PdfAConformance.PdfA2A
                or PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA3A)
            {
                document.PdfDocument.Version = 17;
            }

            // PeachPDF implements no PDF/A level defined against PDF 2.0 (there is no PDF/A-4 support),
            // and every level it does implement is defined against PDF 1.4 or 1.7 - so requesting both
            // is a contradiction the caller needs to resolve, not something to silently pick a winner for.
            if (config.PdfVersion == PdfVersion.Pdf20 && config.PdfAConformance != PdfAConformance.None)
            {
                throw new InvalidOperationException(
                    "PdfGenerateConfig.PdfVersion is set to Pdf20, but PdfAConformance is also set to a " +
                    "level other than None. PeachPDF does not implement PDF/A-4 (the PDF-2.0-based PDF/A " +
                    "level); request PdfVersion.Pdf17 (or leave PdfVersion at its default) when requesting " +
                    "PdfAConformance.");
            }

            // PDF/X-1a/X3/X4 all target PDF 1.4/1.6 (see the PdfXConformance version block below) - PDF
            // 2.0 is a contradiction, same reasoning as the PdfA/Pdf20 check above.
            if (config.PdfVersion == PdfVersion.Pdf20 && config.PdfXConformance != PdfXConformance.None)
            {
                throw new InvalidOperationException(
                    "PdfGenerateConfig.PdfVersion is set to Pdf20, but PdfXConformance is also set to a " +
                    "level other than None. PeachPDF's PDF/X output always targets PDF 1.4 (X1a/X3) or " +
                    "1.6 (X4); request PdfVersion.Pdf17 (or leave PdfVersion at its default) when " +
                    "requesting PdfXConformance.");
            }

            // Same "a PDF file has exactly one header version" reasoning as the PdfAConformance guard
            // above - a second AddPdfPages/AddPages call on the same document requesting a different
            // PdfVersion than the first would leave the file's already-written header disagreeing with
            // how some of its pages/structure elements were painted.
            if (document.PdfDocument.Options.PdfVersionEstablished
                && document.PdfDocument.Options.PdfVersion != config.PdfVersion)
            {
                throw new InvalidOperationException(
                    $"PdfGenerateConfig.PdfVersion must be the same on every AddPdfPages/AddPages call for " +
                    $"a given document - this document was already established as '{document.PdfDocument.Options.PdfVersion}' " +
                    $"by an earlier call, and this call specifies '{config.PdfVersion}'. A single PDF file " +
                    "can only have one header version.");
            }

            document.PdfDocument.Options.PdfVersion = config.PdfVersion;
            document.PdfDocument.Options.PdfVersionEstablished = true;

            if (config.PdfVersion == PdfVersion.Pdf20)
            {
                document.PdfDocument.Version = 20;
            }

            // PDF/A (archival) and PDF/X (print-production) are different documents in practice - no
            // single file conformance-claims both at once, so requesting both is a contradiction the
            // caller needs to resolve, not something to silently pick a winner for.
            if (config.PdfAConformance != PdfAConformance.None && config.PdfXConformance != PdfXConformance.None)
            {
                throw new InvalidOperationException(
                    "PdfGenerateConfig.PdfAConformance and PdfXConformance are both set to a level other " +
                    "than None. A single PDF document can only claim one conformance family - archival " +
                    "(PDF/A) or print-production (PDF/X), not both.");
            }

            // Same "whole-document property, first AddPdfPages/AddPages call wins" reasoning as
            // PdfAConformance above.
            if (document.PdfDocument.Options.PdfXConformanceEstablished
                && document.PdfDocument.Options.PdfXConformance != config.PdfXConformance)
            {
                throw new InvalidOperationException(
                    $"PdfGenerateConfig.PdfXConformance must be the same on every AddPdfPages/AddPages call " +
                    $"for a given document - this document was already established as '{document.PdfDocument.Options.PdfXConformance}' " +
                    $"by an earlier call, and this call specifies '{config.PdfXConformance}'. A single PDF " +
                    "document can only claim one PDF/X conformance level (or none) as a whole.");
            }

            document.PdfDocument.Options.PdfXConformance = config.PdfXConformance;
            document.PdfDocument.Options.PdfXConformanceEstablished = true;
            document.PdfDocument.Options.ColorOptions = config.ColorOptions;

            // ISO 15930-4/6 (PDF/X-1a:2003/PDF/X-3:2003) target PDF 1.4 - already PeachPDF's own
            // historical default (see PdfVersionTests.Default_Pdf17_KeepsHistoricalVersion14), so no
            // explicit change is needed for those two levels; ISO 15930-7 (PDF/X-4) targets PDF 1.6, a
            // real bump from that default.
            if (config.PdfXConformance == PdfXConformance.X4)
            {
                document.PdfDocument.Version = 16;
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
            var document = new PeachPdfDocument(new PdfDocument());
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

            EstablishDocumentOptions(document, config);

            // Collected synchronously first (the builder callback itself is synchronous, matching
            // QuestPDF's own declarative-composition-then-execution shape), then each page's own tree is
            // built/laid out/rendered in turn below, since that part is genuinely asynchronous (image
            // loading, layout).
            var documentBuilder = new DocumentBuilder();
            handler(documentBuilder);

            var properties = new CssPropertyFactory(_pdfSharpAdapter);

            foreach (var pageHandler in documentBuilder.PageHandlers)
            {
                await AddDeclarativePage(document, pageHandler, config, properties);
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
        private async Task AddDeclarativePage(PeachPdfDocument document, Action<IPageDescriptor> pageHandler, PdfGenerateConfig config, CssPropertyFactory properties)
        {
            var pageDescriptor = DocumentBuilder.BuildPage(pageHandler, properties);
            ResolvePendingAutoDirections(pageDescriptor.RootBox, properties);

            var orgPageSize = pageDescriptor.PageSizeOverride
                ?? (config.PageSize != PageSize.Undefined
                    ? PageSizeConverter.ToSize(config.PageSize)
                    : new XSize(config.ManualPageWidth, config.ManualPageHeight));

            if ((pageDescriptor.OrientationOverride ?? config.PageOrientation) == PageOrientation.Landscape)
            {
                orgPageSize = new XSize(orgPageSize.Height, orgPageSize.Width);
            }

            _pdfSharpAdapter.NetworkLoader = config.NetworkLoader ?? new DataUriNetworkLoader();
            _pdfSharpAdapter.AllowLocalFileAccess = config.AllowLocalFileAccess;
            _pdfSharpAdapter.PixelsPerPoint = config.PixelsPerInch / 72d;

            using var container = new HtmlContainer(_pdfSharpAdapter);

            container.MarginTop = pageDescriptor.MarginTopOverride ?? config.MarginTop;
            container.MarginBottom = pageDescriptor.MarginBottomOverride ?? config.MarginBottom;
            container.MarginLeft = pageDescriptor.MarginLeftOverride ?? config.MarginLeft;
            container.MarginRight = pageDescriptor.MarginRightOverride ?? config.MarginRight;
            container.HtmlContainerInt.PageRules = pageDescriptor.PageRules;

            container.PageSize = orgPageSize;
            await container.SetDeclarativeRoot(pageDescriptor.RootBox, config.DefaultLanguage);

            // Mirrors SetContent's own tail: the content page size is the sheet less its margins, and
            // the document origin sits at the top-left of that band.
            var contentPageSize = new XSize(
                orgPageSize.Width - container.MarginLeft - container.MarginRight,
                orgPageSize.Height - container.MarginTop - container.MarginBottom);
            container.PageSize = contentPageSize;
            container.Location = new XPoint(container.MarginLeft, container.MarginTop);

            using var measure = XGraphics.CreateMeasureContext(orgPageSize, XGraphicsUnit.Point, XPageDirection.Downwards);
            container.MaxSize = new XSize(container.PageSize.Width, 0);
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
        /// <c>[dir]</c> attribute-selector apply <c>direction</c> through the normal cascade), a
        /// declarative tree never runs selector-based cascade at all, so this sets the resolved value
        /// directly via <paramref name="properties"/> instead.
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
            var resolvedCreationDate = ApplyDocumentMetadata(document.PdfDocument, container.DocumentMetadata, config.Metadata);

            // PDF/A-1a/2a/3a build on tagged-PDF output (StructureTagBuilder below) and additionally
            // require a real document language - both checked here, once, ahead of the per-page paint
            // loop that would otherwise be wasted work on a document that's about to be rejected.
            var isAccessibleConformance = config.PdfAConformance is
                PdfAConformance.PdfA1A or PdfAConformance.PdfA2A or PdfAConformance.PdfA3A;

            // Wired unconditionally (independent of tagged-PDF output) - a document's own /Lang is
            // useful metadata regardless, and DocumentLanguage is already resolved by SetContent
            // (the document's own <html lang> takes priority, else PdfGenerateConfig.DefaultLanguage -
            // so an explicit DefaultLanguage still reaches /Lang when the document declares none).
            if (!string.IsNullOrEmpty(container.HtmlContainerInt.DocumentLanguage))
            {
                document.PdfDocument.Language = container.HtmlContainerInt.DocumentLanguage;
            }
            else if (isAccessibleConformance)
            {
                throw new InvalidOperationException(
                    "PdfGenerateConfig.PdfAConformance is set to an accessible ('A') level, which requires " +
                    "a document language, but neither the source HTML declares <html lang=\"...\"> nor is " +
                    "PdfGenerateConfig.DefaultLanguage set. Set DefaultLanguage, or add a lang attribute to the document.");
            }

            // Each non-default ConversionMode needs its own destination ICC profile set and parseable -
            // validated once here (fail loudly, don't write a placeholder - same stance as the missing
            // -creation-date/missing-language checks) rather than discovered deep in PdfColorConversionGuard
            // partway through painting.
            if (config.ColorOptions is { ConversionMode: ColorConversionMode.ConvertToOutputIntent } convertToOutputIntent)
            {
                if (convertToOutputIntent.OutputIntentProfile is not { Length: > 0 })
                {
                    throw new InvalidOperationException(
                        "PdfGenerateConfig.ColorOptions.ConversionMode is ColorConversionMode.ConvertToOutputIntent, " +
                        "but ColorOptions.OutputIntentProfile is not set. Set it to the ICC profile every color " +
                        "should be converted into.");
                }

                if (!PeachImage.IccColorProfile.TryCreate(convertToOutputIntent.OutputIntentProfile, out _))
                {
                    throw new InvalidOperationException(
                        "PdfGenerateConfig.ColorOptions.OutputIntentProfile could not be parsed as a valid ICC profile.");
                }
            }
            else if (config.ColorOptions is { ConversionMode: ColorConversionMode.ConvertToProfile } convertToProfile)
            {
                if (convertToProfile.ConvertToProfile is not { Length: > 0 })
                {
                    throw new InvalidOperationException(
                        "PdfGenerateConfig.ColorOptions.ConversionMode is ColorConversionMode.ConvertToProfile, but " +
                        "ColorOptions.ConvertToProfile is not set.");
                }

                if (!PeachImage.IccColorProfile.TryCreate(convertToProfile.ConvertToProfile, out _))
                {
                    throw new InvalidOperationException(
                        "PdfGenerateConfig.ColorOptions.ConvertToProfile could not be parsed as a valid ICC profile.");
                }
            }
            else if (config.ColorOptions is { ConversionMode: ColorConversionMode.GrayscaleViaK } grayscaleViaK)
            {
                if (grayscaleViaK.FallbackCmykProfile is not { Length: > 0 })
                {
                    throw new InvalidOperationException(
                        "PdfGenerateConfig.ColorOptions.ConversionMode is ColorConversionMode.GrayscaleViaK, but " +
                        "ColorOptions.FallbackCmykProfile is not set - it's the CMYK profile every color is " +
                        "converted through before taking only its K channel.");
                }

                if (!PeachImage.IccColorProfile.TryCreate(grayscaleViaK.FallbackCmykProfile, out var grayscaleProfile) ||
                    grayscaleProfile is null || grayscaleProfile.DataColorSpace != PeachImage.IccColorSpace.Cmyk)
                {
                    throw new InvalidOperationException(
                        "PdfGenerateConfig.ColorOptions.FallbackCmykProfile must be a valid CMYK ICC profile when " +
                        "ConversionMode is ColorConversionMode.GrayscaleViaK.");
                }
            }

            var pdfAConformanceRequested = config.PdfAConformance != PdfAConformance.None;
            var pdfXConformanceRequested = config.PdfXConformance != PdfXConformance.None;

            // Every PDF/X level requires a real output intent, and PeachPDF bundles no default press
            // profile (unlike PDF/A's bundled sRGB one - there is no single correct default for a print
            // output intent) - fail loudly rather than silently omit /OutputIntents, same "don't write a
            // placeholder" stance as the missing-creation-date/missing-language checks.
            if (pdfXConformanceRequested && config.ColorOptions?.OutputIntentProfile is not { Length: > 0 })
            {
                throw new InvalidOperationException(
                    "PdfGenerateConfig.PdfXConformance is set, but PdfGenerateConfig.ColorOptions.OutputIntentProfile " +
                    "is not set. Every PDF/X level requires a real ICC output-intent profile - PeachPDF " +
                    "bundles no default press profile, so supply one via ColorOptions.OutputIntentProfile " +
                    "(and ColorOptions.OutputIntentIdentifier).");
            }

            if (pdfXConformanceRequested && string.IsNullOrEmpty(config.ColorOptions?.OutputIntentIdentifier))
            {
                throw new InvalidOperationException(
                    "PdfGenerateConfig.PdfXConformance is set, but PdfGenerateConfig.ColorOptions.OutputIntentIdentifier " +
                    "is not set. Set it to a human-readable name for the output-intent profile's condition " +
                    "(e.g. \"Coated FOGRA39\").");
            }

            PeachImage.IccColorProfile? outputIntentProfile = null;
            if (pdfXConformanceRequested)
            {
                PeachImage.IccColorProfile.TryCreate(config.ColorOptions!.OutputIntentProfile!, out outputIntentProfile);

                if (config.PdfXConformance == PdfXConformance.X1a &&
                    outputIntentProfile?.DataColorSpace != PeachImage.IccColorSpace.Cmyk)
                {
                    throw new InvalidOperationException(
                        "PdfGenerateConfig.PdfXConformance is PdfXConformance.X1a, which requires a CMYK " +
                        "output-intent profile, but ColorOptions.OutputIntentProfile is not a valid CMYK " +
                        "ICC profile. PdfXConformance.X3/X4 accept a CMYK, RGB, or Gray output intent instead.");
                }
            }

            // An XMP metadata stream (EnableXmpMetadata or PdfAConformance) needs a real xmp:CreateDate -
            // fail loudly rather than write a placeholder/default date, same stance as the language
            // check above. Independent of PdfAConformance: a plain EnableXmpMetadata with no resolvable
            // date throws too. PdfXConformance also needs it - PDF/X-4's GTS_PDFXVersion identification is
            // primarily an XMP property (ISO 15930-7 - see PdfMetadataStream's remarks), not just an Info
            // -dictionary one the way PDF/X-1a/X3's is.
            var writeXmpMetadata = config.EnableXmpMetadata
                || pdfAConformanceRequested
                || pdfXConformanceRequested
                || config.Metadata?.CustomXmpProperties.Count > 0;
            if (writeXmpMetadata && resolvedCreationDate is not { } creationDate)
            {
                throw new InvalidOperationException(
                    "An XMP metadata stream is being written (EnableXmpMetadata, PdfAConformance, " +
                    "PdfXConformance, or CustomXmpProperties is set), but no creation date is available: " +
                    "the source HTML has no extractable date, and PdfGenerateConfig.Metadata.CreationDate " +
                    "was not set. Set PdfDocumentMetadata.CreationDate.");
            }

            if (writeXmpMetadata)
            {
                // Rewritten on every call (not just the first) - unlike the output intent below, the
                // packet's content (title/dates/custom properties) can legitimately differ call to call,
                // so always refreshing it is more useful than freezing it at whatever the first call saw.
                var metadataStream = new PdfMetadataStream(
                    document.PdfDocument,
                    document.PdfDocument.Info,
                    resolvedCreationDate!.Value,
                    config.PdfAConformance,
                    config.PdfXConformance,
                    config.Metadata?.CustomXmpProperties ?? []);
                document.PdfDocument.Catalog.SetMetadata(metadataStream);
            }

            // Guarded on "not already present" (rather than unconditionally, like SetMetadata above) -
            // the sRGB output intent never varies call to call, so re-adding it on a second AddPdfPages
            // call with the same PdfAConformance would only leave the first call's ICC-profile stream
            // orphaned (still written into the file, just unreferenced) for no benefit.
            if (pdfAConformanceRequested && !document.PdfDocument.Catalog.Elements.ContainsKey("/OutputIntents"))
            {
                var outputIntent = new PdfOutputIntent(document.PdfDocument, PdfAResources.SRgbIccProfile);
                document.PdfDocument.Catalog.SetOutputIntent(outputIntent);
            }

            // Same "not already present" dedup as PDF/A above - ColorOptions.OutputIntentProfile is
            // validated (non-null, and CMYK under X1a) earlier in this method.
            if (pdfXConformanceRequested && !document.PdfDocument.Catalog.Elements.ContainsKey("/OutputIntents"))
            {
                var outputIntent = new PdfOutputIntent(document.PdfDocument, config.ColorOptions!.OutputIntentProfile!,
                    "/GTS_PDFX", config.ColorOptions.OutputIntentIdentifier!, config.ColorOptions.OutputIntentIdentifier!,
                    explicitN: outputIntentProfile?.ChannelCount);
                document.PdfDocument.Catalog.SetOutputIntent(outputIntent);

                // The legacy PDF/X identification mechanism (predates XMP - still what many prepress RIPs
                // and preflight tools check for X1a/X3): /GTS_PDFXVersion (all levels) and, only for the
                // part-1 "a" variant, /GTS_PDFXConformance. PdfMetadataStream.PdfXIdentifiers is the shared
                // source of truth for the exact identifier strings (and their sourcing) - this and the XMP
                // pdfx: block above always agree by construction. Guarded the same way as the OutputIntent
                // above - these never vary call to call for one document.
                var (gtsVersion, gtsConformance) = PdfMetadataStream.PdfXIdentifiers(config.PdfXConformance);
                document.PdfDocument.Info.Elements.SetString("/GTS_PDFXVersion", gtsVersion, PdfStringEncoding.RawEncoding);
                if (gtsConformance is not null)
                {
                    document.PdfDocument.Info.Elements.SetString("/GTS_PDFXConformance", gtsConformance, PdfStringEncoding.RawEncoding);
                }
            }

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
                // This slot's own resolved physical sheet size, in true PDF points - already falls back
                // to the document's base/configured size (orgPageSize) whenever no @page rule overrides
                // `size` for this slot, so every existing (non-mixed-size) document is unaffected.
                page.Height = geom.SheetHeightPt;
                page.Width = geom.SheetWidthPt;

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

                // No content-area clip pushed here (an earlier version of this method intersected one
                // directly on the raw XGraphics, ahead of FragmentPainter.Paint's own push below) - a raw
                // XGraphics.IntersectClip call made here is invisible to the RGraphics abstraction's own
                // clip-stack bookkeeping, which is what RGraphics.SuspendClipping walks to let a
                // position: fixed box's own paint reach back out to the full page box (margins included,
                // its actual containing block per CSS2.1 §10.1). Baking the content clip in here put it
                // permanently out of that call's reach, silently discarding every fixed box's own geometry
                // that legitimately fell inside the page's margins. FragmentPainter.Paint pushes the
                // equivalent content-area clip itself now (HtmlContainerInt.PageClipOverride, set below) -
                // PaintFootnoteArea, which paints outside that wrapper, pushes its own.

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
                        new XSize(page.Width, page.Height),
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

            // add web links and anchors. bookmarkBoxes rides along on the same full-tree walk
            // GetLinks() already performs (DomUtils.GetAllLinkAndBookmarkBoxes), so building the PDF
            // outline afterwards adds zero net full-tree traversals.
            var bookmarkBoxes = new List<CssBox>();
            HandleLinks(document.PdfDocument, container, fragmentainers, bookmarkBoxes, structureTagBuilder);

            if (formFieldBuilder != null)
            {
                HandleFormFields(document.PdfDocument, container, fragmentainers, formFieldBuilder);
            }

            // PDF outline (bookmarks) - CSS-default-driven (h1-h6 default to bookmark-level 1-6 via the
            // UA stylesheet, everything else defaults to none), so this is unconditional: a document
            // with no headings simply collects zero bookmark boxes above and adds no outline.
            BookmarkOutlineBuilder.Build(document.PdfDocument, container, fragmentainers, bookmarkBoxes);
        }

        #region Private/Protected methods

        /// <summary>
        /// Returns the resolved creation date (override wins over the HTML-extracted date), or
        /// <c>null</c> if neither is available - the caller decides whether that's acceptable (it
        /// isn't when an XMP metadata stream is being written, which needs a real <c>xmp:CreateDate</c>).
        /// </summary>
        private static DateTimeOffset? ApplyDocumentMetadata(PdfDocument pdfDocument, HtmlDocumentMetadata? metadata, PdfDocumentMetadata? overrides)
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

            DateTimeOffset? resolvedCreationDate;
            if (overrides?.CreationDate is { } overrideDate)
            {
                resolvedCreationDate = overrideDate;

                // .LocalDateTime (not .DateTime) - PdfDocumentInformation.CreationDate's underlying
                // PdfDate writes the DateTime's "zzz" offset directly (PdfDate.cs), and for a
                // Kind=Unspecified DateTime (what .DateTime would return) .NET's "zzz" specifier uses
                // the machine's CURRENT local offset regardless of what offset the value actually
                // carried - silently mislabeling the instant whenever overrideDate's own offset isn't
                // the local machine's (e.g. a UTC CreationDate on a non-UTC machine). .LocalDateTime
                // converts to the equivalent Kind=Local DateTime first, so "zzz" then reports the
                // correct offset for that same instant instead.
                info.CreationDate = overrideDate.LocalDateTime;
            }
            else if (metadata?.Date is { } htmlDate)
            {
                // An HTML-extracted date (e.g. <meta name="date" content="2024-03-15">) carries no
                // explicit timezone of its own - htmlDate.Kind is Unspecified. info.CreationDate is set
                // to it completely unchanged (not wrapped/reinterpreted as UTC or converted at all),
                // preserving this method's exact pre-XMP behavior for this path. To still get a
                // DateTimeOffset for XMP that agrees with what PdfDate's "zzz" will independently
                // compute for the very same Unspecified value (which treats it as local, per the same
                // .NET rule noted above), explicitly mark it Local before deriving the offset here -
                // this does not change htmlDate's own wall-clock digits, only which offset is attached.
                resolvedCreationDate = new DateTimeOffset(DateTime.SpecifyKind(htmlDate, DateTimeKind.Local));
                info.CreationDate = htmlDate;
            }
            else
            {
                resolvedCreationDate = null;
            }

            return resolvedCreationDate;
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

            // Bypasses FragmentPainter.Paint's own clip push (this is called directly, not through that
            // wrapper), so it needs its own content-area bound - previously provided for free by a raw
            // XGraphics.IntersectClip AddPdfPages applied ahead of this call, removed because it also
            // (wrongly) bounded position: fixed content the same way; see AddPdfPages's own remarks.
            graphicsAdapter.PushClip(htmlContainer.PageClipOverride ?? htmlContainer.PageBoxRect);

            var rect = footnoteArea.DividerRect;
            graphicsAdapter.DrawRectangle(graphicsAdapter.GetSolidBrush(RColor.Black), rect.X, rect.Y, rect.Width, rect.Height);

            var painter = new FragmentPainter(htmlContainer);
            foreach (var body in footnoteArea.Bodies)
            {
                painter.PaintFragment(graphicsAdapter, body);
            }

            graphicsAdapter.PopClip();
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
        private static void HandleLinks(PdfDocument document, HtmlContainer container, IReadOnlyList<FragmentainerFragment> fragmentainers, List<CssBox> bookmarkBoxes, StructureTagBuilder? structureTagBuilder = null)
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
                    ? PageAnchorResolver.ResolveRectToPage(inner, ppp, fragmentainers, slotToPage, maxMappedSlot, fragmentainers.Count, anchorRect.Value)
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
                    // margins - read off the fragmentainer itself (already corrected against this
                    // page's materialized number, same as the paint loop reads - see
                    // HtmlContainerInt.LayoutMarginBoxes and issue #148) rather than re-deriving it
                    // from the geometry table directly, so this can never disagree with what was
                    // actually painted. PDF rect y counts from the page bottom.
                    var slotGeom = fragmentainers[pageIndex].Geometry;
                    var topPt = slotGeom.MarginTopPt + (link.Rectangle.Top * ppp - inner.PageTopOf(slot)) / ppp;
                    var leftPt = slotGeom.MarginLeftPt + (link.Rectangle.Left * ppp - inner.MarginLeft) / ppp;
                    // This page's own resolved height (already written into /MediaBox by the paint loop
                    // above) - reading it directly here, rather than threading a second value through,
                    // means this can never disagree with what was actually emitted for the page.
                    var xRect = new XRect(
                        leftPt,
                        document.Pages[pageIndex].Height - (topPt + link.Rectangle.Height),
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
                        // bottom edge) - flipping against the TARGET page's own height is required (not the
                        // current slot's - a link can jump between differently-sized pages), exactly like
                        // the xRect construction below already does for the same reason.
                        document.AddNamedDestination(link.AnchorId, t.PageIndex + 1, PdfNamedDestinationParameters.CreateFitHorizontally(document.Pages[t.PageIndex].Height - t.TopPt));
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

            HandleRunningElementLinks(document, container, fragmentainers, ResolveAnchorTarget, structureTagBuilder);
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
        private static void HandleFormFields(PdfDocument document, HtmlContainer container, IReadOnlyList<FragmentainerFragment> fragmentainers, FormFieldBuilder formFieldBuilder)
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

                // Read off the fragmentainer itself (already corrected against this page's
                // materialized number - see HtmlContainerInt.LayoutMarginBoxes and issue #148) rather
                // than re-deriving from the geometry table directly, matching HandleLinks' own fix.
                var slotGeom = fragmentainers[pageIndex].Geometry;
                var topPt = slotGeom.MarginTopPt + (pixelRect.Top - inner.PageTopOf(slot)) / ppp;
                var leftPt = slotGeom.MarginLeftPt + (pixelRect.Left - inner.MarginLeft) / ppp;
                var widthPt = pixelRect.Width / ppp;
                var heightPt = pixelRect.Height / ppp;

                if (widthPt <= 0 || heightPt <= 0)
                    continue;

                var xRect = new XRect(leftPt, document.Pages[pageIndex].Height - (topPt + heightPt), widthPt, heightPt);
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
                        var xRect = new XRect(rectPt.X, document.Pages[pageIndex].Height - (rectPt.Y + rectPt.Height), rectPt.Width, rectPt.Height);

                        PdfLinkAnnotation annotation;

                        if (href[0] == '#')
                        {
                            var anchorId = href[1..];
                            var target = resolveAnchorTarget(anchorId);
                            if (target is not { } t) continue;

                            // See the same flip's comment in HandleLinks above - TopPt is top-down, /FitH's
                            // own top parameter needs bottom-up PDF default user space, flipped against the
                            // TARGET page's own height (not the current page's).
                            document.AddNamedDestination(anchorId, t.PageIndex + 1, PdfNamedDestinationParameters.CreateFitHorizontally(document.Pages[t.PageIndex].Height - t.TopPt));
                            annotation = document.Pages[pageIndex].AddDocumentLink(new PdfRectangle(xRect), anchorId);
                        }
                        else
                        {
                            // Read once per external link per page (a running element's own link is
                            // re-resolved for every page it was selected onto), so the document base has
                            // to be memoized rather than re-walked - see HtmlContainerInt.DocumentBaseUri.
                            var baseUri = container.HtmlContainerInt.DocumentBaseUri;
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