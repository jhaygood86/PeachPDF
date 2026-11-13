using PeachBrackets;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>Renders an HTML body onto a white bitmap at one CSS pixel per device pixel, the way a CSS-pixel host (the WinUI view) does.</summary>
internal static class PageRaster
{
    public static async Task<RasterCanvas> RenderAsync(string body, int width = 200, int height = 100, bool snap = true, bool loadResources = false)
    {
        var context = await TestDocument.NewContextAsync();
        context.PixelsPerPoint = 96.0 / 72.0;
        if (loadResources)
            context.ResourceLoader = uri => HostResourceLoader.LoadAsync(uri);

        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = width * 0.75,
            ViewportHeightPoints = height * 0.75,
            SnapBoxDecorationsToCssPixels = snap,
            SnapBorderWidthsToCssPixels = snap,
        };
        options.SetMargins(0);

        using var document = await HtmlDocument.LoadAsync(TestDocument.Wrap(body), context, options);
        using (var measuring = context.CreateCanvas(1, 1))
        {
            await document.LayoutAsync(measuring);
        }

        var canvas = context.CreateCanvas(width, height);
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, width, height);
        await document.PaintPageAsync(0, canvas);
        return canvas;
    }

    public static (byte R, byte G, byte B) Rgb(RasterCanvas canvas, int x, int y)
    {
        var (r, g, b, _) = TestDocument.Pixel(canvas, x, y);
        return (r, g, b);
    }

    /// <summary>The runs of the given color along row <paramref name="y"/>, as (first x, last x).</summary>
    public static List<(int First, int Last)> RunsOf(RasterCanvas canvas, int y, int width, (byte R, byte G, byte B) color)
    {
        var runs = new List<(int, int)>();
        int? start = null;
        for (var x = 0; x < width; x++)
        {
            if (Rgb(canvas, x, y) == color)
            {
                start ??= x;
            }
            else if (start is { } s)
            {
                runs.Add((s, x - 1));
                start = null;
            }
        }

        if (start is { } last) runs.Add((last, width - 1));
        return runs;
    }
}
