using System.Linq;
using System.Net.Http;
using PeachPDF.Network;

namespace PeachPDF.Cli;

/// <summary>
/// The resource loader the CLI configures for a rendered document. It serves <c>http(s):</c> requests from
/// a caller-configured <see cref="HttpClient"/> (honoring <c>--http-timeout</c>/<c>--http-header</c>/
/// <c>--user-agent</c>/auth/proxy/<c>--insecure</c>, and <c>--no-network</c> when the client is null).
/// <c>data:</c> and <c>file:</c> URIs are handled internally by the adapter regardless — <c>file:</c>
/// subject to <see cref="PdfGenerateConfig.AllowLocalFileAccess"/>, which is where <c>--no-local-files</c>
/// is enforced. The document's base URL comes from <see cref="BaseUri"/> (from <c>--baseurl</c> or the
/// input's own location).
/// </summary>
internal sealed class CliNetworkLoader(RUri? baseUri, HttpClient? httpClient) : RNetworkLoader
{
    public override RUri? BaseUri => baseUri;

    /// <summary>
    /// Not supported: the CLI reads the root document itself (from a file, a URL, or stdin) and passes it
    /// to <c>GeneratePdf</c>, so this loader is only ever asked for the resources that document references.
    /// </summary>
    public override Task<string> GetPrimaryContents() =>
        throw new NotSupportedException("The CLI supplies the root document directly.");

    public override async Task<RNetworkResponse?> GetResourceStream(RUri uri)
    {
        if (uri.IsAbsoluteUri && uri.Scheme is "http" or "https")
        {
            if (httpClient is null)
            {
                return null;
            }

            try
            {
                var response = await httpClient.GetAsync(uri.Uri);
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

        // data: and file: are handled by the adapter; anything else is unresolved.
        return null;
    }
}
