#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;

namespace PeachPDF.Network
{
    public class DataUriNetworkLoader : RNetworkLoader
    {
        public override RUri? BaseUri => null;

        public override Task<string> GetPrimaryContents()
        {
            return null!;
        }

        public override Task<Stream?> GetResourceStream(RUri uri)
        {
            if (uri.Scheme is not "data") return Task.FromResult<Stream?>(null);

            var dataUri = uri.AbsoluteUri;
            var uriComponents = dataUri.Split(':', 2);
            var uriDataComponents = uriComponents[1].Split(';', 2);

            if (uriDataComponents[1].StartsWith("base64,"))
            {
                uriDataComponents[1] = uriDataComponents[1][7..];
            }

            uriDataComponents[1] = Uri.UnescapeDataString(uriDataComponents[1]);

            var contents = Convert.FromBase64String(uriDataComponents[1]);

            return Task.FromResult<Stream?>(new MemoryStream(contents));

        }
    }
}
