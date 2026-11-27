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

    [Fact]
    public async Task ABlobAWorkerMakesWithConvertToBlob_ArrivesWithItsBytesAndType()
    {
        var log = await LogAsync("""
            var code = "var c = new OffscreenCanvas(4, 4); var g = c.getContext('2d'); g.fillRect(0, 0, 4, 4); c.convertToBlob().then(function (b) { postMessage({ b: b, size: b.size }); });";
            var worker = new Worker('data:text/javascript,' + encodeURIComponent(code));
            worker.onmessage = function (e) {
                var img = new Image();
                img.onload = function () { Log.info([e.data.b.type, e.data.b.size === e.data.size, e.data.size > 0, img.width].join(' ')); worker.terminate(); };
                img.onerror = function () { Log.info('image error ' + e.data.b.type + ' ' + e.data.b.size); worker.terminate(); };
                img.src = URL.createObjectURL(e.data.b);
            };
            """);

        Assert.Equal(["image/png true true 4"], log);
    }

    [Fact]
    public async Task AnImageLoadsFromABlobUrlAScriptMade_AndConvertToBlobRejectsAnEmptyOrDetachedCanvas()
    {
        var log = await LogAsync("""
            var c = new OffscreenCanvas(4, 4); var g = c.getContext('2d'); g.fillRect(0, 0, 4, 4);
            new OffscreenCanvas(0, 0).convertToBlob().catch(function (e) { Log.info(e.name); });
            var gone = new OffscreenCanvas(3, 3); structuredClone(gone, { transfer: [gone] });
            gone.convertToBlob().catch(function (e) { Log.info(e.name); });
            c.convertToBlob().then(function (b) {
                var img = new Image();
                img.onload = function () { Log.info('ok ' + img.width); };
                img.onerror = function () { Log.info('image error ' + b.type + ' ' + b.size); };
                img.src = URL.createObjectURL(b);
            });
            """);

        Assert.Equal(["IndexSizeError", "InvalidStateError", "ok 4"], log);
    }

    [Fact]
    public async Task APortTransferredToAWorker_StaysEntangledWithItsPeerInThePage_AndAcrossTwoWorkers()
    {
        var log = await LogAsync("""
            var code = "var other; onmessage = function (e) { if (e.data.port) { other = e.data.port; other.onmessage = function (m) { postMessage('relay ' + m.data + ' ' + (m.ports.length)); }; other.postMessage('hello from ' + e.data.name); } };";
            var url = 'data:text/javascript,' + encodeURIComponent(code);
            var a = new Worker(url), b = new Worker(url);
            var channel = new MessageChannel();
            channel.port2.onmessage = function (m) { Log.info('page got ' + m.data); };
            a.postMessage({ port: channel.port1, name: 'a' }, [channel.port1]);
            var pair = new MessageChannel();
            a.onmessage = function (e) { Log.info('a says ' + e.data); };
            pair.port1.onmessage = function (m) { Log.info('pair ' + m.data); };
            b.postMessage({ port: pair.port2, name: 'b' }, [pair.port2]);
            """);

        Assert.Contains("page got hello from a", log);
        Assert.Contains("pair hello from b", log);
    }
}
