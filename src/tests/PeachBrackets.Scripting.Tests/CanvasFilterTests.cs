namespace PeachBrackets.Scripting.Tests;

/// <summary>The <c>filter</c> of the 2D context and of <c>beginLayer()</c>, and <c>CanvasFilter</c>.</summary>
public class CanvasFilterTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task TheFilterProperty_KeepsATextThatIsAFilterList_AndIgnoresAnythingElse()
    {
        var log = await LogAsync("""
            var seen = [g.filter];
            g.filter = 'blur(  5px)'; seen.push(g.filter);
            g.save(); g.filter = 'none'; seen.push(g.filter); g.restore(); seen.push(g.filter);
            ['blur(10)', 'inherit', '', null, undefined, 'url(#nothing-here-is-not-valid'].forEach(function (v) { g.filter = v; seen.push(g.filter); });
            g.filter = new CanvasFilter({ name: 'gaussianBlur', stdDeviation: 2 }); seen.push(String(g.filter));
            g.filter = 'this is not a filter'; seen.push(String(g.filter));
            Log.info(seen.join('|'));
            """);

        Assert.Equal(["none|blur(  5px)|none|blur(  5px)|blur(  5px)|blur(  5px)|blur(  5px)|blur(  5px)|blur(  5px)|blur(  5px)|[object CanvasFilter]|[object CanvasFilter]"], log);
    }

    [Fact]
    public async Task AFilteredDrawing_IsBlurredAndAFilteredLayerIsBlurredAsAWhole()
    {
        var log = await LogAsync("""
            g.fillStyle = '#f00';
            g.filter = 'blur(4px)'; g.fillRect(10, 5, 10, 10);
            var edge = g.getImageData(9, 10, 1, 1).data[3], middle = g.getImageData(15, 10, 1, 1).data[3];
            g.reset();
            g.fillStyle = '#f00';
            g.beginLayer({ filter: 'blur(4px)' }); g.fillRect(10, 5, 10, 10); g.endLayer();
            var layerEdge = g.getImageData(9, 10, 1, 1).data[3];
            g.reset();
            g.fillStyle = '#f00'; g.filter = 'grayscale(1)'; g.fillRect(0, 0, 40, 20);
            var grey = Array.from(g.getImageData(1, 1, 1, 1).data);
            Log.info([edge > 0 && edge < 255, middle > 120 && middle < 200, layerEdge > 0 && layerEdge < 255, Math.abs(grey[0] - grey[1]) < 2 && grey[0] > 40 && grey[0] < 80].join(' '));
            """);

        Assert.Equal(["true true true true"], log);
    }

    [Fact]
    public async Task ACanvasFilterThatIsNotValid_IsATypeError_AndOneThatIsDoesWhatItSays()
    {
        var log = await LogAsync("""
            var errors = [function () { new CanvasFilter({ name: 'gaussianBlur' }); }, function () { new CanvasFilter({ name: 'colorMatrix', values: [1, 2] }); },
                function () { new CanvasFilter({ name: 'dropShadow', floodColor: 'nope' }); }, function () { new CanvasFilter({ name: 'convolveMatrix', kernelMatrix: [[1, 2], [3]] }); },
                function () { new CanvasFilter({ name: 'turbulence', stitchTiles: 'x' }); }].map(name);
            g.fillStyle = '#f00';
            g.filter = new CanvasFilter({ name: 'colorMatrix', type: 'saturate', values: 0 }); g.fillRect(0, 0, 40, 20);
            var p = Array.from(g.getImageData(1, 1, 1, 1).data);
            Log.info(errors.join(' ') + ' ' + (Math.abs(p[0] - p[1]) < 2 && p[0] > 40 && p[0] < 80));
            """);

        Assert.Equal(["TypeError TypeError TypeError TypeError TypeError true"], log);
    }
}
