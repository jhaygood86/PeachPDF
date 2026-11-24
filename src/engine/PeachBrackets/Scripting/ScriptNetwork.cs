using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using PeachBrackets.Html;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.ScriptHosting;

/// <summary>A request a document's script makes with <c>XMLHttpRequest</c> or <c>fetch</c>.</summary>
/// <param name="Method">the HTTP method, upper-case for the standard ones</param>
/// <param name="Url">the absolute address</param>
/// <param name="Headers">the request headers the script set, in order</param>
/// <param name="Body">the body, or null</param>
/// <param name="FollowRedirects">whether a redirect answer is followed to its target (the default) or returned as it is</param>
public sealed record ScriptNetworkRequest(
    string Method,
    Uri Url,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    byte[]? Body,
    bool FollowRedirects = true);

/// <summary>The answer to a <see cref="ScriptNetworkRequest"/>.</summary>
/// <param name="Status">the status code</param>
/// <param name="StatusText">the reason phrase</param>
/// <param name="Headers">the response headers, in order</param>
/// <param name="Body">the body</param>
/// <param name="Url">the address the answer came from, which differs from the request's after a redirect</param>
public sealed record ScriptNetworkResponse(
    int Status,
    string StatusText,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    byte[] Body,
    Uri Url);

/// <summary>
/// How a document's scripts reach the network. Set on <see cref="HtmlDocumentOptions.ScriptNetwork"/>; without one, a script can read what the
/// document's resource loader can (a GET that answers 200, or fails), which is what its images and style sheets use.
/// </summary>
public interface IScriptNetwork
{
    /// <summary>Sends a request.</summary>
    /// <param name="request">what to send</param>
    /// <param name="cancellationToken">cancels the work</param>
    /// <returns>the answer, whatever its status; null when the request failed before there was one (a network error)</returns>
    ValueTask<ScriptNetworkResponse?> SendAsync(ScriptNetworkRequest request, CancellationToken cancellationToken = default);
}

/// <summary>What a document with no <see cref="IScriptNetwork"/> reaches the network with: the render context's resource loader, for a GET.</summary>
internal sealed class LoaderScriptNetwork(HtmlContainerInt container) : IScriptNetwork
{
    public async ValueTask<ScriptNetworkResponse?> SendAsync(ScriptNetworkRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Method.Equals("GET", StringComparison.Ordinal))
            return null;

        try
        {
            var response = await container.RenderContext.GetResourceStream(request.Url);
            if (response?.ResourceStream is not { } stream)
                return null;

            using var buffer = new MemoryStream();
            await using (stream)
                await stream.CopyToAsync(buffer, cancellationToken);

            var headers = response.ResponseHeaders?.SelectMany(h => h.Value.Select(v => new KeyValuePair<string, string>(h.Key, v))).ToList() ?? [];
            return new ScriptNetworkResponse(200, "OK", headers, buffer.ToArray(), request.Url);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
