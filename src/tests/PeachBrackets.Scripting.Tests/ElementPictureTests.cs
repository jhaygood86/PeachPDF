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
}
