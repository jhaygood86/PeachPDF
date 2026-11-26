namespace PeachBrackets.Scripting.Tests;

/// <summary>The URL parts of an a element, and the URL setters' handling of tabs and newlines.</summary>
public class HyperlinkTests
{
    private static async Task<List<string>> LogAsync(string script)
    {
        var logger = new CollectingLogger();
        var host = new JintScriptHost(new JintScriptOptions { Logger = logger });
        var context = await TestDocument.NewContextAsync();
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap("<script>" + script + "</script>"),
            context,
            new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ScriptHost = host });
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return logger.Texts;
    }

    [Fact]
    public async Task AnAnchorHasTheUrlPartsOfItsHref_ResolvedAgainstTheBaseElement()
    {
        var log = await LogAsync("""
            var base = document.createElement('base'); base.setAttribute('href', 'http://example.org/foo/bar'); document.head.insertBefore(base, document.head.firstChild);
            var a = document.createElement('a'); a.setAttribute('href', '../x?q=1#h');
            console.log([a.href, a.protocol, a.host, a.pathname, a.search, a.hash, a.origin, a.baseURI].join(' '));
            a.port = '8080'; a.hostname = 'te' + String.fromCharCode(9) + 'st.com'; a.hash = '';
            console.log(a.href + ' ' + a.getAttribute('href'));
            var plain = document.createElement('a');
            console.log(plain.protocol + '|' + plain.href + '|' + plain.host + '|');
            try { window.open('http://a b/'); } catch (e) { console.log(e.name); }
            """);

        Assert.Equal(
        [
            "http://example.org/x?q=1#h http: example.org /x ?q=1 #h http://example.org http://example.org/foo/bar",
            "http://test.com:8080/x?q=1 http://test.com:8080/x?q=1",
            ":|||",
            "SyntaxError",
        ], log);
    }

    [Fact]
    public async Task AUrlSetterDropsTabsAndNewlines_ButNotInUserInfo()
    {
        var log = await LogAsync("""
            var u = new URL('https://host:8000/p');
            u.protocol = 'ht' + String.fromCharCode(10) + 'tp'; u.host = 'a' + String.fromCharCode(9) + 'b:81'; u.username = 'x' + String.fromCharCode(9);
            console.log(u.href);
            """);

        Assert.Equal(["http://x%09@ab:81/p"], log);
    }
}
