namespace PeachBrackets.Scripting.Tests;

/// <summary>Small members of the canvas host: the focus ring, a placeholder's size, bitmap sources that are closed or cannot be handed on.</summary>
public class CanvasHostGapsTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task DrawFocusIfNeeded_DrawsARingOnlyForAFocusedElementInsideTheCanvas()
    {
        var log = await LogAsync("""
            var host = document.createElement('div');
            host.innerHTML = '<canvas id="k" width="40" height="20"><a href="#x" id="inside">in</a></canvas><a href="#y" id="outside">out</a>';
            document.body.appendChild(host);
            var k = document.getElementById('k'), kg = k.getContext('2d'), inside = document.getElementById('inside'), outside = document.getElementById('outside');
            function ink() { return Array.from(kg.getImageData(0, 0, 40, 20).data).filter(function (v, i) { return i % 4 === 3 && v > 0; }).length; }
            kg.beginPath(); kg.rect(5, 5, 30, 10);
            kg.drawFocusIfNeeded(inside);
            var unfocused = ink();
            inside.focus();
            kg.drawFocusIfNeeded(inside);
            var focused = ink();
            kg.clearRect(0, 0, 40, 20); outside.focus();
            kg.drawFocusIfNeeded(outside);
            var notInside = ink();
            Log.info([unfocused, focused > 0, notInside, name(function () { kg.drawFocusIfNeeded(1); })].join(' '));
            """);

        Assert.Equal(["0 true 0 TypeError"], log);
    }

    [Fact]
    public async Task APlaceholderCanvas_KeepsTheSizeOfItsBitmap_WhateverItsAttributesSay()
    {
        var log = await LogAsync("""
            var canvas = document.createElement('canvas'); canvas.width = 64; canvas.height = 32; document.body.appendChild(canvas);
            var offscreen = canvas.transferControlToOffscreen();
            canvas.setAttribute('width', '200'); canvas.setAttribute('height', '100');
            Log.info([canvas.clientWidth, canvas.clientHeight, offscreen.width, name(function () { canvas.width = 5; })].join(' '));
            """);

        Assert.Equal(["64 32 64 InvalidStateError"], log);
    }

    [Fact]
    public async Task CreateImageBitmap_RejectsAnEmptyCropOfAnySource_AndAClosedBitmap()
    {
        var log = await LogAsync("""
            var blank = new OffscreenCanvas(4, 4); blank.getContext('2d').fillRect(0, 0, 4, 4);
            blank.convertToBlob().then(function (blob) {
              return Promise.all([
                createImageBitmap(blob, 0, 0, 0, 10).then(function () { return 'resolved'; }, function (e) { return e.name; }),
                createImageBitmap(blob, 0, 0, 2, 2).then(function (b) { return b.width + 'x' + b.height; }, function (e) { return e.name; }),
                createImageBitmap(blank).then(function (b) { b.close(); return createImageBitmap(b).then(function () { return 'resolved'; }, function (e) { return e.name; }); })
              ]);
            }).then(function (results) { Log.info(results.join(' ')); });
            """);

        Assert.Equal(["RangeError 2x2 InvalidStateError"], log);
    }

    [Fact]
    public async Task Serializing_ClosesSvgElementsButNotTheVoidElementsOfHtmlInsideThem()
    {
        var log = await LogAsync("""
            var host = document.createElement('div');
            host.innerHTML = '<svg width="10" height="10"><rect width="5" height="5"/><circle r="2"/><foreignObject><div>a<br>b</div></foreignObject></svg>';
            Log.info(host.innerHTML);
            """);

        Assert.Equal(["<svg width=\"10\" height=\"10\"><rect width=\"5\" height=\"5\"></rect><circle r=\"2\"></circle><foreignObject><div>a<br>b</div></foreignObject></svg>"], log);
    }
}
