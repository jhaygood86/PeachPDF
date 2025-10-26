#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PeachPDF.Network
{
    public class HttpClientNetworkLoader(HttpClient httpClient, Uri? primaryContentsUri) : RNetworkLoader
    {
        public HttpClientNetworkLoader(HttpClient httpClient, string primaryContentsUri)
            : this(httpClient, new Uri(primaryContentsUri))
        {

        }

        public override RUri? BaseUri => primaryContentsUri != null ? new RUri(primaryContentsUri) : null;

        public override async Task<string> GetPrimaryContents()
        {
            if (BaseUri is null)
            {
                throw new InvalidOperationException("Primary contents URL is not set.");
            }

            var networkResponse = await GetResourceStream(BaseUri);

            if (networkResponse?.ResourceStream is null)
            {
                throw new InvalidOperationException("Primary contents stream is null.");
            }

            using var streamReader = new StreamReader(networkResponse.ResourceStream);
            return await streamReader.ReadToEndAsync();
        }

        public override async Task<RNetworkResponse?> GetResourceStream(RUri uri)
        {
            var response = await httpClient.GetAsync(uri.Uri);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var headers = response.Headers
                .Union(response.Content.Headers)
                .ToDictionary(x => x.Key, x => x.Value.ToArray());

            var stream = await response.Content.ReadAsStreamAsync();

            return new RNetworkResponse(stream, headers);
        }
    }
}
