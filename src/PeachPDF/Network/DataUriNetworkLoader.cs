#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;

namespace PeachPDF.Network
{
    /// <summary>
    /// The default <see cref="RNetworkLoader"/> used when <see cref="PeachPDF.PdfGenerateConfig.NetworkLoader"/>
    /// is left unset. Resolves only <c>data:</c> URIs; any other resource URI (remote stylesheets, remote
    /// images, HTTP(S) sources) is silently skipped. This is the safest default for server-side environments
    /// where network access must be opted into explicitly rather than assumed.
    /// </summary>
    public class DataUriNetworkLoader : RNetworkLoader
    {
        /// <inheritdoc/>
        public override RUri? BaseUri => null;

        /// <summary>
        /// Not supported by this loader — the HTML string must always be passed directly to
        /// <c>PdfGenerator.GeneratePdf</c>/<c>AddPdfPages</c> rather than loaded via a network loader.
        /// </summary>
        public override Task<string> GetPrimaryContents()
        {
            return null!;
        }

        /// <inheritdoc/>
        public override Task<RNetworkResponse?> GetResourceStream(RUri uri)
        {
            if (uri.Scheme is not "data") return Task.FromResult<RNetworkResponse?>(null);

            var dataUri = uri.AbsoluteUri;
            var uriComponents = dataUri.Split(':', 2);
            var uriDataComponents = uriComponents[1].Split(';', 2);

            if (uriDataComponents[1].StartsWith("base64,"))
            {
                uriDataComponents[1] = uriDataComponents[1][7..];
            }

            uriDataComponents[1] = Uri.UnescapeDataString(uriDataComponents[1]);

            var contents = Convert.FromBase64String(uriDataComponents[1]);

            var response = new RNetworkResponse(new MemoryStream(contents), null);

            return Task.FromResult<RNetworkResponse?>(response);

        }
    }
}
