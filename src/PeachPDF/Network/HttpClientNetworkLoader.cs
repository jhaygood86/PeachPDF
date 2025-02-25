#nullable enable

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace PeachPDF.Network
{
    public class HttpClientNetworkLoader(HttpClient httpClient, Uri? primaryContentsUri) : RNetworkLoader
    {
        public override RUri? BaseUri => primaryContentsUri != null ? new RUri(primaryContentsUri) : null;

        public override async Task<string> GetPrimaryContents()
        {
            if (BaseUri is null)
            {
                throw new InvalidOperationException("Primary contents URL is not set.");
            }

            var stream = await GetResourceStream(BaseUri);

            if (stream is null)
            {
                throw new InvalidOperationException("Primary contents stream is null.");
            }

            using var streamReader = new StreamReader(stream);
            return await streamReader.ReadToEndAsync();
        }

        public override async Task<Stream?> GetResourceStream(RUri uri)
        {
            var response = await httpClient.GetAsync(uri.Uri);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStreamAsync();
        }
    }
}
