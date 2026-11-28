namespace PeachBrackets.Scripting.Tests;

/// <summary><c>beginLayer()</c> and <c>endLayer()</c>: what a layer composites with, how it nests, and the calls that are not allowed while one is open.</summary>
public class CanvasLayerTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } function px(x, y) { return Array.from(g.getImageData(x, y, 1, 1).data).join(','); } " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task ALayer_IsCompositedWithTheAlphaTheCanvasHadWhenItOpened_NotTheOneInsideIt()
    {
        var log = await LogAsync("""
            g.globalAlpha = 0.5;
            g.beginLayer();
            g.fillStyle = '#f00'; g.fillRect(0, 0, 20, 20);
            // Inside the layer the alpha starts again at 1, so the two rectangles overlap into one opaque red, which then fades as a whole.
            var inside = g.globalAlpha;
            g.fillStyle = '#f00'; g.fillRect(10, 0, 20, 20);
            g.endLayer();
            Log.info([inside, g.globalAlpha, px(5, 5), px(15, 5), px(25, 5)].join(' '));
            """);

        Assert.Equal(["1 0.5 255,0,0,128 255,0,0,128 255,0,0,128"], log);
    }

    [Fact]
    public async Task EndLayer_WithoutALayer_OrWithASaveOpenInsideIt_IsAnInvalidStateError()
    {
        var log = await LogAsync("""
            var lone = name(function () { g.endLayer(); });
            g.save();
            var afterSave = name(function () { g.endLayer(); });
            g.beginLayer();
            var restoreIntoLayer = name(function () { g.restore(); });
            g.save();
            var openSave = name(function () { g.endLayer(); });
            g.restore();
            var closes = name(function () { g.endLayer(); });
            Log.info([lone, afterSave, restoreIntoLayer, openSave, closes].join(' '));
            """);

        Assert.Equal(["InvalidStateError InvalidStateError InvalidStateError InvalidStateError none"], log);
    }

    [Fact]
    public async Task WhileALayerIsOpen_TheCanvasCannotBeReadBack()
    {
        var log = await LogAsync("""
            var other = document.createElement('canvas'); other.width = 5; other.height = 5; var o = other.getContext('2d');
            g.beginLayer();
            var results = [name(function () { g.getImageData(0, 0, 1, 1); }), name(function () { c.toDataURL(); }), name(function () { g.createPattern(c, 'repeat'); }),
                name(function () { o.drawImage(c, 0, 0); }), name(function () { g.putImageData(g.createImageData(1, 1), 0, 0); })];
            g.endLayer();
            Log.info(results.join(' ') + ' ' + name(function () { g.getImageData(0, 0, 1, 1); }));
            """);

        Assert.Equal(["InvalidStateError InvalidStateError InvalidStateError InvalidStateError InvalidStateError none"], log);
    }

    [Fact]
    public async Task ResetWhileALayerIsOpen_DropsIt()
    {
        var log = await LogAsync("""
            g.beginLayer(); g.fillStyle = '#f00'; g.fillRect(0, 0, 40, 20);
            g.reset();
            Log.info(name(function () { g.endLayer(); }) + ' ' + px(5, 5));
            """);

        Assert.Equal(["InvalidStateError 0,0,0,0"], log);
    }

    [Fact]
    public async Task BeginLayerOptions_AreADictionary()
    {
        var log = await LogAsync("""
            g.beginLayer(); g.endLayer(); g.beginLayer(null); g.endLayer(); g.beginLayer({}); g.endLayer(); g.beginLayer([]); g.endLayer();
            Log.info([name(function () { g.beginLayer(''); }), name(function () { g.beginLayer(1); }), name(function () { g.beginLayer(true); })].join(' '));
            """);

        Assert.Equal(["TypeError TypeError TypeError"], log);
    }
}
