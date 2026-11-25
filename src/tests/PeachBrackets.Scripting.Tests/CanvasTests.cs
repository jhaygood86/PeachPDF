using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Scripting.Tests;

/// <summary>The 2D canvas: what a script draws is painted into the page as the canvas element's content.</summary>
public class CanvasTests
{
    // A page with one 40x20 CSS pixel canvas at its top left (30x15 points), drawn on by the script, painted onto a raster canvas.
    private static async Task<(RasterCanvas Page, List<string> Log)> PaintAsync(string script, string attributes = "width=40 height=20")
    {
        var logger = new CollectingLogger();
        var host = new JintScriptHost(new JintScriptOptions { Logger = logger });
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 200, ScriptHost = host, MeasuringCanvasFactory = () => context.CreateCanvas(1, 1) };
        options.SetMargins(0);
        var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap($"<canvas id=c style=\"display:block\" {attributes}></canvas><script>var c = document.getElementById('c'); var g = c.getContext('2d'); {script}</script>"),
            context,
            options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);

        var sheet = document.Pages[0];
        var page = context.CreateCanvas((int)Math.Ceiling(sheet.WidthPoints), (int)Math.Ceiling(sheet.HeightPoints));
        page.DrawRectangle(page.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, sheet.WidthPoints, sheet.HeightPoints);
        await document.PaintPageAsync(0, page);
        return (page, logger.Texts);
    }

    // The color of the pixel at x, y of the page, with the alpha dropped.
    private static (int R, int G, int B) At(RasterCanvas page, int x, int y)
    {
        var buffer = page.ToPixelBuffer();
        var i = (y * buffer.Width + x) * 4;
        var data = buffer.PremultipliedRgba.Span;
        return (data[i], data[i + 1], data[i + 2]);
    }

    [Fact]
    public async Task ARectangleIsFilledInTheCanvasElement()
    {
        var (page, _) = await PaintAsync("g.fillStyle = '#ff0000'; g.fillRect(0, 0, 20, 10);");

        Assert.Equal((255, 0, 0), At(page, 5, 3));
        Assert.Equal((255, 255, 255), At(page, 25, 3));
        Assert.Equal((255, 255, 255), At(page, 5, 12));
    }

    [Fact]
    public async Task PathsFillAndStrokeUnderATransformAndAClip()
    {
        var (page, _) = await PaintAsync("""
            g.save(); g.beginPath(); g.rect(0, 0, 40, 10); g.clip();
            g.fillStyle = 'rgb(0, 0, 255)'; g.scale(2, 2); g.beginPath(); g.arc(10, 5, 4, 0, Math.PI * 2); g.fill();
            g.restore();
            g.strokeStyle = '#00ff00'; g.lineWidth = 4; g.beginPath(); g.moveTo(0, 18); g.lineTo(40, 18); g.stroke();
            """);

        Assert.Equal((0, 0, 255), At(page, 15, 4));
        Assert.Equal((255, 255, 255), At(page, 15, 9));
        Assert.Equal((0, 255, 0), At(page, 20, 13));
    }

    [Fact]
    public async Task AlphaGradientsAndSavedStateApply()
    {
        var (page, log) = await PaintAsync("""
            g.globalAlpha = 0.5; g.fillStyle = '#ff0000'; g.fillRect(0, 0, 10, 20); g.globalAlpha = 1;
            var gradient = g.createLinearGradient(0, 0, 40, 0); gradient.addColorStop(0, '#000000'); gradient.addColorStop(1, '#ffffff');
            g.fillStyle = gradient; g.fillRect(20, 0, 20, 20);
            console.log(g.fillStyle === gradient, c.getContext('2d') === g, g.canvas === c);
            g.save(); g.fillStyle = '#00f'; g.restore(); console.log(g.fillStyle === gradient);
            g.fillStyle = 'red'; console.log(g.fillStyle); g.fillStyle = 'rgba(0, 0, 255, 0.5)'; console.log(g.fillStyle); g.fillStyle = 'not a color'; console.log(g.fillStyle);
            """);

        var half = At(page, 3, 5);
        Assert.InRange(half.G, 100, 160);
        Assert.True(half.R > 240);
        var (left, right) = (At(page, 17, 5), At(page, 28, 5));
        Assert.True(left.R < right.R, $"{left} {right}");
        Assert.Equal(["true true true", "true", "#ff0000", "rgba(0, 0, 255, 0.5)", "rgba(0, 0, 255, 0.5)"], log);
    }

    [Fact]
    public async Task TextIsDrawnAndMeasured()
    {
        var (page, log) = await PaintAsync($$"""
            g.font = '14px {{TestDocument.Family}}'; g.fillStyle = '#000'; g.fillText('Hi', 2, 14);
            console.log(g.measureText('Hi').width > 0, g.measureText('Hi').width < g.measureText('Hi Hi').width);
            """);

        var dark = Enumerable.Range(0, 30).SelectMany(x => Enumerable.Range(0, 15).Select(y => At(page, x, y))).Count(p => p.R < 128);
        Assert.True(dark > 5, $"dark pixels: {dark}");
        Assert.Equal(["true true"], log);
    }

    [Fact]
    public async Task TheCanvasHasItsBitmapSize_AndClearingStartsOver()
    {
        var (page, _) = await PaintAsync("""
            g.fillStyle = '#ff0000'; g.fillRect(0, 0, 40, 20); g.clearRect(0, 0, 40, 20); g.fillStyle = '#0000ff'; g.fillRect(20, 0, 20, 20);
            """);

        Assert.Equal((255, 255, 255), At(page, 5, 5));
        Assert.Equal((0, 0, 255), At(page, 25, 5));

        var (empty, log) = await PaintAsync("console.log(c.width + 'x' + c.height); c.width = 10; console.log(c.width + 'x' + c.height + ' ' + c.getAttribute('width'));", "");
        Assert.Equal(["300x150", "10x150 10"], log);
        Assert.Equal((255, 255, 255), At(empty, 5, 5));
    }
}
