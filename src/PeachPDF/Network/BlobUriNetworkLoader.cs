#nullable enable

using PeachBrackets;
using PeachDrawing.Core;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Network
{
    /// <summary>
    /// Resolves <c>blob:</c> addresses, the object URLs a document's script makes with <c>URL.createObjectURL</c> (see
    /// <c>PeachBrackets.Scripting</c>): the bytes of the object, with its media type as the <c>Content-Type</c> header. Like <see cref="DataUriNetworkLoader"/>
    /// for <c>data:</c> addresses, PeachPDF always puts it ahead of the loader that was configured, so an object URL works wherever an address
    /// does (an image, a style sheet, a font, a script) whichever loader the host chose. Any other address is not its business and resolves to <c>null</c>.
    /// </summary>
    public class BlobUriNetworkLoader : RNetworkLoader
    {
        /// <inheritdoc/>
        public override Uri? BaseUri => null;

        /// <summary>Not supported by this loader: the HTML string is always passed to <c>PdfGenerator</c> directly.</summary>
        public override Task<string> GetPrimaryContents() => null!;

        /// <inheritdoc/>
        public override Task<RNetworkResponse?> GetResourceStream(Uri uri) => Task.FromResult(BlobUrlStore.Load(uri));
    }
}
