namespace PeachBrackets.Scripting.Tests;

/// <summary>What a canvas accepts as a color, what <c>currentcolor</c> is, how a color reads back, and the conic gradient's quadrants.</summary>
public class CanvasColorTests
{
    // Sets fillStyle to a known color, then to the text, and reports what it reads back as: the known color means the text was not a color.
    private static async Task<List<string>> ReadBackAsync(params string[] inputs)
    {
        var script = "var c = document.getElementById('c'); var g = c.getContext('2d'); var inputs = " + System.Text.Json.JsonSerializer.Serialize(inputs) +
            "; inputs.forEach(function (s) { g.fillStyle = '#123456'; g.fillStyle = s; Log.info(g.fillStyle); });";
        var (_, log) = await CanvasParityTests.PaintAsync(script, "width=10 height=10");
        return log;
    }

    private const string Unchanged = "#123456";

    [Theory]
    [InlineData("#0f0f", "#00ff00")]
    [InlineData("#00ff0080", "rgba(0, 255, 0, 0.5)")]
    [InlineData("#0f08", "rgba(0, 255, 0, 0.533)")]
    [InlineData("rgba(  0  ,  255  ,  0  ,  1  )", "#00ff00")]
    [InlineData("rgba(  0  ,  255  ,  0  ,  .499  )", "rgba(0, 255, 0, 0.498)")]
    [InlineData("rgb(0 255 0 / 50%)", "rgba(0, 255, 0, 0.5)")]
    [InlineData("rgba(0, 255, 0, -2)", "rgba(0, 255, 0, 0)")]
    [InlineData("rgba(0, 255, 0, 2)", "#00ff00")]
    [InlineData("hsl(360120, 100%, 50%)", "#00ff00")]
    [InlineData("hsl(-360240, 100%, 50%)", "#00ff00")]
    [InlineData("hsl(120, -200%, 49.9%)", "#7f7f7f")]
    [InlineData("hsla(120, 100%, 0%, -2)", "rgba(0, 0, 0, 0)")]
    [InlineData("hsl(0.5turn 100% 50%)", "#00ffff")]
    public async Task AValidColor_IsReadAsTheGrammarSaysAndSerializedBack(string input, string expected)
    {
        Assert.Equal([expected], await ReadBackAsync(input));
    }

    [Theory]
    [InlineData("rgb(255, 0, 0 / 1)")]
    [InlineData("rgba(255, 0, 0 / 1)")]
    [InlineData("rgb(0 0 0 /)")]
    [InlineData("rgb(255.0, 0, 0,)")]
    [InlineData("rgba(100%, 0, 0, 1)")]
    [InlineData("rgb(50%, 0, 0)")]
    [InlineData("hsl(0, 100%, 50%,)")]
    [InlineData("hsl(0, 100%, 50% / 1)")]
    [InlineData("hsla(0, 100%, 50% / 1)")]
    [InlineData("rgb(0 0 0 0.5)")]
    [InlineData("rgb(0, 0, 0, 0.5, 1)")]
    [InlineData("bogus")]
    [InlineData("")]
    public async Task ATextThatIsNotAColor_LeavesTheStyleAlone(string input)
    {
        Assert.Equal([Unchanged], await ReadBackAsync(input));
    }

    [Fact]
    public async Task Alpha_IsWrittenAsTheShortestNumberThatReadsBackAsTheSameByte()
    {
        var log = await ReadBackAsync("rgba(0,0,0,0.5)", "rgba(0,0,0,0.502)", "rgba(0,0,0,0.25)", "rgba(0,0,0,0.1)", "rgba(0,0,0,0.499)", "rgba(0,0,0,0)", "rgba(255,0,255,0)");

        Assert.Equal(
        [
            "rgba(0, 0, 0, 0.5)", "rgba(0, 0, 0, 0.5)", "rgba(0, 0, 0, 0.25)", "rgba(0, 0, 0, 0.1)", "rgba(0, 0, 0, 0.498)", "rgba(0, 0, 0, 0)", "rgba(255, 0, 255, 0)",
        ],
            log);
    }

    [Fact]
    public async Task CurrentColor_IsTheComputedColorOfTheCanvasElement_WhenTheStyleIsSet()
    {
        var (page, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            c.setAttribute('style', 'display:block; color: #2a2');
            g.fillStyle = '#d22'; g.fillStyle = 'currentColor';
            c.setAttribute('style', 'display:block; color: #22d');
            g.fillRect(0, 0, 10, 10);
            g.fillStyle = 'currentcolor';
            g.fillRect(10, 0, 10, 10);
            Log.info(Array.from(g.getImageData(2, 2, 1, 1).data).join(',') + ' ' + Array.from(g.getImageData(12, 2, 1, 1).data).join(','));
            """, "width=20 height=10");

        // Resolved when set, not when painted: the first rectangle is green though the color changed before it was drawn; the second was set after the change.
        Assert.Equal(["34,170,34,255 34,34,221,255"], log);
    }

    [Fact]
    public async Task CurrentColor_OfACanvasOutsideTheDocument_IsOpaqueBlack()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            document.body.setAttribute('style', 'color: #f00');
            var s = document.createElement('canvas'), q = s.getContext('2d');
            q.fillStyle = '#f00'; q.fillStyle = 'currentColor';
            q.fillRect(0, 0, 4, 4);
            Log.info(Array.from(q.getImageData(1, 1, 1, 1).data).join(','));
            """, "width=10 height=10");

        Assert.Equal(["0,0,0,255"], log);
    }

    [Fact]
    public async Task AConicGradient_StartsAtItsAngleMeasuredFromThreeOClock()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            function px(x, y) { return Array.from(g.getImageData(x, y, 1, 1).data).join(','); }
            var k = g.createConicGradient(3 * Math.PI / 2, 50, 25);
            k.addColorStop(0, '#f00'); k.addColorStop(0.25, '#0f0'); k.addColorStop(0.5, '#0f0'); k.addColorStop(0.75, '#f00');
            g.fillStyle = k; g.fillRect(0, 0, 100, 50);
            Log.info(px(25, 15) + ' | ' + px(75, 40));
            """, "width=100 height=50");

        // It starts pointing up, red, goes green at the right and the bottom, and is red again at the left.
        Assert.Equal(["255,0,0,255 | 0,255,0,255"], log);
    }

    [Fact]
    public async Task AColorStopOffset_ThatIsNotFinite_IsATypeError_AndOneOutOfRangeIsAnIndexSizeError()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var g = document.getElementById('c').getContext('2d');
            var k = g.createLinearGradient(0, 0, 10, 0);
            function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } }
            Log.info([name(function () { k.addColorStop(NaN, '#f00'); }), name(function () { k.addColorStop(-Infinity, '#f00'); }),
                name(function () { k.addColorStop(2, '#f00'); }), name(function () { k.addColorStop(0.5, 'bogus'); })].join(' '));
            """, "width=10 height=10");

        Assert.Equal(["TypeError TypeError IndexSizeError SyntaxError"], log);
    }

    [Theory]
    [InlineData("rgb(255, 0, 255)", "#ff00ff")]
    [InlineData("rgba(255, 0, 255, 1)", "#ff00ff")]
    [InlineData("#ff00ffff", "#ff00ff")]
    [InlineData("#ff00ff00", "rgba(255, 0, 255, 0)")]
    [InlineData("rgba(255, 0, 255,    0)", "rgba(255, 0, 255, 0)")]
    [InlineData("color(srgb 1 0 1)", "color(srgb 1 0 1)")]
    [InlineData("color(srgb 1 0 1 / 1)", "color(srgb 1 0 1)")]
    [InlineData("color(srgb 1 0 1/0)", "color(srgb 1 0 1 / 0)")]
    [InlineData("color(srgb 1 none 1)", "color(srgb 1 none 1)")]
    [InlineData("color(srgb none -1 2 / 3)", "color(srgb none -1 2)")]
    [InlineData("color(display-p3 0.96 0.76  	 0.79)", "color(display-p3 0.96 0.76 0.79)")]
    [InlineData("color(xyz 0.1 0.2 0.3)", "color(xyz-d65 0.1 0.2 0.3)")]
    [InlineData("lab(29% 39 20)", "lab(29 39 20)")]
    [InlineData("oklch(0.5 0.1 120 / 0.5)", "oklch(0.5 0.1 120 / 0.5)")]
    [InlineData("oklch(0.5 none 120)", "oklch(0.5 none 120)")]
    [InlineData("transparent", "rgba(0, 0, 0, 0)")]
    [InlineData("rgb(none none none)", "#000000")]
    [InlineData("rgb(300 none 400)", "#ff00ff")]
    [InlineData("color-mix(in srgb, red, blue)", "color(srgb 0.5 0 0.5)")]
    [InlineData("color-mix(in srgb, red 25%, blue)", "color(srgb 0.25 0 0.75)")]
    [InlineData("color-mix(in srgb, red, color(srgb 1 0 0))", "color(srgb 1 0 0)")]
    [InlineData("color-mix(in hsl, red, blue)", "#ff00ff")]
    [InlineData("rgb(from red g r b)", "color(srgb 0 1 0)")]
    [InlineData("color(from color(srgb 0.25 0.5 0.75 / 0.5) srgb r g b / alpha)", "color(srgb 0.25 0.5 0.75 / 0.5)")]
    [InlineData("hsl(from red h s l)", "#ff0000")]
    public async Task AColorReadsBackAsTheHtmlStandardSerializesIt(string input, string expected)
    {
        var script = "var g = document.getElementById('c').getContext('2d'); var s = " + System.Text.Json.JsonSerializer.Serialize(input) +
            "; var out = []; ['fillStyle', 'strokeStyle', 'shadowColor'].forEach(function (p) { g[p] = '#123456'; g[p] = s; out.push(g[p]); }); Log.info(out.join('|'));";
        var (_, log) = await CanvasParityTests.PaintAsync(script, "width=10 height=10");

        Assert.Equal([string.Join('|', expected, expected, expected)], log);
    }

    [Fact]
    public async Task ColorMixAndRelativeColors_TakeCurrentColorFromTheCanvasElement()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            c.setAttribute('style', 'display:block; color: magenta');
            g.fillStyle = 'color-mix(in srgb, black, currentcolor)';
            var mixed = g.fillStyle;
            g.fillStyle = 'rgb(from currentcolor g r b)';
            Log.info(mixed + ' ' + g.fillStyle);
            """, "width=10 height=10");

        Assert.Equal(["color(srgb 0.5 0 0.5) color(srgb 0 1 1)"], log);
    }

    [Fact]
    public async Task AColorGivenAsAnObject_IsReadWhenTheStyleIsSet()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            function px() { g.fillRect(0, 0, 10, 10); var d = g.getImageData(5, 5, 1, 1).data; g.clearRect(0, 0, 10, 10); return Array.from(d).join(','); }
            var out = [];
            g.fillStyle = { r: 0.2, g: 0.4, b: 0.6 }; out.push(px());
            g.fillStyle = { r: -1, g: 0, b: 0 }; out.push(px());
            g.fillStyle = { r: 0, g: 2, b: 0 }; out.push(px());
            g.fillStyle = { r: 0, g: 1, b: 0, a: 0.5 }; out.push(px());
            g.fillStyle = { r: 0, g: 1, b: 0, a: -1 }; out.push(px());
            g.fillStyle = '#123456'; g.fillStyle = { r: 1, g: 0 }; out.push(g.fillStyle);
            Log.info(out.join(' | '));
            """, "width=10 height=10");

        Assert.Equal(["51,102,153,255 | 0,0,0,255 | 0,255,0,255 | 0,255,0,128 | 0,0,0,0 | #123456"], log);
    }

    [Fact]
    public async Task CssRgbAndCssHslObjects_AreColorsAndTheirComponentsCanChange()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            var out = [];
            g.fillStyle = new CSSRGB(1, 0, 1); out.push(g.fillStyle);
            var rgb = new CSSRGB(0, CSS.percent(50), 0);
            g.fillStyle = rgb; out.push(g.fillStyle);
            rgb.g = 0; g.fillStyle = rgb; out.push(g.fillStyle);
            rgb.alpha = 0; g.fillStyle = rgb; out.push(g.fillStyle);
            var hsl = new CSSHSL(CSS.deg(180), 1, 1);
            g.fillStyle = hsl; out.push(g.fillStyle);
            hsl.l = 0.5; g.fillStyle = hsl; out.push(g.fillStyle);
            hsl.h = CSS.deg(120); g.fillStyle = hsl; out.push(g.fillStyle);
            hsl.h = CSS.turn(0.5); g.fillStyle = hsl; out.push(g.fillStyle);
            Log.info(out.join(' '));
            Log.info([CSS.percent(5).unit, CSS.deg(10).value, String(CSS.percent(5)), new CSSRGB(1, 1, 1) instanceof CSSColorValue, Object.prototype.toString.call(new CSSHSL(CSS.deg(0), 0, 0))].join(' '));
            """, "width=10 height=10");

        Assert.Equal(["#ff00ff #008000 #000000 rgba(0, 0, 0, 0) #ffffff #00ffff #00ff00 #00ffff", "percent 10 5% true [object CSSHSL]"], log);
    }

    [Fact]
    public async Task AGradientWithAColorThatIsNotLegacySrgb_GoesFromStopToStopInOklab()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            function px(x) { return Array.from(g.getImageData(x, 25, 1, 1).data).join(','); }
            var k = g.createLinearGradient(0, 0, 100, 0);
            k.addColorStop(0, '#f00'); k.addColorStop(1, 'color-mix(in srgb, #0f0, #00f)');
            g.fillStyle = k; g.fillRect(0, 0, 100, 50);
            var legacy = g.createLinearGradient(0, 0, 100, 0);
            legacy.addColorStop(0, '#f00'); legacy.addColorStop(1, '#008080');
            g.fillStyle = legacy; g.fillRect(0, 50, 100, 50);
            Log.info(px(25) + ' ' + px(50) + ' ' + px(75));
            Log.info(Array.from(g.getImageData(25, 75, 1, 1).data).join(','));
            """, "width=100 height=100");

        // The WPT expectations for the mixed gradient (within 3), and a gradient of legacy colors for contrast: plain sRGB, 25% of the way from red to teal.
        var near = log[0].Split(' ').Select(p => p.Split(',').Select(int.Parse).ToArray()).ToArray();
        int[][] expected = [[212, 81, 61, 255], [167, 106, 88, 255], [113, 120, 109, 255]];
        for (var i = 0; i < 3; i++)
            Assert.All(Enumerable.Range(0, 4), channel => Assert.InRange(near[i][channel], expected[i][channel] - 3, expected[i][channel] + 3));

        var legacy = log[1].Split(',').Select(int.Parse).ToArray();
        Assert.InRange(legacy[0], 189, 193);
        Assert.InRange(legacy[1], 30, 35);
        Assert.InRange(legacy[2], 30, 35);
    }

    [Fact]
    public async Task CurrentColor_InAGradientStop_IsTheCanvasElementsColor()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            c.setAttribute('style', 'display:block; color: #2a2');
            var k = g.createLinearGradient(0, 0, 10, 0);
            k.addColorStop(0, 'currentcolor'); k.addColorStop(1, 'currentcolor');
            g.fillStyle = k; g.fillRect(0, 0, 10, 10);
            Log.info(Array.from(g.getImageData(5, 5, 1, 1).data).join(','));
            """, "width=10 height=10");

        Assert.Equal(["34,170,34,255"], log);
    }

    [Theory]
    [InlineData("new CSSHWB(CSS.deg(120), 0, 0)", "#00ff00")]
    [InlineData("new CSSLab(CSS.percent(29), 39, 20)", "lab(29 39 20)")]
    [InlineData("new CSSLab(29, 39, 20, 0.5)", "lab(29 39 20 / 0.5)")]
    [InlineData("new CSSOKLab(0.5, 0.1, 0.1)", "oklab(0.5 0.1 0.1)")]
    [InlineData("new CSSLCH(CSS.percent(50), 40, CSS.deg(59.5))", "lch(50 40 59.5)")]
    [InlineData("new CSSOKLCH(0.5, 0.1, CSS.turn(0.5))", "oklch(0.5 0.1 180)")]
    [InlineData("new CSSColor('display-p3', [0.96, 0.76, 0.79])", "color(display-p3 0.96 0.76 0.79)")]
    [InlineData("new CSSColor('srgb', [1, CSS.percent(50), 0], 0.5)", "color(srgb 1 0.5 0 / 0.5)")]
    public async Task TheOtherTypedColorObjects_AreColorsAndReadBackSerialized(string expression, string expected)
    {
        var (_, log) = await CanvasParityTests.PaintAsync(
            "var g = document.getElementById('c').getContext('2d'); g.fillStyle = '#123456'; g.fillStyle = " + expression + "; Log.info(g.fillStyle);", "width=10 height=10");

        Assert.Equal([expected], log);
    }

    [Fact]
    public async Task ATypedColorObject_ThatIsMissingAnArgumentOrHasAnImpossibleOne_Throws()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } }
            Log.info([name(function () { new CSSLab(1, 2); }), name(function () { new CSSHWB(120, 0, 0); }), name(function () { new CSSRGB(1, 'x', 0); }), name(function () { CSSLab(1, 2, 3); }),
                name(function () { new CSSColorValue(); })].join(' '));
            """, "width=10 height=10");

        Assert.Equal(["TypeError TypeError TypeError TypeError TypeError"], log);
    }
}
