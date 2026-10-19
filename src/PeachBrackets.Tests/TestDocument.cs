using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>Loads documents against PeachDrawing's bitmap render context and reads pixels back, the way a standalone consumer would.</summary>
internal static class TestDocument
{
    public const string Family = "TestSans";

    public static async Task<RasterRenderContext> NewContextAsync()
    {
        var context = new RasterRenderContext();
        await using var font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf"));
        await context.AddFont(font, Family);
        return context;
    }

    public static string Wrap(string body, string head = "") =>
        $"<!DOCTYPE html><html><head><style>* {{ font-family: {Family}; }} html, body {{ margin: 0; }}</style>{head}</head><body>{body}</body></html>";

    public static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(
        string html, HtmlDocumentOptions? options = null, bool layout = true)
    {
        var context = await NewContextAsync();
        var document = await HtmlDocument.LoadAsync(html, context, options);

        if (layout)
        {
            using var measuring = context.CreateCanvas(1, 1);
            await document.LayoutAsync(measuring);
        }

        return (context, document);
    }

    /// <summary>Paints page <paramref name="pageIndex"/> onto a white bitmap the size of its sheet.</summary>
    public static async Task<RasterCanvas> PaintAsync(RasterRenderContext context, HtmlDocument document, int pageIndex = 0)
    {
        var page = document.Pages[pageIndex];
        var canvas = context.CreateCanvas((int)Math.Ceiling(page.WidthPoints), (int)Math.Ceiling(page.HeightPoints));
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, page.WidthPoints, page.HeightPoints);
        await document.PaintPageAsync(pageIndex, canvas);
        return canvas;
    }

    public static (byte R, byte G, byte B, byte A) Pixel(RasterCanvas canvas, int x, int y)
    {
        var buffer = canvas.ToPixelBuffer();
        var span = buffer.PremultipliedRgba.Span.Slice(((y * buffer.Width) + x) * 4, 4);
        return (span[0], span[1], span[2], span[3]);
    }

    public static bool HasInk(RasterCanvas canvas, int x, int y, int width, int height)
    {
        for (var j = y; j < y + height; j++)
        {
            for (var i = x; i < x + width; i++)
            {
                var (r, g, b, _) = Pixel(canvas, i, j);
                if (r < 200 || g < 200 || b < 200)
                    return true;
            }
        }

        return false;
    }
}
