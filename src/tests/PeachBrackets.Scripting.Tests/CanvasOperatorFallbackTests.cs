using PeachBrackets.ScriptHosting;
using PeachDrawing.Svg;

namespace PeachBrackets.Scripting.Tests;

/// <summary>A document writer cannot composite with the Porter-Duff operators, so a canvas that uses one is placed as a bitmap; one that does not stays vector.</summary>
public class CanvasOperatorFallbackTests
{
    private static async Task<string> SvgOfAsync(string script)
    {
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 200,
            ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = new CollectingLogger() }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        };
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap($"<canvas id=c style=\"display:block\" width=60 height=40></canvas><script>var g = document.getElementById('c').getContext('2d'); {script}</script>"),
            context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);

        var sheet = document.Pages[0];
        var composer = new SvgComposer(new SvgRenderContext(), sheet.WidthPoints, sheet.HeightPoints);
        Assert.False(composer.Canvas.SupportsCompositeOperators);
        await document.PaintPageAsync(0, composer.Canvas);
        return composer.ToSvg();
    }

    [Fact]
    public async Task ACanvasThatUsesAnOperator_IsPlacedAsABitmapOnAWriterThatCannotComposite()
    {
        var svg = await SvgOfAsync("g.fillStyle = '#d22'; g.fillRect(0, 0, 30, 40); g.globalCompositeOperation = 'source-in'; g.fillStyle = '#22d'; g.fillRect(15, 0, 30, 40);");

        Assert.Contains("<image", svg);
        Assert.Contains("data:image/png", svg);
    }

    [Fact]
    public async Task ACanvasWithoutAnOperator_StaysVector()
    {
        var svg = await SvgOfAsync("g.fillStyle = '#d22'; g.fillRect(0, 0, 30, 40); g.globalCompositeOperation = 'multiply'; g.fillStyle = '#22d'; g.fillRect(15, 0, 30, 40);");

        Assert.DoesNotContain("<image", svg);
        Assert.Contains("<rect", svg);
    }
}
