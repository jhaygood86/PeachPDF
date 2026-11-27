using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Scripting.Tests;

/// <summary>A resource that was redirected to another origin taints a canvas it is drawn on, whichever origin it was asked of.</summary>
public class RedirectTaintTests
{
    // A 1x1 red PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

    private static async Task<List<string>> RunAsync(string requested, string? finalAddress, string script)
    {
        var logger = new CollectingLogger();
        var context = new RasterRenderContext
        {
            DocumentAddress = new Uri("https://page.test/index.html"),
            ResourceLoader = uri => uri.Host == "other.test" || uri.Host == "page.test"
                ? new ValueTask<RNetworkResponse?>(new RNetworkResponse(new MemoryStream(Png), new Dictionary<string, string[]> { ["Content-Type"] = ["image/png"] })
                {
                    FinalAddress = uri.AbsoluteUri == requested && finalAddress is not null ? new Uri(finalAddress) : null,
                })
                : default,
        };
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 200,
            ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = logger }),
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        };
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync("<!DOCTYPE html><html><body><script>" + script + "</script></body></html>", context, options);
        Assert.Empty(logger.Errors);
        return logger.Texts;
    }

    private static string Script(string address) => $$"""
        var image = new Image();
        image.onload = function () {
            var c = document.createElement('canvas'); c.width = 4; c.height = 4;
            c.getContext('2d').drawImage(image, 0, 0);
            var result = 'none'; try { c.toDataURL(); } catch (e) { result = e.name; }
            Log.info(result);
        };
        image.src = '{{address}}';
        """;

    [Fact]
    public async Task AnImageAskedOfTheOwnOrigin_ButRedirectedToAnother_TaintsTheCanvas()
    {
        var log = await RunAsync("https://page.test/a.png", "https://other.test/a.png", Script("https://page.test/a.png"));

        Assert.Equal(["SecurityError"], log);
    }

    [Fact]
    public async Task AnImageAskedOfAnotherOrigin_ButRedirectedToTheOwn_DoesNotTaintIt()
    {
        var log = await RunAsync("https://other.test/a.png", "https://page.test/a.png", Script("https://other.test/a.png"));

        Assert.Equal(["none"], log);
    }

    [Fact]
    public async Task AnImageTheDocumentAlreadyLoaded_KeepsTheAddressItsRedirectEndedAt()
    {
        // The markup's <img> loads first and is cached; the script's Image asks for the same address and is answered from that cache.
        var log = await RunAsync("https://page.test/a.png", "https://other.test/a.png", """
            var markup = document.createElement('img');
            markup.src = 'https://page.test/a.png';
            markup.onload = function () {
                var image = new Image();
                image.onload = function () {
                    var c = document.createElement('canvas'); c.width = 4; c.height = 4;
                    c.getContext('2d').drawImage(image, 0, 0);
                    var result = 'none'; try { c.toDataURL(); } catch (e) { result = e.name; }
                    Log.info(result);
                };
                image.src = 'https://page.test/a.png';
            };
            """);

        Assert.Equal(["SecurityError"], log);
    }

    [Fact]
    public async Task AnImageThatWasNotRedirected_IsTaintedByTheOriginItCameFrom()
    {
        Assert.Equal(["none"], await RunAsync("https://page.test/a.png", null, Script("https://page.test/a.png")));
        Assert.Equal(["SecurityError"], await RunAsync("https://other.test/a.png", null, Script("https://other.test/a.png")));
    }
}
