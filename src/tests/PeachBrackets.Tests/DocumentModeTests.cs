using PeachBrackets;
using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>
/// The mode a document is in, as a host sees it: the DOCTYPE decides, <see cref="HtmlDocumentOptions.DocumentMode"/> overrides it, and the first
/// paragraph of the body, which has no top margin in quirks mode, shows which one was laid out.
/// </summary>
public class DocumentModeTests
{
    private const string Page = "<html><body style='margin:0;font-size:12pt'><p id='p'>text</p></body></html>";

    private static async Task<(DocumentMode Mode, double Top)> LoadAsync(string html, DocumentMode? mode = null)
    {
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300, DocumentMode = mode };
        options.SetMargins(0);

        using var document = await HtmlDocument.LoadAsync(html, context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);

        return (document.DocumentMode, document.GetElementBounds("p")!.Value.Y);
    }

    [Fact]
    public async Task ADocumentWithADoctypeIsInNoQuirksMode()
    {
        var (mode, top) = await LoadAsync("<!DOCTYPE html>" + Page);

        Assert.Equal(DocumentMode.NoQuirks, mode);
        Assert.Equal(12.0, top, 0.5);
    }

    [Fact]
    public async Task ADocumentWithNoDoctypeIsInQuirksMode()
    {
        var (mode, top) = await LoadAsync(Page);

        Assert.Equal(DocumentMode.Quirks, mode);
        Assert.Equal(0.0, top, 0.5);
    }

    [Fact]
    public async Task ATransitionalXhtmlDoctypeIsInLimitedQuirksMode()
    {
        var doctype = "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">";
        var (mode, top) = await LoadAsync(doctype + Page);

        Assert.Equal(DocumentMode.LimitedQuirks, mode);
        Assert.Equal(12.0, top, 0.5);
    }

    [Theory]
    [InlineData(DocumentMode.NoQuirks, 12.0)]
    [InlineData(DocumentMode.LimitedQuirks, 12.0)]
    [InlineData(DocumentMode.Quirks, 0.0)]
    public async Task TheOptionOverridesTheDoctype(DocumentMode forced, double expectedTop)
    {
        var (mode, top) = await LoadAsync(forced == DocumentMode.Quirks ? "<!DOCTYPE html>" + Page : Page, forced);

        Assert.Equal(forced, mode);
        Assert.Equal(expectedTop, top, 0.5);
    }
}
