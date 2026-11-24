using System.Text;
using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Tests;

/// <summary>URL, Headers, fetch, XMLHttpRequest, Blob and the text codecs a script gets, run against a network the test controls.</summary>
public class WebPlatformTests
{
    private sealed class FakeNetwork : IScriptNetwork
    {
        public List<ScriptNetworkRequest> Requests { get; } = [];

        public ValueTask<ScriptNetworkResponse?> SendAsync(ScriptNetworkRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var path = request.Url.AbsolutePath;
            if (path == "/fail")
                return ValueTask.FromResult<ScriptNetworkResponse?>(null);

            var (status, text, type) = path switch
            {
                "/json" => (200, "{\"a\":[1,2]}", "application/json"),
                "/missing" => (404, "no such thing", "text/plain"),
                "/echo" => (200, request.Method + ":" + (request.Body is null ? "" : Encoding.UTF8.GetString(request.Body)) + ":" + string.Join(",", request.Headers.Select(h => h.Key + "=" + h.Value)), "text/plain"),
                _ => (200, "hello " + path, "text/plain; charset=utf-8"),
            };
            var headers = new List<KeyValuePair<string, string>> { new("Content-Type", type), new("X-Custom", "one"), new("X-Custom", "two") };
            return ValueTask.FromResult<ScriptNetworkResponse?>(new ScriptNetworkResponse(status, status == 404 ? "Not Found" : "OK", headers, Encoding.UTF8.GetBytes(text), request.Url));
        }
    }

    private static async Task<(CollectingLogger Logger, FakeNetwork Network)> RunAsync(string script)
    {
        var logger = new CollectingLogger();
        var network = new FakeNetwork();
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 400,
            ScriptHost = new JintScriptHost(new JintScriptOptions { Logger = logger }),
            ScriptNetwork = network,
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
        };
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap("<base href='http://example.test/dir/page.html'><script>" + script + "</script>"), context, options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        return (logger, network);
    }

    private static async Task<List<string>> LogAsync(string script) => (await RunAsync(script)).Logger.Texts;

    [Fact]
    public async Task URL_ParsesResolvesAndSerializes()
    {
        var log = await LogAsync("""
            var u = new URL('HTTP://User:Pa ss@EXAMPLE.com:80/a/./b/../c d?x=1 2#frag ment');
            Log.info([u.href, u.origin, u.protocol, u.username, u.password, u.host, u.port, u.pathname, u.search, u.hash].join('|'));
            var r = new URL('../up?q#h', 'http://h.test/a/b/c');
            Log.info(r.href);
            Log.info(new URL('//other.test/x', 'https://h.test/').href + ' ' + new URL('?only', 'http://h.test/p?old#f').href + ' ' + new URL('#f2', 'http://h.test/p?q').href);
            Log.info(new URL('http://192.168.0.1.:8080/').host + ' ' + new URL('http://0x7f.1/').host + ' ' + new URL('http://[::1]:3000/').host + ' ' + new URL('http://[2001:db8:0:0:0:0:0:1]/').host);
            Log.info(new URL('file:///C:/a/../b').href + ' ' + new URL('mailto:a@b.c').pathname + ' ' + new URL('data:text/plain,hi there').href);
            Log.info(URL.canParse('nope') + ' ' + URL.canParse('nope', 'http://a.test') + ' ' + URL.parse('::') + ' ' + new URL('http://ex%41mple.com/').host);
            """);

        Assert.Equal(
            [
                "http://User:Pa%20ss@example.com/a/c%20d?x=1%202#frag%20ment|http://example.com|http:|User|Pa%20ss|example.com||/a/c%20d|?x=1%202|#frag%20ment",
                "http://h.test/a/up?q#h",
                "https://other.test/x http://h.test/p?only http://h.test/p?q#f2",
                "192.168.0.1:8080 127.0.0.1 [::1]:3000 [2001:db8::1]",
                "file:///C:/b a@b.c data:text/plain,hi there",
                "false true null example.com",
            ],
            log);
    }

    [Fact]
    public async Task URL_RefusesWhatIsNotAURL_AndSettersRewriteIt()
    {
        var log = await LogAsync("""
            var out = [];
            ['', 'http://', 'http://a b/', 'http://[::1/', 'http://a:99999/', 'relative'].forEach(function (s) { try { new URL(s); out.push('ok'); } catch (e) { out.push(e instanceof TypeError); } });
            Log.info(out.join(','));
            var u = new URL('http://example.com/p?a=1#h');
            u.pathname = '/new path'; u.search = 'b=2'; u.hash = ''; u.port = '8080'; u.hostname = 'other.test'; u.protocol = 'https'; u.username = 'me';
            Log.info(u.href);
            u.href = 'ftp://f.test/x'; Log.info(u.href + ' ' + u.protocol);
            """);

        Assert.Equal(["true,true,true,true,true,true", "https://me@other.test:8080/new%20path?b=2", "ftp://f.test/x ftp:"], log);
    }

    [Fact]
    public async Task URLSearchParams_ParsesSerializesAndStaysInSyncWithTheURL()
    {
        var log = await LogAsync("""
            var p = new URLSearchParams('?a=1&b=%C3%A9+x&a=2&c');
            Log.info([p.get('a'), p.getAll('a').join('/'), p.get('b'), p.get('c') === '', p.get('zz'), p.has('c'), p.size].join('|'));
            p.append('d', 'a&b=c d'); p.set('a', 'only'); p.delete('c');
            Log.info(p.toString());
            Log.info(new URLSearchParams({ x: '1', y: '2' }).toString() + ' ' + new URLSearchParams([['k', 'v'], ['k', 'w']]).toString());
            var u = new URL('http://h.test/?q=1');
            u.searchParams.append('r', '2 3');
            Log.info(u.href);
            u.search = '?z=9'; Log.info(u.searchParams.get('z') + ' ' + u.searchParams.has('q'));
            var s = new URLSearchParams('b=2&a=1&b=1'); s.sort(); Log.info(s.toString() + ' ' + Array.from(s.keys()).join(''));
            """);

        Assert.Equal(
            ["1|1/2|é x|true||true|4", "a=only&b=%C3%A9+x&d=a%26b%3Dc+d", "x=1&y=2 k=v&k=w", "http://h.test/?q=1&r=2+3", "9 false", "a=1&b=2&b=1 abb"],
            log);
    }

    [Fact]
    public async Task Headers_NormalizeAndIterateSortedAndLowerCased()
    {
        var log = await LogAsync("""
            var h = new Headers({ 'X-B': ' two ', 'x-a': 'one' });
            h.append('X-B', 'again'); h.append('Set-Cookie', 'a=1'); h.append('Set-Cookie', 'b=2');
            Log.info([h.get('x-b'), h.get('X-A'), h.has('nope'), h.get('nope'), h.getSetCookie().join('+')].join('|'));
            Log.info(Array.from(h).map(function (e) { return e.join('='); }).join(';'));
            h.set('x-a', 'new'); h.delete('x-b'); Log.info(Array.from(h.keys()).join(','));
            var bad = 0; ['bad name', ''].forEach(function (n) { try { h.append(n, 'v'); } catch (e) { bad++; } });
            try { h.append('x', 'a\nb'); } catch (e) { bad++; }
            Log.info(bad);
            """);

        Assert.Equal(["two, again|one|false||a=1+b=2", "set-cookie=a=1;set-cookie=b=2;x-a=one;x-b=two, again", "set-cookie,set-cookie,x-a", "3"], log);
    }

    [Fact]
    public async Task Fetch_ReadsTheResponseWhateverItsStatus_AndRejectsOnANetworkError()
    {
        var log = await LogAsync("""
            fetch('/json').then(function (r) {
                Log.info([r.status, r.ok, r.statusText, r.type, r.url, r.redirected, r.headers.get('content-type'), r.headers.get('x-custom')].join('|'));
                return r.json();
            }).then(function (j) { Log.info('json ' + j.a[1]); return fetch('/missing'); })
              .then(function (r) { Log.info(r.status + ' ' + r.ok); return r.text(); })
              .then(function (t) { Log.info(t); return fetch('/fail'); })
              .catch(function (e) { Log.info((e instanceof TypeError) + ' ' + e.message); });
            Log.info('sent first');
            """);

        Assert.Equal(
            [
                "sent first",
                "200|true|OK|basic|http://example.test/json|false|application/json|one, two",
                "json 2",
                "404 false",
                "no such thing",
                "true Failed to fetch",
            ],
            log);
    }

    [Fact]
    public async Task Fetch_SendsTheMethodBodyAndHeadersTheScriptGave_AndTheBodyCanBeReadOnce()
    {
        var (logger, network) = await RunAsync("""
            var r = new Request('/echo', { method: 'post', body: 'payload', headers: { 'X-Mine': 'yes' } });
            Log.info(r.method + ' ' + r.url + ' ' + r.headers.get('content-type'));
            fetch(r).then(function (res) { return res.text(); }).then(function (t) {
                Log.info(t);
                var again = new Response('x'); return again.text().then(function () { return again.text(); });
            }).catch(function (e) { Log.info('second read: ' + (e instanceof TypeError)); });
            """);

        Assert.Equal("POST:payload:x-mine=yes,content-type=text/plain;charset=UTF-8", logger.Texts[1].Replace("Information:", string.Empty));
        Assert.Equal("POST http://example.test/echo text/plain;charset=UTF-8", logger.Texts[0]);
        Assert.Equal("second read: true", logger.Texts[2]);
        Assert.Single(network.Requests);
    }

    [Fact]
    public async Task Fetch_Rejects_WhenTheSignalIsAbortedFirstOrWhileWaiting()
    {
        var log = await LogAsync("""
            var early = new AbortController(); early.abort();
            fetch('/x', { signal: early.signal }).catch(function (e) { Log.info('early ' + e.name); });
            var late = new AbortController();
            fetch('/y', { signal: late.signal }).then(function () { Log.info('resolved'); }, function (e) { Log.info('late ' + e.name); });
            late.abort();
            """);

        Assert.Equal(["early AbortError", "late AbortError"], log);
    }

    [Fact]
    public async Task Response_StaticsAndTheirRules()
    {
        var log = await LogAsync("""
            var out = [];
            out.push(Response.error().type + Response.error().status);
            var red = Response.redirect('/to', 301); out.push(red.status + red.headers.get('location'));
            var j = Response.json({ a: 1 }); out.push(j.headers.get('content-type'));
            try { new Response('x', { status: 101 }); } catch (e) { out.push(e instanceof RangeError); }
            try { new Response('x', { status: 204 }); } catch (e) { out.push(e instanceof TypeError); }
            var c = new Response('body', { status: 201, statusText: 'Made' }); var clone = c.clone();
            out.push(c.status + c.statusText + c.ok + c.bodyUsed);
            Promise.all([c.text(), clone.arrayBuffer()]).then(function (v) { out.push(v[0] + v[1].byteLength + c.bodyUsed); Log.info(out.join('|')); });
            """);

        Assert.Equal(["error0|301http://example.test/to|application/json|true|true|201Madetruefalse|body4true"], log);
    }

    [Fact]
    public async Task XMLHttpRequest_FiresItsEventsInOrder_AndExposesTheResponse()
    {
        var log = await LogAsync("""
            var x = new XMLHttpRequest(), events = [];
            ['readystatechange', 'loadstart', 'progress', 'load', 'loadend', 'error'].forEach(function (t) {
                x.addEventListener(t, function (e) { events.push(t === 'readystatechange' ? 'rs' + x.readyState : t); });
            });
            x.onloadend = function () {
                Log.info(events.join(','));
                Log.info([x.status, x.statusText, x.responseURL, x.getResponseHeader('X-Custom'), x.getResponseHeader('content-type'), x.responseText, x.response].join('|'));
                Log.info(x.getAllResponseHeaders().replace(/\r\n/g, ';'));
            };
            Log.info(x.readyState);
            x.open('GET', 'data/file.txt'); Log.info(x.readyState);
            x.send();
            Log.info('after send ' + x.readyState);
            """);

        Assert.Equal(
            [
                "0",
                "1",
                "after send 1",
                "rs1,loadstart,rs2,rs3,progress,rs4,load,loadend",
                "200|OK|http://example.test/dir/data/file.txt|one, two|text/plain; charset=utf-8|hello /dir/data/file.txt|hello /dir/data/file.txt",
                "content-type: text/plain; charset=utf-8;x-custom: one, two;",
            ],
            log);
    }

    [Fact]
    public async Task XMLHttpRequest_ResponseTypesPostBodiesAndErrors()
    {
        var (logger, network) = await RunAsync("""
            function get(type, path, done) { var x = new XMLHttpRequest(); x.responseType = type; x.onload = function () { done(x); }; x.onerror = function () { done(x); }; x.open('GET', path); x.send(); }
            get('json', '/json', function (x) { Log.info('json ' + x.response.a.length); });
            get('arraybuffer', '/dir/f', function (x) { Log.info('buf ' + x.response.byteLength + ' ' + (new Uint8Array(x.response)[0])); });
            get('blob', '/dir/f', function (x) { Log.info('blob ' + x.response.size + ' ' + x.response.type); });
            get('', '/fail', function (x) { Log.info('fail ' + x.status + ' ' + x.readyState); });
            var p = new XMLHttpRequest(); p.open('POST', '/echo'); p.setRequestHeader('X-Mine', 'a'); p.setRequestHeader('X-Mine', 'b'); p.setRequestHeader('Host', 'ignored');
            p.onload = function () { Log.info(p.responseText); };
            p.send(new URLSearchParams({ k: 'v' }));
            var s = new XMLHttpRequest(); s.open('GET', '/sync', false); s.send(); Log.info('sync ' + s.status + ' ' + s.responseText);
            var t = new XMLHttpRequest();
            try { t.send(); } catch (e) { Log.info(e.name); }
            try { t.open('TRACE', '/x'); } catch (e) { Log.info(e.name); }
            var n = new XMLHttpRequest(); n.open('GET', '/x'); n.send(); n.abort(); Log.info('aborted ' + n.readyState);
            """);

        Assert.True(logger.Texts.Contains("sync 200 hello /sync"), "missing " + "sync 200 hello /sync" + "; logged: " + string.Join(" # ", logger.Texts));
        Assert.True(logger.Texts.Contains("InvalidStateError"), "missing " + "InvalidStateError" + "; logged: " + string.Join(" # ", logger.Texts));
        Assert.True(logger.Texts.Contains("SecurityError"), "missing " + "SecurityError" + "; logged: " + string.Join(" # ", logger.Texts));
        Assert.True(logger.Texts.Contains("aborted 0"), "missing " + "aborted 0" + "; logged: " + string.Join(" # ", logger.Texts));
        Assert.True(logger.Texts.Contains("json 2"), "missing " + "json 2" + "; logged: " + string.Join(" # ", logger.Texts));
        Assert.True(logger.Texts.Contains("buf 12 104"), "missing " + "buf 12 104" + "; logged: " + string.Join(" # ", logger.Texts));
        Assert.True(logger.Texts.Contains("blob 12 text/plain; charset=utf-8"), "missing " + "blob 12 text/plain; charset=utf-8" + "; logged: " + string.Join(" # ", logger.Texts));
        Assert.True(logger.Texts.Contains("fail 0 4"), "missing " + "fail 0 4" + "; logged: " + string.Join(" # ", logger.Texts));
        Assert.True(logger.Texts.Contains("POST:k=v:x-mine=a, b,content-type=application/x-www-form-urlencoded;charset=UTF-8"), "missing " + "POST:k=v:x-mine=a, b,content-type=application/x-www-form-urlencoded;charset=UTF-8" + "; logged: " + string.Join(" # ", logger.Texts));
        Assert.DoesNotContain(network.Requests, r => r.Headers.Any(h => h.Key.Equals("host", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task DataAndBlobAddressesAreReadWithoutTheNetwork()
    {
        var (logger, network) = await RunAsync("""
            fetch('data:text/plain;base64,aGVsbG8=').then(function (r) { return r.text(); }).then(function (t) { Log.info('data ' + t); });
            var blob = new Blob(['ab', 'cd'], { type: 'Text/X-Test' });
            var url = URL.createObjectURL(blob);
            fetch(url).then(function (r) { Log.info(r.headers.get('content-type') + ' ' + r.headers.get('content-length')); return r.text(); }).then(function (t) {
                Log.info('blob ' + t); URL.revokeObjectURL(url); return fetch(url);
            }).catch(function (e) { Log.info('revoked ' + (e instanceof TypeError)); });
            var x = new XMLHttpRequest(); x.open('GET', 'data:,x%20y'); x.onload = function () { Log.info('xhr ' + x.responseText); }; x.send();
            """);

        Assert.Equal(["data hello", "xhr x y", "text/x-test 4", "blob abcd", "revoked true"], logger.Texts.OrderBy(t => t switch { "data hello" => 0, "xhr x y" => 1, "text/x-test 4" => 2, "blob abcd" => 3, _ => 4 }).ToList());
        Assert.Empty(network.Requests);
    }

    [Fact]
    public async Task Blob_FormData_AndTheTextCodecs()
    {
        var log = await LogAsync("""
            var b = new Blob(['hello ', new Uint8Array([119, 111, 114, 108, 100])], { type: 'text/plain' });
            Log.info(b.size + ' ' + b.type + ' ' + b.slice(6).size + ' ' + b.slice(-5, -2).type.length + ' ' + Object.prototype.toString.call(b));
            b.slice(0, 5, 'X/Y').text().then(function (t) { Log.info(t + ' ' + b.slice(0, 1, 'X/Y').type); });
            var f = new File(['x'], 'a/b.txt', { type: 'text/plain', lastModified: 5 });
            Log.info(f.name + ' ' + f.lastModified + ' ' + (f instanceof Blob) + ' ' + f.size);
            var fd = new FormData(); fd.append('k', 'v'); fd.append('k', 'w'); fd.append('file', f);
            Log.info([fd.get('k'), fd.getAll('k').join(''), fd.has('file'), fd.get('file').name, fd.get('nope')].join('|'));
            var enc = new TextEncoder().encode('h\u00e9\ud83d\ude00');
            Log.info(Array.from(enc).join(',') + ' ' + new TextDecoder().decode(enc) + ' ' + new TextDecoder('utf-16le').encoding + ' ' + new TextDecoder('latin1').decode(new Uint8Array([233])));
            try { new TextDecoder('nope'); } catch (e) { Log.info(e instanceof RangeError); }
            """);

        Assert.Contains("11 text/plain 5 0 [object Blob]", log);
        Assert.Contains("a:b.txt 5 true 1", log);
        Assert.Contains("v|vw|true|a:b.txt|", log);
        Assert.Contains("104,195,169,240,159,152,128 h\u00e9\ud83d\ude00 utf-16le \u00e9", log);
        Assert.Contains("true", log);
        Assert.Contains("hello x/y", log);
    }
}
