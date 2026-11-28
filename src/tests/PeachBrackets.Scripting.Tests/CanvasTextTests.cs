namespace PeachBrackets.Scripting.Tests;

/// <summary>The <c>font</c> of the 2D context and its text style properties.</summary>
public class CanvasTextTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } " + script, "width=40 height=20");
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

    [Fact]
    public async Task AFontFace_IsInTheFontSetOnceAdded_AndAnUnloadableOneRejectsWhenItLoads()
    {
        var log = await LogAsync("""
            var good = new FontFace('Missing', "url('/no/such/font.ttf')");
            var bad = new FontFace('Broken', 'not a source');
            var seen = [good.family, good.status, bad.status, document.fonts.size, document.fonts.add(good) === document.fonts, document.fonts.has(good), document.fonts.size, document.fonts.delete(good), document.fonts.size];
            good.load().then(function () { Log.info('loaded'); }, function (e) { Log.info('rejected ' + e.name + ' ' + good.status); });
            Log.info(seen.join(' '));
            """);

        Assert.Equal(["Missing unloaded error 0 true true 1 true 0", "rejected NetworkError error"], log);
    }

    [Fact]
    public async Task TextMetricsRanges_AnswerWhereTheCharactersAre()
    {
        var log = await LogAsync("""
            g.font = '20px TestSans';
            var tm = g.measureText('Hello World');
            var clusters = tm.getTextClusters();
            var w = tm.width, first = tm.getSelectionRects(0, 5)[0], whole = tm.getActualBoundingBox(0, 11);
            Log.info([clusters.length, clusters[0].text, clusters[10].end, first.x, first.width > 30 && first.width < w, whole.width > 0 && whole.width <= w + 1,
                tm.getIndexFromOffset(-5), tm.getIndexFromOffset(w + 5), tm.getIndexFromOffset(first.width + 1),
                name(function () { tm.getTextClusters(-1, 2); }), name(function () { tm.getTextClusters(5, 3); }), name(function () { tm.getSelectionRects(0, 12); })].join(' '));
            """);

        Assert.Equal(["11 H 11 0 true true 0 11 5 TypeError IndexSizeError IndexSizeError"], log);
    }

    [Fact]
    public async Task ARangeOfATextNode_HasTheRectanglesOfItsCharacters_AndAPointFindsTheCharacterUnderIt()
    {
        var log = await LogAsync("""
            var el = document.createElement('p'); el.style.cssText = 'position: absolute; left: 0; top: 0; margin: 0; font: 20px TestSans'; el.textContent = 'Hello World'; document.body.appendChild(el);
            var text = el.firstChild, range = document.createRange();
            range.setStart(text, 0); range.setEnd(text, 5);
            var all = range.getClientRects(), whole = el.getBoundingClientRect();
            range.setStart(text, 2); range.setEnd(text, 2);
            var collapsed = range.getClientRects();
            var at = document.caretPositionFromPoint(1, whole.y + whole.height / 2), far = document.caretPositionFromPoint(whole.width + 50, whole.y + whole.height / 2);
            Log.info([all.length, all[0].x, all[0].width > 40 && all[0].width < whole.width, collapsed.length, collapsed[0].width, at.offsetNode === text, at.offset, far.offset].join(' '));
            """);

        Assert.Equal(["1 0 true 1 0 true 0 11"], log);
    }

    [Fact]
    public async Task SmallCaps_AreDrawnAndMeasuredAsSmallerCapitals_WhateverTheFontHas()
    {
        var log = await LogAsync("""
            g.font = '32px TestSans';
            var normal = g.measureText('Hello').width, capitals = g.measureText('HELLO').width;
            g.fontVariantCaps = 'small-caps';
            var small = g.measureText('Hello').width, lowerOnly = g.measureText('hello').width;
            g.fontVariantCaps = 'all-small-caps';
            var all = g.measureText('HELLO').width;
            Log.info([small !== normal, small < capitals, lowerOnly < capitals, all < capitals].join(' '));
            """);

        Assert.Equal(["true true true true"], log);
    }

    [Fact]
    public async Task ABidiControl_TakesNoRoomAndNoLetterSpacing_InTheCanvasAndInTheDocument()
    {
        var log = await LogAsync("""
            g.font = '20px TestSans'; g.letterSpacing = '10px';
            var text = '‭AB‬';
            var tm = g.measureText(text);
            var canvasRects = [tm.getSelectionRects(0, 1)[0].width, tm.getSelectionRects(3, 4)[0].width, tm.getSelectionRects(1, 2)[0].width > 10];

            var el = document.createElement('div'); el.innerHTML = text; el.style.font = '20px TestSans'; el.style.letterSpacing = '10px';
            document.body.appendChild(el);
            var range = document.createRange(); range.setStart(el.childNodes[0], 0); range.setEnd(el.childNodes[0], 1);
            var first = range.getClientRects()[0].width;
            range.setStart(el.childNodes[0], 3); range.setEnd(el.childNodes[0], 4);
            var last = range.getClientRects()[0].width;
            Log.info([canvasRects.join(','), first, last].join(' '));
            """);

        Assert.Equal(["0,0,true 0 0"], log);
    }
}
