#nullable enable

using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
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

            if (!DataUriUtils.TryDecodeDataUri(uri.AbsoluteUri, out var mimeType, out var bytes))
                return Task.FromResult<RNetworkResponse?>(null);

            // Report the data URI's own declared MIME type as a Content-Type header so consumers
            // that content-type-sniff the response (e.g. StylesheetLoadHandler, for a
            // data:text/css,... <link>) can recognize it - a data: URI has no real HTTP response.
            var headers = new Dictionary<string, string[]> { ["Content-Type"] = [mimeType] };
            var response = new RNetworkResponse(new MemoryStream(bytes), headers);

            return Task.FromResult<RNetworkResponse?>(response);
        }
    }
}
