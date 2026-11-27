using Xunit;

namespace PeachBrackets.Scripting.Tests;

public class WorkerTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'); " + script, "width=4 height=4");
        return log;
    }

    [Fact]
    public async Task AWorker_EchoesAMessageWithItsDataCloned_AndTimersRunInIt()
    {
        var log = await LogAsync("""
            var code = "onmessage = function (e) { var d = e.data; setTimeout(function () { postMessage({ sum: d.a + d.b, list: d.list.map(function (x) { return x * 2; }), self: self === globalThis, kind: typeof document, name: name, scope: self instanceof DedicatedWorkerGlobalScope }); }, 50); };";
            var worker = new Worker('data:text/javascript,' + encodeURIComponent(code), { name: 'w1' });
            var source = { a: 1, b: 2, list: [1, 2, 3] };
            worker.onmessage = function (e) {
                Log.info([e.data.sum, e.data.list.join(), e.data.self, e.data.kind, e.data.name, e.data.scope, e.data !== source].join(' '));
                worker.terminate();
            };
            worker.postMessage(source);
            """);

        Assert.Equal(["3 2,4,6 true undefined w1 true true"], log);
    }

    [Fact]
    public async Task AWorkerThatThrows_ReportsAnErrorEventOnTheWorkerObject()
    {
        var log = await LogAsync("""
            var worker = new Worker('data:text/javascript,' + encodeURIComponent('throw new Error("boom");'));
            worker.onerror = function (e) { e.preventDefault(); Log.info('error ' + (e.message.indexOf('boom') >= 0)); };
            """);

        Assert.Equal(["error true"], log);
    }

    [Fact]
    public async Task AnImageBitmapAndAnOffscreenCanvas_AreTransferredToAWorker_AndTheSenderIsDetached()
    {
        var log = await LogAsync("""
            var code = "onmessage = function (e) { var d = e.data; var g = d.canvas.getContext('2d'); g.fillStyle = '#0f0'; g.fillRect(0, 0, 4, 4); var p = Array.from(g.getImageData(1, 1, 1, 1).data).join(','); postMessage({ w: d.bitmap.width, h: d.bitmap.height, cw: d.canvas.width, p: p }); };";
            var worker = new Worker('data:text/javascript,' + encodeURIComponent(code));
            var source = document.createElement('canvas'); source.width = 7; source.height = 5; source.getContext('2d').fillRect(0, 0, 7, 5);
            createImageBitmap(source).then(function (bitmap) {
                var offscreen = new OffscreenCanvas(9, 3);
                worker.onmessage = function (e) {
                    Log.info([e.data.w, e.data.h, e.data.cw, e.data.p, bitmap.width, offscreen.width].join(' '));
                    worker.terminate();
                };
                worker.postMessage({ bitmap: bitmap, canvas: offscreen }, [bitmap, offscreen]);
            });
            """);

        Assert.Equal(["7 5 9 0,255,0,255 0 0"], log);
    }
}
