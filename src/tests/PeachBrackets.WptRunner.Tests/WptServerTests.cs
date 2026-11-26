using System.Text;
using PeachBrackets.WptRunner.Server;

namespace PeachBrackets.WptRunner.Tests;

/// <summary>The in-process WPT server, driven the way the runner does: requests in, a fake checkout behind it.</summary>
public sealed class WptServerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wpt-server-").FullName;
    private readonly WptServer _server;

    public WptServerTests() => _server = new WptServer(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string path, string content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private ServerResponse Get(string pathAndQuery, string origin = WptServer.Origin, Dictionary<string, string>? headers = null) =>
        _server.Handle(new ServerRequest("GET", new Uri(origin + "/" + pathAndQuery), headers));

    private static string Text(ServerResponse response) => Encoding.UTF8.GetString(response.Body);

    [Fact]
    public void AFile_IsServedWithTheTypeOfItsExtension_AndAMissingOneIs404()
    {
        Write("a/page.html", "<p>hi");
        Write("a/style.css", "p {}");

        Assert.Equal(("<p>hi", "text/html"), (Text(Get("a/page.html")), Get("a/page.html").ContentType));
        Assert.Equal("text/css", Get("a/style.css").ContentType);
        Assert.Equal(404, Get("a/missing.html").Status);
    }

    [Fact]
    public void APathThatLeavesTheCheckout_IsNotRead()
    {
        // (A request address is normalized by the URL parser before it gets here; a path handed in directly, by a substitution, is checked.)
        Write("a.txt", "inside");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        Assert.NotNull(_server.ReadFile("a.txt"));
        Assert.Null(_server.ReadFile("../a.txt"));
        Assert.Null(_server.ReadFile("sub/../../a.txt"));
        Assert.Equal(404, Get("nothing/here.txt").Status);
    }

    [Fact]
    public void SubstitutionFillsInTheHostsPortsLocationAndQuery()
    {
        Write("t/page.sub.html", "{{host}}|{{domains[www]}}|{{hosts[alt][]}}|{{hosts[][www1]}}|{{ports[http][0]}}|{{ports[https][1]}}|{{location[host]}}|{{location[pathname]}}|{{location[scheme]}}|{{GET[x]}}|{{headers[X-A]}}|{{header_or_default(X-B, none)}}|{{url_base}}|{{nope}}");

        var text = Text(Get("t/page.sub.html?x=a%26b", headers: new Dictionary<string, string> { ["X-A"] = "yes" }));

        Assert.Equal(
            "web-platform.test|www.web-platform.test|not-web-platform.test|www1.web-platform.test|8000|8444|web-platform.test:8000|/t/page.sub.html|http|a&amp;b|yes|none|/|{{nope}}",
            text);
    }

    [Fact]
    public void Substitution_ProducesAUuidAndAFileHash_AndEscapesOnlyMarkup()
    {
        Write("t/data.txt", "hello");
        Write("t/a.sub.html", "{{uuid()}}|{{file_hash(md5, t/data.txt)}}|{{GET[q]}}");
        Write("t/a.sub.js", "var q = '{{GET[q]}}';");

        var html = Text(Get("t/a.sub.html?q=<b>"));
        Assert.Matches(@"^[0-9a-f-]{36}\|5d41402abc4b2a76b9719d911017c592\|&lt;b&gt;$", html);
        Assert.Equal("var q = '<b>';", Text(Get("t/a.sub.js?q=<b>")));
    }

    [Fact]
    public void AFileWithoutSubInItsName_IsLeftAlone_UnlessThePipeAsksForIt()
    {
        Write("t/plain.html", "{{host}}");

        Assert.Equal("{{host}}", Text(Get("t/plain.html")));
        Assert.Equal("web-platform.test", Text(Get("t/plain.html?pipe=sub")));
    }

    [Fact]
    public void ThePipesSetTheStatusAndHeaders()
    {
        Write("t/plain.txt", "x");

        var response = Get("t/plain.txt?pipe=status(418)|header(X-Custom,one)|header(X-Custom,two,True)");

        Assert.Equal(418, response.Status);
        Assert.Equal(["one", "two"], response.Headers.Where(h => h.Key == "X-Custom").Select(h => h.Value));
    }

    [Fact]
    public void HeadersFiles_AddAndReplaceHeaders_FileOverDirectory()
    {
        Write("h/page.html", "x");
        Write("h/__dir__.headers", "Content-Type: text/plain\nX-Dir: dir\nX-Both: dir");
        Write("h/page.html.headers", "Content-Type: application/xhtml+xml; charset=utf-8\nX-Both: file");

        var response = Get("h/page.html");

        Assert.Equal("application/xhtml+xml; charset=utf-8", response.ContentType);
        Assert.Equal("dir", response.Get("X-Dir"));
        Assert.Equal("file", response.Get("X-Both"));
        Assert.Equal("application/xhtml+xml; charset=utf-8", _server.ContentTypeFor("h/page.html"));
    }

    [Fact]
    public void AnAnyJsFile_StandsForItsWindowPage_OnlyWhenItAsksForAWindow()
    {
        Write("t/a.any.js", "// META: script=/resources/helper.js\ntest(() => {}, 'x');");
        Write("t/w.any.js", "// META: global=worker\n");
        Write("t/b.window.js", "// META: global=worker\n");

        var page = Get("t/a.any.html");
        Assert.Equal(200, page.Status);
        var text = Text(page);
        Assert.Contains("/resources/testharness.js", text, StringComparison.Ordinal);
        Assert.Contains("src=\"/resources/helper.js\"", text, StringComparison.Ordinal);
        Assert.Contains("src=\"a.any.js\"", text, StringComparison.Ordinal);

        Assert.Equal(404, Get("t/w.any.html").Status);
        Assert.Equal(200, Get("t/b.window.html").Status);
    }

    [Fact]
    public void APortedHandlerAnswers_AndAnUnportedOneIs501()
    {
        Write("common/redirect.py", "def main(request, response): pass");
        Write("common/unknown.py", "def main(request, response): pass");

        var redirect = Get("common/redirect.py?location=/x/y.html&status=307");
        Assert.Equal(307, redirect.Status);
        Assert.Equal("/x/y.html", redirect.Get("Location"));

        Assert.Equal(501, Get("common/unknown.py").Status);
    }

    [Fact]
    public void TheEchoHandlers_ReturnWhatTheQueryAsksFor()
    {
        Assert.Equal("<b>hi</b>", Text(Get("common/echo.py?content=%3Cb%3Ehi%3C/b%3E")));
        Assert.Equal("0; URL=/next", Get("common/refresh.py?location=/next").Get("Refresh"));

        var custom = Get("common/CustomCorsResponse.py?status=201&content=%22body%22&headers=%7B%22X-A%22%3A%221%22%7D");
        Assert.Equal((201, "body", "1"), (custom.Status, Text(custom), custom.Get("X-A")));

        var typed = Get("dom/nodes/Document-contentType/support/contenttype_setter.py?type=text&subtype=plain&mime=text/html");
        Assert.Equal("text/plain", typed.ContentType);
        Assert.Contains("charset=utf-8", Text(typed), StringComparison.Ordinal);
        Assert.Equal("<!doctype html><meta charset=\"utf-8\">", Text(Get("dom/nodes/encoding.py?label=utf-8")));
    }

    [Fact]
    public void Redirects_CarryCorsHeadersWhenAsked()
    {
        var response = Get("common/redirect.py?location=/a&enable-cors=1", headers: new Dictionary<string, string> { ["Origin"] = "http://www.web-platform.test:8000" });

        Assert.Equal("http://www.web-platform.test:8000", response.Get("Access-Control-Allow-Origin"));
        Assert.Equal("true", response.Get("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task TheLoaderFollowsRedirects_KeepsCookies_AndLogsEachRequest()
    {
        Write("t/final.txt", "arrived");
        Write("t/login.py", "x");
        _server.Override("t/cookie.txt", "ok");

        var loaded = await _server.LoadAsync(new Uri(WptServer.Origin + "/common/redirect.py?location=/t/final.txt"));
        Assert.NotNull(loaded);
        using (var reader = new StreamReader(loaded.ResourceStream!))
            Assert.Equal("arrived", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));

        // The redirect took two requests; a missing file and a foreign host are not served.
        Assert.Equal(["common/redirect.py", "t/final.txt"], _server.Log.Select(r => r.Path));
        Assert.Null(await _server.LoadAsync(new Uri(WptServer.Origin + "/t/missing.txt")));
        Assert.Null(await _server.LoadAsync(new Uri("http://example.com/t/final.txt")));
    }

    [Fact]
    public async Task ACookieASetCookieHeaderSets_IsSentBackOnTheNextRequestToThatHost()
    {
        _server.Override("t/set.txt", "x");
        var first = _server.Handle(new ServerRequest("GET", new Uri(WptServer.Origin + "/t/set.txt?pipe=header(Set-Cookie,a=b; Path=/)")));
        Assert.Equal("a=b; Path=/", first.Get("Set-Cookie"));

        await _server.LoadAsync(new Uri(WptServer.Origin + "/t/set.txt?pipe=header(Set-Cookie,k=v)"));
        await _server.LoadAsync(new Uri(WptServer.Origin + "/t/set.txt"));

        Assert.Equal("k=v", _server.Log[^1].Headers["Cookie"]);
        Assert.False(_server.Log[^1].Headers.ContainsKey("X-None"));
    }

    [Theory]
    [InlineData("http://web-platform.test:8000/x", true)]
    [InlineData("http://www1.web-platform.test:8001/x", true)]
    [InlineData("https://www.web-platform.test:8443/x", true)]
    [InlineData("http://xn--lve-6lad.web-platform.test/x", true)]
    [InlineData("http://not-web-platform.test:8000/x", true)]
    [InlineData("http://web-platform.test:9999/x", false)]
    [InlineData("http://example.com/x", false)]
    [InlineData("ftp://web-platform.test/x", false)]
    public void TheServerAnswersForItsOwnDomainsAndPortsOnly(string url, bool served) =>
        Assert.Equal(served, _server.Serves(new Uri(url)));

    [Fact]
    public void ThePortedHandlersAreListed()
    {
        Assert.Contains("common/redirect.py", WptHandlers.Ported);
        Assert.Null(WptHandlers.Find("common/not-ported.py"));
    }
}
