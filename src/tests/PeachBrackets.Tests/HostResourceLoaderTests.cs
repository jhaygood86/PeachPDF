using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>A host that shows documents answers the document's requests for images with <see cref="HostResourceLoader"/>.</summary>
public class HostResourceLoaderTests
{
    private const string RedPng = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAIAAAAmkwkpAAAAEUlEQVR4nGP4z8AARwgWXg4ArpMP8aaUSCMAAAAASUVORK5CYII=";

    [Fact]
    public async Task ADataUri_IsLoadedWithItsMimeType()
    {
        var response = await HostResourceLoader.LoadAsync(new RUri("data:image/png;base64," + RedPng));

        Assert.NotNull(response?.ResourceStream);
        Assert.Equal("image/png", response!.ResponseHeaders!["Content-Type"][0]);
        Assert.True(response.ResourceStream!.Length > 20);
    }

    [Fact]
    public async Task AFile_IsLoaded_AndAMissingOneIsNot()
    {
        var path = Path.Combine(Path.GetTempPath(), "host-loader-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, "hello", TestContext.Current.CancellationToken);
        try
        {
            using var response = (await HostResourceLoader.LoadAsync(new RUri(new Uri(path))))!.ResourceStream;
            Assert.Equal("hello", await new StreamReader(response!).ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Null(await HostResourceLoader.LoadAsync(new RUri(new Uri(path))));
    }

    [Fact]
    public async Task AWebAddress_IsNotFetchedUnlessTheHostAllowsIt()
    {
        Assert.Null(await HostResourceLoader.LoadAsync(new RUri("https://example.invalid/a.png")));
        Assert.Null(await HostResourceLoader.LoadAsync(new RUri(new Uri("file:///x.png")), allowLocalFiles: false));
    }

    [Fact]
    public async Task AnImageInADocument_IsDrawn()
    {
        // The context the way the views make theirs: it loads what the document refers to through the host loader.
        var context = new RasterRenderContext { ResourceLoader = uri => HostResourceLoader.LoadAsync(uri) };
        await using (var font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")))
            await context.AddFont(font, TestDocument.Family);

        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 100, ViewportHeightPoints = 60 };
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap($"<img src='data:image/png;base64,{RedPng}' style='display:block;width:20pt;height:20pt'>"), context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        var canvas = await TestDocument.PaintAsync(context, document);

        var (r, g, b, _) = TestDocument.Pixel(canvas, 10, 10);
        Assert.True(r > 200 && g < 60 && b < 60, $"pixel was {r},{g},{b}");
    }
}
