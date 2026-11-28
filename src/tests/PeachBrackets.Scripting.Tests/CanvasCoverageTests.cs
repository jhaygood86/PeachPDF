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
}
