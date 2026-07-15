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
                RUri? baseUri;

                if (importingBaseUri is not null)
                {
                    baseUri = importingBaseUri;
                }
                else
                {
                    var baseElement = DomUtils.GetBoxByTagName(htmlContainer.Root, "base");
                    var baseUrl = "";

                    if (baseElement is not null)
                    {
                        baseUrl = baseElement.HtmlTag!.TryGetAttribute("href", "");
                    }

                    baseUri = string.IsNullOrWhiteSpace(baseUrl) ? htmlContainer.Adapter.BaseUri : new RUri(baseUrl);
                }

                var href = baseUri is null ? src : new RUri(baseUri, src).AbsoluteUri;

                var uri = CommonUtils.TryGetUri(href)!;

                Stream? stream = null;
                var isInvalidNetworkResponse = false;

                if (uri.IsFile)
                {
                    var fileInfo = CommonUtils.TryGetFileInfo(uri.AbsoluteUri)!;

                    if (fileInfo.Exists)
                    {
                        stream = fileInfo.OpenRead();
                    }
                }
                else
                {
                    var networkResponse = await htmlContainer.Adapter.GetResourceStream(uri);

                    isInvalidNetworkResponse = true;

                    if (networkResponse?.ResponseHeaders?.TryGetValue("Content-Type", out var contentTypeValues) ?? false)
                    {
                        var contentTypes = contentTypeValues.Select(ContentType.Parse);

                        if (contentTypes.Any(ct => ct.IsMimeType("text", "css")))
                        {
                            stream = networkResponse.ResourceStream;
                            isInvalidNetworkResponse = false;
                        }
                    }

                }

                if (isInvalidNetworkResponse)
                {
                    return (string.Empty, uri);
                }

                if (stream is null)
                {
                    htmlContainer.ReportError(HtmlRenderErrorType.CssParsing, "No stylesheet found by path: " + src);
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