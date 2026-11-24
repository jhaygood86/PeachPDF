using System.Text;
using System.Text.Json;

namespace PeachBrackets.WptRunner.Server;

/// <summary>
/// C# ports of the <c>.py</c> handlers the tests under test use, by the path of the Python file they stand for. A request for a <c>.py</c> file with no
/// entry here is answered 501, and the runner skips a test that names one (see <see cref="Find"/>), so what is missing stays visible.
/// </summary>
internal static class WptHandlers
{
    private static readonly Dictionary<string, Action<ServerRequest, ServerResponse>> Table = new(StringComparer.Ordinal)
    {
        ["common/redirect.py"] = Redirect,
        ["common/redirect-opt-in.py"] = RedirectOptIn,
        ["common/slow-redirect.py"] = (request, response) => Redirect(request, response, enableCors: false),
        ["common/echo.py"] = Echo,
        ["common/refresh.py"] = Refresh,
        ["common/slow.py"] = (_, response) => response.Status = 200,
        ["common/CustomCorsResponse.py"] = CustomCors,
        ["dom/nodes/Document-contentType/support/contenttype_setter.py"] = ContentTypeSetter,
        ["dom/nodes/encoding.py"] = Encoding_,
    };

    /// <summary>The handler for a path relative to the checkout, or null when none is ported.</summary>
    public static Action<ServerRequest, ServerResponse>? Find(string path) => Table.GetValueOrDefault(path);

    /// <summary>The paths with a handler, for the runner's report of what it can serve.</summary>
    public static IReadOnlyCollection<string> Ported => Table.Keys;

    private static void Redirect(ServerRequest request, ServerResponse response) => Redirect(request, response, enableCors: true);

    // common/redirect.py (and slow-redirect.py, which only waits first): status (302 by default) and location, optionally with CORS headers.
    private static void Redirect(ServerRequest request, ServerResponse response, bool enableCors)
    {
        response.Status = int.TryParse(request.First("status"), out var status) ? status : 302;
        response.Set("Location", request.First("location") ?? string.Empty);

        if (enableCors && request.Has("enable-cors") && request.Headers.TryGetValue("Origin", out var origin) && origin.Length > 0)
        {
            response.Set("Content-Type", "text/plain");
            response.Set("Access-Control-Allow-Origin", origin);
            response.Set("Access-Control-Allow-Credentials", "true");
        }
    }

    private static void RedirectOptIn(ServerRequest request, ServerResponse response)
    {
        Redirect(request, response, enableCors: false);
        response.Set("Timing-Allow-Origin", "*");
    }

    // common/echo.py: the `content` parameter as an HTML document.
    private static void Echo(ServerRequest request, ServerResponse response)
    {
        response.Set("X-XSS-Protection", "0");
        response.Set("Content-Type", "text/html");
        response.SetText(request.First("content") ?? string.Empty);
    }

    // common/refresh.py: a blank page whose Refresh header sends the browser on at once.
    private static void Refresh(ServerRequest request, ServerResponse response)
    {
        response.Set("Content-Type", "text/html");
        response.Set("Refresh", "0; URL=" + request.First("location"));
    }

    // common/CustomCorsResponse.py: headers, status and body that the query describes as JSON.
    private static void CustomCors(ServerRequest request, ServerResponse response)
    {
        if (request.First("headers") is { } headers)
        {
            using var document = JsonDocument.Parse(headers);
            foreach (var property in document.RootElement.EnumerateObject())
                response.Set(property.Name, property.Value.ToString());
        }

        response.SetText(request.First("content") is { } content ? JsonSerializer.Deserialize<string>(content) ?? string.Empty : "/* CustomCorsResponse.py content */");
        if (request.First("status") is { } status && int.TryParse(status, out var code))
            response.Status = code;
    }

    // dom/nodes/Document-contentType/support/contenttype_setter.py
    private static void ContentTypeSetter(ServerRequest request, ServerResponse response)
    {
        var type = request.First("type");
        var subtype = request.First("subtype");
        if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(subtype))
            response.Set("Content-Type", type + "/" + subtype);

        if (!string.IsNullOrEmpty(request.First("removeContentType")))
            response.Remove("Content-Type");

        var content = new StringBuilder("<head>");
        if (request.First("mime") is { Length: > 0 } mime)
            content.Append($"<meta http-equiv=\"Content-Type\" content=\"{mime}; charset=utf-8\"/>");
        content.Append("</head>");
        response.SetText(content.ToString());
    }

    // dom/nodes/encoding.py: a page that declares the charset the `label` parameter names.
    private static void Encoding_(ServerRequest request, ServerResponse response)
    {
        response.Set("Content-Type", "text/html");
        response.SetText($"<!doctype html><meta charset=\"{System.Net.WebUtility.HtmlEncode(request.First("label") ?? string.Empty)}\">");
    }
}
