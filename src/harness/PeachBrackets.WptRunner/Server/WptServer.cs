using PeachBrackets.ScriptHosting;
using PeachDrawing.Core;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachBrackets.WptRunner.Server;

/// <summary>
/// The web-platform-tests server, in process: it serves a checkout as the origins the tests are written against (<c>web-platform.test</c> and its
/// sibling domains on the ports WPT uses) and does what <c>wptserve</c> does for the tests this runner runs: <c>.sub.</c> substitution, <c>?pipe=</c>,
/// <c>.headers</c> files, the page a <c>.any.js</c> or <c>.window.js</c> file stands for, ported <c>.py</c> handlers (<see cref="WptHandlers"/>), a stash,
/// cookies and a log of requests. There is no network: a request is a call, answered from the checkout.
/// </summary>
/// <param name="root">the checkout</param>
internal sealed partial class WptServer(string root)
{
    public const string Host = "web-platform.test";
    public const string AltHost = "not-web-platform.test";

    /// <summary>The origin a test page is loaded from.</summary>
    public const string Origin = "http://web-platform.test:8000";

    /// <summary>The origin a secure test page (<c>.https.</c> in its name) is loaded from.</summary>
    public const string SecureOrigin = "https://web-platform.test:8443";

    /// <summary>The origin of the test page this server is serving, which a request the page makes names as its <c>Origin</c>; <see cref="Origin"/> unless a test says otherwise.</summary>
    public string PageOrigin { get; set; } = Origin;

    /// <summary>The origin a test is loaded from: WPT serves a file with <c>.https.</c> in its name over TLS.</summary>
    public static string OriginFor(string wptPath) => System.IO.Path.GetFileName(wptPath).Contains(".https.", StringComparison.Ordinal) ? SecureOrigin : Origin;

    private static readonly string[] Subdomains = ["www", "www1", "www2", "xn--n8j6ds53lwwkrqhv28a", "xn--lve-6lad"];

    private readonly string _root = Path.GetFullPath(root);
    private readonly Dictionary<string, byte[]> _overrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _cookies = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^//\s*META:\s*(\w+)=(.*)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MetaLine();

    /// <summary>Every request the server answered, in order.</summary>
    public List<ServerRequest> Log { get; } = [];

    /// <summary>The server's stash: values a test keeps between its requests, by key.</summary>
    public ServerStash Stash { get; } = new();

    /// <summary>The name of a host in the group: <c>""</c> for the main domain, <c>alt</c> for the alternate one; <c>""</c> for the domain itself or a subdomain's label.</summary>
    public static string HostFor(string group, string subdomain)
    {
        var domain = group == "alt" ? AltHost : Host;
        return subdomain.Length == 0 ? domain : subdomain + "." + domain;
    }

    /// <summary>The ports WPT serves a scheme on, or null for a scheme it does not.</summary>
    public static int[]? Ports(string scheme) => scheme switch
    {
        "http" => [8000, 8001],
        "https" => [8443, 8444],
        "ws" => [8888],
        "wss" => [9443],
        _ => null,
    };

    /// <summary>Whether the server answers for an address: one of its domains (or a subdomain of one) on a port it serves, or the default port.</summary>
    public bool Serves(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
            return false;

        var host = uri.Host;
        var known = host == Host || host == AltHost || host.EndsWith("." + Host, StringComparison.Ordinal) || host.EndsWith("." + AltHost, StringComparison.Ordinal);
        return known && (uri.IsDefaultPort || Ports(uri.Scheme)!.Contains(uri.Port));
    }

    /// <summary>Replaces a file with fixed bytes (the harness's report script, say) for every request.</summary>
    public void Override(string path, string text) => _overrides[path] = Encoding.UTF8.GetBytes(text);

    /// <summary>Reads a file of the checkout by its path relative to it, or null when there is none or it is outside.</summary>
    public byte[]? ReadFile(string relative)
    {
        var full = Resolve(relative);
        return full is not null && File.Exists(full) ? File.ReadAllBytes(full) : null;
    }

    /// <summary>The server's answer to one request.</summary>
    public ServerResponse Handle(ServerRequest request)
    {
        Log.Add(request);
        request.Server = this;
        var response = new ServerResponse();
        var path = request.Path;

        if (_overrides.TryGetValue(path, out var fixedBytes))
        {
            response.Set("Content-Type", ContentTypeOf(path));
            response.Body = fixedBytes;
            return Pipe(request, response);
        }

        if (path.EndsWith(".py", StringComparison.Ordinal))
        {
            if (WptHandlers.Find(path) is { } handler)
            {
                handler(request, response);
                return Pipe(request, response);
            }

            response.Status = 501;
            response.SetText("No handler is ported for " + path);
            return response;
        }

        var generated = GeneratedPage(path);
        if (generated is not null)
        {
            response.Set("Content-Type", "text/html; charset=utf-8");
            response.SetText(generated);
            return Pipe(request, response);
        }

        var full = Resolve(path);
        if (full is null || !File.Exists(full))
        {
            response.Status = 404;
            response.SetText("Not found: " + path);
            return response;
        }

        response.Set("Content-Type", ContentTypeOf(path));
        response.Body = File.ReadAllBytes(full);
        ApplyHeadersFiles(full, response);

        if (System.IO.Path.GetFileName(full).Contains(".sub.", StringComparison.Ordinal))
            Substitute(request, response);

        return Pipe(request, response);
    }

    /// <summary>
    /// Fetches an address the way a browser's resource loader does: the server answers, redirects are followed, cookies are kept. Null for an address
    /// the server does not serve, or an answer that is not a success.
    /// </summary>
    public ValueTask<RNetworkResponse?> LoadAsync(Uri uri)
    {
        for (var hops = 0; hops < 10; hops++)
        {
            if (!Serves(uri))
                return ValueTask.FromResult<RNetworkResponse?>(null);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_cookies.TryGetValue(uri.Host, out var jar) && jar.Count > 0)
                headers["Cookie"] = string.Join("; ", jar.Select(c => c.Key + "=" + c.Value));

            var response = Handle(new ServerRequest("GET", uri, headers));
            foreach (var (name, value) in response.Headers.Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)))
                StoreCookie(uri.Host, value);

            if (response.IsRedirect)
            {
                uri = new Uri(uri, response.Get("Location")!);
                continue;
            }

            if (response.Status is < 200 or >= 300)
                return ValueTask.FromResult<RNetworkResponse?>(null);

            var responseHeaders = response.Headers
                .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(h => h.Value).ToArray(), StringComparer.OrdinalIgnoreCase);
            return ValueTask.FromResult<RNetworkResponse?>(new RNetworkResponse(new MemoryStream(response.Body), responseHeaders));
        }

        return ValueTask.FromResult<RNetworkResponse?>(null);
    }

    /// <summary>
    /// Answers a request a script made (<c>fetch</c>, <c>XMLHttpRequest</c>): any method, the headers and body the script gave, redirects followed
    /// (or returned as they are), cookies kept. Null for an address the server does not serve, which a browser reports as a network error.
    /// </summary>
    public ScriptNetworkResponse? Send(ScriptNetworkRequest request)
    {
        var uri = request.Url;
        var method = request.Method;
        var body = request.Body;
        var headers = request.Headers.ToList();

        for (var hops = 0; hops < 20; hops++)
        {
            if (!Serves(uri))
                return null;

            var sent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in headers)
                sent[name] = sent.TryGetValue(name, out var existing) ? existing + ", " + value : value;
            if (_cookies.TryGetValue(uri.Host, out var jar) && jar.Count > 0 && !sent.ContainsKey("Cookie"))
                sent["Cookie"] = string.Join("; ", jar.Select(c => c.Key + "=" + c.Value));
            sent.TryAdd("Host", uri.Authority);

            if (Referer(request.Referrer, request.ReferrerPolicy, uri) is { } referer)
                sent["Referer"] = referer;

            // A browser names the page's origin on a request that is not a plain same-origin GET or HEAD.
            if (!sent.ContainsKey("Origin") && (method is not ("GET" or "HEAD") || !string.Equals(uri.GetLeftPart(UriPartial.Authority), PageOrigin, StringComparison.OrdinalIgnoreCase)))
                sent["Origin"] = PageOrigin;

            var response = Handle(new ServerRequest(method, uri, sent, body));
            foreach (var (_, value) in response.Headers.Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)))
                StoreCookie(uri.Host, value);

            // A response can change the policy for what follows it (Referrer-Policy, a list of which the last known value wins).
            if (response.Get("Referrer-Policy") is { } declared && declared.Split(',', StringSplitOptions.TrimEntries).LastOrDefault(KnownPolicies.Contains) is { } policy)
                request = request with { ReferrerPolicy = policy };

            if (response.IsRedirect && request.FollowRedirects)
            {
                uri = new Uri(uri, response.Get("Location")!);
                // 303 always, and 301 and 302 for a POST, become a GET without a body.
                if (response.Status == 303 && method is not ("GET" or "HEAD") || response.Status is 301 or 302 && method == "POST")
                {
                    method = "GET";
                    body = null;
                    headers.RemoveAll(h => h.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase));
                }

                continue;
            }

            var payload = method == "HEAD" ? [] : response.Body;
            return new ScriptNetworkResponse(response.Status, response.StatusText ?? ReasonPhrase(response.Status), [.. response.Headers], payload, uri);
        }

        return null;
    }

    private static readonly HashSet<string> KnownPolicies =
        ["no-referrer", "no-referrer-when-downgrade", "same-origin", "origin", "strict-origin", "origin-when-cross-origin", "strict-origin-when-cross-origin", "unsafe-url"];

    // The Referer a request to <target> carries: the referrer address cut down as the policy says (URL without fragment or credentials, or just its origin).
    private static string? Referer(string? referrer, string policy, Uri target)
    {
        if (referrer is null || !Uri.TryCreate(referrer, UriKind.Absolute, out var source) || source.Scheme is not ("http" or "https"))
            return null;

        var full = new UriBuilder(source) { Fragment = string.Empty, UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri.TrimEnd('#');
        var origin = source.GetLeftPart(UriPartial.Authority) + "/";
        var sameOrigin = string.Equals(source.GetLeftPart(UriPartial.Authority), target.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
        var downgrade = source.Scheme == "https" && target.Scheme == "http";
        return (policy.Length == 0 ? "strict-origin-when-cross-origin" : policy) switch
        {
            "no-referrer" => null,
            "no-referrer-when-downgrade" => downgrade ? null : full,
            "same-origin" => sameOrigin ? full : null,
            "origin" => origin,
            "strict-origin" => downgrade ? null : origin,
            "origin-when-cross-origin" => sameOrigin ? full : origin,
            "unsafe-url" => full,
            _ => sameOrigin ? full : downgrade ? null : origin,
        };
    }

    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK", 201 => "Created", 202 => "Accepted", 204 => "No Content", 206 => "Partial Content",
        301 => "Moved Permanently", 302 => "Found", 303 => "See Other", 304 => "Not Modified", 307 => "Temporary Redirect", 308 => "Permanent Redirect",
        400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", 418 => "I'm a teapot",
        500 => "Internal Server Error", 501 => "Not Implemented", 502 => "Bad Gateway", 503 => "Service Unavailable",
        _ => string.Empty,
    };

    /// <summary>The Content-Type the server gives a path: what its <c>.headers</c> file says, else what its extension implies.</summary>
    public string ContentTypeFor(string path)
    {
        var full = Resolve(path);
        var response = new ServerResponse();
        response.Set("Content-Type", ContentTypeOf(path));
        if (full is not null)
            ApplyHeadersFiles(full, response);

        return response.ContentType ?? "application/octet-stream";
    }

    private void StoreCookie(string host, string setCookie)
    {
        var pair = setCookie.Split(';')[0];
        var equals = pair.IndexOf('=');
        if (equals <= 0)
            return;

        if (!_cookies.TryGetValue(host, out var jar))
            _cookies[host] = jar = new Dictionary<string, string>(StringComparer.Ordinal);

        var name = pair[..equals].Trim();
        var value = pair[(equals + 1)..].Trim();
        if (setCookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase) || setCookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
            jar.Remove(name);
        else
            jar[name] = value;
    }

    // The path of a file under the checkout, or null when it would leave it.
    private string? Resolve(string relative)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        return full.StartsWith(_root, StringComparison.Ordinal) ? full : null;
    }

    // `__dir__.headers` for the directory, then `<file>.headers`: each line is `Name: value`, and a later one for a name replaces an earlier.
    private static void ApplyHeadersFiles(string file, ServerResponse response)
    {
        foreach (var headersFile in new[] { System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, "__dir__.headers"), file + ".headers" })
        {
            if (!File.Exists(headersFile))
                continue;

            foreach (var line in File.ReadAllLines(headersFile))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;

                response.Set(line[..colon].Trim(), line[(colon + 1)..].Trim());
            }
        }
    }

    private void Substitute(ServerRequest request, ServerResponse response)
    {
        var type = response.ContentType ?? string.Empty;
        var isMarkup = type.Contains("html", StringComparison.OrdinalIgnoreCase) || type.Contains("xml", StringComparison.OrdinalIgnoreCase);
        var isText = isMarkup || type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || type.Contains("javascript", StringComparison.OrdinalIgnoreCase)
            || type.Contains("json", StringComparison.OrdinalIgnoreCase);
        if (!isText)
            return;

        response.SetText(Substitution.Apply(Encoding.UTF8.GetString(response.Body), request, this, escapeHtml: isMarkup));
    }

    // `?pipe=sub|status(404)|header(Name,value)`: the pipes this server implements, in order; any other is ignored.
    private ServerResponse Pipe(ServerRequest request, ServerResponse response)
    {
        if (request.First("pipe") is not { Length: > 0 } pipe)
            return response;

        foreach (var step in SplitPipe(pipe))
        {
            var open = step.IndexOf('(');
            var name = (open < 0 ? step : step[..open]).Trim();
            var arguments = open < 0 ? [] : step[(open + 1)..step.LastIndexOf(')')].Split(',').Select(a => a.Trim()).ToList();

            switch (name)
            {
                case "sub":
                    Substitute(request, response);
                    break;
                case "status" when arguments.Count > 0 && int.TryParse(arguments[0], out var status):
                    response.Status = status;
                    break;
                case "header" when arguments.Count >= 2:
                    if (arguments.Count > 2 && arguments[2] == "True")
                        response.Append(arguments[0], arguments[1]);
                    else
                        response.Set(arguments[0], arguments[1]);
                    break;
            }
        }

        return response;
    }

    // Splits on the bars that are not inside parentheses.
    private static IEnumerable<string> SplitPipe(string pipe)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < pipe.Length; i++)
        {
            if (pipe[i] == '(') depth++;
            else if (pipe[i] == ')') depth--;
            else if (pipe[i] == '|' && depth == 0)
            {
                yield return pipe[start..i];
                start = i + 1;
            }
        }

        yield return pipe[start..];
    }

    private static string ContentTypeOf(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".js" or ".mjs" => "text/javascript",
        ".json" => "application/json",
        ".txt" => "text/plain",
        ".xml" => "application/xml",
        ".htm" or ".html" => "text/html",
        ".xht" or ".xhtml" => "application/xhtml+xml",
        ".css" => "text/css",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };

    // `x.any.html` and `x.window.html` stand for the page of `x.any.js` / `x.window.js`; null for any other path.
    private string? GeneratedPage(string path)
    {
        var match = Regex.Match(path, @"^(.*\.(?:any|window))\.html$", RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        var jsPath = match.Groups[1].Value + ".js";
        return ReadFile(jsPath) is { } bytes ? GenerateTestPage(jsPath, Encoding.UTF8.GetString(bytes)) : null;
    }

    /// <summary>The page the WPT server generates for the window global of a <c>.any.js</c> / <c>.window.js</c> file; null when the file asks for no window.</summary>
    public static string? GenerateTestPage(string wptPath, string script)
    {
        var scripts = new List<string>();
        string[]? globals = null;
        foreach (Match m in MetaLine().Matches(script))
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Value.Trim();
            if (key == "script")
                scripts.Add(value);
            else if (key == "global")
                globals = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        // With no `global` a .any.js runs in a window as well as the workers; "worker" or "dedicatedworker" alone has no window.
        if (wptPath.EndsWith(".any.js", StringComparison.Ordinal) && globals is not null && !globals.Contains("window"))
            return null;

        var name = wptPath[(wptPath.LastIndexOf('/') + 1)..];
        var page = new StringBuilder();
        page.Append("<!doctype html>\n<meta charset=utf-8>\n<title>").Append(System.Net.WebUtility.HtmlEncode(name)).Append("</title>\n");
        page.Append("<script src=\"/resources/testharness.js\"></script>\n<script src=\"/resources/testharnessreport.js\"></script>\n<div id=log></div>\n");
        foreach (var s in scripts)
            page.Append("<script src=\"").Append(System.Net.WebUtility.HtmlEncode(s)).Append("\"></script>\n");
        page.Append("<script src=\"").Append(System.Net.WebUtility.HtmlEncode(name)).Append("\"></script>\n<script>done();</script>\n");
        return page.ToString();
    }
}
