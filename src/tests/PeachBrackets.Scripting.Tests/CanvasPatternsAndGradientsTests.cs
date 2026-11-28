namespace PeachBrackets.Scripting.Tests;

/// <summary>Patterns, gradients, the <c>clear</c> operator and OffscreenCanvas contexts, read back as pixels and as what the API throws.</summary>
public class CanvasPatternsAndGradientsTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } function px(x, y) { return Array.from(g.getImageData(x, y, 1, 1).data).join(','); } " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task TheClearOperator_EmptiesWhatTheClipsLeave_AndASourceWithNoContextIsTransparent()
    {
        var log = await LogAsync("""
            g.fillStyle = '#0f0'; g.fillRect(0, 0, 40, 20);
            g.globalCompositeOperation = 'clear'; g.fillStyle = '#f00'; g.fillRect(0, 0, 5, 5);
            var cleared = px(30, 15);
            g.globalCompositeOperation = 'source-over'; g.fillStyle = '#0f0'; g.fillRect(0, 0, 40, 20);
            var empty = document.createElement('canvas'); empty.width = 10; empty.height = 10;
            g.globalCompositeOperation = 'copy'; g.drawImage(empty, 0, 0);
            Log.info(g.globalCompositeOperation + ' ' + cleared + ' ' + px(30, 15));
            """);

        Assert.Equal(["copy 0,0,0,0 0,0,0,0"], log);
    }

    [Fact]
    public async Task AGradientWithNoDirection_PaintsNothing_AndOneWithAlphaMixesColorAndAlphaSeparately()
    {
        var log = await LogAsync("""
            g.fillStyle = '#0f0'; g.fillRect(0, 0, 40, 20);
            var flat = g.createLinearGradient(5, 5, 5, 5); flat.addColorStop(0, '#f00'); flat.addColorStop(1, '#f00');
            g.fillStyle = flat; g.fillRect(0, 0, 40, 20);
            var same = px(20, 10);
            g.clearRect(0, 0, 40, 20);
            var fade = g.createLinearGradient(0, 0, 40, 0); fade.addColorStop(0, 'rgba(255,255,0,0)'); fade.addColorStop(1, 'rgba(0,0,255,1)');
            g.fillStyle = fade; g.fillRect(0, 0, 40, 20);
            var mid = g.getImageData(20, 10, 1, 1).data;
            Log.info(same + ' ' + (Math.abs(mid[0] - 128) < 8) + ' ' + (Math.abs(mid[3] - 128) < 8));
            """);

        Assert.Equal(["0,255,0,255 true true"], log);
    }

    [Fact]
    public async Task APattern_IsCheckedWhenMade_AndFollowsItsTransform()
    {
        var log = await LogAsync("""
            var tile = document.createElement('canvas'); tile.width = 4; tile.height = 4;
            var t = tile.getContext('2d'); t.fillStyle = '#0f0'; t.fillRect(0, 0, 4, 4);
            var empty = document.createElement('canvas'); empty.width = 0;
            var errors = [name(function () { g.createPattern(null, 'repeat'); }), name(function () { g.createPattern('x', 'repeat'); }), name(function () { g.createPattern(empty, 'repeat'); }),
                name(function () { g.createPattern(tile, 'bogus'); }), name(function () { g.createPattern(tile, undefined); })].join(',');
            var pattern = g.createPattern(tile, 'no-repeat');
            pattern.setTransform(new DOMMatrix([1, 0, 0, 1, 20, 0]));
            g.fillStyle = pattern; g.fillRect(0, 0, 40, 20);
            var bad = name(function () { pattern.setTransform({ a: 1, m11: 2 }); });
            Log.info(errors + ' ' + px(2, 2) + ' ' + px(22, 2) + ' ' + bad + ' ' + (g.createPattern(tile, '') !== null) + ' ' + (g.createPattern(tile, null) !== null));
            """);

        Assert.Equal(["TypeError,TypeError,InvalidStateError,SyntaxError,SyntaxError 0,0,0,0 0,255,0,255 TypeError true true"], log);
    }

    [Fact]
    public async Task AnOffscreenCanvas_HasItsOwnContextInterface_TakesEnforcedRangeSizes_AndRejectsOtherContextTypes()
    {
        var log = await LogAsync("""
            var o = new OffscreenCanvas(10, 10), x = o.getContext('2d');
            Log.info([Object.getPrototypeOf(x) === OffscreenCanvasRenderingContext2D.prototype, x instanceof CanvasRenderingContext2D, typeof x.fillRect,
                name(function () { o.getContext('bogus'); }), name(function () { o.getContext('2D'); }), name(function () { o.width = '100em'; }), name(function () { o.height = -1; }),
                name(function () { o.width = 20.9; }), o.width, c.getContext('webgl')].join(' '));
            """);

        Assert.Equal(["true false function TypeError TypeError TypeError TypeError none 20 "], log);
    }

    [Fact]
    public async Task PuttingPixelsBackAtAFractionalPosition_ChangesNothing()
    {
        var log = await LogAsync("""
            for (var i = 0; i < 256; i++) { g.fillStyle = 'rgba(' + i + ',' + (Math.floor(i * 1.5) % 256) + ',' + (Math.floor(i * 23.3) % 256) + ',' + (i / 256) + ')'; g.fillRect(i % 16, Math.floor(i / 16), 1, 1); }
            var a = g.getImageData(0.1, 0.2, 15.8, 15.9), before = Array.from(a.data);
            g.putImageData(a, 0.1, 0.2);
            var b = g.getImageData(0.1, 0.2, 15.8, 15.9);
            Log.info(before.filter(function (v, k) { return v !== b.data[k]; }).length + ' of ' + before.length);
            """);

        Assert.Equal(["0 of 900"], log);
    }
}

/// <summary>ImageData in 16-bit float, and canvases in another color space, as far as an 8-bit sRGB canvas can hold them.</summary>
public class CanvasFloatPixelsTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task AnImageDataInFloat16_HasItsOwnArrayAndFormat_AndTheFormatMustMatchTheData()
    {
        var log = await LogAsync("""
            var made = new ImageData(2, 2, { pixelFormat: 'rgba-float16' });
            var own = new Float16Array(8), wrapped = new ImageData(own, 2, 1, { pixelFormat: 'rgba-float16' });
            made.data[0] = 1024; made.data[1] = -1;
            Log.info([made.pixelFormat, Object.prototype.toString.call(made.data), made.data.length, made.data[0], made.data[1], wrapped.data === own,
                name(function () { new ImageData(own, 2, 1); }), name(function () { new ImageData(new Uint8ClampedArray(8), 2, 1, { pixelFormat: 'rgba-float16' }); }),
                name(function () { new ImageData(2, 2, { pixelFormat: 'float16' }); })].join(' '));
            """);

        Assert.Equal(["rgba-float16 [object Float16Array] 16 1024 -1 true InvalidStateError InvalidStateError TypeError"], log);
    }

    [Fact]
    public async Task Float16Pixels_RoundTripThroughACanvas_InAnotherColorSpaceToo()
    {
        var log = await LogAsync("""
            g.fillStyle = '#ff8000'; g.fillRect(0, 0, 40, 20);
            var srgb = g.getImageData(1, 1, 1, 1, { pixelFormat: 'rgba-float16' }).data;
            var p3 = g.getImageData(1, 1, 1, 1, { pixelFormat: 'rgba-float16', colorSpace: 'display-p3' }).data;
            g.clearRect(0, 0, 40, 20);
            g.putImageData(new ImageData(new Float16Array([1, 0.5, 0, 1]), 1, 1, { pixelFormat: 'rgba-float16' }), 3, 3);
            Log.info([srgb[0], srgb[1].toFixed(2), srgb[2], p3[0].toFixed(2) !== '1.00', Array.from(g.getImageData(3, 3, 1, 1).data).join(',')].join(' '));
            """);

        Assert.Equal(["1 0.50 0 true 255,128,0,255"], log);
    }

    [Fact]
    public async Task ADisplayP3Unorm8Canvas_KeepsWhatIsPutOnItAtP3sEightBitPrecision()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d', { colorSpace: 'display-p3' });
            g.putImageData(new ImageData(new Uint8ClampedArray([5, 250, 128, 255]), 1, 1, { colorSpace: 'srgb' }), 0, 0);
            Log.info(Array.from(g.getImageData(0, 0, 1, 1, { colorSpace: 'srgb' }).data).join(','));
            """, "width=4 height=4");

        Assert.Equal(["9,250,128,255"], log);
    }
}

/// <summary>createImageBitmap's crop and options, and media elements, which have no decoder.</summary>
public class CreateImageBitmapTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function px(ctx, x, y) { return Array.from(ctx.getImageData(x, y, 1, 1).data).join(','); } " +
            "function pixelsOf(bitmap) { var k = document.createElement('canvas'); k.width = bitmap.width; k.height = bitmap.height; var kc = k.getContext('2d'); kc.drawImage(bitmap, 0, 0); return kc; } " +
            "var data = new ImageData(new Uint8ClampedArray([255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,0,255]), 2, 2); " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task ACropPastTheImage_IsTransparentThere_AndFlipYTurnsTheResultOver()
    {
        var log = await LogAsync("""
            Promise.all([createImageBitmap(data, -1, -1, 3, 3), createImageBitmap(data, { imageOrientation: 'flipY' }), createImageBitmap(data, { resizeWidth: 4, resizeHeight: 4, resizeQuality: 'pixelated' })]).then(function (r) {
                var outside = pixelsOf(r[0]), flipped = pixelsOf(r[1]), big = pixelsOf(r[2]);
                Log.info([r[0].width + 'x' + r[0].height, px(outside, 0, 0), px(outside, 1, 1), px(flipped, 0, 0), px(flipped, 0, 1), r[2].width + 'x' + r[2].height, px(big, 3, 3)].join(' '));
            });
            """);

        Assert.Equal(["3x3 0,0,0,0 255,0,0,255 0,0,255,255 255,0,0,255 4x4 255,255,0,255"], log);
    }

    [Fact]
    public async Task BadArguments_RejectWithTheErrorTheyDeserve()
    {
        var log = await LogAsync("""
            var names = [];
            function note(p) { return p.then(function () { names.push('ok'); }, function (e) { names.push(e.name); }); }
            note(createImageBitmap(data, 0, 0, 0, 10)).then(function () { return note(createImageBitmap(data, { resizeWidth: 0, resizeHeight: 5 })); })
                .then(function () { return note(createImageBitmap(data, { imageOrientation: 'sideways' })); }).then(function () { return note(createImageBitmap(data, 4294967400, 10, 10, 10)); })
                .then(function () { return note(createImageBitmap(null)); }).then(function () { Log.info(names.join(',')); });
            """);

        Assert.Equal(["RangeError,InvalidStateError,TypeError,ok,TypeError"], log);
    }

    [Fact]
    public async Task AVideoWithASource_FailsToLoad_SoAScriptWaitingForItHearsAboutIt()
    {
        var log = await LogAsync("""
            var video = document.createElement('video');
            video.onerror = function () { Log.info('error ' + video.networkState + ' ' + video.error.code + ' ' + video.readyState); };
            video.src = 'movie.mp4';
            """);

        Assert.Equal(["error 3 4 0"], log);
    }
}

/// <summary>ImageBitmapRenderingContext, which takes a bitmap from the script and shows it.</summary>
public class BitmapRendererTests
{
    [Fact]
    public async Task TransferFromImageBitmap_ShowsTheBitmap_DetachesIt_AndTheCanvasHasOnlyOneKindOfContext()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c');
            var data = new ImageData(new Uint8ClampedArray([0,255,0,255]), 1, 1);
            var shown = document.createElement('canvas'); shown.width = 2; shown.height = 2;
            var renderer = shown.getContext('bitmaprenderer');
            createImageBitmap(data).then(function (bitmap) {
                renderer.transferFromImageBitmap(bitmap);
                var copy = document.createElement('canvas'); copy.width = 2; copy.height = 2;
                var k = copy.getContext('2d'); k.drawImage(shown, 0, 0);
                var closed = (function () { try { renderer.transferFromImageBitmap(bitmap); return 'none'; } catch (e) { return e.name; } })();
                Log.info([Array.from(k.getImageData(0, 0, 1, 1).data).join(','), bitmap.width, closed, shown.getContext('2d'), renderer.canvas === shown, renderer instanceof ImageBitmapRenderingContext].join(' '));
            });
            """, "width=4 height=4");

        Assert.Equal(["0,255,0,255 0 InvalidStateError  true true"], log);
    }
}

/// <summary>Smaller gaps: canvas sources, pattern strokes, root font units, no-alpha canvases and OffscreenCanvas transfers.</summary>
public class CanvasSmallGapTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } function px(ctx, x, y) { return Array.from(ctx.getImageData(x, y, 1, 1).data).join(','); } " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task ACanvasIsDrawnAsAPicture_WithItsSourceRectangleClipped_AndNearestPixelsWhenSmoothingIsOff()
    {
        var log = await LogAsync("""
            var source = document.createElement('canvas'); source.width = 4; source.height = 4;
            var s = source.getContext('2d'); s.fillStyle = '#00f'; s.fillRect(0, 0, 2, 4); s.fillStyle = '#f00'; s.fillRect(2, 0, 2, 4);
            g.imageSmoothingEnabled = false;
            g.drawImage(source, 0, 0, 4, 4, 0, 0, 20, 20);
            g.fillStyle = '#0f0'; g.fillRect(20, 0, 20, 20);
            g.drawImage(source, 2, 0, 4, 4, 20, 0, 20, 20);
            Log.info([px(g, 9, 5), px(g, 10, 5), px(g, 22, 5), px(g, 29, 5), px(g, 30, 5)].join(' '));
            """);

        Assert.Equal(["0,0,255,255 255,0,0,255 255,0,0,255 255,0,0,255 0,255,0,255"], log);
    }

    [Fact]
    public async Task AStrokeWithAPattern_DrawsThePatternAlongTheLine()
    {
        var log = await LogAsync("""
            var tile = document.createElement('canvas'); tile.width = 2; tile.height = 2;
            var t = tile.getContext('2d'); t.fillStyle = '#0f0'; t.fillRect(0, 0, 2, 2);
            g.strokeStyle = g.createPattern(tile, 'repeat'); g.lineWidth = 6;
            g.beginPath(); g.moveTo(0, 10); g.lineTo(40, 10); g.stroke();
            Log.info(px(g, 20, 10) + ' ' + px(g, 20, 2));
            """);

        Assert.Equal(["0,255,0,255 0,0,0,0"], log);
    }

    [Fact]
    public async Task ACanvasWithNoAlpha_IsBlackWhereNothingIsDrawn_AndTransferToImageBitmapStartsItAgain()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var o = new OffscreenCanvas(4, 4), x = o.getContext('2d', { alpha: false });
            x.lineWidth = 7;
            var first = Array.from(x.getImageData(0, 0, 1, 1).data).join(',');
            x.fillStyle = '#0f0'; x.fillRect(0, 0, 4, 4);
            var bitmap = o.transferToImageBitmap();
            var after = Array.from(x.getImageData(0, 0, 1, 1).data).join(',');
            var none = (function () { try { new OffscreenCanvas(1, 1).transferToImageBitmap(); return 'none'; } catch (e) { return e.name; } })();
            var placeholder = document.createElement('canvas'), moved = placeholder.transferControlToOffscreen();
            var twice = (function () { try { placeholder.transferControlToOffscreen(); return 'none'; } catch (e) { return e.name; } })();
            var context = (function () { try { placeholder.getContext('2d'); return 'none'; } catch (e) { return e.name; } })();
            Log.info([first, after, x.lineWidth, bitmap.width, none, moved.width, twice, context, o.getContext('webgl')].join(' '));
            """, "width=4 height=4");

        Assert.Equal(["0,0,0,255 0,0,0,255 7 4 InvalidStateError 300 InvalidStateError InvalidStateError "], log);
    }

    [Fact]
    public async Task RemIsTheRootElementsFontSize_NotTheCanvasElements()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            document.documentElement.style.fontSize = '5px'; c.style.fontSize = '20px';
            g.font = '2rem serif'; var rem = g.font;
            g.font = '2em serif'; Log.info(rem + ' | ' + g.font);
            """, "width=4 height=4");

        Assert.Equal(["10px serif | 40px serif"], log);
    }
}

/// <summary>Canvases of another color space and of floating-point precision: what is drawn is kept in the canvas's space, outside the sRGB gamut too.</summary>
public class CanvasWideGamutTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'); function f(a) { return Array.from(a).map(function (v) { return Math.round(v * 100) / 100; }).join(','); } " + script, "width=8 height=8");
        return log;
    }

    [Fact]
    public async Task ADisplayP3ColorOnAFloatSrgbCanvas_KeepsItsPlaceOutsideTheSrgbGamut()
    {
        var log = await LogAsync("""
            var g = c.getContext('2d', { colorType: 'float16', colorSpace: 'srgb' });
            g.fillStyle = 'color(display-p3 1 0 0)'; g.fillRect(0, 0, 8, 8);
            Log.info(f(g.getImageData(0, 0, 1, 1, { pixelFormat: 'rgba-float16', colorSpace: 'display-p3' }).data) + ' ' + f(g.getImageData(0, 0, 1, 1, { pixelFormat: 'rgba-float16' }).data)
                + ' ' + Array.from(g.getImageData(0, 0, 1, 1).data).join(','));
            """);

        Assert.Equal(["1,0,0,1 1.09,-0.23,-0.15,1 255,0,0,255"], log);
    }

    [Fact]
    public async Task APictureOfFloats_KeepsAValueAboveOneThroughAnImageBitmap_AndAnUnorm8CanvasKeepsItsOwnSpace()
    {
        var log = await LogAsync("""
            var g = c.getContext('2d', { colorType: 'float16' });
            var data = new ImageData(new Float16Array([2, 0.5, 0, 1]), 1, 1, { pixelFormat: 'rgba-float16' });
            var p3 = document.createElement('canvas').getContext('2d', { colorSpace: 'display-p3' });
            p3.fillStyle = '#ff0000'; p3.fillRect(0, 0, 1, 1);
            createImageBitmap(data).then(function (bitmap) {
                g.drawImage(bitmap, 0, 0);
                Log.info(f(g.getImageData(0, 0, 1, 1, { pixelFormat: 'rgba-float16' }).data) + ' ' + Array.from(p3.getImageData(0, 0, 1, 1).data).join(',') + ' '
                    + Array.from(p3.getImageData(0, 0, 1, 1, { colorSpace: 'srgb' }).data).join(','));
            });
            """);

        Assert.Equal(["2,0.5,0,1 234,51,35,255 255,0,0,255"], log);
    }
}

/// <summary>HTML in canvas: the children of a canvas with content="drawable" are laid out, painted on request and drawn onto its 2D context.</summary>
public class HtmlInCanvasTests
{
    private static async Task<List<string>> LogAsync(string body, string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'); var g = c.getContext('2d'); function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } } " + script, "width=4 height=4");
        return log;
    }

    private const string Setup = """
        var host = document.createElement('div');
        host.innerHTML = '<canvas id="k" width="60" height="40" content="drawable"><div id="kid" drawable style="width:30px;height:20px;background:#0f0"></div><div id="plain" style="width:5px;height:5px"></div></canvas>';
        document.body.appendChild(host);
        var k = document.getElementById('k'), kid = document.getElementById('kid'), kg = k.getContext('2d');
        function paint() { return new Promise(function (resolve) { k.addEventListener('paint', resolve, { once: true }); k.requestPaint(); }); }
        """;

    [Fact]
    public async Task ADrawableChild_HasASizeOnceTheCanvasHasPainted_AndIsDrawnWithItsOwnPixels()
    {
        var log = await LogAsync("", Setup + """
            var before = name(function () { k.getElementImageDefaultSize(kid); });
            paint().then(function (event) {
                var size = k.getElementImageDefaultSize(kid);
                kg.drawElementImage(kid, 5, 6);
                var pixel = Array.from(kg.getImageData(10, 10, 1, 1).data).join(',');
                var outside = Array.from(kg.getImageData(40, 10, 1, 1).data).join(',');
                Log.info([before, size.width, size.height, event instanceof CanvasPaintEvent, event.changedElements.length, pixel, outside, k.getElementTransform(kid).e, k.getElementTransform(kid).f].join(' '));
            });
            """);

        Assert.Equal(["InvalidStateError 30 20 true 0 0,255,0,255 0,0,0,0 5 6"], log);
    }

    [Fact]
    public async Task OnlyDrawableChildrenOfAContentDrawableCanvas_CanBeCapturedOrDrawn_AndAChangeIsReportedAsOne()
    {
        var log = await LogAsync("", Setup + """
            paint().then(function () {
                var plain = document.getElementById('plain');
                var errors = [name(function () { k.captureElementImage(plain); }), name(function () { kg.drawElementImage(plain, 0, 0); }), name(function () { k.updateElementGeometry(document.body); })].join(',');
                var image = k.captureElementImage(kid);
                var other = document.createElement('canvas').getContext('2d');
                var changed;
                k.onpaint = function (e) { changed = e.changedElements; };
                kid.style.height = '25px';
                new Promise(function (resolve) { requestAnimationFrame(function () { setTimeout(resolve, 0); }); }).then(function () {
                    Log.info([errors, 'width' in image, changed.length, changed[0] === kid, k.getElementImageDefaultSize(kid).height].join(' '));
                });
            });
            """);

        Assert.Equal(["InvalidStateError,InvalidStateError,InvalidStateError false 1 true 25"], log);
    }

    [Fact]
    public async Task AnElementWithGeometry_IsFoundWhereTheCanvasPutIt_AboveTheCanvas_AndIsClosedByClose()
    {
        var log = await LogAsync("", Setup + """
            paint().then(function () {
                kg.drawElementImage(kid, 0, 0);
                k.updateElementGeometry(kid, { canvasTransform: new DOMMatrix().translate(10, 10) });
                var box = k.getBoundingClientRect();
                var r = kid.getBoundingClientRect();
                var hit = document.elementsFromPoint(box.left + 15, box.top + 15).slice(0, 2).map(function (e) { return e.id || e.tagName; }).join('/');
                var miss = document.elementFromPoint(box.left + 55, box.top + 35).id;
                var image = k.captureElementImage(kid);
                image.close();
                Log.info([r.left - box.left, r.top - box.top, r.width, r.height, hit, miss, name(function () { kg.drawElementImage(image, 0, 0); })].join(' '));
            });
            """);

        Assert.Equal(["10 10 30 20 kid/k k InvalidStateError"], log);
    }

    [Fact]
    public async Task AClipLimitsWhereAPointerFindsAnElement_InTheSpaceOfItsTransform_AndRemovalPaintsTheCanvas()
    {
        var log = await LogAsync("", Setup + """
            paint().then(function () {
                var box = k.getBoundingClientRect();
                k.updateElementGeometry(kid, { canvasTransform: new DOMMatrix().translate(10, 10), canvasClip: { x: 0, y: 0, width: 10, height: 10 } });
                var at = function (x, y) { return document.elementsFromPoint(box.left + x, box.top + y)[0].id || document.elementsFromPoint(box.left + x, box.top + y)[0].tagName; };
                var inside = at(15, 15), outside = at(25, 25);
                k.updateElementGeometry(kid, { canvasClip: { x: 10, y: 10, width: -10, height: -10 } });
                var kept = at(15, 15);
                var removed = 0;
                k.onpaint = function () { removed++; };
                kid.remove();
                new Promise(function (resolve) { requestAnimationFrame(function () { setTimeout(resolve, 0); }); }).then(function () {
                    Log.info([inside, outside, kept, removed > 0].join(" "));
                });
            });
            """);

        Assert.Equal(["kid k kid true"], log);
    }

    [Fact]
    public async Task ACanvasInsideAnElement_IsDrawnAsWhatAScriptDrewOnIt_NotAsItsChildren()
    {
        var log = await LogAsync("", """
            var host = document.createElement('div');
            host.innerHTML = '<canvas id="outer" width="100" height="40" content="drawable"><div id="holder" drawable style="width:40px;height:40px"><canvas id="inner" width="40" height="40" content="drawable"><div id="part" drawable style="width:20px;height:20px;background:#00f"></div><div style="width:40px;height:10px;background:#f00"></div></canvas></div></canvas>';
            document.body.appendChild(host);
            var outer = document.getElementById('outer'), inner = document.getElementById('inner'), holder = document.getElementById('holder'), part = document.getElementById('part');
            var og = outer.getContext('2d'), ig = inner.getContext('2d');
            inner.onpaint = function () { ig.drawElementImage(part, 20, 20); };
            outer.onpaint = function () {
                og.drawElementImage(holder, 0, 0);
                var drawn = Array.from(og.getImageData(25, 25, 1, 1).data).join(',');
                var children = Array.from(og.getImageData(5, 5, 1, 1).data).join(',');
                Log.info(drawn + ' ' + children);
            };
            """);

        Assert.Equal(["0,0,255,255 0,0,0,0"], log);
    }

    [Fact]
    public async Task ATransferredOffscreenCanvas_ChangesGeometryInATask_AndTellsTheCanvasWhichElements()
    {
        var log = await LogAsync("", Setup.Replace("kg = k.getContext('2d');", "kg = null;") + """
            paint().then(function () {
                var offscreen = k.transferControlToOffscreen();
                var og = offscreen.getContext('2d');
                var image = k.captureElementImage(kid);
                var standalone = name(function () { new OffscreenCanvas(10, 10).getContext('2d').drawElementImage(kid, 0, 0); });
                k.addEventListener('elementgeometryupdate', function (event) {
                    var t = k.getElementTransform(kid);
                    Log.info([standalone, event instanceof ElementGeometryUpdateEvent, Object.isFrozen(event.elements), event.elements.length, event.elements[0] === kid, t.e, t.f].join(' '));
                }, { once: true });
                og.drawElementImage(image, 3, 4);
                offscreen.updateElementGeometry(image, { canvasTransform: new DOMMatrix().translate(7, 8) });
                var before = k.getElementTransform(kid).e;
                Log.info('before ' + before);
            });
            """);

        Assert.Equal(["before 0", "InvalidStateError true true 1 true 7 8"], log);
    }

    [Fact]
    public async Task ARangeOverEachOfThreeSurrogatePairsIsAWholeCharacterFurtherAlong()
    {
        var log = await LogAsync("", """
            var el = document.createElement('div'); el.textContent = '🏁🎶🏁'; el.style.font = '30px sans-serif'; document.body.appendChild(el);
            var xs = [0, 2, 4].map(function (i) {
                var r = document.createRange(); r.setStart(el.childNodes[0], i); r.setEnd(el.childNodes[0], i + 2);
                var rect = r.getClientRects()[0];
                return [rect.x, rect.width];
            });
            Log.info((xs[1][0] > xs[0][0] && xs[2][0] > xs[1][0] && Math.abs(xs[1][0] - xs[0][0] - xs[0][1]) < 0.5 && Math.abs(xs[2][0] - xs[1][0] - xs[1][1]) < 0.5).toString());
            """);

        Assert.Equal(["true"], log);
    }
}
