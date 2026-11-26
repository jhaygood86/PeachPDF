using System.Text;

namespace PeachBrackets.WptRunner.Server;

/// <summary>
/// C# ports of the request-echoing, status, redirect, CORS and stash handlers the <c>xhr/</c> and <c>fetch/api/</c> tests (and a few others) use.
/// Each stands for the <c>.py</c> file of the same path; where the Python writes raw bytes to the socket or sleeps, the port answers at once with
/// the same content (the clock here is virtual, and a response is delivered whole).
/// </summary>
internal static class NetworkHandlers
{
    private static string Header(ServerRequest request, string name, string fallback) => request.Headers.TryGetValue(name, out var value) ? value : fallback;

    public static IEnumerable<KeyValuePair<string, Action<ServerRequest, ServerResponse>>> All()
    {
        foreach (var directory in new[] { "xhr/resources/", "fetch/api/resources/" })
        {
            yield return new(directory + "status.py", Status);
            yield return new(directory + "inspect-headers.py", directory.StartsWith("xhr", StringComparison.Ordinal) ? XhrInspectHeaders : FetchInspectHeaders);
            yield return new(directory + "trickle.py", Trickle);
            yield return new(directory + "cache.py", Cache);
            yield return new(directory + "echo-content.py", EchoContent);
        }

        yield return new("xhr/resources/content.py", Content);
        yield return new("xhr/resources/redirect.py", XhrRedirect);
        yield return new("xhr/resources/headers.py", Headers);
        yield return new("xhr/resources/delay.py", Delay);
        yield return new("xhr/resources/requri.py", RequestUri);
        yield return new("xhr/resources/reset-token.py", ResetToken);
        yield return new("xhr/resources/accept.py", (request, response) => { response.Set("Content-Type", "text/plain"); response.SetText(Header(request, "Accept", "NO")); });
        yield return new("xhr/resources/accept-language.py", (request, response) => { response.Set("Content-Type", "text/plain"); response.SetText(Header(request, "Accept-Language", "NO")); });
        yield return new("xhr/resources/echo-method.py", (request, response) => { response.Set("Content-Type", "text/plain"); response.SetText(request.Method); });
        yield return new("xhr/resources/echo-content-type.py", (request, response) => { response.Set("Content-Type", "text/plain"); response.SetText(Header(request, "Content-Type", string.Empty)); });
        yield return new("xhr/resources/echo-content-cors.py", EchoContentCors);
        yield return new("xhr/resources/corsenabled.py", CorsEnabled);
        yield return new("xhr/resources/conditional.py", Conditional);
        yield return new("xhr/resources/access-control-basic-allow.py", BasicAllow);
        yield return new("xhr/resources/access-control-basic-allow-star.py", BasicAllowStar);
        yield return new("xhr/resources/access-control-basic-denied.py", BasicDenied);
        yield return new("xhr/resources/access-control-basic-put-allow.py", BasicPutAllow);
        yield return new("xhr/resources/access-control-basic-options-not-supported.py", OptionsNotSupported);
        yield return new("xhr/resources/access-control-preflight-denied.py", PreflightDenied);
        yield return new("xhr/resources/access-control-preflight-request-invalid-status.py", PreflightInvalidStatus);
        yield return new("xhr/resources/redirect-cors.py", RedirectCors);
        yield return new("xhr/resources/content-in-chunks.py", ContentInChunks);
        yield return new("xhr/resources/access-control-origin-header.py", OriginHeader);
        yield return new("xhr/resources/access-control-basic-allow-no-credentials.py", BasicAllow);
        yield return new("xhr/resources/last-modified.py", LastModified);
        yield return new("fetch/api/resources/echo-header-bytes.py", EchoHeaderBytes);

        yield return new("fetch/api/resources/clean-stash.py", CleanStash);
        yield return new("fetch/api/resources/stash-take.py", StashTake);
        yield return new("fetch/api/resources/stash-put.py", StashPut);
        yield return new("fetch/api/resources/preflight.py", Preflight);
        yield return new("fetch/api/resources/redirect.py", FetchRedirect);
        yield return new("fetch/api/resources/method.py", Method);
        yield return new("fetch/api/resources/dump-authorization-header.py", DumpAuthorization);
    }

    // status.py: a status with the text the `text` parameter gives, content and type from the query.
    private static void Status(ServerRequest request, ServerResponse response)
    {
        response.Status = int.TryParse(request.First("code"), out var code) ? code : 200;
        response.StatusText = request.First("text", "OMG");
        response.Set("Content-Type", request.First("type", string.Empty)!);
        response.Set("X-Request-Method", request.Method);
        response.SetText(request.First("content", string.Empty)!);
    }

    // xhr content.py: the request echoed back in headers, and its body (or the `content` parameter) as the body.
    private static void Content(ServerRequest request, ServerResponse response)
    {
        var charset = request.First("response_charset_label") is { } label ? ";charset=" + label : string.Empty;
        response.Set("Content-Type", "text/plain" + charset);
        response.Set("X-Request-Method", request.Method);
        response.Set("X-Request-Query", request.Url.Query.Length > 1 ? request.Url.Query.TrimStart('?') : "NO");
        response.Set("X-Request-Content-Length", Header(request, "Content-Length", request.Body.Length > 0 ? request.Body.Length.ToString() : "NO"));
        response.Set("X-Request-Content-Type", Header(request, "Content-Type", "NO"));
        if (request.First("content") is { } content)
            response.SetText(content);
        else
            response.Body = request.Body;
    }

    private static void XhrRedirect(ServerRequest request, ServerResponse response)
    {
        var code = int.TryParse(request.First("code"), out var parsed) ? parsed : 302;
        if (request.Has("followed"))
        {
            response.Set("Content:Type", "text/plain");
            response.SetText("MAGIC HAPPENED");
            return;
        }

        var location = request.First("location") ?? "/" + request.Path + "?followed";
        if (location.StartsWith("redirect.py", StringComparison.Ordinal))
            location += "&code=" + code;

        response.Status = code;
        response.StatusText = "WEBSRT MARKETING";
        response.Set("Location", location);
        response.SetText("TEST");
    }

    private static void Headers(ServerRequest request, ServerResponse response)
    {
        response.Set("Content-Type", "text/plain");
        response.Set("X-Custom-Header", "test");
        response.Set("Set-Cookie", "test");
        response.Set("Set-Cookie2", "test");
        response.Set("X-Custom-Header-Empty", string.Empty);
        response.Set("X-Custom-Header-Comma", "1");
        response.Append("X-Custom-Header-Comma", "2");
        response.Set("X-Custom-Header-Bytes", "â\u0080¦");
        response.SetText("TEST");
    }

    private static void Delay(ServerRequest request, ServerResponse response)
    {
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Access-Control-Allow-Methods", "YO");
        response.Set("Content-Type", "text/plain");
        response.SetText("TEST_DELAY");
    }

    private static void Trickle(ServerRequest request, ServerResponse response)
    {
        var count = int.TryParse(request.First("count"), out var parsed) ? Math.Min(parsed, 200) : 50;
        response.Set("Content-Type", "text/plain");
        response.SetText(string.Concat(Enumerable.Repeat("TEST_TRICKLE\n", count)));
    }

    private static void RequestUri(ServerRequest request, ServerResponse response) =>
        response.SetText(request.Has("full") ? request.Url.AbsoluteUri : request.Url.AbsolutePath);

    private static void ResetToken(ServerRequest request, ServerResponse response)
    {
        response.Set("Access-Control-Allow-Origin", Header(request, "Origin", string.Empty));
        request.Server!.Stash.Put(request.First("token") ?? string.Empty, string.Empty);
        response.SetText("PASS");
    }

    private static void XhrInspectHeaders(ServerRequest request, ServerResponse response)
    {
        if (request.Has("cors"))
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Credentials", "true");
            response.Set("Access-Control-Allow-Methods", "GET, POST, PUT, FOO");
            response.Set("Access-Control-Allow-Headers", "x-test, x-foo");
            response.Set("Access-Control-Expose-Headers", "x-request-method, x-request-content-type, x-request-query, x-request-content-length");
        }

        response.Set("content-type", "text/plain");
        var filterValue = request.First("filter_value", string.Empty)!;
        var filterName = request.First("filter_name", string.Empty)!.ToLowerInvariant();
        var text = new StringBuilder();
        foreach (var (name, value) in request.Headers)
        {
            if (filterValue.Length > 0)
            {
                if (value == filterValue)
                    text.Append(name).Append(',');
            }
            else if (name.Equals(filterName, StringComparison.OrdinalIgnoreCase))
            {
                text.Append(name).Append(": ").Append(value).Append('\n');
            }
        }

        response.SetText(text.ToString());
    }

    private static void FetchInspectHeaders(ServerRequest request, ServerResponse response)
    {
        var checkedHeaders = request.First("headers")?.Split('|') ?? [];
        foreach (var header in checkedHeaders)
        {
            if (request.Headers.TryGetValue(header, out var value))
                response.Append("x-request-" + header, value);
        }

        if (request.Has("cors"))
        {
            response.Append("Access-Control-Allow-Origin", Header(request, "Origin", "*"));
            response.Append("Access-Control-Allow-Credentials", "true");
            response.Append("Access-Control-Allow-Methods", "GET, POST, HEAD");
            response.Append("Access-Control-Expose-Headers", string.Join(", ", checkedHeaders.Select(h => "x-request-" + h)));
            response.Append("Access-Control-Allow-Headers", request.First("allow_headers") ?? string.Join(", ", request.Headers.Keys));
        }

        response.Set("content-type", "text/plain");
    }

    private static void Cache(ServerRequest request, ServerResponse response)
    {
        const string etag = "\"123abc\"";
        if (Header(request, "If-None-Match", string.Empty) == etag)
        {
            response.Status = 304;
            response.StatusText = "Not Modified";
            response.Set("X-HTTP-STATUS", "304");
            return;
        }

        response.Set("ETag", etag);
        response.Set("Content-Type", "text/plain");
        response.SetText("lorem ipsum dolor sit amet");
    }

    private static void EchoContent(ServerRequest request, ServerResponse response)
    {
        response.Set("X-Request-Method", request.Method);
        response.Set("X-Request-Content-Length", Header(request, "Content-Length", request.Body.Length > 0 ? request.Body.Length.ToString() : "NO"));
        response.Set("X-Request-Content-Type", Header(request, "Content-Type", "NO"));
        response.Set("Content-Type", "text/plain");
        response.Body = request.Body;
    }

    private static void EchoContentCors(ServerRequest request, ServerResponse response)
    {
        response.Set("X-Request-Method", request.Method);
        response.Set("X-Request-Content-Length", Header(request, "Content-Length", request.Body.Length > 0 ? request.Body.Length.ToString() : "NO"));
        response.Set("X-Request-Content-Type", Header(request, "Content-Type", "NO"));
        response.Set("Access-Control-Allow-Credentials", "true");
        response.Set("Content-Type", "text/plain");
        // The Python asks the `origin` parameter for all three, falling back to a different request header each time.
        var named = request.First("origin");
        if ((named ?? request.Headers.GetValueOrDefault("Origin")) is { } allowed)
            response.Set("Access-Control-Allow-Origin", allowed);
        if ((named ?? request.Headers.GetValueOrDefault("Access-Control-Request-Headers")) is { } headers)
            response.Set("Access-Control-Allow-Headers", headers);
        if ((named ?? request.Headers.GetValueOrDefault("Access-Control-Request-Method")) is { } methods)
            response.Set("Access-Control-Allow-Methods", "OPTIONS, " + methods);
        response.Body = request.Body;
    }

    private static void CorsEnabled(ServerRequest request, ServerResponse response)
    {
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Access-Control-Allow-Credentials", "true");
        response.Set("Access-Control-Allow-Methods", "GET, POST, PUT, FOO");
        response.Set("Access-Control-Allow-Headers", request.Has("safelist_content_type") ? "content-type" : "x-test, x-foo");
        response.Set("Access-Control-Expose-Headers", "x-request-method, x-request-content-type, x-request-query, x-request-content-length, x-request-data");
        response.Set("X-Request-Method", request.Method);
        response.Set("X-Request-Query", request.Url.Query.Length > 1 ? request.Url.Query.TrimStart('?') : "NO");
        response.Set("X-Request-Content-Length", Header(request, "Content-Length", request.Body.Length > 0 ? request.Body.Length.ToString() : "NO"));
        response.Set("X-Request-Content-Type", Header(request, "Content-Type", "NO"));
        response.Set("X-Request-Data", Encoding.Latin1.GetString(request.Body));
        response.SetText("Test");
    }

    private static void Conditional(ServerRequest request, ServerResponse response)
    {
        var tag = request.First("tag");
        var date = request.First("date", string.Empty)!;
        var cors = request.First("cors");
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Headers", "IF-NONE-MATCH");
            return;
        }

        if (!string.IsNullOrEmpty(tag))
            response.Set("ETag", $"\"{tag}\"");
        else if (date.Length > 0)
            response.Set("Last-Modified", date);

        if (!string.IsNullOrEmpty(cors))
            response.Set("Access-Control-Allow-Origin", "*");

        var match = request.Headers.GetValueOrDefault("If-None-Match");
        var modified = request.Headers.GetValueOrDefault("If-Modified-Since");
        if ((match is not null && match == tag) || (modified is not null && modified == date))
        {
            response.Status = 304;
            response.StatusText = "SUPERCOOL";
            return;
        }

        if (string.IsNullOrEmpty(cors))
            response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Content-Type", "text/plain");
        response.SetText("MAYBE NOT");
    }

    // content-in-chunks.py: each hex `chunk` parameter, joined (the chunking itself is not visible to a script here).
    private static void ContentInChunks(ServerRequest request, ServerResponse response)
    {
        response.Set("Content-Type", request.First("type", "text/plain")!);
        using var body = new MemoryStream();
        foreach (var chunk in request.All("chunk"))
            body.Write(Convert.FromHexString(chunk));
        response.Body = body.ToArray();
    }

    private static void OriginHeader(ServerRequest request, ServerResponse response)
    {
        response.Set("Content-Type", "text/plain");
        response.Set("Cache-Control", "no-cache, no-store");
        response.Set("Access-Control-Allow-External", "true");
        response.Set("Access-Control-Allow-Origin", "*");
        response.SetText("PASS: Cross-domain access allowed.\nHTTP_ORIGIN: " + Header(request, "Origin", string.Empty));
    }

    // last-modified.py: the well-formed.xml next to it, with the file's modification time as Last-Modified.
    private static void LastModified(ServerRequest request, ServerResponse response)
    {
        var file = request.Server!.ReadFile("xhr/resources/well-formed.xml");
        response.Set("Last-Modified", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", System.Globalization.CultureInfo.InvariantCulture));
        response.Set("Content-Type", "application/xml");
        response.Body = file ?? [];
    }

    private static void EchoHeaderBytes(ServerRequest request, ServerResponse response)
    {
        response.Set("content-type", "text/plain");
        var names = (request.First("headers") ?? string.Empty).Split('|');
        response.SetText(string.Join("\n", names.Select(n => n + ": " + Convert.ToHexString(Encoding.Latin1.GetBytes(Header(request, n, string.Empty))).ToLowerInvariant())));
    }

    // ---- CORS fixtures

    private static void BasicAllow(ServerRequest request, ServerResponse response)
    {
        response.Set("Content-Type", "text/plain");
        response.Set("Access-Control-Allow-Credentials", "true");
        response.Set("Access-Control-Allow-Origin", Header(request, "Origin", string.Empty));
        response.SetText("PASS: Cross-domain access allowed.");
    }

    private static void BasicAllowStar(ServerRequest request, ServerResponse response)
    {
        response.Set("Content-Type", "text/plain");
        response.Set("Access-Control-Allow-Origin", "*");
        response.SetText("PASS: Cross-domain access allowed.");
    }

    private static void BasicDenied(ServerRequest request, ServerResponse response)
    {
        response.Set("Cache-Control", "no-store");
        response.Set("Content-Type", "text/plain");
        response.SetText("FAIL: Cross-domain access allowed.");
    }

    private static void BasicPutAllow(ServerRequest request, ServerResponse response)
    {
        response.Set("Content-Type", "text/plain");
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Credentials", "true");
            response.Set("Access-Control-Allow-Methods", "PUT");
            response.Set("Access-Control-Allow-Origin", Header(request, "Origin", string.Empty));
        }
        else if (request.Method == "PUT")
        {
            response.Set("Access-Control-Allow-Credentials", "true");
            response.Set("Access-Control-Allow-Origin", Header(request, "Origin", string.Empty));
            response.SetText("PASS: Cross-domain access allowed.\n" + Encoding.UTF8.GetString(request.Body));
        }
        else
        {
            response.SetText("Wrong method: " + request.Method);
        }
    }

    private static void OptionsNotSupported(ServerRequest request, ServerResponse response)
    {
        response.Set("Cache-Control", "no-store");
        if (request.Method != "OPTIONS")
        {
            if (request.Headers.TryGetValue("Origin", out var origin))
            {
                response.Set("Access-Control-Allow-Credentials", "true");
                response.Set("Access-Control-Allow-Origin", origin);
            }
            else
            {
                response.Status = 500;
            }
        }
        else
        {
            response.Status = 400;
        }
    }

    private static void PreflightDenied(ServerRequest request, ServerResponse response)
    {
        var stash = request.Server!.Stash;
        var token = request.First("token") ?? string.Empty;
        var state = stash.Take(token) as string ?? "Uninitialized";
        var command = request.First("command");

        void Fail(string message)
        {
            response.SetText("FAIL: " + message);
            response.Status = 400;
        }

        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Origin", Header(request, "Origin", string.Empty));
        response.Set("Access-Control-Max-Age", "1");
        if (command == "reset")
        {
            if (request.Method == "GET")
            {
                stash.Put(token, string.Empty);
                response.SetText("Server state reset");
            }
            else
            {
                Fail("Invalid Method.");
            }
        }
        else if (state == "Uninitialized")
        {
            if (request.Method == "OPTIONS")
            {
                response.SetText("This request should not be displayed.");
                stash.Put(token, "Denied");
            }
            else
            {
                Fail(state);
            }
        }
        else if (state == "Denied")
        {
            if (request.Method == "GET" && command == "complete")
            {
                stash.Put(token, string.Empty);
                response.SetText("Request successfully blocked.");
            }
            else
            {
                stash.Put(token, "Deny Ignored");
                Fail("The request was not denied.");
            }
        }
        else if (state == "Deny Ignored")
        {
            stash.Put(token, string.Empty);
            Fail(state);
        }
        else
        {
            stash.Put(token, string.Empty);
            Fail("Unknown Error.");
        }
    }

    private static void PreflightInvalidStatus(ServerRequest request, ServerResponse response)
    {
        if (request.Method == "OPTIONS")
        {
            if (int.TryParse(request.First("code"), out var code) && code != 0)
                response.Status = code;
            response.Set("Access-Control-Max-Age", "1");
            response.Set("Access-Control-Allow-Headers", "x-pass");
        }

        response.Set("Cache-Control", "no-store");
        response.Set("Access-Control-Allow-Origin", Header(request, "Origin", string.Empty));
    }

    private static void RedirectCors(ServerRequest request, ServerResponse response)
    {
        var location = request.First("location") ?? string.Empty;
        if (request.Method == "OPTIONS")
        {
            if (request.Has("redirect_preflight"))
            {
                response.Status = 302;
                response.Set("Location", location);
            }

            response.Set("Access-Control-Allow-Methods", "GET");
            response.Set("Access-Control-Max-Age", "1");
        }
        else if (request.Method == "GET")
        {
            response.Status = 302;
            response.Set("Location", location);
        }

        if (request.Has("allow_origin"))
            response.Set("Access-Control-Allow-Origin", Header(request, "Origin", string.Empty));
        if (request.First("allow_header") is { } allowed)
            response.Set("Access-Control-Allow-Headers", allowed);
    }

    // ---- fetch fixtures

    private static void CleanStash(ServerRequest request, ServerResponse response) =>
        response.SetText(request.Server!.Stash.Take(request.First("token") ?? string.Empty) is not null ? "1" : "0");

    // The directory of the request path, as stash keys are scoped by it.
    private static string Directory(ServerRequest request)
    {
        var path = request.Url.AbsolutePath;
        return path[..(path.LastIndexOf('/') + 1)];
    }

    private static void StashTake(ServerRequest request, ServerResponse response)
    {
        response.Set("Access-Control-Allow-Origin", "*");
        response.Set("Content-Type", "application/json");
        var value = request.Server!.Stash.Take(request.First("key") ?? string.Empty, Directory(request));
        response.SetText(System.Text.Json.JsonSerializer.Serialize(value));
    }

    private static void StashPut(ServerRequest request, ServerResponse response)
    {
        if (request.Method == "OPTIONS")
        {
            response.Set("Access-Control-Allow-Origin", "*");
            response.Set("Access-Control-Allow-Methods", "*");
            response.Set("Access-Control-Allow-Headers", "*");
            response.SetText("done");
            return;
        }

        if (!request.Has("disallow_cross_origin"))
        {
            response.Set("Access-Control-Allow-Origin", "*");
        }
        else
        {
            var sameOrigin = request.First("mode") == "no-cors" || request.First("frame_origin") == request.Url.GetLeftPart(UriPartial.Authority);
            if (!sameOrigin)
            {
                response.SetText("not stashing for cors request");
                return;
            }
        }

        request.Server!.Stash.Put(request.First("key") ?? string.Empty, request.First("value") ?? string.Empty, Directory(request));
        response.SetText("done");
    }

    private sealed class PreflightState
    {
        public string ControlRequestHeaders = string.Empty;
        public string Preflight = "0";
        public string PreflightReferrer = string.Empty;
        public string PreflightUserAgent = string.Empty;
        public int OptionsCount;
        public bool FailNext;
    }

    private static void Preflight(ServerRequest request, ServerResponse response)
    {
        var stash = request.Server!.Stash;
        var state = new PreflightState();
        var token = request.First("token");
        if (!string.IsNullOrEmpty(token) && stash.Take(token) is PreflightState taken)
            state = taken;

        response.Set("Content-Type", "text/plain");
        void Save()
        {
            if (!string.IsNullOrEmpty(token))
                stash.Put(token, state);
        }

        var shouldFail = request.Method != "OPTIONS" && state.FailNext;
        if (shouldFail)
        {
            state.FailNext = false;
        }
        else if (request.First("origin") is { } origins)
        {
            foreach (var origin in origins.Split(", "))
                response.Append("Access-Control-Allow-Origin", origin);
        }
        else
        {
            response.Append("Access-Control-Allow-Origin", "*");
        }

        if (request.Has("clear-stash"))
        {
            response.SetText(state.Preflight == "1" ? "1" : "0");
            return;
        }

        if (request.Has("options-count"))
        {
            Save();
            response.SetText(state.OptionsCount.ToString());
            return;
        }

        if (request.Has("fail-next"))
        {
            state.FailNext = true;
            Save();
            response.SetText("1");
            return;
        }

        if (request.Has("credentials"))
            response.Append("Access-Control-Allow-Credentials", "true");

        if (request.Method == "OPTIONS")
        {
            if (!request.Headers.ContainsKey("Access-Control-Request-Method"))
            {
                response.Status = 400;
                response.SetText("ERROR: No access-control-request-method in preflight!");
                return;
            }

            if (Header(request, "Accept", string.Empty) != "*/*")
            {
                response.Status = 400;
                response.SetText("ERROR: Invalid access in preflight!");
                return;
            }

            if (request.Has("control_request_headers"))
                state.ControlRequestHeaders = Header(request, "Access-Control-Request-Headers", string.Empty);
            if (request.First("max_age") is { } maxAge)
                response.Append("Access-Control-Max-Age", maxAge);
            if (request.First("allow_headers") is { } allowHeaders)
                response.Append("Access-Control-Allow-Headers", allowHeaders);
            if (request.First("allow_methods") is { } allowMethods)
                response.Append("Access-Control-Allow-Methods", allowMethods);

            response.Status = int.TryParse(request.First("preflight_status"), out var status) ? status : 200;
            state.Preflight = "1";
            state.PreflightReferrer = Header(request, "Referer", string.Empty);
            state.PreflightUserAgent = Header(request, "User-Agent", string.Empty);
            state.OptionsCount++;
            Save();
            return;
        }

        if (request.Has("checkUserAgentHeaderInPreflight") && Header(request, "User-Agent", string.Empty) != state.PreflightUserAgent)
        {
            response.Status = 400;
            response.SetText("ERROR: No user-agent header in preflight");
            return;
        }

        response.Append("Access-Control-Expose-Headers", "x-did-preflight, x-control-request-headers, x-referrer, x-preflight-referrer, x-origin");
        response.Append("x-did-preflight", state.Preflight);
        response.Append("x-control-request-headers", state.ControlRequestHeaders);
        response.Append("x-preflight-referrer", state.PreflightReferrer);
        response.Append("x-referrer", Header(request, "Referer", string.Empty));
        response.Append("x-origin", Header(request, "Origin", string.Empty));
        Save();
    }

    private sealed class RedirectState
    {
        public int Count;
        public string Preflight = "0";
    }

    private static void FetchRedirect(ServerRequest request, ServerResponse response)
    {
        var stash = request.Server!.Stash;
        var state = new RedirectState();
        var status = 302;
        response.Set("Content-Type", "text/plain");
        response.Set("Cache-Control", "no-cache");
        response.Set("Pragma", "no-cache");
        if (request.Headers.TryGetValue("Origin", out var origin))
        {
            response.Append("Access-Control-Allow-Origin", origin);
            response.Append("Access-Control-Allow-Credentials", "true");
        }
        else
        {
            response.Append("Access-Control-Allow-Origin", "*");
        }

        var token = request.First("token");
        if (!string.IsNullOrEmpty(token) && stash.Take(token) is RedirectState taken)
            state = taken;

        if (request.Method == "OPTIONS")
        {
            if (request.First("allow_headers") is { } allowed)
                response.Append("Access-Control-Allow-Headers", allowed);
            state.Preflight = "1";
            if (!request.Has("redirect_preflight"))
            {
                if (!string.IsNullOrEmpty(token))
                    stash.Put(token, state);
                return;
            }
        }

        if (int.TryParse(request.First("redirect_status"), out var redirectStatus))
            status = redirectStatus;

        state.Count++;
        if (request.First("location") is { } url)
        {
            if (!request.Has("simple") && (url.StartsWith("http:", StringComparison.Ordinal) || url.StartsWith("https:", StringComparison.Ordinal) || !url.Contains(':')))
            {
                var parameters = string.Join("&", ServerRequest.ParseQuery(request.Url.Query).GroupBy(p => p.Key).Select(g => Uri.EscapeDataString(g.Key) + "=" + Uri.EscapeDataString(g.First().Value)));
                url += (url.Contains('?') ? "&" : "?") + parameters + "&count=" + state.Count;
            }

            response.Append("Location", url);
        }

        if (request.First("redirect_referrerpolicy") is { } policy)
            response.Append("Referrer-Policy", policy);

        if (!string.IsNullOrEmpty(token))
        {
            stash.Put(token, state);
            if (int.TryParse(request.First("max_count"), out var max) && state.Count > max)
            {
                response.SetText((state.Count - 1).ToString());
                return;
            }
        }

        response.Status = status;
    }

    private static void Method(ServerRequest request, ServerResponse response)
    {
        if (request.Has("cors"))
        {
            response.Append("Access-Control-Allow-Origin", "*");
            response.Append("Access-Control-Allow-Credentials", "true");
            response.Append("Access-Control-Allow-Methods", "GET, POST, PUT, FOO");
            response.Append("Access-Control-Allow-Headers", "x-test, x-foo");
            response.Append("Access-Control-Expose-Headers", "x-request-method");
        }

        response.Append("x-request-method", request.Method);
        foreach (var name in new[] { "Content-Type", "Content-Length", "Content-Encoding", "Content-Language", "Content-Location" })
            response.Append("x-request-" + name.ToLowerInvariant(), Header(request, name, name == "Content-Length" && request.Body.Length > 0 ? request.Body.Length.ToString() : "NO"));
        response.Body = request.Body;
    }

    private static void DumpAuthorization(ServerRequest request, ServerResponse response)
    {
        response.Set("Content-Type", "text/html");
        response.Set("Cache-Control", "no-cache");
        if (request.Has("strip_auth_header") && request.Method == "OPTIONS" && Header(request, "Access-Control-Request-Headers", string.Empty).Contains("authorization", StringComparison.OrdinalIgnoreCase))
        {
            response.Status = 500;
            response.SetText("fail");
            return;
        }

        if (request.Headers.TryGetValue("Origin", out var origin))
        {
            response.Append("Access-Control-Allow-Origin", origin);
            response.Append("Access-Control-Allow-Credentials", "true");
        }
        else
        {
            response.Append("Access-Control-Allow-Origin", "*");
        }

        response.Append("Access-Control-Allow-Headers", "Authorization");
        response.SetText(request.Headers.TryGetValue("Authorization", out var authorization) ? authorization : "none");
    }
}
