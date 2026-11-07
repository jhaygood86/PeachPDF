using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.CSS;

/// <summary>
/// A relative <c>url()</c> in a style sheet is relative to the style sheet, not to the page that links it (CSS Values 4, resolving URLs). webkit.org links
/// <c>/wp-content/themes/webkit/style.css</c>, which says <c>url('images/webkit.svg')</c>: read against the page, it asked for <c>/images/webkit.svg</c> and
/// every icon and logo background of the page was missing.
/// </summary>
public class StylesheetRelativeUrlTests
{
    private static string BackgroundOf(string css, Uri? urlBase)
    {
        var sheet = new StylesheetParser { UrlBase = urlBase }.Parse(css);
        return sheet.StyleRules.Single().Style.GetPropertyValue("background-image");
    }

    private static readonly Uri Sheet = new("https://example.org/wp-content/themes/t/style.css?2022");

    [Theory]
    [InlineData("a { background-image: url('images/x.svg') }", "https://example.org/wp-content/themes/t/images/x.svg")]
    [InlineData("a { background-image: url(images/x.svg) }", "https://example.org/wp-content/themes/t/images/x.svg")]
    [InlineData("a { background-image: url(../img/x.png) }", "https://example.org/wp-content/themes/img/x.png")]
    [InlineData("a { background-image: url(/root/x.png) }", "https://example.org/root/x.png")]
    public void ARelativeUrl_IsTakenFromTheStyleSheet(string css, string expected)
    {
        Assert.Contains(expected, BackgroundOf(css, Sheet));
    }

    [Theory]
    [InlineData("a { background-image: url(data:image/png;base64,AAAA) }", "data:image/png;base64,AAAA")]
    [InlineData("a { background-image: url(https://cdn.example/x.png) }", "https://cdn.example/x.png")]
    [InlineData("a { background-image: url('#frag') }", "#frag")]
    public void AUrlThatIsNotRelative_IsLeftAlone(string css, string expected)
    {
        Assert.Contains(expected, BackgroundOf(css, Sheet));
    }

    [Fact]
    public void ASheetWithNoLocation_KeepsTheUrlAsWritten()
    {
        var value = BackgroundOf("a { background-image: url(images/x.svg) }", null);

        Assert.Contains("images/x.svg", value);
        Assert.DoesNotContain("://", value);
    }
}
