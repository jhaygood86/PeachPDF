using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.Svg;
using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using PeachPDF;
using PeachPDF.Network;

/// <summary>
/// The harness's SVG mode (<c>--svg</c>): renders a showcase's HTML a second time, through <c>PeachBrackets.HtmlDocument</c> onto the
/// <see cref="SvgCanvas"/> of <c>PeachDrawing.Svg</c> instead of onto a PDF page, and writes one SVG file per page. It is the same engine,
/// layout and paint code that produce the PDF, so what it writes is a direct look at what the SVG backend does with real documents - and
/// it does not touch <c>PeachPDF</c>'s public API: the options are mapped from <see cref="PdfGenerateConfig"/> here.
/// </summary>
internal static class SvgShowcaseRenderer
{
    /// <summary>Renders <paramref name="html"/> and returns the text of each page's SVG document.</summary>
    public static async Task<IReadOnlyList<string>> RenderPagesAsync(string html, PdfGenerateConfig config, SvgTextMode textMode)
    {
        var context = new ShowcaseSvgRenderContext
        {
            NetworkLoader = config.NetworkLoader ?? new DataUriNetworkLoader(),
            AllowLocalFileAccess = config.AllowLocalFileAccess,
            PixelsPerPoint = config.PixelsPerInch / 72d,
            RasterizationDpi = config.RasterizationDpi,
            MaxRasterPixels = config.MaxRasterPixels,
            TextHinting = config.TextHinting,
            TextStemDarkening = config.TextStemDarkening,
            RasterAntiAliasing = config.RasterAntiAliasing,
        };

        // An SVG is one continuous sheet, not a stack of paper pages: the sheet is made far taller than any document, then cut to the content.
        // A document's own @page size rule would paginate it again, so a rule stating the endless size is appended after the document's.
        var sheet = SheetSize(config) with { Height = EndlessSheetPoints };
        var options = new HtmlDocumentOptions
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
        };

        // A zero-height marker after everything (below floats too) says where the content ends, and no forced page break survives.
        var marker = $"<div id=\"{EndMarker}\" style=\"clear:both;height:0;margin:0;padding:0;border:0\"></div><style>*{{break-before:auto!important;break-after:auto!important;page-break-before:auto!important;page-break-after:auto!important;page:auto!important}}@page {{ size: {sheet.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)}pt {EndlessSheetPoints}pt }}</style>";
        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        html = bodyEnd >= 0 ? html.Insert(bodyEnd, marker) : html + marker;
        var composerOptions = new SvgComposerOptions { TextMode = textMode, Background = PaintColor.White };
        var document = await HtmlDocument.LoadAsync(html, context, options);
        try
        {
            // The same shrink-to-fit / scale-to-page measurement pass the PDF generator makes: a document wider than its page is
            // laid out again at the scale that fits.
            var laidOut = false;
            SvgCanvas Measure(HtmlDocument d)
            {
                return new SvgComposer(context, Math.Max(d.ContentArea.Width, 1), Math.Max(d.ContentArea.Height, 1), composerOptions).Canvas;
            }

            if (config.ScaleToPageSize || config.ShrinkToFit)
            {
                var measure = Measure(document);
                await document.LayoutAsync(measure);

                var contentArea = document.ContentArea;
                var basePixelsPerPoint = config.PixelsPerInch / 72d;
                var minPixelsPerPoint = config.MinContentWidth > 0 ? config.MinContentWidth / contentArea.Width : basePixelsPerPoint;
                var candidate = Math.Max(minPixelsPerPoint * (document.ContentExtent.Width / contentArea.Width), minPixelsPerPoint);
                var effective = (config.ShrinkToFit && candidate > 1) || config.ScaleToPageSize ? candidate : context.PixelsPerPoint;

                if (effective != context.PixelsPerPoint)
                {
                    context.ClearFontCache();
                    context.PixelsPerPoint = effective;
                    document.Dispose();
                    document = await HtmlDocument.LoadAsync(html, context, options);
                }
                else
                {
                    laidOut = true;
                }
            }

            if (!laidOut)
                await document.LayoutAsync(Measure(document));

            // The sheet ends where the content does (and keeps the bottom margin); the marker's bounds are in points.
            var composed = new List<SvgComposer>();
            foreach (var htmlPage in document.Pages)
            {
                var height = htmlPage.HeightPoints;
                if (document.GetElementBounds(EndMarker) is { } end)
                    height = Math.Min(height, end.Bottom + config.MarginBottom);

                var page = new SvgComposer(context, htmlPage.WidthPoints, Math.Max(height, 1), composerOptions);
                await document.PaintPageAsync(htmlPage.Index, page.Canvas);
                composed.Add(page);
            }

            // The links go on once every page is painted (that is when their places are known), and are written with the page.
            for (var i = 0; i < composed.Count; i++)
                document.PaintLinks(i, composed[i].Canvas);

            return composed.ConvertAll(page => page.ToSvg());
        }
        finally
        {
            document.Dispose();
        }
    }

    private const string EndMarker = "peach-svg-end-of-content";

    private const double EndlessSheetPoints = 100_000;

    private static Size SheetSize(PdfGenerateConfig config)
    {
        var size = config.PageSize switch
        {
            PageSize.A3 => new Size(842, 1191),
            PageSize.A4 => new Size(595, 842),
            PageSize.A5 => new Size(420, 595),
            PageSize.A6 => new Size(298, 420),
            PageSize.Letter => new Size(612, 792),
            PageSize.Legal => new Size(612, 1008),
            PageSize.Undefined => new Size(config.ManualPageWidth, config.ManualPageHeight),
            _ => new Size(595, 842),
        };

        return config.PageOrientation == PageOrientation.Landscape ? new Size(size.Height, size.Width) : size;
    }
}

/// <summary>
/// What <c>PeachPDF</c>'s own render context adds to the PDF one for HTML - the CSS generic font families, the document's resource loading,
/// SVG glyphs - on top of the SVG render context, so the SVG backend renders HTML the way the PDF one does.
/// </summary>
internal sealed class ShowcaseSvgRenderContext : SvgRenderContext
{
    public ShowcaseSvgRenderContext()
    {
        AddFontFamilyMapping("Helvetica", "Arial");
        if (!IsFontExists("Arial"))
            AddFontFamilyMapping("Arial", FontDefaults.DefaultFamily);

        foreach (var (keyword, generic) in FontDefaults.GenericFamilies)
            AddFontFamilyMapping(keyword, FontSet.ResolveGeneric(generic, IsFontExists) ?? FontDefaults.DefaultFamily);

        AddFontFamilyMapping("system-ui", FontSet.ResolveGeneric(GenericFamily.SystemUi, IsFontExists) ?? FontDefaults.DefaultFamily);
    }

    public RNetworkLoader NetworkLoader { get; set; } = new DataUriNetworkLoader();

    public bool AllowLocalFileAccess { get; set; } = true;

    private FileUriNetworkLoader? _fileLoader;

    private FileUriNetworkLoader FileLoader => _fileLoader ??= new FileUriNetworkLoader();

    public override RUri? BaseUri => NetworkLoader.BaseUri ?? (AllowLocalFileAccess ? FileLoader.BaseUri : null);

    public override async Task<RNetworkResponse?> GetResourceStream(RUri uri)
    {
        if (!uri.IsAbsoluteUri)
            return null;

        if (uri.Scheme is "data")
            return await (NetworkLoader as DataUriNetworkLoader ?? new DataUriNetworkLoader()).GetResourceStream(uri);

        if (uri.Scheme is "file")
        {
            if (!AllowLocalFileAccess)
                return null;

            return await (NetworkLoader as FileUriNetworkLoader ?? FileLoader).GetResourceStream(uri);
        }

        return await NetworkLoader.GetResourceStream(uri);
    }

    public override ISvgGlyphPainter CreateSvgGlyphPainter(Canvas host) => new PeachBrackets.Svg.SvgGlyphPainter(host, this);

    protected override IReadOnlyList<AxisSetting>? ResolveVariationAxes(string? variations, double sizeInPixels) =>
        FontDefaults.ToAxisSettings(variations, sizeInPixels);

    protected override void OnFontFamilyAdded(string familyName)
    {
        if (!IsFontExists(FontDefaults.DefaultFamily))
            AddFontFamilyMapping(FontDefaults.DefaultFamily, familyName);
    }
}
