using System.Threading.Tasks;

namespace PeachPDF.Network
{
    /// <summary>
    /// Controls how PeachPDF loads the root HTML document and every external resource it references
    /// (stylesheets, images). Set an instance on <see cref="PeachPDF.PdfGenerateConfig.NetworkLoader"/> to
    /// integrate PeachPDF with a custom resource source (a cloud blob store, a bundler manifest, an in-memory
    /// dictionary, etc.). PeachPDF ships four concrete implementations: <see cref="DataUriNetworkLoader"/>
    /// (the default when none is configured), <see cref="FileUriNetworkLoader"/> for local files,
    /// <see cref="MimeKitNetworkLoader"/> for MHTML archives, and <see cref="HttpClientNetworkLoader"/> for
    /// HTTP(S) sources. <c>data:</c> and <c>file:</c> URIs are always handled internally regardless of which
    /// loader is configured.
    /// </summary>
    public abstract class RNetworkLoader
    {
        /// <summary>
        /// Returns the root HTML document as a string. Called once at the start of rendering when <c>null</c>
        /// is passed as the HTML argument to a <c>PdfGenerator.GeneratePdf</c>/<c>AddPdfPages</c> overload.
        /// </summary>
        public abstract Task<string> GetPrimaryContents();

        /// <summary>
        /// Returns the content of an external resource (a stylesheet or image) referenced by the document, or
        /// <c>null</c> if the resource cannot be resolved.
        /// </summary>
        /// <param name="uri">The resource URI, resolved against <see cref="BaseUri"/> or a <c>&lt;base href&gt;</c> element if relative.</param>
        public abstract Task<RNetworkResponse?> GetResourceStream(RUri uri);

        /// <summary>
        /// The document's base URL, used to resolve relative <c>href</c>, <c>src</c>, and CSS <c>url()</c>
        /// references. If <c>null</c>, relative references are resolved against the local file system.
        /// </summary>
        public abstract RUri? BaseUri { get; }
    }
}
