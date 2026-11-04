using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PeachBrackets;

/// <summary>
/// Loads what a document refers to - an image, a style sheet, a font - for a host that shows documents and has no resource loader of its own: a
/// <c>data:</c> URI, a file, and (when the host allows it) a web address. A <see cref="RenderContext"/> answers
/// <see cref="RenderContext.GetResourceStream"/> with it.
/// </summary>
/// <example>
/// <code>
/// public override ValueTask&lt;RNetworkResponse?&gt; GetResourceStream(RUri uri) =&gt; HostResourceLoader.LoadAsync(uri, allowNetwork: false);
/// </code>
/// </example>
public static class HostResourceLoader
{
    private static readonly Lazy<HttpClient> SharedClient = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

    /// <summary>Loads the resource a URI names, or answers <see langword="null"/> when the host does not load that kind of address or it cannot be read.</summary>
    /// <param name="uri">the address of the resource</param>
    /// <param name="allowNetwork">whether <c>http</c> and <c>https</c> addresses are fetched; off by default, so a document cannot make the host call out</param>
    /// <param name="allowLocalFiles">whether <c>file</c> addresses are read</param>
    /// <returns>the content, with a <c>Content-Type</c> header where the address declares one, or <see langword="null"/></returns>
    public static ValueTask<RNetworkResponse?> LoadAsync(RUri uri, bool allowNetwork = false, bool allowLocalFiles = true)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            return default;

        switch (uri.Scheme)
        {
            case "data":
                if (!DataUri.TryDecode(uri.AbsoluteUri, out var mimeType, out var bytes))
                    return default;

                return new ValueTask<RNetworkResponse?>(new RNetworkResponse(new MemoryStream(bytes), new Dictionary<string, string[]> { ["Content-Type"] = [mimeType] }));

            case "file" when allowLocalFiles:
                try
                {
                    var path = uri.Uri.LocalPath;
                    return File.Exists(path) ? new ValueTask<RNetworkResponse?>(new RNetworkResponse(File.OpenRead(path), null)) : default;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return default;
                }

            case "http" or "https" when allowNetwork:
                return FetchAsync(uri);

            default:
                return default;
        }
    }

    // What was fetched lately, so a document that is styled again (a hover or focus change loads the page's style sheets and fonts anew) does not
    // go back to the network for every one of them. Held in memory only, for a few minutes, and only what the server did not say to keep out of a cache.
    private static TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);
    private const int MaxCachedBytes = 8 * 1024 * 1024;
    private const long CacheBudget = 64L * 1024 * 1024;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, CachedResponse> Cache = [];
    private static long _cachedBytes;

    /// <summary>
    /// How long a fetched web response is kept for the next request of the same address (five minutes by default; <see cref="TimeSpan.Zero"/> keeps
    /// nothing). A response of more than 8 MB, one the server marked <c>no-store</c> or <c>private</c>, and one with no declared length is never kept.
    /// </summary>
    public static TimeSpan CacheLifetime
    {
        get => _cacheLifetime;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            lock (CacheGate)
            {
                _cacheLifetime = value;
                if (value == TimeSpan.Zero)
                {
                    Cache.Clear();
                    _cachedBytes = 0;
                }
            }
        }
    }

    private sealed record CachedResponse(byte[] Content, Dictionary<string, string[]> Headers, DateTime Expires);

    private static bool TryGetCached(string address, out RNetworkResponse? response)
    {
        lock (CacheGate)
        {
            if (Cache.TryGetValue(address, out var hit))
            {
                if (hit.Expires > DateTime.UtcNow)
                {
                    response = new RNetworkResponse(new MemoryStream(hit.Content, writable: false), new Dictionary<string, string[]>(hit.Headers));
                    return true;
                }

                Cache.Remove(address);
                _cachedBytes -= hit.Content.LongLength;
            }
        }

        response = null;
        return false;
    }

    private static void Remember(string address, byte[] content, Dictionary<string, string[]> headers)
    {
        lock (CacheGate)
        {
            if (Cache.Remove(address, out var old))
                _cachedBytes -= old.Content.LongLength;

            // Over budget: start again rather than keep an ordering of what was used.
            if (_cachedBytes + content.LongLength > CacheBudget)
            {
                Cache.Clear();
                _cachedBytes = 0;
            }

            Cache[address] = new CachedResponse(content, headers, DateTime.UtcNow + _cacheLifetime);
            _cachedBytes += content.LongLength;
        }
    }

    private static async ValueTask<RNetworkResponse?> FetchAsync(RUri uri)
    {
        if (_cacheLifetime > TimeSpan.Zero && TryGetCached(uri.AbsoluteUri, out var cached))
            return cached;

        try
        {
            var response = await SharedClient.Value.GetAsync(uri.Uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return null;

            var headers = new Dictionary<string, string[]>();
            foreach (var header in response.Content.Headers)
                headers[header.Key] = [.. header.Value];

            var cacheControl = response.Headers.CacheControl;
            var cacheable = _cacheLifetime > TimeSpan.Zero && (cacheControl is null || (!cacheControl.NoStore && !cacheControl.Private));
            if (cacheable && response.Content.Headers.ContentLength is { } length && length <= MaxCachedBytes)
            {
                var content = await response.Content.ReadAsByteArrayAsync();
                Remember(uri.AbsoluteUri, content, headers);
                return new RNetworkResponse(new MemoryStream(content, writable: false), new Dictionary<string, string[]>(headers));
            }

            return new RNetworkResponse(await response.Content.ReadAsStreamAsync(), headers);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }
}
