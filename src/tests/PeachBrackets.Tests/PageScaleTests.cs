namespace PeachBrackets.Tests;

/// <summary>
/// <c>HtmlDocumentOptions.PageScale</c>: the document lays itself out on a sheet that many times larger, margins and an <c>@page</c> rule's size, margins, border and
/// padding included, so a host that draws each page at the inverse scale holds that much more on a page of the configured size.
/// </summary>
public class PageScaleTests
{
    private static HtmlDocumentOptions Options(double scale) => new()
    {
        PageWidthPoints = 600,
        PageHeightPoints = 800,
        MarginTopPoints = 10,
        MarginRightPoints = 10,
        MarginBottomPoints = 10,
        MarginLeftPoints = 10,
        PageScale = scale,
    };

    private static async Task<HtmlDocument> LoadAsync(string head, double scale, string body = "<p>text</p>")
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, head), Options(scale));
        return document;
    }

    [Fact]
    public async Task TheConfiguredSheetAndMarginsAreMultiplied()
    {
        using var plain = await LoadAsync("", 1);
        using var scaled = await LoadAsync("", 2);

        Assert.Equal(600, plain.Pages[0].WidthPoints, 3);
        Assert.Equal(1200, scaled.Pages[0].WidthPoints, 3);
        Assert.Equal(1600, scaled.Pages[0].HeightPoints, 3);

        // Margins of 10 become 20 on a sheet twice the size, so the content area is the doubled sheet less the doubled margins: exactly twice as large.
        Assert.Equal(plain.ContentArea.Width * 2, scaled.ContentArea.Width, 3);
        Assert.Equal(plain.ContentArea.Height * 2, scaled.ContentArea.Height, 3);
    }

    [Fact]
    public async Task APageRulesSizeAndMarginsAreMultipliedToo()
    {
        const string rule = "<style>@page { size: 300pt 400pt; margin: 20pt 30pt }</style>";
        using var plain = await LoadAsync(rule, 1);
        using var scaled = await LoadAsync(rule, 3);

        Assert.Equal(300, plain.Pages[0].WidthPoints, 3);
        Assert.Equal(900, scaled.Pages[0].WidthPoints, 3);
        Assert.Equal(1200, scaled.Pages[0].HeightPoints, 3);
        Assert.Equal(plain.ContentArea.Width * 3, scaled.ContentArea.Width, 3);
        Assert.Equal(plain.ContentArea.Height * 3, scaled.ContentArea.Height, 3);
    }

    [Fact]
    public async Task APerPageRuleIsMultipliedForThePagesItAppliesTo()
    {
        // :first changes the sheet's own size and margins for page one only; the scale reaches what the rule declares, not only the base page.
        const string rules = "<style>@page { size: 300pt 400pt; margin: 20pt } @page :first { size: 200pt 300pt; margin: 10pt }</style>";
        const string body = "<p>one</p><p style=\"break-before: page\">two</p>";
        using var plain = await LoadAsync(rules, 1, body);
        using var scaled = await LoadAsync(rules, 2, body);

        Assert.Equal(2, plain.Pages.Count);
        Assert.Equal(plain.Pages.Count, scaled.Pages.Count);
        for (var i = 0; i < plain.Pages.Count; i++)
        {
            Assert.Equal(plain.Pages[i].WidthPoints * 2, scaled.Pages[i].WidthPoints, 3);
            Assert.Equal(plain.Pages[i].HeightPoints * 2, scaled.Pages[i].HeightPoints, 3);
        }
    }

    [Fact]
    public async Task AnOrientationKeywordRotatesTheScaledSheet()
    {
        const string rule = "<style>@page :first { size: landscape }</style>";
        using var plain = await LoadAsync(rule, 1);
        using var scaled = await LoadAsync(rule, 2);

        Assert.Equal(plain.Pages[0].WidthPoints * 2, scaled.Pages[0].WidthPoints, 3);
        Assert.Equal(plain.Pages[0].HeightPoints * 2, scaled.Pages[0].HeightPoints, 3);
        Assert.True(scaled.Pages[0].WidthPoints > scaled.Pages[0].HeightPoints, "landscape is wider than tall");
    }

    [Fact]
    public async Task APageBoxsBorderAndPaddingAreMultipliedToo()
    {
        const string rule = "<style>@page { margin: 10pt; border: 4pt solid black; padding: 6pt }</style>";
        using var plain = await LoadAsync(rule, 1);
        using var scaled = await LoadAsync(rule, 2);

        Assert.Equal(plain.ContentArea.Width * 2, scaled.ContentArea.Width, 3);
        Assert.Equal(plain.ContentArea.Height * 2, scaled.ContentArea.Height, 3);
    }

    [Fact]
    public async Task ViewportUnitsSeeTheLargerSheet()
    {
        const string body = "<div id=\"d\" style=\"width: 50vw; height: 10pt\"></div>";
        using var plain = await LoadAsync("", 1, body);
        using var scaled = await LoadAsync("", 2, body);

        Assert.Equal(plain.GetElementBounds("d")!.Value.Width * 2, scaled.GetElementBounds("d")!.Value.Width, 2);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-2d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task ANonsensicalScaleIsTakenAsOne(double scale)
    {
        using var plain = await LoadAsync("", 1);
        using var odd = await LoadAsync("", scale);

        Assert.Equal(plain.Pages[0].WidthPoints, odd.Pages[0].WidthPoints, 3);
        Assert.Equal(plain.ContentArea.Width, odd.ContentArea.Width, 3);
    }

    [Fact]
    public async Task AScreenDocumentHasNoSheetToScale()
    {
        var options = Options(2);
        options.MediaType = MediaType.Screen;
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>text</p>"), options);
        using var _ = document;

        Assert.Equal(600, document.Pages[0].WidthPoints, 3);
    }

    [Fact]
    public async Task AWiderSheetFitsWhatTheConfiguredOneDoesNot()
    {
        const string body = "<div style=\"width: 900pt; height: 10pt\">wide</div>";
        using var plain = await LoadAsync("", 1, body);
        using var scaled = await LoadAsync("", 2, body);

        Assert.True(plain.ContentExtent.Width > plain.ContentArea.Width, "900pt overflows a 580pt content area");
        Assert.True(scaled.ContentExtent.Width <= scaled.ContentArea.Width, "and fits in a 1160pt one");
    }
}
