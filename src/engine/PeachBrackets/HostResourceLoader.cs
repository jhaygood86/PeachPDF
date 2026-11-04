using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.IO;
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

    private static async ValueTask<RNetworkResponse?> FetchAsync(RUri uri)
    {
        try
        {
            var response = await SharedClient.Value.GetAsync(uri.Uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return null;

            var headers = new Dictionary<string, string[]>();
            foreach (var header in response.Content.Headers)
                headers[header.Key] = [.. header.Value];

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
