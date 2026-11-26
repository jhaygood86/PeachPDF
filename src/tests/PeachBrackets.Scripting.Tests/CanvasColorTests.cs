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
}
