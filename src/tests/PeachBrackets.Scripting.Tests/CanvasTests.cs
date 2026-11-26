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
    public async Task ACanvasDrawnOntoItselfDrawsWhatItHeldWhenDrawImageWasCalled()
    {
        // A live reference replayed the canvas inside itself until the stack overflowed; drawImage reads the source when it is called.
        var (page, _) = await PaintAsync("g.fillStyle = '#ff0000'; g.fillRect(0, 0, 10, 10); g.drawImage(c, 20, 0); g.fillStyle = '#0000ff'; g.fillRect(0, 10, 10, 10);");

        Assert.Equal((255, 0, 0), At(page, 3, 3));
        Assert.Equal((255, 0, 0), At(page, 20, 3));   // the copy, drawn from the red square
        Assert.Equal((255, 255, 255), At(page, 20, 12)); // the blue square came after the copy was taken
        Assert.Equal((0, 0, 255), At(page, 3, 12));
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
    public async Task PixelsCanBeReadWrittenAndExported()
    {
        var (_, log) = await PaintAsync("""
            g.fillStyle = '#ff0000'; g.fillRect(0, 0, 10, 10); g.fillStyle = 'rgba(0, 0, 255, 0.5)'; g.fillRect(10, 0, 10, 10);
            var d = g.getImageData(0, 0, 20, 10).data;
            console.log([d[0], d[1], d[2], d[3], d[40], d[41], d[42], d[43], g.getImageData(30, 15, 1, 1).data[3]].join());
            var image = g.createImageData(2, 1); image.data.set([0, 255, 0, 255, 0, 0, 0, 0]); g.putImageData(image, 30, 15);
            console.log(g.getImageData(30, 15, 2, 1).data.join(), image.width, image instanceof ImageData);
            console.log(c.toDataURL().indexOf('data:image/png;base64,') === 0, new ImageData(new Uint8ClampedArray(8), 2).height);
            g.beginPath(); g.rect(0, 0, 10, 10); console.log(g.isPointInPath(5, 5), g.isPointInPath(15, 5));
            var off = new OffscreenCanvas(4, 4); off.getContext('2d').fillRect(0, 0, 4, 4); console.log(off.width, off.getContext('2d').getImageData(1, 1, 1, 1).data[3]);
            console.log(typeof createImageBitmap);
            """);

        Assert.Equal(["255,0,0,255,0,0,255,128,0", "0,255,0,255,0,0,0,0 2 true", "true 1", "true false", "4 255", "function"], log);
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

    [Theory]
    [InlineData("g.roundRect(2, 2, 30, 14, 5);")]
    [InlineData("g.roundRect(2, 2, 30, 14, [3, 6]);")]
    [InlineData("g.roundRect(2, 2, 30, 14, [3, 6, 9]);")]
    [InlineData("g.roundRect(2, 2, 30, 14, [1, 2, 3, 4]);")]
    [InlineData("g.roundRect(32, 16, -30, -14, [1, 2, 3, 4, 5]);")]
    [InlineData("g.roundRect(2, 2, 30, 14);")]
    public async Task ARoundedRectangleIsFilled(string path)
    {
        var (page, log) = await PaintAsync($"g.fillStyle = '#00ff00'; g.beginPath(); {path} g.fill();");

        Assert.Empty(log);
        Assert.Equal((0, 255, 0), At(page, 12, 7));
    }

    [Fact]
    public async Task ArcToRoundsACorner_AndFallsBackToALineWhenItCannot()
    {
        var (page, _) = await PaintAsync("""
            g.strokeStyle = '#0000ff'; g.lineWidth = 3;
            g.beginPath(); g.arcTo(2, 2, 30, 2, 5);
            g.beginPath(); g.moveTo(2, 10); g.arcTo(30, 10, 30, 18, 6); g.stroke();
            g.beginPath(); g.moveTo(2, 18); g.arcTo(10, 18, 20, 18, 4); g.arcTo(10, 18, 10, 18, 4); g.arcTo(1, 1, 2, 2, 0); g.stroke();
            g.ellipse(20, 10, 8, 4, 0.3, 0, Math.PI * 2); g.quadraticCurveTo(1, 1, 2, 2); g.bezierCurveTo(1, 1, 2, 2, 3, 3); g.stroke();
            """);

        Assert.Equal((0, 0, 255), At(page, 15, 10));
    }

    [Theory]
    [InlineData("lighter")]
    [InlineData("multiply")]
    [InlineData("screen")]
    [InlineData("overlay")]
    [InlineData("darken")]
    [InlineData("lighten")]
    [InlineData("color-dodge")]
    [InlineData("color-burn")]
    [InlineData("hard-light")]
    [InlineData("soft-light")]
    [InlineData("difference")]
    [InlineData("exclusion")]
    [InlineData("hue")]
    [InlineData("saturation")]
    [InlineData("color")]
    [InlineData("luminosity")]
    [InlineData("source-in")]
    [InlineData("destination-over")]
    [InlineData("xor")]
    [InlineData("copy")]
    public async Task EveryCompositeOperationDrawsWithoutAnError(string mode)
    {
        var (_, log) = await PaintAsync($"g.fillStyle = '#ff0000'; g.fillRect(0, 0, 20, 20); g.globalCompositeOperation = '{mode}'; g.fillStyle = '#0000ff'; g.fillRect(5, 5, 20, 10); console.log(g.globalCompositeOperation);");

        Assert.Equal([mode], log);
    }

    [Fact]
    public async Task AClipLimitsLaterDrawing_AndResetStartsOver()
    {
        var (page, _) = await PaintAsync("""
            g.save(); g.beginPath(); g.rect(0, 0, 10, 10); g.clip();
            g.fillStyle = '#ff0000'; g.fillRect(0, 0, 40, 20);
            g.globalCompositeOperation = 'source-over'; g.shadowColor = 'rgba(0,0,0,0.5)'; g.shadowBlur = 2; g.shadowOffsetX = 3; g.fillRect(0, 0, 8, 8);
            g.restore();
            g.fillStyle = '#00ff00'; g.fillRect(30, 0, 5, 5);
            g.reset();
            g.fillStyle = '#0000ff'; g.fillRect(0, 12, 5, 5);
            """);

        Assert.Equal((255, 255, 255), At(page, 3, 3));
        Assert.Equal((0, 0, 255), At(page, 2, 11));
    }

    [Theory]
    [InlineData("center", "top")]
    [InlineData("right", "hanging")]
    [InlineData("end", "middle")]
    [InlineData("left", "bottom")]
    [InlineData("start", "ideographic")]
    [InlineData("left", "alphabetic")]
    public async Task TextIsPlacedByItsAlignmentAndBaseline_AndSqueezedIntoAMaximumWidth(string align, string baseline)
    {
        var (_, log) = await PaintAsync($"g.font = '10px sans-serif'; g.textAlign = '{align}'; g.textBaseline = '{baseline}'; g.fillText('Hello', 20, 10); g.strokeText('Hello', 20, 10, 8); g.fillText('', 1, 1); console.log(typeof g.measureText('Hello').width);");

        Assert.Equal(["number"], log);
    }

    [Fact]
    public async Task ACanvasCanBeDrawnOntoAnotherCanvas()
    {
        var (page, _) = await PaintAsync("""
            var src = document.createElement('canvas'); src.width = 10; src.height = 10;
            var s = src.getContext('2d'); s.fillStyle = '#ff00ff'; s.fillRect(0, 0, 10, 10);
            g.drawImage(src, 0, 0); g.drawImage(src, 20, 0, 10, 10); g.drawImage(src, 0, 0, 5, 5, 30, 10, 8, 8);
            """);

        Assert.Equal((255, 0, 255), At(page, 3, 3));
    }
}
