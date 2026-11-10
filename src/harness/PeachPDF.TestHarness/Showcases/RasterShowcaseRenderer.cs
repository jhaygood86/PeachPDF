using PeachBrackets;
using PeachDrawing;
using PeachPDF;
using PeachPDF.Network;

/// <summary>
/// The harness's raster benchmark (<c>--raster-benchmark</c>): renders a showcase's HTML through <c>PeachBrackets.HtmlDocument</c> onto the
/// CPU <see cref="RasterCanvas"/> of <c>PeachDrawing</c>, one canvas per page, and throws the pixels away. The PDF path reaches the software
/// rasterizer only for raster fallbacks (filters, shadows, flattened transparency), so the scanline, blend, gradient, bitmap and glyph-fill
/// kernels are measured here, across the whole showcase corpus, with the same layout and paint code that produce the PDF.
/// </summary>
internal static class RasterShowcaseRenderer
{
    /// <summary>Lays out and paints every page of <paramref name="html"/> onto raster canvases; returns the number of pages painted.</summary>
    public static async Task<int> RenderPagesAsync(string html, PdfGenerateConfig config)
    {
        var loader = config.NetworkLoader ?? new DataUriNetworkLoader();
        var context = new RasterRenderContext
        {
            ResourceLoader = async uri =>
            {
                if (!uri.IsAbsoluteUri)
                    return null;
                if (uri.Scheme is "file" && !config.AllowLocalFileAccess)
                    return null;
                if (uri.Scheme is "file")
                    return await new FileUriNetworkLoader().GetResourceStream(uri);
                return await loader.GetResourceStream(uri);
            },
        };

        var (width, height) = PageSizePoints(config);
        var options = new HtmlDocumentOptions
        {
            PageWidthPoints = width,
            PageHeightPoints = height,
            MarginTopPoints = config.MarginTop,
            MarginRightPoints = config.MarginRight,
            MarginBottomPoints = config.MarginBottom,
            MarginLeftPoints = config.MarginLeft,
            ColorScheme = config.PreferredColorScheme == PdfColorScheme.Dark ? ColorSchemePreference.Dark : ColorSchemePreference.Light,
            IgnoreAuthorStyleSheets = config.IgnoreAuthorStyleSheets,
            SnapBoxDecorationsToCssPixels = config.SnapBoxDecorationsToCssPixels,
            SnapBorderWidthsToCssPixels = config.SnapBorderWidthsToCssPixels,
            AnimationProgress = config.AnimationProgress,
            DefaultLanguage = string.IsNullOrEmpty(config.DefaultLanguage) ? null : config.DefaultLanguage,
        };

        using var document = await HtmlDocument.LoadAsync(html, context, options);
        using (var measuring = context.CreateCanvas(1, 1))
        {
            await document.LayoutAsync(measuring);
        }

        var pages = 0;
        foreach (var page in document.Pages)
        {
            using var canvas = context.CreateCanvas(Math.Max((int)page.WidthPoints, 1), Math.Max((int)page.HeightPoints, 1));
            await document.PaintPageAsync(page.Index, canvas);
            pages++;
        }

        return pages;
    }

    private static (double Width, double Height) PageSizePoints(PdfGenerateConfig config)
    {
        var (w, h) = config.PageSize switch
        {
            PageSize.A3 => (842d, 1191d),
            PageSize.A4 => (595d, 842d),
            PageSize.A5 => (420d, 595d),
            PageSize.A6 => (298d, 420d),
            PageSize.Letter => (612d, 792d),
            PageSize.Legal => (612d, 1008d),
            PageSize.Undefined => (config.ManualPageWidth, config.ManualPageHeight),
            _ => (595d, 842d),
        };

        return config.PageOrientation == PageOrientation.Landscape ? (h, w) : (w, h);
    }
}
