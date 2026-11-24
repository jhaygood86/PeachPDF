using System.Text;

namespace PeachBrackets.WptRunner.Server;

/// <summary>One request to the WPT server: what a handler of the real <c>wptserve</c> would see.</summary>
internal sealed class ServerRequest
{
    private readonly List<KeyValuePair<string, string>> _query;

    public ServerRequest(string method, Uri url, IReadOnlyDictionary<string, string>? headers = null, byte[]? body = null)
    {
        Method = method;
        Url = url;
        Headers = new Dictionary<string, string>(headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        Body = body ?? [];
        _query = ParseQuery(url.Query);
    }

    public string Method { get; }

    public Uri Url { get; }

    /// <summary>The request headers, names compared without regard to case.</summary>
    public Dictionary<string, string> Headers { get; }

    public byte[] Body { get; }

    /// <summary>The path without the leading slash, percent-decoded: the file a request names, relative to the checkout.</summary>
    public string Path => Uri.UnescapeDataString(Url.AbsolutePath).TrimStart('/');

    /// <summary>The first value of a query parameter, or <paramref name="fallback"/>.</summary>
    public string? First(string name, string? fallback = null) =>
        _query.FirstOrDefault(p => p.Key == name) is { Key: not null } found ? found.Value : fallback;

    public bool Has(string name) => _query.Any(p => p.Key == name);

    public IReadOnlyList<string> All(string name) => [.. _query.Where(p => p.Key == name).Select(p => p.Value)];

    internal static List<KeyValuePair<string, string>> ParseQuery(string query)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            var key = equals < 0 ? part : part[..equals];
            var value = equals < 0 ? string.Empty : part[(equals + 1)..];
            pairs.Add(new KeyValuePair<string, string>(Decode(key), Decode(value)));
        }

        return pairs;
    }

    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
}

/// <summary>The WPT server's answer, which a handler fills in the way a <c>wptserve</c> one does.</summary>
internal sealed class ServerResponse
{
    private readonly List<KeyValuePair<string, string>> _headers = [];

    public int Status { get; set; } = 200;

    public byte[] Body { get; set; } = [];

    public IReadOnlyList<KeyValuePair<string, string>> Headers => _headers;

    /// <summary>Sets a header, replacing any of that name.</summary>
    public void Set(string name, string value)
    {
        Remove(name);
        _headers.Add(new KeyValuePair<string, string>(name, value));
    }

    /// <summary>Adds a header, keeping any of that name.</summary>
    public void Append(string name, string value) => _headers.Add(new KeyValuePair<string, string>(name, value));

    public void Remove(string name) => _headers.RemoveAll(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

    public string? Get(string name) => _headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Key: not null } found ? found.Value : null;

    public string? ContentType => Get("Content-Type");

    /// <summary>
    /// The body as text: UTF-8 with a byte order mark removed, as a browser decodes a page. (Leaving it would put a U+FEFF before the DOCTYPE, which then
    /// is not the first token and the page is laid out in quirks mode.)
    /// </summary>
    public string Text => Encoding.UTF8.GetString(Body is [0xEF, 0xBB, 0xBF, .. var rest] ? rest : Body);

    /// <summary>Sets the body from text, as UTF-8.</summary>
    public void SetText(string text) => Body = Encoding.UTF8.GetBytes(text);

    public bool IsRedirect => Status is 301 or 302 or 303 or 307 or 308 && Get("Location") is not null;
}
