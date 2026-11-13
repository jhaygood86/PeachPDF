using PeachBrackets;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// A host with no base address of its own (a <see cref="RasterRenderContext"/> has none) still gets its stylesheets: they resolve against the
/// document's <c>&lt;base href&gt;</c>, and a relative reference with nothing to resolve it against is skipped instead of failing the load.
/// </summary>
public class StylesheetBaseTests
{
    private const string Box = "<div style='width:100pt;height:50pt'></div>";

    private static async Task<(byte R, byte G, byte B)> PaintedAsync(string html, Dictionary<string, string> files, List<string> requested)
    {
        var context = new RasterRenderContext
        {
            ResourceLoader = uri =>
            {
                requested.Add(uri.AbsoluteUri);
                return ValueTask.FromResult<RNetworkResponse?>(files.TryGetValue(uri.AbsoluteUri, out var css)
                    ? new RNetworkResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(css)), new() { ["Content-Type"] = ["text/css"] })
                    : null);
            },
        };
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen };
        options.SetMargins(0);

        using var document = await HtmlDocument.LoadAsync(html, context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        using var canvas = await TestDocument.PaintAsync(context, document);
        var (r, g, b, _) = TestDocument.Pixel(canvas, 10, 10);
        return (r, g, b);
    }

    [Fact]
    public async Task ALinkedStylesheet_IsLoadedFromTheBaseElementsAddress()
    {
        var requested = new List<string>();
        var files = new Dictionary<string, string> { ["http://host.test/css/s.css"] = "div { background: rgb(255, 0, 0) }" };

        var pixel = await PaintedAsync("<!DOCTYPE html><html><head><base href='http://host.test/css/'><link rel=stylesheet href='s.css'><style>html,body{margin:0}</style></head><body>" + Box + "</body></html>", files, requested);

        Assert.Equal((255, 0, 0), pixel);
        Assert.Contains("http://host.test/css/s.css", requested);
    }

    [Fact]
    public async Task AnImportInAStyleElement_IsLoadedFromTheBaseElementsAddress()
    {
        var requested = new List<string>();
        var files = new Dictionary<string, string> { ["http://host.test/css/imported.css"] = "div { background: rgb(0, 0, 255) }" };

        var pixel = await PaintedAsync("<!DOCTYPE html><html><head><base href='http://host.test/css/'><style>@import 'imported.css'; html,body{margin:0}</style></head><body>" + Box + "</body></html>", files, requested);

        Assert.Equal((0, 0, 255), pixel);
    }

    [Fact]
    public async Task ARelativeStylesheet_WithNoBaseAddressAnywhere_IsSkippedAndTheDocumentStillPaints()
    {
        var requested = new List<string>();

        var pixel = await PaintedAsync("<!DOCTYPE html><html><head><link rel=stylesheet href='s.css'><style>@import 'i.css'; html,body{margin:0} div{background:rgb(0,128,0)}</style></head><body>" + Box + "</body></html>", [], requested);

        Assert.Equal((0, 128, 0), pixel);
        Assert.Empty(requested); // nothing was asked for: a relative address cannot be fetched
    }

    [Fact]
    public async Task AMalformedBaseElement_IsIgnoredAndTheDocumentStillPaints()
    {
        var requested = new List<string>();

        var pixel = await PaintedAsync("<!DOCTYPE html><html><head><base href='http://[bad'><style>html,body{margin:0} div{background:rgb(0,128,0)}</style></head><body>" + Box + "</body></html>", [], requested);

        Assert.Equal((0, 128, 0), pixel);
    }
}
