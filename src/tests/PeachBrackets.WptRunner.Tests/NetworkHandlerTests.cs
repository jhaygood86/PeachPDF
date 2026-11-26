using System.Text;
using PeachBrackets.ScriptHosting;
using PeachBrackets.WptRunner.Server;

namespace PeachBrackets.WptRunner.Tests;

/// <summary>The ported xhr/fetch handlers: what they answer, and the stash and referrer plumbing they rely on.</summary>
public sealed class NetworkHandlerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wpt-handlers-").FullName;
    private readonly WptServer _server;

    public NetworkHandlerTests() => _server = new WptServer(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ServerResponse Request(string method, string pathAndQuery, Dictionary<string, string>? headers = null, string? body = null) =>
        _server.Handle(new ServerRequest(method, new Uri(WptServer.Origin + "/" + pathAndQuery), headers, body is null ? null : Encoding.UTF8.GetBytes(body)));

    private static string Text(ServerResponse response) => Encoding.UTF8.GetString(response.Body);

    [Fact]
    public void StatusAnswersWithTheCodeAndTheReasonPhraseAsked()
    {
        var response = Request("GET", "xhr/resources/status.py?code=404&text=Nope&content=x&type=text/plain");

        Assert.Equal((404, "Nope", "x", "text/plain"), (response.Status, response.StatusText, Text(response), response.ContentType));
    }

    [Fact]
    public void ContentEchoesTheRequestInItsHeaders()
    {
        var response = Request("POST", "xhr/resources/content.py?a=1", new() { ["Content-Type"] = "text/x" }, "body");

        Assert.Equal(("POST", "a=1", "text/x", "body"), (response.Get("X-Request-Method"), response.Get("X-Request-Query"), response.Get("X-Request-Content-Type"), Text(response)));
    }

    [Fact]
    public void TheStashGivesAValueBackOnceAndScopesItByDirectory()
    {
        Request("GET", "fetch/api/resources/stash-put.py?key=k&value=v");

        Assert.Equal("null", Text(Request("GET", "fetch/api/resources/stash-take.py?key=other")));
        Assert.Equal("\"v\"", Text(Request("GET", "fetch/api/resources/stash-take.py?key=k")));
        Assert.Equal("null", Text(Request("GET", "fetch/api/resources/stash-take.py?key=k")));
        _server.Stash.Put("t", "x");
        Assert.Equal("1", Text(Request("GET", "fetch/api/resources/clean-stash.py?token=t")));
        Assert.Equal("0", Text(Request("GET", "fetch/api/resources/clean-stash.py?token=t")));
    }

    [Fact]
    public void ThePreflightHandlerRemembersThatAPreflightCameBeforeTheRequest()
    {
        var preflight = Request("OPTIONS", "fetch/api/resources/preflight.py?token=a&allow_methods=PUT", new() { ["Access-Control-Request-Method"] = "PUT", ["Accept"] = "*/*" });
        var actual = Request("PUT", "fetch/api/resources/preflight.py?token=a");

        Assert.Equal(200, preflight.Status);
        Assert.Equal("PUT", preflight.Get("Access-Control-Allow-Methods"));
        Assert.Equal("1", actual.Get("x-did-preflight"));
        Assert.Equal(400, Request("OPTIONS", "fetch/api/resources/preflight.py").Status);
    }

    [Fact]
    public void ARedirectCountsItsHopsInTheLocation()
    {
        var first = Request("GET", "fetch/api/resources/redirect.py?token=r&location=/x%3Fy");
        var second = Request("GET", "fetch/api/resources/redirect.py?token=r&location=/x%3Fy");

        Assert.Equal(302, first.Status);
        Assert.EndsWith("&count=1", first.Get("Location"));
        Assert.EndsWith("&count=2", second.Get("Location"));
    }

    [Fact]
    public void TheRefererFollowsTheReferrerPolicy_AndAResponseCanChangeThePolicy()
    {
        var page = WptServer.Origin + "/dir/page.html?q=1#frag";
        _server.Send(new ScriptNetworkRequest("GET", new Uri(WptServer.Origin + "/xhr/resources/inspect-headers.py?filter_name=referer"), [], null, true, page, ""));
        Assert.Equal(page.Replace("#frag", ""), _server.Log[^1].Headers["Referer"]);

        _server.Send(new ScriptNetworkRequest("GET", new Uri("http://www1.web-platform.test:8000/xhr/resources/inspect-headers.py"), [], null, true, page, ""));
        Assert.Equal(WptServer.Origin + "/", _server.Log[^1].Headers["Referer"]);

        _server.Send(new ScriptNetworkRequest("GET", new Uri(WptServer.Origin + "/xhr/resources/inspect-headers.py"), [], null, true, page, "no-referrer"));
        Assert.False(_server.Log[^1].Headers.ContainsKey("Referer"));
    }
}
