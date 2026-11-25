using System.Text;
using PeachBrackets.ScriptHosting;

namespace PeachBrackets.Scripting.Tests;

/// <summary>Streams, the fetch CORS and redirect rules, and form-data bodies: what a script sees, run against a network the test controls.</summary>
public class StreamsAndCorsTests
{
    private sealed class Network(Func<ScriptNetworkRequest, ScriptNetworkResponse?> answer) : IScriptNetwork
    {
        public List<ScriptNetworkRequest> Requests { get; } = [];

        public ValueTask<ScriptNetworkResponse?> SendAsync(ScriptNetworkRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(answer(request));
        }
    }

    private static ScriptNetworkResponse Reply(ScriptNetworkRequest request, int status, string body, params (string Name, string Value)[] headers) =>
        new(status, status == 200 ? "OK" : "Other", [.. headers.Select(h => new KeyValuePair<string, string>(h.Name, h.Value)), new("Content-Type", "text/plain")],
            Encoding.UTF8.GetBytes(body), request.Url);

    private static async Task<(List<string> Log, Network Network)> RunAsync(string script, Func<ScriptNetworkRequest, ScriptNetworkResponse?>? answer = null)
    {
        var logger = new CollectingLogger();
        var network = new Network(answer ?? (r => Reply(r, 200, "hello " + r.Url.AbsolutePath)));
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
        return (logger.Texts, network);
    }

    private static string Header(ScriptNetworkRequest request, string name) =>
        request.Headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    [Fact]
    public async Task ReadableStream_QueuesChunks_AndTheReaderSeesThemInOrderThenDone()
    {
        var (log, _) = await RunAsync("""
            var i = 0;
            var s = new ReadableStream({ pull: function (c) { if (i < 3) c.enqueue('c' + (i++)); else c.close(); } });
            var r = s.getReader();
            Log.info(s.locked);
            (async function () {
                var out = [];
                for (;;) { var x = await r.read(); if (x.done) break; out.push(x.value); }
                Log.info(out.join(',') + ' ' + (await r.closed));
            })();
            """);

        Assert.Equal(["true", "c0,c1,c2 undefined"], log);
    }

    [Fact]
    public async Task ReadableStream_Tee_PipeThrough_AndAsyncIteration()
    {
        var (log, _) = await RunAsync("""
            function source() { return new ReadableStream({ start: function (c) { c.enqueue('a'); c.enqueue('b'); c.close(); } }); }
            (async function () {
                var pair = source().tee();
                async function all(s) { var o = []; for await (var v of s) o.push(v); return o.join(''); }
                Log.info(await all(pair[0]) + await all(pair[1]));
                var upper = new TransformStream({ transform: function (chunk, c) { c.enqueue(chunk.toUpperCase()); } });
                Log.info(await all(source().pipeThrough(upper)));
                var seen = [];
                var sink = new WritableStream({ write: function (chunk) { seen.push(chunk); }, close: function () { seen.push('end'); } });
                await source().pipeTo(sink);
                Log.info(seen.join(','));
            })();
            """);

        Assert.Equal(["abab", "AB", "a,b,end"], log);
    }

    [Fact]
    public async Task WritableStream_AppliesBackpressure_AndRejectsWritesAfterClose()
    {
        var (log, _) = await RunAsync("""
            (async function () {
                var done = [];
                var ws = new WritableStream({ write: function (c) { return new Promise(function (r) { setTimeout(function () { done.push(c); r(); }, 1); }); } }, { highWaterMark: 1 });
                var w = ws.getWriter();
                Log.info(w.desiredSize);
                var p1 = w.write('x');
                Log.info(w.desiredSize);
                await w.ready; await p1;
                Log.info(done.join(''));
                await w.close();
                try { await w.write('y'); } catch (e) { Log.info(e instanceof TypeError); }
                await w.closed;
                Log.info('closed');
            })();
            """);

        Assert.Equal(["1", "0", "x", "true", "closed"], log);
    }

    [Fact]
    public async Task TextStreams_AndBodyStreams_ConnectResponsesBlobsAndText()
    {
        var (log, _) = await RunAsync("""
            (async function () {
                var bytes = new TextEncoder().encode('héllo');
                var halves = new ReadableStream({ start: function (c) { c.enqueue(bytes.slice(0, 2)); c.enqueue(bytes.slice(2)); c.close(); } });
                var text = '';
                var r = halves.pipeThrough(new TextDecoderStream()).getReader();
                for (;;) { var x = await r.read(); if (x.done) break; text += x.value; }
                Log.info(text);
                var response = new Response(new Blob(['abc']).stream());
                Log.info(response.bodyUsed + ' ' + (await response.text()));
                var again = new Response('xyz');
                var chunks = 0, total = 0, reader = again.body.getReader();
                for (;;) { var y = await reader.read(); if (y.done) break; chunks++; total += y.value.length; }
                Log.info(chunks + ' ' + total + ' ' + again.bodyUsed);
                var clone = new Response('dup'); var copy = clone.clone();
                Log.info((await clone.text()) + (await copy.text()));
                Log.info(new Response(null).body === null);
            })();
            """);

        Assert.Equal(["héllo", "false abc", "1 3 true", "dupdup", "true"], log);
    }

    [Fact]
    public async Task FormData_MultipartBodies_ReadBackIntoEntriesAndFiles()
    {
        var (log, _) = await RunAsync("""
            (async function () {
                var fd = new FormData();
                fd.append('a', 'one'); fd.append('b', 'two é'); fd.append('f', new File(['file text'], 'n.txt', { type: 'text/plain' }));
                var back = await new Response(fd).formData();
                Log.info([back.get('a'), back.get('b'), back.get('f').name, back.get('f').type, await back.get('f').text()].join('|'));
                var u = await new Response('x=1&y=2', { headers: { 'content-type': 'application/x-www-form-urlencoded' } }).formData();
                Log.info(u.get('y'));
                try { await new Response('x', { headers: { 'content-type': 'text/plain' } }).formData(); } catch (e) { Log.info(e instanceof TypeError); }
            })();
            """);

        Assert.Equal(["one|two é|n.txt|text/plain|file text", "2", "true"], log);
    }

    [Fact]
    public async Task Headers_ForbidMethodOverrideValuesThatNameAForbiddenMethod()
    {
        var (log, _) = await RunAsync("""
            var h = new Headers(); // a plain Headers has no guard; a Request's does
            var r = new Request('/x', { headers: { 'X-HTTP-Method-Override': 'TRACE', 'X-Method': 'ok', 'X-HTTP-Method': 'get, track' } });
            Log.info([r.headers.has('x-http-method-override'), r.headers.get('x-method'), r.headers.has('x-http-method')].join('|'));
            var ok = new Request('/x', { headers: { 'X-HTTP-Method-Override': 'PATCH' } });
            Log.info(ok.headers.get('x-http-method-override'));
            """);

        Assert.Equal(["false|ok|false", "PATCH"], log);
    }

    private static ScriptNetworkResponse? Cors(ScriptNetworkRequest r) => r.Url.AbsolutePath switch
    {
        "/open" => Reply(r, 200, "open", ("Access-Control-Allow-Origin", "*"), ("X-Hidden", "h"), ("X-Shown", "s"), ("Access-Control-Expose-Headers", "x-shown")),
        "/closed" => Reply(r, 200, "closed"),
        "/preflighted" when r.Method == "OPTIONS" => Reply(r, 200, "", ("Access-Control-Allow-Origin", "*"), ("Access-Control-Allow-Methods", "PUT"), ("Access-Control-Allow-Headers", "x-token")),
        "/preflighted" => Reply(r, 200, "done", ("Access-Control-Allow-Origin", "*")),
        "/refused" when r.Method == "OPTIONS" => Reply(r, 200, "", ("Access-Control-Allow-Origin", "*")),
        "/refused" => Reply(r, 200, "never", ("Access-Control-Allow-Origin", "*")),
        "/redirect" => Reply(r, 302, "", ("Location", "/open"), ("Access-Control-Allow-Origin", "*")),
        _ => Reply(r, 200, "plain"),
    };

    [Fact]
    public async Task Fetch_CrossOrigin_NeedsAllowOrigin_AndShowsOnlySafelistedAndExposedHeaders()
    {
        var (log, network) = await RunAsync("""
            (async function () {
                var open = await fetch('http://other.test/open');
                Log.info([open.type, open.headers.get('x-shown'), open.headers.get('x-hidden'), await open.text()].join('|'));
                try { await fetch('http://other.test/closed'); } catch (e) { Log.info('closed ' + (e instanceof TypeError)); }
                var opaque = await fetch('http://other.test/closed', { mode: 'no-cors' });
                Log.info([opaque.type, opaque.status, opaque.url === '', opaque.body === null].join('|'));
                try { await fetch('http://other.test/open', { mode: 'same-origin' }); } catch (e) { Log.info('same-origin ' + (e instanceof TypeError)); }
                var same = await fetch('/dir/local');
                Log.info(same.type);
            })();
            """, Cors);

        Assert.Equal(["cors|s||open", "closed true", "opaque|0|true|true", "same-origin true", "basic"], log);
        Assert.All(network.Requests.Where(r => r.Url.Host == "other.test"), r => Assert.NotEqual("OPTIONS", r.Method));
    }

    [Fact]
    public async Task Fetch_CrossOrigin_PreflightsANonSimpleRequest_AndStopsWhenItIsNotAllowed()
    {
        var (log, network) = await RunAsync("""
            (async function () {
                var ok = await fetch('http://other.test/preflighted', { method: 'PUT', headers: { 'X-Token': '1' } });
                Log.info(await ok.text());
                try { await fetch('http://other.test/refused', { method: 'PUT' }); } catch (e) { Log.info('refused ' + (e instanceof TypeError)); }
            })();
            """, Cors);

        Assert.Equal(["done", "refused true"], log);
        var preflight = network.Requests.First(r => r.Method == "OPTIONS");
        Assert.Equal("PUT", Header(preflight, "Access-Control-Request-Method"));
        Assert.Equal("x-token", Header(preflight, "Access-Control-Request-Headers"));
        Assert.Equal("*/*", Header(preflight, "Accept"));
        Assert.Equal(1, network.Requests.Count(r => r.Url.AbsolutePath == "/refused"));
    }

    [Fact]
    public async Task Fetch_Redirects_AreFollowedByHand_AndTheModeDecidesWhatTheScriptGets()
    {
        var (log, network) = await RunAsync("""
            (async function () {
                var followed = await fetch('http://other.test/redirect');
                Log.info([followed.redirected, followed.url, await followed.text()].join('|'));
                var manual = await fetch('/dir/redirect', { redirect: 'manual' });
                Log.info([manual.type, manual.status, manual.headers.get('location')].join('|'));
                try { await fetch('/dir/redirect', { redirect: 'error' }); } catch (e) { Log.info('error ' + (e instanceof TypeError)); }
            })();
            """, r => r.Url.AbsolutePath == "/dir/redirect" ? Reply(r, 302, "", ("Location", "/dir/target")) : Cors(r));

        Assert.Equal(["true|http://other.test/open|open", "opaqueredirect|0|", "error true"], log);
        Assert.All(network.Requests, r => Assert.False(r.FollowRedirects));
    }

    [Fact]
    public async Task ABundleGlobal_FirstNamedWhileAPromiseJobIsWaiting_IsStillThereForThatJob()
    {
        var (log, _) = await RunAsync("""
            Promise.resolve().then(function () { Log.info(typeof WritableStream + ' ' + typeof ReadableStream); });
            var ws = new WritableStream();
            Log.info('made');
            """);

        Assert.Contains("made", log);
        Assert.Contains("function function", log);
    }

    [Fact]
    public async Task WritableStream_FinishesTheWriteInFlightWhenAborted_AndPipeToClosesTheDestination()
    {
        var (log, _) = await RunAsync("""
            (async function () { try {
                var events = [], release;
                var ws = new WritableStream({ write: function (chunk) { events.push('write ' + chunk); return new Promise(function (r) { release = r; }); }, abort: function (reason) { events.push('abort ' + reason); } });
                var writer = ws.getWriter();
                var first = writer.write(1), second = writer.write(2).catch(function (e) { return 'rejected ' + e; });
                await new Promise(function (r) { setTimeout(r, 0); });
                var aborted = writer.abort('why');
                release();
                var a1 = await first, a2 = await second, a3 = await aborted; Log.info((a1 === undefined) + ',' + a2 + ',' + (a3 === undefined) + ',' + events.join(','));
                var closed = [];
                var sink = new WritableStream({ write: function (c) { closed.push(c); }, close: function () { closed.push('close'); } });
                await new ReadableStream({ start: function (c) { c.enqueue('a'); c.enqueue('b'); c.close(); } }).pipeTo(sink);
                Log.info(closed.join());
            } catch (e) { Log.info('threw ' + e + ' ' + e.stack); } })();
            """);

        Assert.Equal(["true,rejected why,true,write 1,abort why", "a,b,close"], log);
    }

    [Fact]
    public async Task ByteStreams_ServeBYOBReads_AndStructuredCloneTransfersBuffers()
    {
        var (log, _) = await RunAsync("""
            (async function () {
                var stream = new ReadableStream({
                    type: 'bytes',
                    pull: function (c) { var v = c.byobRequest.view; v[0] = 7; v[1] = 8; c.byobRequest.respond(2); c.close(); }
                });
                var reader = stream.getReader({ mode: 'byob' });
                var result = await reader.read(new Uint8Array(4));
                Log.info(result.done + ' ' + result.value.byteLength + ' ' + Array.from(result.value).join());
                var again = await reader.read(new Uint8Array(4));
                Log.info(again.done + ' ' + again.value.byteLength);
                var buffer = new ArrayBuffer(8), copy = structuredClone({ b: buffer, n: [1, 2] }, { transfer: [buffer] });
                Log.info(buffer.byteLength + ' ' + copy.b.byteLength + ' ' + copy.n.join());
                try { structuredClone(function () {}); } catch (e) { Log.info(e.name); }
                var defaults = new ReadableStream({ type: 'bytes', start: function (c) { c.enqueue(new Uint8Array([1, 2, 3])); c.close(); } }).getReader();
                Log.info(Array.from((await defaults.read()).value).join());
            })();
            """);

        Assert.Equal(["false 2 7,8", "true 0", "0 8 1,2", "DataCloneError", "1,2,3"], log);
    }

    [Fact]
    public async Task MessageChannel_DeliversClonedMessagesInOrder_AndTransfersPorts()
    {
        var (log, _) = await RunAsync("""
            (async function () { try {
                var channel = new MessageChannel(), got = [];
                channel.port2.onmessage = function (e) { got.push(JSON.stringify(e.data) + ':' + e.ports.length); };
                var original = { n: 1 };
                channel.port1.postMessage(original);
                channel.port1.postMessage('two');
                original.n = 99;
                await new Promise(function (r) { setTimeout(r, 20); });
                Log.info(got.join(' '));
                var other = new MessageChannel(), moved;
                channel.port2.onmessage = function (e) { moved = e.ports[0]; };
                channel.port1.postMessage('carry', [other.port2]);
                await new Promise(function (r) { setTimeout(r, 20); });
                var back; other.port1.onmessage = function (e) { back = e.data; };
                moved.postMessage('hello');
                other.port1.postMessage('x');
                await new Promise(function (r) { setTimeout(r, 20); });
                Log.info((moved instanceof MessagePort) + ' ' + back);
                try { channel.port1.postMessage(1, [channel.port1]); } catch (e) { Log.info(e.name); }
                var seen = new Promise(function (r) { addEventListener('message', r, { once: true }); });
                postMessage({ a: [1, 2] }, '*');
                var event = await seen;
                Log.info(JSON.stringify(event.data) + ' ' + (event.source === window));
            } catch (e) { Log.info('threw ' + e + ' ' + e.stack); } })();
            """);

        Assert.Equal(["{\"n\":1}:0 \"two\":0", "true hello", "DataCloneError", "{\"a\":[1,2]} true"], log);
    }

    [Fact]
    public async Task Streams_AreTransferred_ThroughPostMessage_AndStructuredClone()
    {
        var (log, _) = await RunAsync("""
            (async function () { try {
                var rs = new ReadableStream({ start: function (c) { c.enqueue('a'); c.enqueue({ b: 2 }); c.close(); } });
                var moved = structuredClone(rs, { transfer: [rs] });
                Log.info(rs.locked + ' ' + (moved instanceof ReadableStream));
                var reader = moved.getReader(), chunks = [];
                for (var r = await reader.read(); !r.done; r = await reader.read()) chunks.push(JSON.stringify(r.value));
                Log.info(chunks.join());
                var written = [], ws = new WritableStream({ write: function (c) { written.push(c); } });
                var movedWriter = structuredClone(ws, { transfer: [ws] });
                var w = movedWriter.getWriter();
                await w.write(1); await w.write('two'); await w.close();
                await new Promise(function (r) { setTimeout(r, 50); });
                Log.info(ws.locked + ' ' + written.join());
                var locked = new ReadableStream(); locked.getReader();
                try { structuredClone(locked, { transfer: [locked] }); } catch (e) { Log.info(e.name); }
                var t = new TransformStream();
                try { structuredClone(t, { transfer: [t] }); } catch (e) { Log.info(e.name); }
            } catch (e) { Log.info('threw ' + e + ' ' + e.stack); } })();
            """);

        Assert.Equal(["true true", "\"a\",{\"b\":2}", "false 1,two", "DataCloneError", "DataCloneError"], log);
    }
}
