using PeachBrackets.Annotations;
using PeachBrackets.Building;
using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Annotations;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachBrackets.Html.Pages;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.TextGeometry;
using PeachBrackets.Html.Updates;
using PeachBrackets.Selection;
using PeachBrackets.Tagging;
using PeachBrackets.Text;
using PeachBrackets.Updates;
using PeachBrackets.Utilities;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using TextPosition = PeachBrackets.Text.TextPosition;
using TextRange = PeachBrackets.Text.TextRange;

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
    private IReadOnlyList<TextRun>? _textRuns;
    private bool _recordTextRuns;
    private bool _selectionStyling;
    private bool _incrementalUpdates;
    private DocumentSelection? _selection;
    private IReadOnlyList<TextRange> _selectedRanges = [];
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
            document.ApplyTextOptions(options);

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
            document.ApplyTextOptions(options);
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

        await LayOutAsync(measuringCanvas);
    }

    // The layout itself, which an edit of the text runs again over the same tree.
    // With a changed box, the layout is picked up from the pass before the one that placed it when it can be (the slot it resumed in).
    private async Task<int?> LayOutAsync(Canvas measuringCanvas, CssBox? changed = null)
    {
        var pageSize = Utils.ToPoints(_container.PageSize, _unitsPerPoint);
        _container.MaxSize = Utils.ToPixels(new Size(pageSize.Width, 0), _unitsPerPoint);

        int? resumedSlot = null;
        if (changed is not null)
            resumedSlot = await _container.TryResumeLayoutAt(changed, measuringCanvas);

        if (resumedSlot is null)
            await _container.PerformLayout(measuringCanvas);

        var fragmentainers = _container.FragmentTree?.Fragmentainers ?? [];
        _pages = fragmentainers
            .Select((fragmentainer, index) => new DocumentPage(index, fragmentainer.Geometry.SheetWidthPt, fragmentainer.Geometry.SheetHeightPt))
            .ToArray();
        _painter?.Dispose();
        _painter = new PagePainter(_container, _renderContext);
        return resumedSlot;
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
    /// Puts the links of one page on <paramref name="canvas"/> - <see cref="Canvas.AddLink"/> for a link to an address,
    /// <see cref="Canvas.AddDestinationLink"/> for a link to a place in the document - and names on it the places on this page that links
    /// lead to (<see cref="Canvas.AddDestination"/>), so a canvas for a format with hyperlinks writes them. Call it for each page <b>after every
    /// page has been painted</b>: where a link is, if a transform moved what it covers, is only known once its page has been painted, and a link
    /// can lead to a page that comes later. A canvas with no hyperlinks ignores it.
    /// </summary>
    /// <param name="pageIndex">the zero-based index of the page <paramref name="canvas"/> draws</param>
    /// <param name="canvas">the canvas of that page</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of the document</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public void PaintLinks(int pageIndex, Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pages = Pages;
        if ((uint)pageIndex >= (uint)pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"The document has {pages.Count} page(s).");

        var units = _unitsPerPoint;
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in Annotations.Links)
        {
            // The place this link leads to, when it is on this page.
            if (link.Anchor is { } anchorId && link.Destination is { } destination && destination.PageIndex == pageIndex && named.Add(anchorId))
                canvas.AddDestination(anchorId, destination.Top * units);

            if (link.PageIndex != pageIndex)
                continue;

            var bounds = new Rect(link.Bounds.X * units, link.Bounds.Y * units, link.Bounds.Width * units, link.Bounds.Height * units);
            var quad = link.Quad is { } corners ? corners.Select(c => new PaintPoint(c.X * units, c.Y * units)).ToArray() : null;
            var handle = link.Anchor is { } anchor && link.Destination is not null
                ? canvas.AddDestinationLink(bounds, anchor, quad)
                : canvas.AddLink(bounds, link.Uri ?? string.Empty, quad);

            if (handle is not null && link.Source is { } source)
                StructureTagSink?.LinkAdded(source, handle);
        }
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
    /// The text of the document as it was laid out, one run per line of a block, in reading order, positioned on its page in points (the space
    /// <see cref="Annotations"/> uses) with the x position of every character: what a consumer needs to build text selection, copy and find.
    /// Needs <see cref="HtmlDocumentOptions.RecordTextRuns"/>. Text drawn vertically, in a margin box or in a footnote is not included, and
    /// text moved by a transform is reported where layout put it.
    /// </summary>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public IReadOnlyList<TextRun> TextRuns
    {
        get
        {
            _ = Pages;
            if (!_recordTextRuns)
                throw new InvalidOperationException("Set HtmlDocumentOptions.RecordTextRuns before loading the document to read its text runs.");

            return _textRuns ??= TextRunCollector.Collect(_container, _container.FragmentTree?.Fragmentainers ?? []);
        }
    }

    /// <summary>The text runs on one page, in reading order.</summary>
    /// <param name="pageIndex">the zero-based index of the page</param>
    /// <returns>the runs of that page</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of the document</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public IReadOnlyList<TextRun> GetTextRuns(int pageIndex)
    {
        ThrowIfNotAPage(pageIndex);
        return TextRuns.Where(run => run.PageIndex == pageIndex).ToArray();
    }

    /// <summary>
    /// The character at a point of a page: the position of the start of the character the point is over, in <see cref="TextRuns"/>. A point
    /// in a gap or margin is taken to the nearest line, and one beyond a line's end to its nearest character.
    /// </summary>
    /// <param name="pageIndex">the zero-based index of the page</param>
    /// <param name="x">the distance from the left edge of the sheet, in points</param>
    /// <param name="y">the distance from the top edge of the sheet, in points</param>
    /// <returns>the position, or null if the page has no text</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of the document</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public TextPosition? HitTest(int pageIndex, double x, double y)
    {
        ThrowIfNotAPage(pageIndex);
        return TextRunQueries.HitTest(TextRuns, pageIndex, x, y, caret: false);
    }

    /// <summary>
    /// The place between two characters nearest a point of a page, where a text cursor or the end of a drag selection goes. See
    /// <see cref="HitTest"/>, which gives the character itself.
    /// </summary>
    /// <param name="pageIndex">the zero-based index of the page</param>
    /// <param name="x">the distance from the left edge of the sheet, in points</param>
    /// <param name="y">the distance from the top edge of the sheet, in points</param>
    /// <returns>the position, or null if the page has no text</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of the document</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public TextPosition? GetCaretPosition(int pageIndex, double x, double y)
    {
        ThrowIfNotAPage(pageIndex);
        return TextRunQueries.HitTest(TextRuns, pageIndex, x, y, caret: true);
    }

    /// <summary>The rectangles, one per line and in points, that cover the text between two positions: what to fill to show a selection.</summary>
    /// <param name="from">where the selection starts (either end may come first)</param>
    /// <param name="to">where it ends, not included</param>
    /// <returns>the rectangles, each as tall as its line's font</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public IReadOnlyList<Rect> GetSelectionRects(TextPosition from, TextPosition to) => TextRunQueries.SelectionRects(TextRuns, from, to);

    /// <summary>
    /// The plain text between two positions as it would be copied: a line break between blocks and at a forced break (a <c>pre</c> line, a
    /// <c>&lt;br&gt;</c>), a single space where a paragraph wrapped, and no space that was not in the text.
    /// </summary>
    /// <param name="from">where the text starts (either end may come first)</param>
    /// <param name="to">where it ends, not included</param>
    /// <returns>the text</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public string GetText(TextPosition from, TextPosition to) => TextRunQueries.GetText(TextRuns, from, to);

    /// <summary>Finds every place a string occurs in the text of the document, as <see cref="GetText"/> reads it.</summary>
    /// <param name="query">the text to look for</param>
    /// <param name="ignoreCase">whether to ignore the case of letters (ordinal, not culture aware)</param>
    /// <returns>where each occurrence starts and ends, in order; empty if there is none</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public IReadOnlyList<TextRange> FindText(string query, bool ignoreCase = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        return TextRunQueries.Find(TextRuns, query, ignoreCase);
    }

    /// <summary>The word at a position, as a double click selects it, or null if the position is not in any text.</summary>
    /// <param name="position">a position in <see cref="TextRuns"/></param>
    /// <returns>the range of the word</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public TextRange? GetWordAt(TextPosition position) => TextRunQueries.WordAt(TextRuns, position);

    /// <summary>The whole block (paragraph, list item, <c>pre</c>) at a position, as a triple click selects it, or null if the position is not in any text.</summary>
    /// <param name="position">a position in <see cref="TextRuns"/></param>
    /// <returns>the range of the block</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public TextRange? GetBlockAt(TextPosition position) => TextRunQueries.BlockAt(TextRuns, position);

    /// <summary>
    /// Selects the text between pairs of positions of <see cref="TextRuns"/>, painted by <see cref="PaintPageAsync"/> in the document's
    /// <c>::selection</c> style: the background fills the selected characters (and the spaces between selected words), and a <c>color</c> the
    /// rules set replaces the text colour. Pass null or no ranges to clear the selection. Repaint the pages to show a change.
    /// </summary>
    /// <param name="ranges">the selected ranges, as <see cref="FindText"/>, <see cref="GetWordAt"/> and <see cref="GetBlockAt"/> return them</param>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> or <see cref="HtmlDocumentOptions.EnableSelectionStyling"/> was not set</exception>
    public void SetSelectedRanges(IEnumerable<TextRange>? ranges)
    {
        var runs = TextRuns;
        if (!_selectionStyling)
            throw new InvalidOperationException("Set HtmlDocumentOptions.EnableSelectionStyling before loading the document to paint a selection.");

        _selectedRanges = ranges?.ToArray() ?? [];
        _container.SelectionModel = _selectedRanges.Count == 0 ? null : SelectionModel.Build(runs, _selectedRanges);
    }

    private readonly List<(string Name, int Priority, int Order, TextRange[] Ranges)> _highlights = [];
    private int _highlightOrder;

    /// <summary>
    /// Paints the text between pairs of positions of <see cref="TextRuns"/> in a named highlight: the rules of <c>::highlight(<i>name</i>)</c> style
    /// it, as the CSS Custom Highlight API does for ranges a page registers. The names <c>spelling-error</c>, <c>grammar-error</c> and
    /// <c>target-text</c> are the built-in highlights (<c>::spelling-error</c> and so on), which an embedder applies to what its checker or find
    /// finds. A highlight with no rule matching paints nothing. Highlights are painted lowest <paramref name="priority"/> first, equal priorities in
    /// the order they were first set, the built-in ones below the custom ones, and the selection over all of them; where several change the text
    /// colour the one painted last wins. Setting a name again replaces its ranges. Repaint the pages to show a change; a text edit removes every
    /// highlight, since the positions they hold no longer mean the same text.
    /// </summary>
    /// <param name="name">the highlight's name: a CSS identifier, without the <c>::highlight()</c> around it</param>
    /// <param name="ranges">the highlighted ranges; null or none removes the highlight</param>
    /// <param name="priority">where it is painted among the custom highlights; higher is painted later, over the others</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or is not a CSS identifier</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> or <see cref="HtmlDocumentOptions.EnableSelectionStyling"/> was not set</exception>
    public void SetHighlight(string name, IEnumerable<TextRange>? ranges, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsCssIdentifier(name))
            throw new ArgumentException("A highlight name is a CSS identifier.", nameof(name));

        _ = TextRuns;
        if (!_selectionStyling)
            throw new InvalidOperationException("Set HtmlDocumentOptions.EnableSelectionStyling before loading the document to paint highlights.");

        var existing = _highlights.FindIndex(h => h.Name == name);
        var order = existing >= 0 ? _highlights[existing].Order : _highlightOrder++;
        var array = ranges?.ToArray() ?? [];
        if (existing >= 0) _highlights.RemoveAt(existing);
        if (array.Length > 0) _highlights.Add((name, priority, order, array));

        RebuildHighlights();
    }

    /// <summary>
    /// Finds the text a URL text fragment directive names (the <c>text=[prefix-,]start[,end][,-suffix]</c> after <c>#:~:</c>) and paints it in the
    /// <c>::target-text</c> style - what a browser does when a link carries one. The match is case-insensitive over the document's text, the first
    /// one whose prefix and suffix are around it; an <c>end</c> extends it to the next occurrence of that text. A directive that matches nothing
    /// removes the <c>target-text</c> highlight. Several <c>text=</c> terms joined by <c>&amp;</c> are tried in order. Repaint the pages to show it.
    /// </summary>
    /// <param name="directive">the directive, such as <c>text=quick%20brown,fox</c>; percent-encoding is decoded</param>
    /// <returns>the range it matched, or null</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directive"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> or <see cref="HtmlDocumentOptions.EnableSelectionStyling"/> was not set</exception>
    public TextRange? ApplyTextFragmentDirective(string directive)
    {
        ArgumentNullException.ThrowIfNull(directive);
        var runs = TextRuns;

        TextRange? match = null;
        foreach (var term in directive.Split('&'))
        {
            if (!term.StartsWith("text=", StringComparison.Ordinal)) continue;

            match = MatchTextDirective(runs, term.Substring(5));
            if (match is not null) break;
        }

        SetHighlight("target-text", match is { } found ? [found] : null);
        return match;
    }

    private TextRange? MatchTextDirective(IReadOnlyList<TextRun> runs, string term)
    {
        if (runs.Count == 0) return null;

        var parts = term.Split(',').Select(Uri.UnescapeDataString).ToList();
        string? prefix = null;
        string? suffix = null;
        if (parts.Count > 1 && parts[0].EndsWith('-')) { prefix = parts[0][..^1]; parts.RemoveAt(0); }
        if (parts.Count > 1 && parts[^1].StartsWith('-')) { suffix = parts[^1][1..]; parts.RemoveAt(parts.Count - 1); }
        if (parts.Count is < 1 or > 2 || parts[0].Length == 0) return null;

        var start = parts[0];
        var end = parts.Count == 2 ? parts[1] : null;
        var origin = new TextPosition(0, 0);
        var last = new TextPosition(runs.Count - 1, runs[^1].Text.Length);

        foreach (var candidate in FindText(start, ignoreCase: true))
        {
            if (prefix is not null && !GetText(origin, candidate.Start).TrimEnd().EndsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

            var range = candidate;
            if (end is not null)
            {
                var tail = FindText(end, ignoreCase: true).Where(e => e.Start.CompareTo(candidate.End) >= 0).Cast<TextRange?>().FirstOrDefault();
                if (tail is null) continue;
                range = new TextRange(candidate.Start, tail.Value.End);
            }

            if (suffix is not null && !GetText(range.End, last).TrimStart().StartsWith(suffix.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

            return range;
        }

        return null;
    }

    /// <summary>Removes a named highlight (<see cref="SetHighlight"/> with no ranges).</summary>
    /// <param name="name">the highlight's name</param>
    /// <returns>whether there was one</returns>
    public bool RemoveHighlight(string name)
    {
        var removed = _highlights.RemoveAll(h => h.Name == name) > 0;
        if (removed) RebuildHighlights();
        return removed;
    }

    /// <summary>Removes every named highlight; the selection is not one.</summary>
    public void ClearHighlights()
    {
        _highlights.Clear();
        RebuildHighlights();
    }

    /// <summary>The names of the highlights set, in the order they are painted.</summary>
    public IReadOnlyList<string> HighlightNames => OrderedHighlights().Select(h => h.Name).ToArray();

    private IEnumerable<(string Name, int Priority, int Order, TextRange[] Ranges)> OrderedHighlights() => _highlights
        .OrderBy(h => BuiltInHighlightRank(h.Name) is { } rank ? rank : int.MaxValue / 2)
        .ThenBy(h => h.Priority)
        .ThenBy(h => h.Order);

    // The built-in highlights sit below every custom one, in the order CSS Pseudo-Elements 4 lists them.
    private static int? BuiltInHighlightRank(string name) => name switch
    {
        "spelling-error" => 0,
        "grammar-error" => 1,
        "target-text" => 2,
        _ => null
    };

    private static string HighlightPseudoName(string name) => BuiltInHighlightRank(name) is not null ? name : $"highlight({name})";

    private void RebuildHighlights()
    {
        var runs = TextRuns;
        _container.HighlightLayers = _highlights.Count == 0
            ? []
            : OrderedHighlights().Select(h => new HtmlContainerInt.HighlightLayer(HighlightPseudoName(h.Name), SelectionModel.Build(runs, h.Ranges))).ToArray();
    }

    private static bool IsCssIdentifier(string name)
    {
        if (name.Length == 0 || char.IsDigit(name[0]) || (name[0] == '-' && name.Length > 1 && char.IsDigit(name[1])) || name == "-") return false;
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 0x7F)) return false;
        }

        return true;
    }

    /// <summary>
    /// The document's selection: an anchor and a focus, movement by character, word, line and block, and the text and rectangles of what is
    /// selected, for a consumer to drive from its pointer and keyboard events. With <see cref="HtmlDocumentOptions.EnableSelectionStyling"/> it paints
    /// itself through <see cref="SetSelectedRanges"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public DocumentSelection Selection
    {
        get
        {
            _ = TextRuns;
            return _selection ??= new DocumentSelection(this);
        }
    }

    internal bool SelectionStylingEnabled => _selectionStyling;

    /// <summary>
    /// The innermost element the text at a position belongs to - the element a character of <see cref="TextRuns"/> was written in - or null if
    /// the position is not in the text.
    /// </summary>
    /// <param name="position">a position in <see cref="TextRuns"/></param>
    /// <returns>the element, or null</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> was not set</exception>
    public DocumentElement? GetElementAt(TextPosition position)
    {
        var runs = TextRuns;
        if ((uint)position.RunIndex >= (uint)runs.Count)
            return null;

        var run = runs[position.RunIndex];
        if (run.Slices.Count == 0)
            return null;

        // The word the character is in (the one before it at an edge between two).
        var slice = run.Slices[0];
        foreach (var candidate in run.Slices)
        {
            if (candidate.TextStart <= position.CharIndex)
                slice = candidate;
        }

        var box = slice.Word.OwnerBox;
        while (box.HtmlTag is null && box.ParentBox is { } parent)
            box = parent;

        return box.HtmlTag is null ? null : new DocumentElement(box);
    }

    /// <summary>The ranges last given to <see cref="SetSelectedRanges"/>; empty when nothing is selected.</summary>
    public IReadOnlyList<TextRange> SelectedRanges => _selectedRanges;

    /// <summary>The element with the given <c>id</c>, as a handle for <see cref="SetStyle"/>, or null if there is none.</summary>
    /// <param name="elementId">the element's <c>id</c></param>
    /// <returns>the element, or null</returns>
    public DocumentElement? GetElement(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        return DomUtils.GetBoxById(_container.Root, elementId) is { } box ? new DocumentElement(box) : null;
    }

    /// <summary>
    /// The innermost element drawn at a point of a page - the one whose text, line or box is under it - or null if there is none. A pointer
    /// handler uses it to find what was clicked.
    /// </summary>
    /// <param name="pageIndex">the zero-based index of the page</param>
    /// <param name="x">the distance from the left edge of the sheet, in points</param>
    /// <param name="y">the distance from the top edge of the sheet, in points</param>
    /// <returns>the element, or null</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of the document</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public DocumentElement? GetElementAt(int pageIndex, double x, double y)
    {
        ThrowIfNotAPage(pageIndex);
        var fragmentainers = _container.FragmentTree?.Fragmentainers ?? [];
        return StyleUpdater.ElementAt(_container, fragmentainers[pageIndex], x, y) is { } box ? new DocumentElement(box) : null;
    }

    /// <summary>
    /// Changes one CSS property of an element of the laid-out document, in place, and reports what has to be repainted - no layout pass runs, and
    /// the document is not loaded again. Applied for the properties that only change how something is painted: <c>color</c> (which the
    /// element's descendants that inherited it follow), <c>background-color</c>, <c>text-decoration-line</c>, <c>-style</c> and <c>-color</c>, and
    /// <c>outline-color</c>. Anything else, a value that does not parse, or the background of <c>html</c> or <c>body</c> is not applied
    /// and leaves the document untouched; the result says why. Repaint the regions it returns (<see cref="PaintPageRegionAsync"/>).
    /// </summary>
    /// <param name="element">the element, from <see cref="GetElement"/> or <see cref="GetElementAt(int, double, double)"/></param>
    /// <param name="property">the CSS property name</param>
    /// <param name="value">the CSS value, as it would be written in a style sheet</param>
    /// <returns>whether it was applied and the regions that changed</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.EnableIncrementalUpdates"/> was not set</exception>
    public UpdateResult SetStyle(DocumentElement element, string property, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        _ = Pages;
        if (!_incrementalUpdates)
            throw new InvalidOperationException("Set HtmlDocumentOptions.EnableIncrementalUpdates before loading the document to restyle it in place.");

        return StyleUpdater.Apply(_container, element.Box, property, value, _container.FragmentTree?.Fragmentainers ?? []);
    }

    /// <summary>
    /// Replaces the text between two positions of <see cref="TextRuns"/> with <paramref name="text"/>, in the document itself, and lays it
    /// out again: the way to type, delete or paste into a document an embedder shows. Text boxes the replacement empties are removed, and a range
    /// across several text boxes (inline elements, paragraphs) is deleted between its ends, without joining the blocks. Nothing is changed, and the
    /// result says why, for a document whose layout depends on every page (footnotes, page floats, <c>target-counter</c>, named pages, running
    /// elements, <c>@page</c> rules, multiple columns, container queries), while a structure tag sink is attached, and for a position in generated
    /// content, <c>svg</c> or <c>math</c>, or on a line break. The caret in the result is just after the inserted text; the document's
    /// <see cref="Selection"/> is collapsed to it.
    /// </summary>
    /// <param name="from">where the replaced text starts (either end may come first)</param>
    /// <param name="to">where it ends, not included; the same as <paramref name="from"/> to insert</param>
    /// <param name="text">the new text, which may be empty</param>
    /// <param name="measuringCanvas">a canvas made by the same render context, used to measure text; nothing is drawn on it</param>
    /// <returns>whether it was applied, the page count before and after, and what to repaint</returns>
    /// <exception cref="ArgumentNullException">an argument is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out, or <see cref="HtmlDocumentOptions.RecordTextRuns"/> or <see cref="HtmlDocumentOptions.EnableIncrementalUpdates"/> was not set</exception>
    public Task<TextEditResult> ReplaceTextAsync(TextPosition from, TextPosition to, string text, Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(text);
        return EditAsync(measuringCanvas, () =>
        {
            var runs = TextRuns;
            var (start, startReason) = TextEditor.ToSource(runs, from, isRangeStart: from.CompareTo(to) <= 0 && from != to);
            if (start is not { } a) return (null, startReason);

            var (end, endReason) = TextEditor.ToSource(runs, to, isRangeStart: from.CompareTo(to) > 0);
            if (end is not { } b) return (null, endReason);

            _editAnchor = CssBidiParagraphResolver.ParagraphRootOf((from.CompareTo(to) <= 0 ? a : b).Box);
            return (TextEditor.Replace(_container, a, b, text, out var reason), reason);
        });
    }

    /// <summary>Inserts text at a position: <see cref="ReplaceTextAsync"/> over an empty range.</summary>
    /// <param name="at">where to insert</param>
    /// <param name="text">the text</param>
    /// <param name="measuringCanvas">a canvas made by the same render context, used to measure text</param>
    /// <returns>see <see cref="ReplaceTextAsync"/></returns>
    public Task<TextEditResult> InsertTextAsync(TextPosition at, string text, Canvas measuringCanvas) => ReplaceTextAsync(at, at, text, measuringCanvas);

    /// <summary>Deletes the text between two positions: <see cref="ReplaceTextAsync"/> with no new text.</summary>
    /// <param name="from">where the deleted text starts</param>
    /// <param name="to">where it ends, not included</param>
    /// <param name="measuringCanvas">a canvas made by the same render context, used to measure text</param>
    /// <returns>see <see cref="ReplaceTextAsync"/></returns>
    public Task<TextEditResult> DeleteTextAsync(TextPosition from, TextPosition to, Canvas measuringCanvas) => ReplaceTextAsync(from, to, string.Empty, measuringCanvas);

    /// <summary>
    /// Appends text to an element's own text - into its last text, or as a new text when it has none - so an element with no text, which
    /// has no position to <see cref="InsertTextAsync(TextPosition, string, Canvas)"/> at, can be given some. An element that holds blocks, or is
    /// <c>svg</c>, <c>math</c>, an image or a form control, is refused.
    /// </summary>
    /// <param name="element">the element, from <see cref="GetElement"/> or <see cref="GetElementAt(int, double, double)"/></param>
    /// <param name="text">the text</param>
    /// <param name="measuringCanvas">a canvas made by the same render context, used to measure text</param>
    /// <returns>see <see cref="ReplaceTextAsync"/></returns>
    public Task<TextEditResult> InsertTextAsync(DocumentElement element, string text, Canvas measuringCanvas)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(text);
        return EditAsync(measuringCanvas, () =>
        {
            _editAnchor = CssBidiParagraphResolver.ParagraphRootOf(element.Box);
            return (TextEditor.InsertInto(element.Box, text, out var reason), reason);
        });
    }

    /// <summary>
    /// Splits the paragraph at a position into two, as Enter does: the text after it (and the inline elements it is inside) moves to a new
    /// paragraph of the same element and style after this one. Only a plain paragraph - a block in a block, with only inline content and no
    /// generated content - is split; anything else is refused. The caret is the start of the new paragraph's text, or null when it has none
    /// (the split was at the end), in which case <see cref="TextEditResult.Block"/> is the new, empty paragraph.
    /// Structural selectors (<c>:nth-child</c>, <c>:last-child</c>) are not re-evaluated; the new paragraph copies the style of the old one.
    /// </summary>
    /// <param name="at">where to split</param>
    /// <param name="measuringCanvas">a canvas made by the same render context, used to measure text</param>
    /// <returns>see <see cref="ReplaceTextAsync"/></returns>
    public Task<TextEditResult> SplitParagraphAsync(TextPosition at, Canvas measuringCanvas)
    {
        CssBox? created = null;
        return EditAsync(measuringCanvas, () =>
        {
            var (point, reason) = TextEditor.ToSource(TextRuns, at, isRangeStart: true);
            if (point is not { } source) return (null, reason);

            _editAnchor = CssBidiParagraphResolver.ParagraphRootOf(source.Box);
            if (TextEditor.Split(_container, source, out reason) is not var (caret, block)) return (null, reason);

            created = block;
            return (caret ?? new TextEditor.SourcePoint(block!, 0), null);
        }, () => created);
    }

    /// <summary>
    /// Joins the paragraph holding a position with the one after it, as Delete at the end of a paragraph does: the next paragraph's content
    /// moves to the end of this one and the next paragraph is removed. Both must be plain paragraphs (see <see cref="SplitParagraphAsync"/>) of the
    /// same element, adjacent in their parent. The caret is where the two met.
    /// </summary>
    /// <param name="at">a position in the first of the two paragraphs</param>
    /// <param name="measuringCanvas">a canvas made by the same render context, used to measure text</param>
    /// <returns>see <see cref="ReplaceTextAsync"/></returns>
    public Task<TextEditResult> JoinWithNextParagraphAsync(TextPosition at, Canvas measuringCanvas) =>
        EditAsync(measuringCanvas, () =>
        {
            var (point, reason) = TextEditor.ToSource(TextRuns, at, isRangeStart: true);
            if (point is not { } source) return (null, reason);

            _editAnchor = CssBidiParagraphResolver.ParagraphRootOf(source.Box);
            return TextEditor.Join(_container, source, out reason) is { } joined ? (joined.Caret ?? source, null) : (null, reason);
        });

    // The paragraph the edit being made starts in, found before it changes anything: layout is picked up from the pass that placed it.
    private CssBox? _editAnchor;

    private async Task<TextEditResult> EditAsync(Canvas measuringCanvas, Func<(TextEditor.SourcePoint? Caret, string? Reason)> change, Func<CssBox?>? createdBlock = null)
    {
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        var before = TextRuns;
        if (!_incrementalUpdates)
            throw new InvalidOperationException("Set HtmlDocumentOptions.EnableIncrementalUpdates before loading the document to edit its text.");

        var pagesBefore = Pages.Count;
        if (TextEditor.DocumentRefusal(_container) is { } refusal)
            return TextEditResult.NotApplied(refusal, pagesBefore);

        _editAnchor = null;
        var (caretPoint, reason) = change();
        if (caretPoint is null)
            return TextEditResult.NotApplied(reason ?? "The text could not be changed.", pagesBefore);

        // What the old layout looked like, to say where the new one differs.
        var signature = Signature(before, pagesBefore);

        _container.ClipReport.Words.Clear();
        _container.SelectionModel = null;
        _selectedRanges = [];
        _highlights.Clear();
        _container.HighlightLayers = [];
        _textRuns = null;
        _annotations = null;
        var resumedSlot = await LayOutAsync(measuringCanvas, _editAnchor);
        _editAnchor = null;

        var runs = TextRuns;
        var first = FirstDifferentPage(signature, Signature(runs, _pages!.Count));
        var regions = new List<DirtyRegion>();
        if (first is { } firstPage)
        {
            for (var page = firstPage; page < _pages.Count; page++)
                regions.Add(new DirtyRegion(page, new Rect(0, 0, _pages[page].WidthPoints, _pages[page].HeightPoints)));
        }

        var caret = TextEditor.ToPosition(runs, caretPoint.Value);
        var selection = Selection;
        if (caret is { } position) selection.Collapse(position);
        else selection.RemoveAllRanges();

        var block = createdBlock?.Invoke() is { } box ? new DocumentElement(box) : null;
        int? resumedPage = null;
        if (resumedSlot is { } slot)
        {
            var fragmentainers = _container.FragmentTree?.Fragmentainers ?? [];
            for (var i = 0; i < fragmentainers.Count && resumedPage is null; i++)
            {
                if (fragmentainers[i].SlotIndex >= slot) resumedPage = i;
            }
        }

        return new TextEditResult(true, null, pagesBefore, _pages.Count, first, regions, caret, block, resumedPage);
    }

    private static List<(string Text, double X, double Y, double Width)>[] Signature(IReadOnlyList<TextRun> runs, int pages)
    {
        var signature = new List<(string, double, double, double)>[pages];
        for (var i = 0; i < pages; i++) signature[i] = [];

        foreach (var run in runs)
        {
            if (run.PageIndex < pages)
                signature[run.PageIndex].Add((run.Text, Math.Round(run.Bounds.X, 2), Math.Round(run.Bounds.Y, 2), Math.Round(run.Bounds.Width, 2)));
        }

        return signature;
    }

    private static int? FirstDifferentPage(List<(string Text, double X, double Y, double Width)>[] before, List<(string Text, double X, double Y, double Width)>[] after)
    {
        for (var page = 0; page < Math.Max(before.Length, after.Length); page++)
        {
            if (page >= before.Length || page >= after.Length || !before[page].SequenceEqual(after[page]))
                return page;
        }

        return null;
    }

    /// <summary>
    /// Paints the part of one page that lies inside <paramref name="region"/>, onto a canvas that already holds the page as it was: the
    /// way to show an <see cref="UpdateResult"/> without painting the whole page again. The canvas is the same one <see cref="PaintPageAsync"/>
    /// takes; the region is cleared by the caller (paint the page's background first if it is not opaque).
    /// </summary>
    /// <param name="pageIndex">the zero-based index of the page</param>
    /// <param name="canvas">the canvas of that page</param>
    /// <param name="region">the area to paint, in points from the top-left of the sheet</param>
    /// <returns>a task that completes when the region is drawn</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page of the document</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public async Task PaintPageRegionAsync(int pageIndex, Canvas canvas, Rect region)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ThrowIfNotAPage(pageIndex);

        canvas.PushClip(new Rect(region.X * _unitsPerPoint, region.Y * _unitsPerPoint, region.Width * _unitsPerPoint, region.Height * _unitsPerPoint));
        try
        {
            await PaintPageAsync(pageIndex, canvas);
        }
        finally
        {
            canvas.PopClip();
        }
    }

    private void ApplyTextOptions(HtmlDocumentOptions options)
    {
        _incrementalUpdates = options.EnableIncrementalUpdates;
        _recordTextRuns = options.RecordTextRuns;
        _selectionStyling = options.EnableSelectionStyling;
        _container.SelectionBackground = options.SelectionBackground;
        _container.SelectionForeground = options.SelectionForeground;
    }

    private void ThrowIfNotAPage(int pageIndex)
    {
        var pages = Pages;
        if ((uint)pageIndex >= (uint)pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, $"The document has {pages.Count} page(s).");
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
