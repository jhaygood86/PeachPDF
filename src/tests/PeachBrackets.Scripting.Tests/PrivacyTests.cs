using PeachBrackets.Scripting.Internal.Canvas;
using PeachDrawing;
using PeachDrawing.Core;
using Xunit;

namespace PeachBrackets.Scripting.Tests;

/// <summary>What a picture of an element drawn for a script may show: images from the document's own origin, not from another.</summary>
public class PrivacyTests
{
    private static async Task<(string Same, string Cross)> DrawAsync()
    {
        var pixels = new byte[8 * 8 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (255, 0, 0, 255);

        var png = PngWriter.Encode(8, 8, pixels);
        var context = await TestDocument.NewContextAsync();
        context.DocumentAddress = new Uri("https://example.test/page.html");
        context.ResourceLoader = uri => uri.Host is "example.test" or "other.test" && uri.AbsolutePath.EndsWith(".png", StringComparison.Ordinal)
            ? new ValueTask<RNetworkResponse?>(new RNetworkResponse(new MemoryStream(png), new Dictionary<string, string[]> { ["Content-Type"] = ["image/png"] }))
            : default;

        var log = new CollectingLogger();
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 300,
            ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = log }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
            CanvasSurfaces = true,
        };
        options.SetMargins(0);

        var html = TestDocument.Wrap("""
            <canvas id="c" width="40" height="20" content="drawable"><div id="child" drawable style="width:40px;height:20px;background:rgb(0,0,255)">
              <img src="https://example.test/same.png" width="10" height="10" style="position:absolute;left:0;top:0">
              <img src="https://other.test/cross.png" width="10" height="10" style="position:absolute;left:20px;top:0">
            </div></canvas>
            <script>
              var c = document.getElementById('c');
              function go() {
                c.addEventListener('paint', function () {
                  var g = c.getContext('2d');
                  g.drawElementImage(document.getElementById('child'), 0, 0);
                  var p = function (x, y) { return Array.from(g.getImageData(x, y, 1, 1).data).join(','); };
                  Log.info(p(5, 5) + ' ' + p(25, 5));
                }, { once: true });
                c.requestPaint();
              }
              window.addEventListener('load', function () { setTimeout(go, 50); });
            </script>
            """);
        using var document = await HtmlDocument.LoadAsync(html, context, options);
        Assert.True(log.Errors.Count == 0, string.Join(" / ", log.Errors));
        var line = Assert.Single(log.Texts).Split(' ');
        return (line[0], line[1]);
    }

    [Fact]
    public async Task APictureOfAnElement_ShowsAnImageOfItsOwnOrigin_AndNotOneOfAnother()
    {
        var (same, cross) = await DrawAsync();

        Assert.Equal("255,0,0,255", same);
        Assert.Equal("0,0,255,255", cross);
    }
}
