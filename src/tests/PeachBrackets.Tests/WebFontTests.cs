using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>Fonts a document brings with <c>@font-face</c>, through the raster context a host builds without a PDF.</summary>
public class WebFontTests
{
    private static string FontPath => Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf");

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string html, Func<Uri, ValueTask<RNetworkResponse?>>? loader)
    {
        var context = await TestDocument.NewContextAsync();
        context.ResourceLoader = loader;
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300, ViewportHeightPoints = 100 };
        options.SetMargins(0);
        var document = await HtmlDocument.LoadAsync(html, context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        return (context, document);
    }

    [Fact]
    public async Task AFontFetchedAsAStreamThatIsNotInMemory_IsRegisteredAndUsed()
    {
        // A response body that is neither a MemoryStream nor a FileStream is copied asynchronously.
        ValueTask<RNetworkResponse?> Loader(Uri uri) => uri.AbsoluteUri.EndsWith("web.ttf", StringComparison.Ordinal)
            ? new ValueTask<RNetworkResponse?>(new RNetworkResponse(new BufferedStream(File.OpenRead(FontPath)), new Dictionary<string, string[]> { ["Content-Type"] = ["font/ttf"] }))
            : default;

        var (context, document) = await LoadAsync(
            "<style>@font-face{font-family:WebSans;src:url(https://fonts.test/web.ttf)} p{font-family:WebSans;font-size:30pt;margin:0}</style><p>Hamburg</p>", Loader);
        using (document)
        {
            var canvas = await TestDocument.PaintAsync(context, document);
            Assert.True(TestDocument.HasInk(canvas, 0, 0, 200, 50), "the text is drawn");
        }
    }

    [Fact]
    public async Task AFontNamedLocally_IsRegisteredUnderTheFamilyTheDocumentGivesIt()
    {
        var (context, document) = await LoadAsync(
            "<style>@font-face{font-family:LocalSans;src:local('" + TestDocument.Family + "')} p{font-family:LocalSans;font-size:30pt;margin:0}</style><p>Hamburg</p>", null);
        using (document)
        {
            var canvas = await TestDocument.PaintAsync(context, document);
            Assert.True(TestDocument.HasInk(canvas, 0, 0, 200, 50), "the text is drawn");
        }
    }

    [Fact]
    public async Task AFontInAFormatTheContextCannotRead_IsLeftOut_AndTheTextStillDraws()
    {
        ValueTask<RNetworkResponse?> Loader(Uri uri) => new(new RNetworkResponse(new MemoryStream([1, 2, 3, 4]), new Dictionary<string, string[]> { ["Content-Type"] = ["font/otf"] }));

        var (context, document) = await LoadAsync(
            "<style>@font-face{font-family:Odd;src:url(https://fonts.test/odd.bin) format('embedded-opentype')} p{font-family:Odd,sans-serif;font-size:30pt;margin:0}</style><p>Hamburg</p>", Loader);
        using (document)
        {
            var canvas = await TestDocument.PaintAsync(context, document);
            Assert.True(TestDocument.HasInk(canvas, 0, 0, 200, 50), "the fallback text is drawn");
        }
    }
}
