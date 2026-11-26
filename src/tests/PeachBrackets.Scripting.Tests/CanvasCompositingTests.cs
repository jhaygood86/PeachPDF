using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Tests;

/// <summary>Canvas features that need pixels: Porter-Duff compositing, partial clears, pattern fills, color spaces, and the geometry interfaces.</summary>
public class CanvasCompositingTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var logger = new CollectingLogger();
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 400,
            ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = logger }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        };
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap("<canvas id=c width=20 height=20></canvas><script>var c = document.getElementById('c'); var g = c.getContext('2d'); function px(x, y, ctx) { return Array.from((ctx || g).getImageData(x, y, 1, 1).data).join(','); }" + script + "</script>"),
            context, options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return logger.Texts;
    }

    [Fact]
    public async Task PorterDuffOperations_CombineTheShapeWithWhatWasThere()
    {
        var log = await LogAsync("""
            function scene(op) {
                g.clearRect(0, 0, 20, 20);
                g.globalCompositeOperation = 'source-over';
                g.fillStyle = '#f00'; g.fillRect(0, 0, 10, 20);
                g.globalCompositeOperation = op;
                g.fillStyle = '#00f'; g.fillRect(5, 0, 10, 20);
                return [px(2, 5), px(7, 5), px(12, 5), px(18, 5)].join(' ');
            }
            Log.info('source-in ' + scene('source-in'));
            Log.info('destination-over ' + scene('destination-over'));
            Log.info('destination-out ' + scene('destination-out'));
            Log.info('xor ' + scene('xor'));
            Log.info('copy ' + scene('copy'));
            """);

        Assert.Equal(
        [
            "source-in 0,0,0,0 0,0,255,255 0,0,0,0 0,0,0,0",
            "destination-over 255,0,0,255 255,0,0,255 0,0,255,255 0,0,0,0",
            "destination-out 255,0,0,255 0,0,0,0 0,0,0,0 0,0,0,0",
            "xor 255,0,0,255 0,0,0,0 0,0,255,255 0,0,0,0",
            "copy 0,0,0,0 0,0,255,255 0,0,255,255 0,0,0,0",
        ],
            log);
    }

    [Fact]
    public async Task ClearRect_ErasesPartOfTheCanvas_AndPatternsFill()
    {
        var log = await LogAsync("""
            g.fillStyle = '#0f0'; g.fillRect(0, 0, 20, 20);
            g.clearRect(5, 5, 5, 5);
            Log.info([px(2, 2), px(7, 7), px(12, 12)].join(' '));
            var tile = document.createElement('canvas'); tile.width = 4; tile.height = 4;
            var t = tile.getContext('2d'); t.fillStyle = '#f00'; t.fillRect(0, 0, 2, 4); t.fillStyle = '#00f'; t.fillRect(2, 0, 2, 4);
            g.clearRect(0, 0, 20, 20);
            g.fillStyle = g.createPattern(tile, 'repeat');
            g.fillRect(0, 0, 20, 8);
            Log.info([px(0, 0), px(3, 0), px(4, 5), px(19, 3), px(5, 10)].join(' '));
            """);

        Assert.Equal(["0,255,0,255 0,0,0,0 0,255,0,255", "255,0,0,255 0,0,255,255 255,0,0,255 0,0,255,255 0,0,0,0"], log);
    }

    [Fact]
    public async Task ColorSpaces_ConvertAtTheImageDataBoundary()
    {
        var log = await LogAsync("""
            var p3 = document.createElement('canvas').getContext('2d', { colorSpace: 'display-p3' });
            Log.info(JSON.stringify(p3.getContextAttributes()));
            g.fillStyle = '#ff0000'; g.fillRect(0, 0, 20, 20);
            var asP3 = g.getImageData(0, 0, 1, 1, { colorSpace: 'display-p3' });
            Log.info(asP3.colorSpace + ' ' + Array.from(asP3.data).join(','));
            var linear = g.getImageData(0, 0, 1, 1, { colorSpace: 'srgb-linear' });
            Log.info(Array.from(linear.data).join(','));
            try { document.createElement('canvas').getContext('2d', { colorSpace: 'rec2020' }); } catch (e) { Log.info(e instanceof TypeError); }
            g.putImageData(new ImageData(new Uint8ClampedArray([234, 51, 35, 255]), 1, 1, { colorSpace: 'display-p3' }), 5, 5);
            Log.info(px(5, 5));
            """);

        Assert.Equal(
        [
            "{\"alpha\":true,\"desynchronized\":false,\"willReadFrequently\":false,\"colorSpace\":\"display-p3\",\"colorType\":\"unorm8\",\"toneMapping\":{\"mode\":\"standard\"}}",
            "display-p3 234,51,35,255",
            "255,0,0,255",
            "true",
            "255,0,0,255",
        ],
            log);
    }

    [Fact]
    public async Task DOMMatrix_DOMPoint_AndDOMRect_Compute()
    {
        var log = await LogAsync("""
            var m = new DOMMatrix().translate(10, 20).scale(2);
            Log.info(m.toString() + ' ' + m.is2D + ' ' + m.a + ',' + m.e + ',' + m.f);
            var p = m.transformPoint({ x: 1, y: 1 });
            Log.info(p.x + ',' + p.y + ' ' + (p instanceof DOMPoint));
            Log.info(m.inverse().multiply(m).isIdentity + ' ' + new DOMMatrix('translate(5px, 6px) scale(2)').toString());
            g.setTransform(1, 0, 0, 1, 3, 4); g.scale(2, 2);
            var t = g.getTransform();
            Log.info((t instanceof DOMMatrix) + ' ' + t.a + ',' + t.e);
            g.setTransform(new DOMMatrix([1, 0, 0, 1, 7, 8])); Log.info(g.getTransform().e);
            var r = new DOMRect(5, 6, -3, 4);
            Log.info([r.left, r.right, r.top, r.bottom].join());
            Log.info(document.body.getBoundingClientRect() instanceof DOMRect);
            """);

        Assert.Equal(["matrix(2, 0, 0, 2, 10, 20) true 2,10,20", "12,22 true", "true matrix(2, 0, 0, 2, 5, 6)", "true 2,3", "7", "2,5,6,10", "true"], log);
    }
}
