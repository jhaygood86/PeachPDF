using PeachPDF.Network;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// A minimal <see cref="RNetworkLoader"/> backed by an in-memory URL → bytes map, for tests that need
    /// to verify relative-URL resolution across multiple fetched documents (nested stylesheets, @font-face
    /// inside an @import, etc.) without touching the network or the file system.
    /// </summary>
    internal sealed class InMemoryNetworkLoader(RUri baseUri, string primaryHtml) : RNetworkLoader
    {
        private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _resources = new();

        public override RUri? BaseUri { get; } = baseUri;

        public void AddResource(string absoluteUrl, byte[] bytes, string contentType) =>
            _resources[absoluteUrl] = (bytes, contentType);

        public void AddTextResource(string absoluteUrl, string text, string contentType) =>
            AddResource(absoluteUrl, Encoding.UTF8.GetBytes(text), contentType);

        public override Task<string> GetPrimaryContents() => Task.FromResult(primaryHtml);

        public override Task<RNetworkResponse?> GetResourceStream(RUri uri)
        {
            if (_resources.TryGetValue(uri.AbsoluteUri, out var resource))
            {
                var headers = new Dictionary<string, string[]> { ["Content-Type"] = [resource.ContentType] };
                return Task.FromResult<RNetworkResponse?>(new RNetworkResponse(new MemoryStream(resource.Bytes), headers));
            }

            return Task.FromResult<RNetworkResponse?>(null);
        }
    }
}
