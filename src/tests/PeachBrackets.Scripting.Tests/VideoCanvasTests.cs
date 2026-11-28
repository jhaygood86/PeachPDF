using PeachBrackets.Media;

namespace PeachBrackets.Scripting.Tests;

/// <summary>A <c>video</c> as the source of <c>drawImage</c>, <c>createPattern</c> and <c>createImageBitmap</c>.</summary>
public class VideoCanvasTests
{
    private const string Setup = """
        var v = document.createElement('video'); v.setAttribute('crossorigin', 'anonymous');
        var c = document.createElement('canvas'); c.width = 40; c.height = 20;
        function px(g, x, y) { return Array.from(g.getImageData(x, y, 1, 1).data).join(','); }
        function name(f) { try { f(); return 'none'; } catch (e) { return e.name; } }
        """;

    private static Task<(List<string> Log, HtmlDocument Document)> RunAsync(string script, MediaElementTests.FakeProvider provider) =>
        MediaElementTests.RunAsync(Setup + script, provider);

    [Fact]
    public async Task DrawImage_DrawsThePictureTheVideoShowsNow_AtTheSizeOfTheVideo()
    {
        var provider = new MediaElementTests.FakeProvider();
        var (log, document) = await RunAsync("""
            var g = c.getContext('2d');
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { g.drawImage(v, 0, 0); Log.info(px(g, 5, 5) + ' ' + px(g, 39, 19) + ' ' + v.videoWidth + 'x' + v.videoHeight); }, 100);
            """, provider);
        using var _ = document;

        Assert.Equal(["255,0,0,255 255,0,0,255 40x20"], log);

        // The frame is a lease: given back as soon as its picture was copied.
        Assert.All(provider.Sessions, s => Assert.Equal(s.FramesLeased, s.FramesReleased));
        Assert.True(provider.Sessions[0].FramesLeased > 0);
    }

    [Fact]
    public async Task DrawImage_FollowsThePlayingVideo()
    {
        var provider = new MediaElementTests.FakeProvider { Color = position => position < TimeSpan.FromSeconds(1) ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255) };
        var (log, document) = await RunAsync("""
            var g = c.getContext('2d');
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { v.play(); g.drawImage(v, 0, 0); Log.info(px(g, 5, 5)); }, 100);
            setTimeout(function () { g.drawImage(v, 0, 0); Log.info(px(g, 5, 5)); }, 1600);
            """, provider);
        using var _ = document;

        Assert.Equal(["255,0,0,255", "0,0,255,255"], log);
    }

    [Fact]
    public async Task AVideoWithNoPictureYet_DrawsNothing_HasNoPattern_AndCannotMakeABitmap()
    {
        var (log, document) = await RunAsync("""
            var g = c.getContext('2d');
            var results = [];
            results.push(name(function () { g.drawImage(v, 0, 0); }));
            results.push(String(g.createPattern(v, 'repeat')));
            results.push(px(g, 5, 5));
            createImageBitmap(v).then(function () { results.push('resolved'); }, function (e) { results.push(e.name); Log.info(results.join(',')); });
            """, new MediaElementTests.FakeProvider());
        using var _ = document;

        Assert.Equal(["none,null,0,0,0,0,InvalidStateError"], log);
    }

    [Fact]
    public async Task AVideoThatCannotBePlayed_IsTheSameAsOneWithNoPictureYet()
    {
        var (log, document) = await RunAsync("""
            var g = c.getContext('2d');
            v.src = 'https://media.test/a.bad';
            setTimeout(function () { Log.info(name(function () { g.drawImage(v, 0, 0); }) + ' ' + g.createPattern(v, 'repeat') + ' ' + px(g, 5, 5)); }, 100);
            """, new MediaElementTests.FakeProvider());
        using var _ = document;

        Assert.Equal(["none null 0,0,0,0"], log);
    }

    [Fact]
    public async Task CreatePattern_FillsWithThePictureOfTheVideo()
    {
        var provider = new MediaElementTests.FakeProvider { Color = _ => ((byte)0, (byte)255, (byte)0) };
        var (log, document) = await RunAsync("""
            var g = c.getContext('2d');
            v.src = 'https://media.test/a.webm';
            setTimeout(function () {
                g.fillStyle = g.createPattern(v, 'repeat'); g.fillRect(0, 0, 40, 20);
                Log.info(px(g, 3, 3) + ' ' + px(g, 38, 18));
            }, 100);
            """, provider);
        using var _ = document;

        Assert.Equal(["0,255,0,255 0,255,0,255"], log);
    }

    [Fact]
    public async Task CreateImageBitmap_TakesAPictureOfTheVideo_ThatStaysWhenTheVideoMovesOn()
    {
        var provider = new MediaElementTests.FakeProvider { Color = position => position < TimeSpan.FromSeconds(1) ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255) };
        var (log, document) = await RunAsync("""
            var g = c.getContext('2d');
            v.src = 'https://media.test/a.webm';
            setTimeout(function () {
                createImageBitmap(v).then(function (bitmap) {
                    v.currentTime = 5;
                    setTimeout(function () { g.drawImage(bitmap, 0, 0); Log.info(bitmap.width + 'x' + bitmap.height + ' ' + px(g, 5, 5)); }, 100);
                });
            }, 100);
            """, provider);
        using var _ = document;

        Assert.Equal(["40x20 255,0,0,255"], log);
    }

    [Theory]
    [InlineData(MediaColorSpace.Srgb, "srgb", "srgb", "255,0,0,255")]
    [InlineData(MediaColorSpace.Srgb, "display-p3", "display-p3", "234,51,35,255")]
    [InlineData(MediaColorSpace.DisplayP3, "srgb", "srgb", "255,0,0,255")]
    [InlineData(MediaColorSpace.DisplayP3, "display-p3", "display-p3", "255,0,0,255")]
    [InlineData(MediaColorSpace.DisplayP3, "display-p3", "srgb", "255,0,0,255")]
    public async Task APictureInAnotherColorSpace_IsConvertedIntoTheCanvasSpace(MediaColorSpace frameSpace, string canvasSpace, string readSpace, string expected)
    {
        var provider = new MediaElementTests.FakeProvider { Space = frameSpace };
        var (log, document) = await RunAsync($$"""
            var g = c.getContext('2d', { colorSpace: '{{canvasSpace}}' });
            v.src = 'https://media.test/a.webm';
            setTimeout(function () {
                g.drawImage(v, 0, 0);
                Log.info(Array.from(g.getImageData(5, 5, 1, 1, { colorSpace: '{{readSpace}}' }).data).join(','));
            }, 100);
            """, provider);
        using var _ = document;

        Assert.Equal([expected], log);
    }

    [Fact]
    public async Task ARec2020Picture_StaysNeutral_AndIsLightenedByItsTransferFunction()
    {
        // Gray is gray in any space, but Rec. 2020's transfer function is not sRGB's: the same code value is a lighter gray on an sRGB canvas.
        var provider = new MediaElementTests.FakeProvider { Space = MediaColorSpace.Rec2020, Color = _ => ((byte)128, (byte)128, (byte)128) };
        var (log, document) = await RunAsync("""
            var g = c.getContext('2d');
            v.src = 'https://media.test/a.webm';
            setTimeout(function () { g.drawImage(v, 0, 0); var d = g.getImageData(5, 5, 1, 1).data; Log.info(Math.abs(d[0] - d[1]) <= 1 && Math.abs(d[1] - d[2]) <= 1 && d[0] > 130 && d[0] < 160); }, 100);
            """, provider);
        using var _ = document;

        Assert.Equal(["true"], log);
    }

    [Fact]
    public async Task AVideoFromAnotherOrigin_TaintsTheCanvas_UnlessItWasAskedForWithCrossorigin()
    {
        var (log, document) = await RunAsync("""
            function taints(crossorigin) {
                var video = document.createElement('video');
                if (crossorigin) video.setAttribute('crossorigin', 'anonymous');
                video.src = 'https://media.test/a.webm';
                return new Promise(function (resolve) {
                    setTimeout(function () {
                        var canvas = document.createElement('canvas'); canvas.width = 10; canvas.height = 10;
                        var g = canvas.getContext('2d');
                        g.drawImage(video, 0, 0);
                        resolve(name(function () { canvas.toDataURL(); }));
                    }, 100);
                });
            }
            taints(false).then(function (a) { return taints(true).then(function (b) { Log.info(a + ' ' + b); }); });
            """, new MediaElementTests.FakeProvider());
        using var _ = document;

        Assert.Equal(["SecurityError none"], log);
    }
}
