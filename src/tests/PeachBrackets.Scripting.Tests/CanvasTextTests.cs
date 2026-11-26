namespace PeachBrackets.Scripting.Tests;

/// <summary>The <c>font</c> of the 2D context and its text style properties.</summary>
public class CanvasTextTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task TheFontShorthand_IsGivenBackAsTheCanvasSerializesIt()
    {
        var log = await LogAsync("""
            var seen = [];
            ['20PX   SERIF', '  15px sans-serif  ', 'small-caps italic 400 12px/2 "Unknown Font #2", sans-serif', 'italic 300 12px serif', 'bold 1pt monospace', '12px Unquoted Name, "Quoted"',
             '20px cursive,fantasy', 'oblique 10deg 12px serif', 'xx-large serif', '1000% serif', '1em sans-serif'].forEach(function (value) { g.font = value; seen.push(g.font); });
            Log.info(seen.join('|'));
            """);

        Assert.Equal(["20px serif|15px sans-serif|italic small-caps 12px \"Unknown Font #2\", sans-serif|italic 300 12px serif|bold 1.333333px monospace|12px \"Unquoted Name\", Quoted|" +
            "20px cursive, fantasy|oblique 10deg 12px serif|32px serif|160px serif|16px sans-serif"], log);
    }

    [Fact]
    public async Task AFontThatIsNotValid_LeavesTheFontAsItWas()
    {
        var log = await LogAsync("""
            g.font = '20px serif';
            var seen = [];
            ['', 'bogus', 'inherit', '10px {bogus}', '10px initial', '10px default', 'var(--x)', '1em serif; background: green', '10px', 'bold', '12px "unterminated'].forEach(function (value) { g.font = value; seen.push(g.font); });
            Log.info(seen.filter(function (v) { return v !== '20px serif'; }).length + ' ' + seen.length);
            """);

        Assert.Equal(["0 11"], log);
    }

    [Fact]
    public async Task TheTextStyleProperties_TakeTheirOwnWords_AndLengthsAreGivenBackWithTheirUnit()
    {
        var log = await LogAsync("""
            var out = [g.letterSpacing, g.wordSpacing, g.fontKerning, g.fontStretch, g.fontVariantCaps, g.textRendering, g.lang];
            g.letterSpacing = '1PX'; g.wordSpacing = '-0.5EM'; g.fontKerning = 'none'; g.fontStretch = 'ultra-expanded'; g.fontVariantCaps = 'all-small-caps'; g.textRendering = 'optimizeSpeed'; g.lang = 'tr';
            out.push(g.letterSpacing, g.wordSpacing, g.fontKerning, g.fontStretch, g.fontVariantCaps, g.textRendering, g.lang);
            g.letterSpacing = '1min'; g.wordSpacing = 'normal'; g.fontKerning = 'NONE'; g.fontStretch = 'ulTra-condensed'; g.fontVariantCaps = 'bogus'; g.textRendering = 'optimizespeed'; g.letterSpacing = NaN;
            out.push(g.letterSpacing, g.wordSpacing, g.fontKerning, g.fontStretch, g.fontVariantCaps, g.textRendering);
            Log.info(out.join(' '));
            """);

        Assert.Equal(["0px 0px auto normal normal auto inherit 1px -0.5em none ultra-expanded all-small-caps optimizeSpeed tr 1px -0.5em none ultra-expanded all-small-caps optimizeSpeed"], log);
    }

    [Fact]
    public async Task ARelativeFontSize_IsRelativeToTheCanvasElementsOwnFont()
    {
        var log = await LogAsync("""
            c.style.fontSize = '30px';
            g.font = '2em serif'; var inElement = g.font;
            var detached = document.createElement('canvas').getContext('2d'); detached.font = '2em serif';
            Log.info(inElement + ' | ' + detached.font);
            """);

        Assert.Equal(["60px serif | 20px serif"], log);
    }

    [Fact]
    public async Task DocumentFontsReady_IsAPromiseThatSettlesOnceTheFontsOfThePageAreRegistered()
    {
        var log = await LogAsync("""
            var ready = document.fonts.ready;
            ready.then(function () { Log.info('settled ' + document.fonts.status); });
            Log.info((ready instanceof Promise) + ' ' + document.fonts.status);
            """);

        Assert.Equal(["true loading", "settled loaded"], log);
    }
}
