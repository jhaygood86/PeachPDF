using PeachBrackets.Annotations;
using PeachBrackets.Building;
using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Annotations;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachBrackets.Html.Pages;
using PeachBrackets.Html.Parse;
using PeachBrackets.Tagging;
using PeachBrackets.Utilities;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace PeachBrackets;

/// <summary>
/// An HTML document, laid out into pages and painted onto any <see cref="Canvas"/>: a PDF page, a bitmap, or one you implement.
/// </summary>
/// <remarks>
/// <para>
/// A document goes through three steps. <see cref="LoadAsync"/> parses the HTML and cascades its style sheets; <see cref="LayoutAsync"/>
/// breaks the content into pages; <see cref="PaintPageAsync"/> draws one page - its <c>@page</c> background and border, the content, footnotes
/// and margin boxes - onto a canvas you give it. What the pages contain besides pixels (links, outline entries, form controls) is available
/// from <see cref="Annotations"/>, so a consumer that writes a format with those concepts can carry them over.
/// </para>
/// <para>
/// Everything is measured in points (1/72 inch) except the canvases: a canvas is in the layout units of the <see cref="RenderContext"/> the
/// document was loaded with, <see cref="RenderContext.LayoutUnitsPerPoint"/> units to the point, with its origin at the top-left corner of
/// the sheet and y growing downward. A document is not thread safe.
/// </para>
/// </remarks>
public sealed class HtmlDocument : IDisposable
{
    private readonly HtmlContainerInt _container;

    /// <summary>The laid-out document the public surface wraps; reached by the test projects that assert on the box tree itself.</summary>
    internal HtmlContainerInt Container => _container;
    private readonly RenderContext _renderContext;
    private readonly double _unitsPerPoint;
    private PagePainter? _painter;
    private DocumentAnnotations? _annotations;
    private IReadOnlyList<DocumentPage>? _pages;
    private bool _disposed;

    private HtmlDocument(HtmlContainerInt container, RenderContext renderContext)
    {
        _container = container;
        _renderContext = renderContext;
        _unitsPerPoint = renderContext.LayoutUnitsPerPoint;
    }

    /// <summary>
    /// Parses <paramref name="html"/> and cascades its style sheets, ready to be laid out.
    /// </summary>
    /// <param name="html">the HTML source</param>
    /// <param name="renderContext">the fonts, images and resource loading the document uses; set its <see cref="RenderContext.LayoutUnitsPerPoint"/> before loading</param>
    /// <param name="options">the page size, margins and style environment, or null for the defaults of <see cref="HtmlDocumentOptions"/></param>
    /// <returns>the loaded document</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> or <paramref name="renderContext"/> is null</exception>
    public static async Task<HtmlDocument> LoadAsync(string html, RenderContext renderContext, HtmlDocumentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(renderContext);

        options ??= new HtmlDocumentOptions();
        var unitsPerPoint = renderContext.LayoutUnitsPerPoint;

        var container = new HtmlContainerInt(renderContext);
        var document = new HtmlDocument(container, renderContext);

        try
        {
            container.MarginTop = options.MarginTopPoints * unitsPerPoint;
            container.MarginRight = options.MarginRightPoints * unitsPerPoint;
            container.MarginBottom = options.MarginBottomPoints * unitsPerPoint;
            container.MarginLeft = options.MarginLeftPoints * unitsPerPoint;

            container.Media = string.IsNullOrEmpty(options.Media) ? "print" : options.Media;
            container.PreferredColorScheme = options.ColorScheme;
            container.IgnoreAuthorStyleSheets = options.IgnoreAuthorStyleSheets;
            container.SnapBoxDecorationsToCssPixels = options.SnapBoxDecorationsToCssPixels;
            container.SnapBorderWidthsToCssPixels = options.SnapBorderWidthsToCssPixels;
            container.AnimationProgress = options.AnimationProgress;
            container.InteractiveForms = options.CollectFormFields;

            // Read while the DOM tree is generated, when every text box is cut into words: hyphens: auto and the language-dependent
            // line-break tailorings need the language then, not after loading returns.
            container.DefaultLanguage = string.IsNullOrEmpty(options.DefaultLanguage) ? null : options.DefaultLanguage;

            // Parse-time @page relative units resolve against the page size as it stands during parsing, so the physical sheet goes in first.
            var sheet = new Size(options.PageWidthPoints, options.PageHeightPoints);
            container.PageSize = Utils.ToPixels(sheet, unitsPerPoint);

            var userStyles = options.StyleSheet?.Data
                ?? (string.IsNullOrWhiteSpace(options.UserStyleSheet)
                    ? null
                    : await PeachBrackets.Html.CssData.Parse(renderContext, options.UserStyleSheet, combineWithDefault: true));

            await container.SetHtml(html, userStyles);

            // An @page { size } rule in the document replaces the configured sheet; the content area is what is left inside the margins.
            var resolvedSheet = container.CssPageSize ?? sheet;
            // The margins are read back from the container, since an @page margin rule corrects them in place while the document is parsed.
            var content = new Size(
                resolvedSheet.Width - ((container.MarginLeft + container.MarginRight) / unitsPerPoint),
                resolvedSheet.Height - ((container.MarginTop + container.MarginBottom) / unitsPerPoint));
            container.PageSize = Utils.ToPixels(content, unitsPerPoint);
            container.Location = new PaintPoint(container.MarginLeft, container.MarginTop);

            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Makes a document from a page built in code instead of parsed from HTML, ready to be laid out.
    /// </summary>
    /// <param name="page">the page, made by <see cref="DocumentBuildContext.CreatePage"/> and filled with nodes</param>
    /// <param name="options">the page size, margins and style environment, or null for the defaults of <see cref="HtmlDocumentOptions"/></param>
    /// <param name="styleSheet">rules applied to the tree, including an <c>@page</c> rule for geometry the page did not set itself, or null for none</param>
    /// <returns>the document</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null</exception>
    public static async Task<HtmlDocument> CreateAsync(DeclarativePage page, HtmlDocumentOptions? options = null, StyleSheet? styleSheet = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        options ??= new HtmlDocumentOptions();
        var renderContext = page.Context.RenderContext;
        var unitsPerPoint = renderContext.LayoutUnitsPerPoint;
        var properties = page.Context.Properties;
        var overrides = page.Overrides;

        ResolvePendingAutoDirections(page.Root.Box, properties);

        var container = new HtmlContainerInt(renderContext);
        var document = new HtmlDocument(container, renderContext);

        try
        {
            container.MarginTop = options.MarginTopPoints * unitsPerPoint;
            container.MarginRight = options.MarginRightPoints * unitsPerPoint;
            container.MarginBottom = options.MarginBottomPoints * unitsPerPoint;
            container.MarginLeft = options.MarginLeftPoints * unitsPerPoint;

            container.PageRules = DomParser.BuildDeclarativePageRules(
                page.BuildPageRules(), styleSheet?.Data,
                marginLeftIsExplicit: overrides.HasFlag(PageGeometryOverrides.MarginLeft),
                marginTopIsExplicit: overrides.HasFlag(PageGeometryOverrides.MarginTop),
                marginRightIsExplicit: overrides.HasFlag(PageGeometryOverrides.MarginRight),
                marginBottomIsExplicit: overrides.HasFlag(PageGeometryOverrides.MarginBottom),
                sizeIsExplicit: overrides.HasFlag(PageGeometryOverrides.Size));

            var sheet = new Size(options.PageWidthPoints, options.PageHeightPoints);
            container.PageSize = Utils.ToPixels(sheet, unitsPerPoint);
            container.SnapBoxDecorationsToCssPixels = options.SnapBoxDecorationsToCssPixels;
            container.SnapBorderWidthsToCssPixels = options.SnapBorderWidthsToCssPixels;
            await container.SetDeclarativeRoot(page.Root.Box, options.DefaultLanguage, styleSheet?.Data, properties.DisplayContentsShells);

            if (styleSheet is not null)
            {
                // A document-level style sheet's own base @page rule may still adjust whole-page geometry, but only for an edge or size the page
                // itself left unset. Run after the root is set (it needs the container for em/rem-relative margin resolution).
                DomParser.CascadeApplyPageStyles(container, page.Root.Box, styleSheet.Data,
                    allowMarginLeft: !overrides.HasFlag(PageGeometryOverrides.MarginLeft),
                    allowMarginTop: !overrides.HasFlag(PageGeometryOverrides.MarginTop),
                    allowMarginRight: !overrides.HasFlag(PageGeometryOverrides.MarginRight),
                    allowMarginBottom: !overrides.HasFlag(PageGeometryOverrides.MarginBottom),
                    allowSize: !overrides.HasFlag(PageGeometryOverrides.Size));

                if (container.CssPageSize.HasValue)
                    sheet = container.CssPageSize.Value;
            }

            // The content page size is the sheet less its margins, and the document origin sits at the top-left of that band.
            var content = new Size(
                sheet.Width - ((container.MarginLeft + container.MarginRight) / unitsPerPoint),
                sheet.Height - ((container.MarginTop + container.MarginBottom) / unitsPerPoint));
            container.PageSize = Utils.ToPixels(content, unitsPerPoint);
            container.Location = new PaintPoint(container.MarginLeft, container.MarginTop);

            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void ResolvePendingAutoDirections(CssBox root, CssPropertyFactory properties)
    {
        if (root.PendingAutoDirection)
        {
            properties.Set(root, "direction", BidiDirectionalityResolver.ScanForStrongDirection(root) ?? Keywords.Ltr);
            root.PendingAutoDirection = false;
        }

        foreach (var child in root.Boxes)
            ResolvePendingAutoDirections(child, properties);
    }

    /// <summary>
    /// The size of the area the content flows into: the sheet less its margins, in points. Known as soon as the document is loaded.
    /// </summary>
    public Size ContentArea => Utils.ToPoints(_container.PageSize, _unitsPerPoint);

    /// <summary>
    /// The extent the laid-out content actually occupies, in points. Wider than <see cref="ContentArea"/> when something does not fit across,
    /// which is how a consumer that wants to shrink the content to fit finds out by how much.
    /// </summary>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public Size ContentExtent
    {
        get
        {
            _ = Pages;
            return Utils.ToPoints(_container.ActualSize, _unitsPerPoint);
        }
    }

    /// <summary>The language the document declares (<c>&lt;html lang&gt;</c>) or was given as the default, or null if there is none.</summary>
    public string? Language => _container.DocumentLanguage;

    /// <summary>What the document says about itself - its title and <c>&lt;meta&gt;</c> elements - or null if it says nothing.</summary>
    public HtmlDocumentMetadata? Metadata => _container.DocumentMetadata;

    /// <summary>
    /// Receives the logical structure of the content as it is painted, or null (the default) to record none. Set it before
    /// <see cref="PaintPageAsync"/>; a consumer that writes a tagged format implements <see cref="IStructureTagSink"/> to build its structure tree.
    /// </summary>
    public IStructureTagSink? StructureTagSink
    {
        get => _container.StructureTagBuilder;
        set => _container.StructureTagBuilder = value;
    }

    /// <summary>
    /// Breaks the document into pages. Call it once, after <see cref="LoadAsync"/> and before anything that needs the pages.
    /// </summary>
    /// <param name="measuringCanvas">a canvas made by the same render context, used to measure text; nothing is drawn on it</param>
    /// <returns>a task that completes when the pages are known</returns>
    /// <exception cref="ArgumentNullException"><paramref name="measuringCanvas"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document was already laid out</exception>
    public async Task LayoutAsync(Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        ThrowIfDisposed();
        if (_pages is not null)
            throw new InvalidOperationException("The document is already laid out.");

        var pageSize = Utils.ToPoints(_container.PageSize, _unitsPerPoint);
        _container.MaxSize = Utils.ToPixels(new Size(pageSize.Width, 0), _unitsPerPoint);
        await _container.PerformLayout(measuringCanvas);

        var fragmentainers = _container.FragmentTree?.Fragmentainers ?? [];
        _pages = fragmentainers
            .Select((fragmentainer, index) => new DocumentPage(index, fragmentainer.Geometry.SheetWidthPt, fragmentainer.Geometry.SheetHeightPt))
            .ToArray();
        _painter = new PagePainter(_container, _renderContext);
    }

    /// <summary>The pages of the laid-out document, in order.</summary>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public IReadOnlyList<DocumentPage> Pages
    {
        get
        {
            ThrowIfDisposed();
            return _pages ?? throw new InvalidOperationException("Call LayoutAsync before reading the pages.");
        }
    }

    /// <summary>
    /// Paints one page onto <paramref name="canvas"/>.
    /// </summary>
    /// <param name="pageIndex">the zero-based index of the page, from 0 to <c>Pages.Count - 1</c></param>
    /// <param name="canvas">the canvas to draw on: the size of the page's sheet in layout units, origin at its top-left corner</param>
    /// <returns>a task that completes when the page is drawn</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of the document</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public async Task PaintPageAsync(int pageIndex, Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pages = Pages;
        if ((uint)pageIndex >= (uint)pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"The document has {pages.Count} page(s).");

        var fragmentainer = _painter!.Fragmentainers[pageIndex];
        await _painter.PaintPageAsync(
            fragmentainer,
            pageIndex + 1,
            () => new CanvasLease(canvas),
            (x, y) =>
            {
                canvas.PushTransform(Matrix3x2.CreateTranslation((float)(x * _unitsPerPoint), (float)(y * _unitsPerPoint)));
                return new TransformScope(canvas);
            });
    }

    /// <summary>
    /// What the pages hold besides pixels: the links, the outline and the form controls, positioned on their pages in points.
    /// </summary>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public DocumentAnnotations Annotations
    {
        get
        {
            _ = Pages;
            return _annotations ??= DocumentAnnotationCollector.Collect(_container, _container.FragmentTree?.Fragmentainers ?? []);
        }
    }

    /// <summary>
    /// The words that were painted but cut off by an ancestor's <c>overflow</c> clip, with how much of each showed. Read it after painting
    /// the pages to find out whether content was silently lost.
    /// </summary>
    public IReadOnlyList<ClippedTextEntry> ClippedText => _container.ClipReport.Words;

    /// <summary>
    /// The bounds of the element with the given <c>id</c> on its page, in points from the top-left of the content area, or null if there is
    /// no such element.
    /// </summary>
    /// <param name="elementId">the element's <c>id</c></param>
    /// <returns>the element's rectangle, or null</returns>
    public Rect? GetElementBounds(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        var rect = _container.GetElementRectangle(elementId);
        return rect.HasValue ? Utils.ToPoints(rect.Value, _unitsPerPoint) : null;
    }

    /// <summary>Releases the document's cached images and layout state.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _painter?.Dispose();
        _container.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class TransformScope(Canvas canvas) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            canvas.PopTransform();
        }
    }
}
