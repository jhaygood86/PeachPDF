namespace PeachBrackets.Scripting.Tests;

/// <summary>Scenarios of the canvas host that the suite pinned by single calls only: encoders on every kind of canvas, the ways a bitmap is cut, scaled and turned.</summary>
public class CanvasCoverageTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); function name(p) { return p.then(function () { return 'resolved'; }, function (e) { return e.name; }); } function size(b) { return b.width + 'x' + b.height; } " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task ToDataURL_EncodesEveryKindOfCanvas_AndFallsBackToPngForATypeItCannotWrite()
    {
        var log = await LogAsync("""
            var bare = document.createElement('canvas'); bare.width = 3; bare.height = 2;
            var zero = document.createElement('canvas'); zero.width = 0;
            var p3 = document.createElement('canvas'); p3.width = 2; p3.height = 2;
            var pg = p3.getContext('2d', { colorSpace: 'display-p3' }); pg.fillStyle = 'color(display-p3 1 0 0)'; pg.fillRect(0, 0, 2, 2);
            var fl = document.createElement('canvas'); fl.width = 2; fl.height = 2;
            var fg = fl.getContext('2d', { colorSpace: 'display-p3', colorType: 'float16' }); fg.fillStyle = 'color(display-p3 0 1 0)'; fg.fillRect(0, 0, 2, 2);
            var head = function (u) { return u.slice(0, 22); };
            Log.info([head(bare.toDataURL()), zero.toDataURL(), head(bare.toDataURL('image/jpeg')).slice(0, 15), head(p3.toDataURL('image/gif')), head(fl.toDataURL('image/gif')), head(fl.toDataURL()), head(p3.toDataURL())].join(' '));
            """);

        Assert.Equal(["data:image/png;base64, data:, data:image/jpeg data:image/png;base64, data:image/png;base64, data:image/png;base64, data:image/png;base64,"], log);
    }

    [Fact]
    public async Task CreateImageBitmap_CutsScalesAndTurnsItsSource()
    {
        var log = await LogAsync("""
            var src = document.createElement('canvas'); src.width = 8; src.height = 4;
            var sg = src.getContext('2d'); sg.fillStyle = 'red'; sg.fillRect(0, 0, 4, 4); sg.fillStyle = 'blue'; sg.fillRect(4, 0, 4, 4);
            function pixel(bitmap, x, y) { var k = document.createElement('canvas'); k.width = bitmap.width; k.height = bitmap.height; var kg = k.getContext('2d'); kg.drawImage(bitmap, 0, 0); return Array.from(kg.getImageData(x, y, 1, 1).data).join(','); }
            Promise.all([
              createImageBitmap(src, { resizeWidth: 16 }).then(size),
              createImageBitmap(src, { resizeHeight: 2 }).then(size),
              createImageBitmap(src, 8, 4, -4, -4).then(function (b) { return size(b) + ' ' + pixel(b, 0, 0); }),
              createImageBitmap(src, 6, 0, 4, 4).then(function (b) { return size(b) + ' ' + pixel(b, 0, 0) + ' ' + pixel(b, 3, 0); }),
              createImageBitmap(src, { imageOrientation: 'flipY' }).then(function (b) { return pixel(b, 0, 0); }),
              createImageBitmap(src, { resizeWidth: 2, resizeHeight: 2, resizeQuality: 'pixelated' }).then(function (b) { return size(b) + ' ' + pixel(b, 0, 0) + ' ' + pixel(b, 1, 0); }),
              name(createImageBitmap(src, { resizeWidth: 100000, resizeHeight: 100000 })),
              name(createImageBitmap(src, 0, 0, 0, 4))
            ]).then(function (results) { Log.info(results.join(' | ')); });
            """);

        Assert.Equal(["16x8 | 4x2 | 4x4 0,0,255,255 | 4x4 0,0,255,255 0,0,0,0 | 255,0,0,255 | 2x2 255,0,0,255 0,0,255,255 | InvalidStateError | RangeError"], log);
    }

    [Fact]
    public async Task TheFontShorthandAndTheColorFunctions_ReadBackInTheirCanonicalForm()
    {
        var log = await LogAsync("""
            var out = [];
            ['ultra-condensed bold 10px serif', 'bolder 12px serif', 'lighter 12px serif', 'xx-small serif', 'italic small-caps 700 12px/1.5 "A B", serif', 'condensed 10px serif', '12px', 'normal normal normal 10px serif', 'larger serif', 'smaller serif', '12pt serif', '1em serif', '50% serif', 'inherit', 'bold bold 10px serif', 'oblique 10deg 10px serif', '10px "unterminated'].forEach(function (f) { g.font = '10px sans-serif'; g.font = f; out.push(f + ' => ' + g.font); });
            ['hsl(0.5turn 100% 50%)', 'hsl(3.14159rad 100% 50%)', 'hsl(200grad 100% 50%)', 'hsl(-60 100% 50%)', 'hsl(300 100% 50%)', 'hsl(240 100% 50%)', 'hsl(120 100% 50%)', 'hwb(0 0% 0%)', 'hsl(0 100% 50% / 50%)', 'hsl(0deg, 100%, 50%)', 'hsl(none 100% 50%)', 'hsl(abc 100% 50%)'].forEach(function (f) { g.fillStyle = '#000'; g.fillStyle = f; out.push(f + ' => ' + g.fillStyle); });
            Log.info(out.join(' ; '));
            """);
        string[] expected =
        [
            "ultra-condensed bold 10px serif => bold ultra-condensed 10px serif",
            "bolder 12px serif => bold 12px serif",
            "lighter 12px serif => 100 12px serif",
            "xx-small serif => 9px serif",
            "italic small-caps 700 12px/1.5 \"A B\", serif => italic small-caps bold 12px \"A B\", serif",
            "condensed 10px serif => condensed 10px serif",
            "12px => 10px sans-serif",
            "normal normal normal 10px serif => 10px serif",
            "larger serif => 19.2px serif",
            "smaller serif => 13.333333px serif",
            "12pt serif => 16px serif",
            "1em serif => 16px serif",
            "50% serif => 8px serif",
            "inherit => 10px sans-serif",
            "bold bold 10px serif => 10px sans-serif",
            "oblique 10deg 10px serif => oblique 10deg 10px serif",
            "10px \"unterminated => 10px sans-serif",
            "hsl(0.5turn 100% 50%) => #00ffff",
            "hsl(3.14159rad 100% 50%) => #00ffff",
            "hsl(200grad 100% 50%) => #00ffff",
            "hsl(-60 100% 50%) => #ff00ff",
            "hsl(300 100% 50%) => #ff00ff",
            "hsl(240 100% 50%) => #0000ff",
            "hsl(120 100% 50%) => #00ff00",
            "hwb(0 0% 0%) => #ff0000",
            "hsl(0 100% 50% / 50%) => rgba(255, 0, 0, 0.5)",
            "hsl(0deg, 100%, 50%) => #ff0000",
            "hsl(none 100% 50%) => #ff0000",
            "hsl(abc 100% 50%) => #000000",
        ];
        Assert.Equal(expected, log[0].Split(" ; "));
    }

    [Fact]
    public async Task AWideCanvas_ReadsBackItsFloatingPointPixels_AfterShapesGradientsAndPictures()
    {
        var log = await LogAsync("""
            var wide = document.createElement('canvas'); wide.width = 8; wide.height = 8;
            var wg = wide.getContext('2d', { colorSpace: 'display-p3', colorType: 'float16' });
            wg.fillStyle = 'color(display-p3 1 0 0)'; wg.fillRect(0, 0, 4, 8);
            var gradient = wg.createLinearGradient(0, 0, 8, 0); gradient.addColorStop(0, 'color(display-p3 0 1 0)'); gradient.addColorStop(1, 'blue');
            wg.fillStyle = gradient; wg.fillRect(4, 0, 4, 8);
            wg.globalAlpha = 0.5; wg.fillStyle = 'white'; wg.fillRect(0, 0, 8, 2);
            var data = wg.getImageData(0, 0, 8, 8, { pixelFormat: 'rgba-float16' }).data;
            var plain = wg.getImageData(0, 0, 8, 8).data;
            var copy = document.createElement('canvas'); copy.width = 8; copy.height = 8;
            var cg = copy.getContext('2d', { colorSpace: 'display-p3', colorType: 'float16' }); cg.drawImage(wide, 0, 0);
            var again = cg.getImageData(0, 7, 1, 1, { pixelFormat: 'rgba-float16' }).data;
            Log.info([data.length, data[(7 * 8) * 4 + 3], plain[(7 * 8) * 4] > 200, again[0] > 0.9 && again[3] === 1, wide.toDataURL().length > 40].join(' '));
            """);

        Assert.Equal(["256 1 true true true"], log);
    }

    [Fact]
    public async Task Text_IsPlacedByItsAlignmentBaselineAndDirection()
    {
        var log = await LogAsync("""
            var k = document.createElement('canvas'); k.width = 120; k.height = 60; document.body.appendChild(k);
            var kg = k.getContext('2d'); kg.font = '20px TestSans'; kg.fillStyle = 'black';
            function ink() {
              var d = kg.getImageData(0, 0, 120, 60).data, x0 = 999, x1 = -1, y0 = 999, y1 = -1;
              for (var y = 0; y < 60; y++) for (var x = 0; x < 120; x++) if (d[(y * 120 + x) * 4 + 3] > 40) { x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y); }
              return { x0: x0, x1: x1, y0: y0, y1: y1 };
            }
            function place(settings) { kg.clearRect(0, 0, 120, 60); for (var key in settings) kg[key] = settings[key]; kg.fillText('Hi', 60, 30); return ink(); }
            var left = place({ textAlign: 'left', textBaseline: 'alphabetic', direction: 'ltr' }), center = place({ textAlign: 'center' }), right = place({ textAlign: 'right' });
            var start = place({ textAlign: 'start', direction: 'rtl' }), end = place({ textAlign: 'end', direction: 'rtl' });
            kg.direction = 'ltr'; kg.textAlign = 'left';
            var top = place({ textBaseline: 'top' }), hanging = place({ textBaseline: 'hanging' }), middle = place({ textBaseline: 'middle' }), ideographic = place({ textBaseline: 'ideographic' }), bottom = place({ textBaseline: 'bottom' }), alphabetic = place({ textBaseline: 'alphabetic' });
            Log.info([left.x0 >= 59, center.x0 < left.x0 && center.x1 > 60, right.x1 <= 61, start.x0 >= 59 ? 'start-right' : 'start-left', end.x0 >= 59 ? 'end-right' : 'end-left',
              top.y0 > middle.y0, middle.y0 > bottom.y0, hanging.y0 >= top.y0 - 8, hanging.y0 > middle.y0 && ideographic.y0 > alphabetic.y0, bottom.y1 < alphabetic.y1].join(' '));
            """);

        Assert.Equal(["true true true start-left end-right true true true true true"], log);
    }

    [Fact]
    public async Task AnImageElementOfAnInlineSvg_LoadsWhenItsAddressIsSetOrChanged()
    {
        var log = await LogAsync("""
            var ns = 'http://www.w3.org/2000/svg';
            var svg = document.createElementNS(ns, 'svg'); svg.setAttribute('width', '20'); svg.setAttribute('height', '20');
            var picture = document.createElementNS(ns, 'image'); picture.setAttribute('width', '20'); picture.setAttribute('height', '20');
            var events = [];
            picture.addEventListener('load', function () { events.push('load'); });
            picture.addEventListener('error', function () { events.push('error'); });
            svg.appendChild(picture); document.body.appendChild(svg);
            picture.setAttribute('href', 'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%224%22 height=%224%22%3E%3Crect width=%224%22 height=%224%22 fill=%22red%22/%3E%3C/svg%3E');
            var other = document.createElementNS(ns, 'image'); other.setAttribute('href', 'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 width=%222%22 height=%222%22%3E%3C/svg%3E'); svg.appendChild(other);
            setTimeout(function () { Log.info(events.join(',')); }, 50);
            """);

        Assert.Equal(["load"], log);
    }

    [Fact]
    public async Task ShapedText_GivesItsRunsAndClusters_AndTheContextDrawsThemWhereTheySay()
    {
        var log = await LogAsync("""
            var k = document.createElement('canvas'); k.width = 160; k.height = 40; document.body.appendChild(k);
            var kg = k.getContext('2d'); kg.font = '16px TestSans'; kg.textBaseline = 'alphabetic';
            function inkColumns() { var d = kg.getImageData(0, 0, 160, 40).data, lo = 999, hi = -1; for (var y = 0; y < 40; y++) for (var x = 0; x < 160; x++) if (d[(y * 160 + x) * 4 + 3] > 40) { lo = Math.min(lo, x); hi = Math.max(hi, x); } return [lo, hi]; }
            var shaped = kg.shapeText('Hello there');
            var runs = shaped.glyphRuns(), clusters = shaped.glyphClusters(0, 5);
            kg.fillGlyphRun(runs[0], 10, 30);
            var drawn = inkColumns();
            kg.clearRect(0, 0, 160, 40); kg.strokeGlyphRuns(runs, 10, 30); var stroked = inkColumns();
            kg.clearRect(0, 0, 160, 40); kg.fillGlyphRuns(clusters, 10, 30); var viaClusters = inkColumns();
            kg.clearRect(0, 0, 160, 40); kg.strokeGlyphRun(clusters[0], 10, 30);
            var tm = kg.measureText('Hello'), cl = tm.getTextClusters({ align: 'center', baseline: 'top' });
            kg.clearRect(0, 0, 160, 40); kg.fillTextCluster(cl[0], 80, 20, { x: 5, y: 2, align: 'left', baseline: 'middle' }); var single = inkColumns();
            kg.strokeTextCluster(cl[1], 80, 20);
            var back = shaped.positionToAdvance(5), index = shaped.advanceToPosition(back + 1);
            var box = shaped.actualBoundingBox(0, 5), rects = shaped.selectionRects(0, 5);
            function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } }
            Log.info([runs.length, clusters.length, clusters[1].start, drawn[0] >= 10, drawn[1] < 160, stroked[0] >= 8, viaClusters[1] > viaClusters[0], single[0] >= 84, back > 30, index === 5, box.width > 20, rects.length >= 1,
              name(function () { shaped.positionToAdvance(99); }), name(function () { kg.fillGlyphRun({}, 0, 0); }), name(function () { kg.fillGlyphRuns({}, 0, 0); }), name(function () { kg.fillGlyphRuns([1], 0, 0); }),
              name(function () { kg.fillTextCluster(1, 0, 0); }), name(function () { kg.fillTextCluster(cl[0], 0, 0, { align: 'sideways' }); })].join(' '));
            """);

        Assert.Equal(["1 5 1 true true true true true true true true true IndexSizeError TypeError TypeError TypeError TypeError TypeError"], log);
    }

    [Fact]
    public async Task AnSvgImage_IsDrawnAtItsNaturalSizeThenScaled_OnSrgbAndWideCanvases()
    {
        var log = await LogAsync("""
            var svg = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="20" height="10"><rect width="10" height="10" fill="rgb(255,0,0)"/><rect x="10" width="10" height="10" fill="rgb(0,0,255)"/></svg>');
            var img = new Image();
            img.onload = function () {
              function px(ctx, x, y) { return Array.from(ctx.getImageData(x, y, 1, 1).data).join(','); }
              var a = document.createElement('canvas'); a.width = 40; a.height = 20; var ag = a.getContext('2d');
              ag.drawImage(img, 0, 0, 40, 20);
              var b = document.createElement('canvas'); b.width = 40; b.height = 20; var bg = b.getContext('2d');
              bg.drawImage(img, 10, 0, 10, 10, 0, 0, 40, 20);
              var w = document.createElement('canvas'); w.width = 40; w.height = 20; var wg = w.getContext('2d', { colorSpace: 'display-p3', colorType: 'float16' });
              wg.drawImage(img, 0, 0, 40, 20);
              var s = document.createElement('canvas'); s.width = 40; s.height = 20; var sg = s.getContext('2d', { colorSpace: 'display-p3' });
              sg.drawImage(img, 0, 0, 40, 20);
              var empty = document.createElement('canvas'); empty.width = 40; empty.height = 20; var eg = empty.getContext('2d'); eg.drawImage(img, 0, 0, 0, 0);
              Log.info([px(ag, 5, 10), px(ag, 35, 10), px(bg, 5, 10), px(bg, 35, 10), px(wg, 5, 10).split(',')[3], px(sg, 35, 10).split(',')[3], px(eg, 5, 5)].join(' '));
            };
            img.src = svg;
            """);

        Assert.Equal(["255,0,0,255 0,0,255,255 0,0,255,255 0,0,255,255 255 255 0,0,0,0"], log);
    }
}
