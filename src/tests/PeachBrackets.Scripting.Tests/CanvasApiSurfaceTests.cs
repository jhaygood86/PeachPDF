namespace PeachBrackets.Scripting.Tests;

/// <summary>The Web IDL surface of the 2D context and <c>ImageData</c>: required arguments, overloads, the types an argument must have, and what put pixels replace.</summary>
public class CanvasApiSurfaceTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task AMethodCalledWithFewerArgumentsThanItRequires_IsATypeError()
    {
        var log = await LogAsync("""
            var calls = [
                function () { g.scale(); }, function () { g.scale(1); }, function () { g.rotate(); }, function () { g.translate(0); }, function () { g.transform(1, 0, 0, 1, 0); },
                function () { g.setTransform(1, 0, 0, 1, 0); }, function () { g.setTransform(1); }, function () { g.createLinearGradient(0, 0, 1); }, function () { g.createRadialGradient(0, 0, 1, 0, 0); },
                function () { g.createPattern(c); }, function () { g.fillRect(0, 0, 0); }, function () { g.strokeRect(0, 0); }, function () { g.moveTo(0); }, function () { g.arc(0, 0, 1, 0); },
                function () { g.drawImage(c); }, function () { g.fillText('x', 0); }, function () { g.getImageData(0, 0, 1); }, function () { g.putImageData(g.createImageData(1, 1)); },
                function () { c.getContext(); },
            ];
            Log.info(calls.map(name).join(' '));
            """);

        Assert.Equal([string.Join(' ', Enumerable.Repeat("TypeError", 19))], log);
    }

    [Fact]
    public async Task SetTransform_TakesSixNumbersOrAMatrixOrNothing()
    {
        var log = await LogAsync("""
            g.setTransform(2, 0, 0, 2, 5, 5);
            g.setTransform();
            var identity = g.getTransform().a + ' ' + g.getTransform().e;
            g.setTransform(new DOMMatrix([3, 0, 0, 3, 1, 1]));
            Log.info([identity, g.getTransform().a, name(function () { g.setTransform(1, 2, 3); }), name(function () { g.setTransform(1); })].join(' '));
            """);

        Assert.Equal(["1 0 3 TypeError TypeError"], log);
    }

    [Fact]
    public async Task GetContext_ReturnsNullForAnyTypeButTheExactString2d()
    {
        var log = await LogAsync("""
            var types = ['', '2d#', '2D', '2d\0', '2ｄ', 'null', 'undefined', 'webgl'];
            Log.info(types.map(function (t) { return c.getContext(t) === null; }).join(' ') + ' ' + (c.getContext('2d') === g));
            """);

        Assert.Equal(["true true true true true true true true true"], log);
    }

    [Fact]
    public async Task TheContextPrototype_IsNotWritableOrConfigurable_ButItsMethodsAre()
    {
        var log = await LogAsync("""
            var ctor = window.CanvasRenderingContext2D, proto = ctor.prototype;
            ctor.prototype = null; var kept = ctor.prototype === proto;
            delete ctor.prototype; var stillThere = ctor.prototype === proto;
            var d = Object.getOwnPropertyDescriptor(ctor, 'prototype');
            var fill = proto.fill; proto.fill = 1; var set = proto.fill === 1; delete proto.fill; var gone = proto.fill === undefined; proto.fill = fill;
            Log.info([kept, stillThere, d.writable, d.configurable, set, gone].join(' '));
            """);

        Assert.Equal(["true true false false true true"], log);
    }

    [Fact]
    public async Task TheImageDataConstructor_HasTwoOverloadsAndChecksItsArguments()
    {
        var log = await LogAsync("""
            var out = [];
            out.push(name(function () { ImageData(1, 1); }));
            out.push(name(function () { new ImageData(10); }));
            out.push(name(function () { new ImageData(0, 10); }));
            out.push(name(function () { new ImageData('width', 'height'); }));
            out.push(name(function () { new ImageData(1 << 31, 1 << 31); }));
            out.push(name(function () { new ImageData(new Uint8ClampedArray(0), 1); }));
            out.push(name(function () { new ImageData(new Uint8ClampedArray(3), 1); }));
            out.push(name(function () { new ImageData(new Uint8ClampedArray(4), 0); }));
            out.push(name(function () { new ImageData(new Uint8ClampedArray(4), 1, 2); }));
            out.push(name(function () { new ImageData(new Uint8Array(8), 1, 2); }));
            out.push(name(function () { new ImageData(new Uint8Array(100), 25); }));
            out.push(name(function () { new ImageData(null, 4, 4); }));
            var data = new Uint8ClampedArray(28);
            var fromData = new ImageData(data, 7);
            out.push(fromData.width + 'x' + fromData.height + (fromData.data === data));
            Log.info(out.join(' '));
            """);

        Assert.Equal(["TypeError TypeError IndexSizeError IndexSizeError IndexSizeError InvalidStateError InvalidStateError IndexSizeError IndexSizeError TypeError IndexSizeError TypeError 7x1true"], log);
    }

    [Fact]
    public async Task CreateImageData_ChecksItsThis_ItsSizesAndItsSource()
    {
        var log = await LogAsync("""
            var imgdata = g.createImageData(1, 1);
            var create = CanvasRenderingContext2D.prototype.createImageData;
            Log.info([name(function () { create.call(null, imgdata); }), name(function () { create.call({}, imgdata); }), name(function () { g.createImageData(10, 0); }),
                name(function () { g.createImageData(0.99, 10); }), name(function () { g.createImageData(Infinity, 1); }), name(function () { g.createImageData(NaN, 1); }),
                name(function () { g.createImageData({ width: 1, height: 1, data: [] }); }), g.createImageData(-3, 2).width].join(' '));
            """);

        Assert.Equal(["TypeError TypeError IndexSizeError IndexSizeError TypeError TypeError TypeError 3"], log);
    }

    [Fact]
    public async Task PutImageData_TakesOnlyAnImageData_AndReplacesWhatIsUnderIt()
    {
        var log = await LogAsync("""
            g.fillStyle = '#0f0'; g.fillRect(0, 0, 40, 20);
            var d = g.createImageData(4, 4);
            for (var i = 0; i < d.data.length; i += 4) { d.data[i] = 255; d.data[i + 3] = 128; }
            g.putImageData(d, 10, 5);
            var wrong = name(function () { g.putImageData({ width: 1, height: 1, data: [255, 0, 0, 255] }, 0, 0); });
            // A pixel under the put is the data's own (half-transparent red), not a blend with the green that was there.
            Log.info([wrong, Array.from(g.getImageData(11, 6, 1, 1).data).join(','), Array.from(g.getImageData(20, 6, 1, 1).data).join(',')].join(' '));
            """);

        Assert.Equal(["TypeError 255,0,0,128 0,255,0,255"], log);
    }

    [Fact]
    public async Task APutDirtyRectangle_ThatReachesOutsideTheImage_IsShortenedNotSlid()
    {
        var log = await LogAsync("""
            g.fillStyle = '#0f0'; g.fillRect(0, 0, 40, 20);
            var d = g.createImageData(10, 10);
            for (var i = 0; i < d.data.length; i += 4) { d.data[i] = 255; d.data[i + 3] = 255; }
            // dirtyX -4, width 6: only x 0 and 1 of the image are inside it, drawn at 20 + 0.
            g.putImageData(d, 20, 5, -4, 0, 6, 10);
            Log.info([Array.from(g.getImageData(20, 6, 1, 1).data).join(','), Array.from(g.getImageData(21, 6, 1, 1).data).join(','), Array.from(g.getImageData(22, 6, 1, 1).data).join(',')].join(' '));
            """);

        Assert.Equal(["255,0,0,255 255,0,0,255 0,255,0,255"], log);
    }

    [Fact]
    public async Task GetImageData_EnforcesTheRangeOfALong()
    {
        var log = await LogAsync("""
            Log.info([name(function () { g.getImageData(10, 0xffffffff, 2147483647, 10); }), name(function () { g.getImageData(0, 0, Infinity, 1); }), name(function () { g.getImageData(NaN, 0, 1, 1); }),
                name(function () { g.getImageData(0, 0, 1, 1); })].join(' '));
            """);

        Assert.Equal(["TypeError TypeError TypeError none"], log);
    }
}
