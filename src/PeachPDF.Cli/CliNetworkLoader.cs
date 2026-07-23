using System.Linq;
using System.Net.Http;
using PeachPDF.Network;

namespace PeachPDF.Cli;

/// <summary>
/// The resource loader the CLI configures for a rendered document. It derives from
/// <see cref="FileUriNetworkLoader"/> so that PeachPDF's adapter routes <c>file:</c> resource requests
/// to it (letting <c>--no-local-files</c> refuse them), while <c>http(s):</c> requests are served by a
/// caller-configured <see cref="HttpClient"/> (honoring <c>--http-timeout</c>/<c>--http-header</c>/
/// <c>--user-agent</c>/auth/proxy/<c>--insecure</c>, and <c>--no-network</c> when the client is null).
/// <c>data:</c> URIs are handled internally by the adapter regardless. The document's base URL comes
/// from <see cref="BaseUri"/> (from <c>--baseurl</c> or the input's own location).
/// </summary>
internal sealed class CliNetworkLoader : FileUriNetworkLoader
{
    private readonly RUri? _baseUri;
    private readonly HttpClient? _httpClient;
    private readonly bool _allowLocalFiles;

    public CliNetworkLoader(RUri? baseUri, HttpClient? httpClient, bool allowLocalFiles)
    {
        _baseUri = baseUri;
        _httpClient = httpClient;
        _allowLocalFiles = allowLocalFiles;
    }

    public override RUri? BaseUri => _baseUri;

    public override async Task<RNetworkResponse?> GetResourceStream(RUri uri)
    {
        if (uri.IsAbsoluteUri && uri.Scheme is "file")
        {
            return _allowLocalFiles ? await base.GetResourceStream(uri) : null;
        }

        if (uri.IsAbsoluteUri && uri.Scheme is "http" or "https")
        {
            if (_httpClient is null)
            {
                return null;
            }

            try
            {
                var response = await _httpClient.GetAsync(uri.Uri);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var headers = response.Headers
                    .Union(response.Content.Headers)
                    .ToDictionary(header => header.Key, header => header.Value.ToArray());
                var stream = await response.Content.ReadAsStreamAsync();

                return new RNetworkResponse(stream, headers);
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

        // data: is handled by the adapter; anything else is unresolved.
        return null;
    }
}
