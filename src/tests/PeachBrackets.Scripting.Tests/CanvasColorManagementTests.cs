namespace PeachBrackets.Scripting.Tests;

/// <summary>What a canvas does with a file's own color space and orientation, and the files it writes (<c>toBlob</c>, <c>toDataURL</c>, <c>convertToBlob</c>).</summary>
public class CanvasColorManagementTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var (_, log) = await CanvasParityTests.PaintAsync("var c = document.getElementById('c'), g = c.getContext('2d'); " + script, "width=40 height=20");
        return log;
    }

    [Fact]
    public async Task APng_WrittenFromADisplayP3Canvas_CarriesItsSpace_SoItReadsBackAsTheSameColor()
    {
        var log = await LogAsync("""
            var p3 = document.createElement('canvas'); p3.width = 4; p3.height = 4;
            var pg = p3.getContext('2d', { colorSpace: 'display-p3' });
            pg.fillStyle = 'color(display-p3 1 0 0)'; pg.fillRect(0, 0, 4, 4);
            var url = p3.toDataURL();
            var img = new Image();
            img.onload = function () {
                var back = document.createElement('canvas'); back.width = 4; back.height = 4;
                var bg = back.getContext('2d', { colorSpace: 'display-p3' });
                bg.drawImage(img, 0, 0);
                var inP3 = Array.from(bg.getImageData(1, 1, 1, 1).data).join(',');
                var inSrgb = Array.from(bg.getImageData(1, 1, 1, 1, { colorSpace: 'srgb' }).data).join(',');
                Log.info(url.slice(0, 22) + ' ' + inP3 + ' ' + inSrgb);
            };
            img.src = url;
            """);

        Assert.Equal(["data:image/png;base64, 255,0,0,255 255,0,0,255"], log);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    [InlineData("image/png")]
    [InlineData("text/plain")]
    public async Task ToDataUrl_WritesTheTypeAsked_AndPngForOneItCannotWrite(string type)
    {
        var log = await LogAsync($$"""
            g.fillStyle = '#f80'; g.fillRect(0, 0, 40, 20);
            Log.info(c.toDataURL('{{type}}').slice(0, 30));
            """);

        var expected = type is "image/jpeg" or "image/webp" ? type : "image/png";
        Assert.Single(log);
        Assert.StartsWith($"data:{expected};base64,", log[0]);
    }

    [Fact]
    public async Task AFloatCanvasInLinearLight_KeepsItsPrecisionThroughAPngBlob()
    {
        var log = await LogAsync("""
            (async function () {
                var cv = document.createElement('canvas'); cv.width = 1024; cv.height = 1;
                var cx = cv.getContext('2d', { colorSpace: 'srgb-linear', colorType: 'float16' });
                for (var i = 0; i < 1024; i++) { var v = i / 1023; cx.fillStyle = 'color(srgb ' + v + ' ' + v + ' ' + v + ')'; cx.fillRect(i, 0, 1, 1); }
                var blob = await new Promise(function (resolve) { cv.toBlob(resolve, 'image/png'); });
                var url = URL.createObjectURL(blob);
                var img = new Image();
                await new Promise(function (resolve) { img.onload = resolve; img.src = url; });
                var dst = document.createElement('canvas'); dst.width = 1024; dst.height = 1;
                var dg = dst.getContext('2d', { colorSpace: 'srgb', colorType: 'float16' });
                dg.drawImage(img, 0, 0);
                var data = dg.getImageData(0, 0, 1024, 1, { pixelFormat: 'rgba-float16' }).data;
                var seen = new Set();
                for (var k = 0; k < 1024; k++) seen.add(data[4 * k]);
                Log.info(blob.type + ' ' + img.width + ' ' + (seen.size > 1000)); URL.revokeObjectURL(url);
            })();
            """);

        Assert.Equal(["image/png 1024 true"], log);
    }

[Fact]
    public async Task AFloatCanvasInLinearLight_IsWrittenAsJpegWithTheSrgbCurve_SoItKeepsItsSteps()
    {
        var log = await LogAsync("""
            (async function () {
                var cv = document.createElement('canvas'); cv.width = 1024; cv.height = 1;
                var cx = cv.getContext('2d', { colorSpace: 'srgb-linear', colorType: 'float16' });
                for (var i = 0; i < 1024; i++) { var v = i / 1023; cx.fillStyle = 'color(srgb ' + v + ' ' + v + ' ' + v + ')'; cx.fillRect(i, 0, 1, 1); }
                var blob = await new Promise(function (resolve) { cv.toBlob(resolve, 'image/jpeg'); });
                var url = URL.createObjectURL(blob);
                var img = new Image();
                await new Promise(function (resolve) { img.onload = resolve; img.src = url; });
                var dst = document.createElement('canvas'); dst.width = 1024; dst.height = 1;
                var dg = dst.getContext('2d', { colorSpace: 'srgb', colorType: 'float16' });
                dg.drawImage(img, 0, 0);
                var data = dg.getImageData(0, 0, 1024, 1, { pixelFormat: 'rgba-float16' }).data;
                var seen = new Set();
                for (var k = 0; k < 1024; k++) seen.add(data[4 * k]);
                Log.info(blob.type + ' ' + (seen.size > 220));
            })();
            """);

        Assert.Equal(["image/jpeg true"], log);
    }
}
