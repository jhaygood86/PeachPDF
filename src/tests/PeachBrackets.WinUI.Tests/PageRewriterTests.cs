using PeachBrackets.WinUI.Example;

namespace PeachBrackets.WinUI.Tests;

/// <summary>The demo's preparation of a fetched web page: the classes its startup script would have set, and the base address.</summary>
public class PageRewriterTests
{
    private const string Page = """
        <!DOCTYPE html><html lang="en" class="view-grid"><head><title>t</title><link rel="stylesheet" href="/a.css"></head>
        <body class="home"><p id="p">Hello <b>there</b></p>
        <script>document.documentElement.classList.add("dusk")</script>
        <noscript><iframe src="https://example.test/ns.html"></iframe></noscript></body></html>
        """;

    [Fact]
    public void TheRootClassesAreAdded_AndTheOnesThePageHasAreKept()
    {
        var html = PageRewriter.Rewrite(Page, "https://example.test/", ["dusk", "text-settings-size-standard"]);

        var root = html[html.IndexOf("<html", StringComparison.Ordinal)..html.IndexOf('>', html.IndexOf("<html", StringComparison.Ordinal))];
        Assert.Contains("view-grid", root);
        Assert.Contains("dusk", root);
        Assert.Contains("text-settings-size-standard", root);
    }

    [Fact]
    public void ARepeatedOrBlankClass_IsAddedOnceOrNotAtAll()
    {
        var html = PageRewriter.Rewrite(Page, "https://example.test/", ["dusk", "dusk", " ", "", "view-grid"]);

        var root = html[html.IndexOf("<html", StringComparison.Ordinal)..html.IndexOf('>', html.IndexOf("<html", StringComparison.Ordinal))];
        Assert.Equal(1, root.Split("dusk").Length - 1);
        Assert.Equal(1, root.Split("view-grid").Length - 1);
    }

    [Fact]
    public void TheBaseAddress_IsTheFirstThingInTheHead()
    {
        var html = PageRewriter.Rewrite(Page, "https://example.test/", []);

        var head = html.IndexOf("<head>", StringComparison.Ordinal) + "<head>".Length;
        Assert.StartsWith("<base href=\"https://example.test/\">", html[head..]);
    }

    [Fact]
    public void APageWithABaseOfItsOwn_KeepsIt()
    {
        const string withBase = "<!DOCTYPE html><html><head><base href=\"https://other.test/x/\"><title>t</title></head><body></body></html>";

        var html = PageRewriter.Rewrite(withBase, "https://example.test/", []);

        Assert.Equal(1, html.Split("<base ").Length - 1);
        Assert.Contains("https://other.test/x/", html);
        Assert.DoesNotContain("https://example.test/", html);
    }

    [Fact]
    public void APageWithNoHead_GetsOneToHoldTheBase()
    {
        var html = PageRewriter.Rewrite("<p>just a fragment</p>", "https://example.test/", ["x"]);

        Assert.Contains("<head><base href=\"https://example.test/\"></head>", html);
        Assert.Contains("<p>just a fragment</p>", html);
    }

    [Fact]
    public void TheRestOfThePage_IsNotChanged()
    {
        var html = PageRewriter.Rewrite(Page, "https://example.test/", ["dusk"]);

        Assert.Contains("<p id=\"p\">Hello <b>there</b></p>", html);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/a.css\">", html);

        // Scripts are written out untouched (the control runs none), and a noscript is markup, as the control renders it.
        Assert.Contains("document.documentElement.classList.add(\"dusk\")", html);
        Assert.Contains("<noscript><iframe src=\"https://example.test/ns.html\"></iframe></noscript>", html);
    }

    [Theory]
    [InlineData(true, "dark", "dusk")]
    [InlineData(false, "dusk", "dark")]
    public void ArsTechnica_GetsTheThemeClassOfTheDemosTheme_AndItsTextSettings(bool dark, string expected, string notExpected)
    {
        var classes = WebPageTraits.RootClasses("web_arstechnica", dark);

        Assert.Contains(expected, classes);
        Assert.DoesNotContain(notExpected, classes);
        Assert.Contains("text-settings-size-standard", classes);
        Assert.Contains("text-settings-links-standard", classes);
        Assert.Contains("text-settings-width-standard", classes);
        Assert.Contains("text-settings-position-story", classes);
    }

    [Fact]
    public void AnotherSite_GetsNoClasses()
    {
        Assert.Empty(WebPageTraits.RootClasses("web_peachpdf", dark: true));
    }
}
