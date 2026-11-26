using System.Text;
using PeachBrackets.WptRunner.Server;

namespace PeachBrackets.WptRunner.Tests;

/// <summary>Every ported handler answers the shapes of request the xhr and fetch tests send it, none of them throwing.</summary>
public sealed class NetworkHandlerSweepTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wpt-sweep-").FullName;
    private readonly WptServer _server;

    public NetworkHandlerSweepTests() => _server = new WptServer(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ServerResponse Send(string method, string pathAndQuery, Dictionary<string, string>? headers = null, string? body = null) =>
        _server.Handle(new ServerRequest(method, new Uri(WptServer.Origin + "/" + pathAndQuery), headers, body is null ? null : Encoding.UTF8.GetBytes(body)));

    private static string Text(ServerResponse response) => Encoding.UTF8.GetString(response.Body);

    private static readonly string[] Queries =
    [
        "", "?code=301", "?code=302&location=redirect.py", "?cors=1&filter_name=accept&filter_value=x&headers=accept|x-foo&allow_headers=x",
        "?token=t&origin=http://other.test&allow_methods=PUT&allow_headers=x-a&full=1&followed=1&count=3&chunk=4142&chunk=43&type=text/x",
        "?tag=abc&date=Mon, 01 Jan 2020 00:00:00 GMT&cors=1&safelist_content_type=1&response_charset_label=utf-8&content=hi&text=Nope",
    ];

    private static readonly Dictionary<string, string>[] HeaderSets =
    [
        new(),
        new() { ["Origin"] = "http://other.test", ["Accept"] = "text/html", ["Content-Type"] = "text/plain", ["Content-Length"] = "4", ["If-None-Match"] = "\"abc\"" },
        new() { ["Access-Control-Request-Method"] = "PUT", ["Access-Control-Request-Headers"] = "x-a", ["If-Modified-Since"] = "Mon, 01 Jan 2020 00:00:00 GMT", ["Authorization"] = "Basic eDp5", ["Cookie"] = "a=b" },
    ];

    [Fact]
    public void EveryHandlerAnswersEveryShapeOfRequestWithoutThrowing()
    {
        var count = 0;
        foreach (var (path, _) in NetworkHandlers.All())
        {
            foreach (var method in new[] { "GET", "POST", "OPTIONS", "PUT", "HEAD" })
            {
                foreach (var headers in HeaderSets)
                {
                    foreach (var query in Queries)
                    {
                        var response = Send(method, path + query, headers, "body");
                        Assert.InRange(response.Status, 100, 599);
                        count++;
                    }
                }
            }
        }

        Assert.True(count > 1000);
    }

    [Fact]
    public void ConditionalAnswersNotModifiedForAMatchingTag_AndTheCacheHandlerForItsEtag()
    {
        Assert.Equal(304, Send("GET", "xhr/resources/conditional.py?tag=abc", new() { ["If-None-Match"] = "abc" }).Status);
        Assert.Equal("MAYBE NOT", Text(Send("GET", "xhr/resources/conditional.py?tag=abc")));
        Assert.Equal(304, Send("GET", "xhr/resources/cache.py", new() { ["If-None-Match"] = "\"123abc\"" }).Status);
        Assert.Equal("lorem ipsum dolor sit amet", Text(Send("GET", "fetch/api/resources/cache.py")));
    }

    [Fact]
    public void ContentInChunksJoinsTheChunks_AndTrickleRepeatsItsLine()
    {
        Assert.Equal("ABC", Text(Send("GET", "xhr/resources/content-in-chunks.py?chunk=4142&chunk=43")));
        Assert.Equal(3, Text(Send("GET", "xhr/resources/trickle.py?count=3")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void ARedirectFollowsToItsMarker()
    {
        var first = Send("GET", "xhr/resources/redirect.py?code=301");

        Assert.Equal(301, first.Status);
        Assert.Equal("MAGIC HAPPENED", Text(Send("GET", "xhr/resources/redirect.py?followed")));
    }
}
