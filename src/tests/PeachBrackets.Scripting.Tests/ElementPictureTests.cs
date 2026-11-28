namespace PeachBrackets.Scripting.Tests;

/// <summary>The picture of an element a canvas draws: elements below the top of the page, filters that reference SVG, and images captured before a change.</summary>
public class ElementPictureTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } " + script, "width=4 height=4");
        return log;
    }

    [Fact]
    public async Task AnElementBelowAnotherWithAnSvgFilter_IsPaintedThroughTheFilterToo()
    {
        var log = await LogAsync("""
            var img = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
            var host = document.createElement('div');
            host.innerHTML = '<svg width="0" height="0"><filter id="red"><feFlood flood-color="red"/></filter><filter id="pic"><feImage href="' + img + '"/></filter></svg><canvas id="k" width="100" height="300" content="drawable">'
              + '<div id="a" drawable style="width:100px;height:100px;background:green;filter:url(#red)"></div>'
              + '<div id="b" drawable style="width:100px;height:100px;background:green;filter:url(#red)"></div>'
              + '<div id="d" drawable style="width:100px;height:100px;background:green;filter:url(#pic)"></div></canvas>';
            document.body.appendChild(host);
            var k = document.getElementById('k'), kg = k.getContext('2d');
            k.addEventListener('paint', function () {
              ['a', 'b', 'd'].forEach(function (id, i) { kg.drawElementImage(document.getElementById(id), 0, i * 100); });
              var p = function (y) { return Array.from(kg.getImageData(50, y, 1, 1).data).join(','); };
              Log.info([p(50), p(150), p(250)].join(' '));
            }, { once: true });
            k.requestPaint();
            """);

        Assert.Equal(["255,0,0,255 255,0,0,255 255,0,0,127"], log);
    }

    [Fact]
    public async Task ACapturedElementImage_KeepsWhatTheElementLookedLikeThen_AndADisplayNoneCanvasCannotBeDrawnInto()
    {
        var log = await LogAsync("""
            var host = document.createElement('div');
            host.innerHTML = '<canvas id="k" width="100" height="100" content="drawable"><div id="kid" drawable style="width:100px;height:100px;background:rgb(0,255,0)"></div></canvas><canvas id="hidden" width="10" height="10" content="drawable" style="display:none"><div id="inner" drawable></div></canvas>';
            document.body.appendChild(host);
            var k = document.getElementById('k'), kg = k.getContext('2d'), kid = document.getElementById('kid'), hidden = document.getElementById('hidden');
            k.addEventListener('paint', function () {
              var image = k.captureElementImage(kid);
              kid.style.background = 'rgb(0,0,255)';
              kg.drawElementImage(image, 0, 0);
              var green = Array.from(kg.getImageData(5, 5, 1, 1).data).join(',');
              Log.info(green + ' ' + name(function () { hidden.getContext('2d').drawElementImage(document.getElementById('inner'), 0, 0); }));
            }, { once: true });
            k.requestPaint();
            """);

        Assert.Equal(["0,255,0,255 InvalidStateError"], log);
    }

    [Fact]
    public async Task ADrawableChildWithATransformAndABrokenInlineInside_ReportsItsRectanglesInTheCanvas()
    {
        var log = await LogAsync("""
            var host = document.createElement('div');
            host.innerHTML = '<canvas id="k" width="200" height="120" content="drawable">'
              + '<div id="turned" drawable style="width:60px;height:20px;background:rgb(0,0,255);transform:rotate(90deg);transform-origin:0 0"></div>'
              + '<div id="text" drawable style="width:50px;font:10px TestSans"><span id="words">aaaa bbbb cccc dddd eeee</span></div></canvas>';
            document.body.appendChild(host);
            var k = document.getElementById('k'), kg = k.getContext('2d');
            var turned = document.getElementById('turned'), words = document.getElementById('words'), text = document.getElementById('text');
            k.addEventListener('paint', function () {
              kg.drawElementImage(turned, 100, 0);
              kg.drawElementImage(text, 0, 60);
              var a = turned.getBoundingClientRect(), b = words.getBoundingClientRect();
              Log.info([Math.round(a.width), Math.round(a.height), b.height > 12, b.width <= 52].join(' '));
            }, { once: true });
            k.requestPaint();
            """);

        Assert.Equal(["20 60 true true"], log);
    }

    [Fact]
    public async Task ThePageIsLaidOutAgainForEveryChange_AndTheElementPictureIsTheOneOfThePaint()
    {
        var log = await LogAsync("""
            var host = document.createElement('div');
            host.innerHTML = '<div id="box" style="width:10px;height:10px"></div><canvas id="k" width="40" height="40" content="drawable"><div id="kid" drawable style="width:10px;height:10px;background:rgb(0,255,0)"></div></canvas>';
            document.body.appendChild(host);
            var box = document.getElementById('box'), kid = document.getElementById('kid'), k = document.getElementById('k'), kg = k.getContext('2d');
            var widths = [];
            for (var i = 0; i < 12; i++) { box.style.width = (10 + i) + 'px'; widths.push(Math.round(box.getBoundingClientRect().width)); }
            box.style.width = '21px'; widths.push(Math.round(box.getBoundingClientRect().width));
            box.style.width = '10px'; widths.push(Math.round(box.getBoundingClientRect().width));
            box.style.width = '21px'; widths.push(Math.round(box.getBoundingClientRect().width));
            k.addEventListener('paint', function () {
              var seen = [];
              for (var i = 0; i < 12; i++) { kid.style.width = (10 + i) + 'px'; kg.clearRect(0, 0, 40, 40); kg.drawElementImage(kid, 0, 0); seen.push(Array.from(kg.getImageData(9 + i < 40 ? 9 + i : 39, 5, 1, 1).data)[3]); }
              kid.style.width = '10px'; kg.clearRect(0, 0, 40, 40); kg.drawElementImage(kid, 0, 0);
              kid.style.width = '11px'; kg.clearRect(0, 0, 40, 40); kg.drawElementImage(kid, 0, 0);
              kid.style.width = '10px'; kg.clearRect(0, 0, 40, 40); kg.drawElementImage(kid, 0, 0);
              Log.info(widths.join(',') + ' ' + seen.join(',') + ' ' + Array.from(kg.getImageData(10, 5, 1, 1).data)[3]);
            }, { once: true });
            k.requestPaint();
            """);

        Assert.Equal(["10,11,12,13,14,15,16,17,18,19,20,21,21,10,21 255,0,0,0,0,0,0,0,0,0,0,0 0"], log);
    }
}
