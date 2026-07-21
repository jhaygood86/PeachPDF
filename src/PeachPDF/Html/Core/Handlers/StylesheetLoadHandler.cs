// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using MimeKit;
using PeachPDF;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Handler for loading a stylesheet data.
    /// </summary>
    internal static class StylesheetLoadHandler
    {
        /// <summary>
        /// Load stylesheet string from given source (file path or uri).
        /// </summary>
        /// <param name="htmlContainer">the container of the html to handle load stylesheet for</param>
        /// <param name="src">the file path or uri to load the stylesheet from</param>
        /// <param name="importingBaseUri">
        /// When resolving a reference found inside another already-loaded stylesheet (e.g. a nested
        /// <c>@import</c> or an <c>@font-face src</c>), the resolved location of that stylesheet. Relative
        /// references in fetched CSS always resolve against the stylesheet's own URL, not the document's
        /// base — so when this is given it takes precedence over the document's &lt;base&gt;/adapter base.
        /// Null for top-level loads (a document's own &lt;link&gt;/&lt;style&gt;), which keep resolving
        /// against the document base as before.
        /// </param>
        /// <returns>the stylesheet string, and the absolute URI it was resolved/fetched from (both null on failure)</returns>
        public static async Task<(string? Content, RUri? ResolvedUri)> LoadStylesheet(HtmlContainerInt htmlContainer, string src, RUri? importingBaseUri = null)
        {
            try
            {
                // Local files, data: URIs and remote resources all resolve uniformly through the network
                // loader now - a local stylesheet is served by FileUriNetworkLoader with a text/css
                // Content-Type, so it passes the same gate as a fetched one (no separate file-system path).
                var uri = CommonUtils.ResolveAgainstDocumentBase(htmlContainer, src, importingBaseUri);

                if (uri is null)
                {
                    // An unresolvable reference (e.g. a malformed URL) is treated as "not a stylesheet" -
                    // return empty and keep rendering, matching the missing/wrong-content-type handling below.
                    return (string.Empty, null);
                }

                var networkResponse = await htmlContainer.Adapter.GetResourceStream(uri);

                Stream? stream = null;

                if (networkResponse?.ResponseHeaders?.TryGetValue("Content-Type", out var contentTypeValues) ?? false)
                {
                    var contentTypes = contentTypeValues.Select(ContentType.Parse);

                    if (contentTypes.Any(ct => ct.IsMimeType("text", "css")))
                    {
                        stream = networkResponse.ResourceStream;
                    }
                }

                if (stream is null)
                {
                    // Missing resource, or a response whose Content-Type isn't text/css: treated as "not a
                    // stylesheet" - return empty so the rest of the document still renders.
                    return (string.Empty, uri);
                }

                using var sr = new StreamReader(stream);
                return (await sr.ReadToEndAsync(), uri);
            }
            catch (Exception ex)
            {
                htmlContainer.ReportError(HtmlRenderErrorType.CssParsing, "Exception in handling stylesheet source", ex);
                return (null, null);
            }
        }
    }
}