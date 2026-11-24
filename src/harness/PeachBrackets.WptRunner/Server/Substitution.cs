using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachBrackets.WptRunner.Server;

/// <summary>
/// The <c>sub</c> pipe of <c>wptserve</c>: <c>{{host}}</c>, <c>{{domains[www]}}</c>, <c>{{hosts[alt][www]}}</c>, <c>{{ports[http][0]}}</c>,
/// <c>{{location[...]}}</c>, <c>{{GET[name]}}</c>, <c>{{headers[name]}}</c>, <c>{{uuid()}}</c>, <c>{{file_hash(algorithm, path)}}</c>,
/// <c>{{header_or_default(name, default)}}</c> and <c>{{url_base}}</c>, replaced in a file served with <c>.sub.</c> in its name or through
/// <c>?pipe=sub</c>. A placeholder this does not know is left as written, so the failure is visible in the page.
/// </summary>
internal static partial class Substitution
{
    [GeneratedRegex(@"\{\{([^{}]*?)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_]\w*)(?<index>(?:\[[^\]]*\])*)(?:\((?<args>.*)\))?\s*$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Expression();

    [GeneratedRegex(@"\[([^\]]*)\]", RegexOptions.CultureInvariant)]
    private static partial Regex Index();

    public static string Apply(string text, ServerRequest request, WptServer server, bool escapeHtml)
    {
        return Placeholder().Replace(text, match =>
        {
            var value = Evaluate(match.Groups[1].Value, request, server);
            if (value is null)
                return match.Value;

            return escapeHtml ? System.Net.WebUtility.HtmlEncode(value) : value;
        });
    }

    private static string? Evaluate(string content, ServerRequest request, WptServer server)
    {
        if (Expression().Match(content) is not { Success: true } m)
            return null;

        var keys = Index().Matches(m.Groups["index"].Value).Select(k => k.Groups[1].Value).ToList();
        var arguments = m.Groups["args"].Success ? SplitArguments(m.Groups["args"].Value) : null;

        switch (m.Groups["name"].Value)
        {
            case "host" when keys.Count == 0:
                return WptServer.Host;
            case "url_base":
                return "/";
            case "uuid" when arguments is not null:
                return Guid.NewGuid().ToString();
            case "domains" when keys.Count == 1:
                return WptServer.HostFor(string.Empty, keys[0]);
            case "hosts" when keys.Count == 2:
                return WptServer.HostFor(keys[0], keys[1]);
            case "ports" when keys.Count == 2 && int.TryParse(keys[1], out var index):
                return WptServer.Ports(keys[0]) is { } ports && index >= 0 && index < ports.Length ? ports[index].ToString() : null;
            case "location" when keys.Count == 1:
                return Location(request.Url, keys[0]);
            case "GET" when keys.Count == 1:
                return request.First(keys[0]) ?? string.Empty;
            case "headers" when keys.Count == 1:
                return request.Headers.TryGetValue(keys[0], out var header) ? header : string.Empty;
            case "header_or_default" when arguments is { Count: 2 }:
                return request.Headers.TryGetValue(arguments[0], out var present) ? present : arguments[1];
            case "file_hash" when arguments is { Count: 2 }:
                return FileHash(arguments[0], arguments[1], server);
            default:
                return null;
        }
    }

    // The fields of the request's own URL, as script's location object has them.
    private static string? Location(Uri url, string field)
    {
        var port = url.IsDefaultPort ? string.Empty : url.Port.ToString();
        return field switch
        {
            "scheme" => url.Scheme,
            "host" => port.Length == 0 ? url.Host : url.Host + ":" + port,
            "hostname" => url.Host,
            "port" => port,
            "path" or "pathname" => url.AbsolutePath,
            "query" => url.Query,
            "href" => url.AbsoluteUri,
            "server" => url.GetLeftPart(UriPartial.Authority),
            _ => null,
        };
    }

    private static string? FileHash(string algorithm, string path, WptServer server)
    {
        var bytes = server.ReadFile(path.Trim('"', '\'', ' ').TrimStart('/'));
        if (bytes is null)
            return null;

        var hash = algorithm.Trim().ToLowerInvariant() switch
        {
            "md5" => MD5.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            "sha256" => SHA256.HashData(bytes),
            _ => null,
        };
        return hash is null ? null : Convert.ToHexStringLower(hash);
    }

    // The arguments of a call: split on commas, each with its surrounding quotes and white space removed.
    private static List<string> SplitArguments(string text) =>
        [.. text.Split(',').Select(a => a.Trim().Trim('"', '\''))];
}
