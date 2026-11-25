namespace PeachBrackets.Scripting.Tests;

/// <summary>Images a script makes or points at: load and error events, size, completeness and decode().</summary>
public class ImageTests
{
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static async Task<List<string>> LogAsync(string script, string body = "")
    {
        var logger = new CollectingLogger();
        var host = new JintScriptHost(new JintScriptOptions { Logger = logger });
        var context = await TestDocument.NewContextAsync();
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap(body + "<script>" + script + "</script>"),
            context,
            new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ScriptHost = host, MeasuringCanvasFactory = () => context.CreateCanvas(1, 1) });
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return logger.Texts;
    }

    [Fact]
    public async Task ANewImageLoadsItsSourceAndFiresLoad()
    {
        var log = await LogAsync($$"""
            var img = new Image(5, 6);
            console.log([img.tagName, img instanceof HTMLImageElement, img.complete, img.naturalWidth, img.width, img.height].join());
            img.onload = function () { console.log(['load', img.complete, img.naturalWidth, img.naturalHeight, img.currentSrc.indexOf('data:image/png') === 0, img.width].join()); };
            img.addEventListener('error', function () { console.log('error'); });
            img.src = '{{Png}}';
            console.log(img.complete);
            """);

        Assert.Equal(["IMG,true,true,0,5,6", "false", "load,true,1,1,true,5"], log);
    }

    [Fact]
    public async Task ABrokenImageFiresError_AndDecodeRejects()
    {
        var log = await LogAsync("""
            var img = document.createElement('img');
            img.onerror = function () { console.log('error ' + img.complete + ' ' + img.naturalWidth); };
            img.onload = function () { console.log('load'); };
            img.setAttribute('src', 'data:image/png;base64,AAAA');
            img.decode().then(function () { console.log('decoded'); }, function (e) { console.log('rejected ' + e.name); });
            """);

        Assert.Equal(["error true 0", "rejected EncodingError"], log);
    }

    [Fact]
    public async Task AnImageInThePageReportsItsSizeAndDecodes()
    {
        var log = await LogAsync("""
            var img = document.getElementById('p');
            console.log(img.complete);
            img.decode().then(function () { console.log([img.complete, img.naturalWidth, img.naturalHeight].join()); });
            """, $"<img id=p src=\"{Png}\">");

        Assert.Equal(["false", "true,1,1"], log);
    }
}
