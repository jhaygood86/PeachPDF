using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets;

/// <summary>
/// Loads what a document refers to - an image, a style sheet, a font - for a host that shows documents and has no resource loader of its own: a
/// <c>data:</c> URI, a file, and (when the host allows it) a web address. A <see cref="RenderContext"/> answers
/// <see cref="RenderContext.GetResourceStream"/> with it.
/// </summary>
/// <example>
/// <code>
/// public override ValueTask&lt;RNetworkResponse?&gt; GetResourceStream(Uri uri) =&gt; HostResourceLoader.LoadAsync(uri, allowNetwork: false);
/// </code>
/// </example>
public static class HostResourceLoader
{
    // Compressed responses are asked for and decoded (a page's HTML and style sheets are a fraction of their size on the wire). A browser's own fetch
    // does that itself, and its handler refuses to be configured to.
    private static readonly Lazy<HttpClient> SharedClient = new(() => new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromSeconds(30) });

    private static HttpMessageHandler CreateHandler() => OperatingSystem.IsBrowser()
        ? new HttpClientHandler()
        : new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };

    private static string? _userAgent;

    /// <summary>
    /// The <c>User-Agent</c> the requests for web addresses carry. By default it is what Chromium sends on the same platform followed by this
    /// package's own token, <c>Mozilla/5.0 (&lt;platform&gt;; &lt;oscpu&gt;) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36
    /// PeachBrackets/&lt;version&gt;</c>, with the platform and CPU values Chromium reports there (frozen, as Chromium freezes them: for example
    /// <c>Windows NT 10.0; Win64; x64</c>, <c>Macintosh; Intel Mac OS X 10_15_7</c>, <c>X11; Linux x86_64</c> and <c>Linux; Android 10; K</c>), so
    /// that a site that serves browsers serves this too while the trailing token says who is asking. Set it to send something else; null or
    /// white space restores the default. A host that fetches pages itself sends it too, to be seen as the same client.
    /// </summary>
    public static string UserAgent
    {
        get => _userAgent ?? HostUserAgent.Default;
        set => _userAgent = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Loads the resource a URI names, or answers <see langword="null"/> when the host does not load that kind of address or it cannot be read.</summary>
    /// <param name="uri">the address of the resource</param>
    /// <param name="allowNetwork">whether <c>http</c> and <c>https</c> addresses are fetched; off by default, so a document cannot make the host call out</param>
    /// <param name="allowLocalFiles">whether <c>file</c> addresses are read</param>
    /// <returns>the content, with a <c>Content-Type</c> header where the address declares one, or <see langword="null"/></returns>
    public static ValueTask<RNetworkResponse?> LoadAsync(Uri uri, bool allowNetwork = false, bool allowLocalFiles = true)
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
                    var path = uri.LocalPath;
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

    /// <summary>
    /// Fetches several web addresses at once (at most <paramref name="concurrency"/> in flight), so that a document that asks for them one after another
    /// finds each in the cache (see <see cref="CacheLifetime"/>). Addresses that are not <c>http</c> or <c>https</c>, or that fail, are skipped.
    /// </summary>
    /// <param name="addresses">the addresses to fetch</param>
    /// <param name="allowNetwork">whether web addresses are fetched at all</param>
    /// <param name="concurrency">how many requests may be in flight at a time</param>
    /// <returns>a task that completes when every request has finished</returns>
    public static async ValueTask PrefetchAsync(IEnumerable<Uri> addresses, bool allowNetwork, int concurrency = 8)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        if (!allowNetwork || _cacheLifetime <= TimeSpan.Zero)
            return;

        using var gate = new SemaphoreSlim(concurrency);
        var fetches = addresses
            .Where(a => a.IsAbsoluteUri && a.Scheme is "http" or "https")
            .DistinctBy(a => a.AbsoluteUri)
            .Select(async address =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var response = await FetchAsync(address).ConfigureAwait(false);
                    response?.ResourceStream?.Dispose();
                }
                catch (Exception)
                {
                    // The document asks again when it needs the resource, and reports the failure then.
                }
                finally
                {
                    gate.Release();
                }
            })
            .ToList();

        await Task.WhenAll(fetches).ConfigureAwait(false);
    }

    // What was fetched lately, so a document that is styled again (a hover or focus change loads the page's style sheets and fonts anew) does not
    // go back to the network for every one of them. Held in memory only, for a few minutes, and only what the server did not say to keep out of a cache.
    private static TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);
    private const int MaxCachedBytes = 8 * 1024 * 1024;
    private const long CacheBudget = 64L * 1024 * 1024;
    private static readonly Lock CacheGate = new();
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
                    response = new RNetworkResponse(new MemoryStream(hit.Content, writable: false), [with(hit.Headers)]);
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

    private static async ValueTask<RNetworkResponse?> FetchAsync(Uri uri)
    {
        using var activity = EngineTelemetry.Source.StartActivity("resource.fetch");
        activity?.SetTag("url", uri.AbsoluteUri);
        if (_cacheLifetime > TimeSpan.Zero && TryGetCached(uri.AbsoluteUri, out var cached))
        {
            activity?.SetTag("cache", "hit");
            return cached;
        }

        activity?.SetTag("cache", "miss");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            // A browser's fetch cannot set it (the browser owns it), so there it is left alone.
            if (!OperatingSystem.IsBrowser())
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            var response = await SharedClient.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            activity?.SetTag("status", (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
                return null;

            var headers = new Dictionary<string, string[]>();
            foreach (var header in response.Content.Headers)
                headers[header.Key] = [.. header.Value];

            var cacheControl = response.Headers.CacheControl;
            var cacheable = _cacheLifetime > TimeSpan.Zero && (cacheControl is null || (!cacheControl.NoStore && !cacheControl.Private));
            var stream = await response.Content.ReadAsStreamAsync();

            // A response that declares more than the limit is not worth reading ahead. One that declares nothing (chunked, or decompressed - a
            // decoded response no longer has a length) is read up to the limit and kept if it fits, because most of what a page loads is like that.
            if (!cacheable || response.Content.Headers.ContentLength is > MaxCachedBytes)
                return new RNetworkResponse(stream, headers);

            var content = await ReadUpToAsync(stream, MaxCachedBytes);
            if (content.Complete)
            {
                activity?.SetTag("bytes", content.Bytes.Length);
                await stream.DisposeAsync();
                Remember(uri.AbsoluteUri, content.Bytes, headers);
                return new RNetworkResponse(new MemoryStream(content.Bytes, writable: false), [with(headers)]);
            }

            // Bigger than the limit: hand back what was read followed by the rest, uncached.
            return new RNetworkResponse(new PrefixedStream(content.Bytes, stream), headers);
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

    // Reads the stream until it ends or until more than limit bytes have been read; Complete says whether it ended (then Bytes is all of it).
    private static async ValueTask<(byte[] Bytes, bool Complete)> ReadUpToAsync(Stream stream, int limit)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0)
                return (buffer.ToArray(), true);

            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit)
                return (buffer.ToArray(), false);
        }
    }

    /// <summary>The bytes already read from a stream followed by the rest of it, as one forward-only stream that closes the original with it.</summary>
    private sealed class PrefixedStream(byte[] prefix, Stream rest) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position < prefix.Length)
            {
                var take = Math.Min(buffer.Length, prefix.Length - _position);
                prefix.AsSpan(_position, take).CopyTo(buffer);
                _position += take;
                return take;
            }

            return rest.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < prefix.Length)
            {
                var take = Math.Min(buffer.Length, prefix.Length - _position);
                prefix.AsMemory(_position, take).CopyTo(buffer);
                _position += take;
                return take;
            }

            return await rest.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                rest.Dispose();

            base.Dispose(disposing);
        }
    }
}
