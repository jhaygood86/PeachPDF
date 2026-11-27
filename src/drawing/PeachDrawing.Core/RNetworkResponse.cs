using System.Collections.Generic;
using System.IO;

namespace PeachDrawing.Core
{
    /// <summary>
    /// The result of resolving a resource URI via <see cref="RenderContext.GetResourceStream"/>.
    /// </summary>
    /// <param name="ResourceStream">The resource's content stream, or <c>null</c> if the resource has no body.</param>
    /// <param name="ResponseHeaders">
    /// HTTP-style response headers for the resource, if any. For stylesheet resources, a host may inspect this
    /// for a <c>Content-Type</c> header and only accepts the body as CSS when it is <c>text/css</c>.
    /// </param>
    public record RNetworkResponse(Stream? ResourceStream, Dictionary<string, string[]>? ResponseHeaders)
    {
        /// <summary>
        /// The address the body was finally read from when the request was redirected (the last of the hops), or <c>null</c> when the loader does not say or
        /// there was no redirect. A document asks it whether a resource that was requested from its own origin ended up on another one.
        /// </summary>
        public System.Uri? FinalAddress { get; init; }
    }
}
