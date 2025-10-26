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
        /// <returns>the stylesheet string</returns>
        public static async Task<string?> LoadStylesheet(HtmlContainerInt htmlContainer, string src)
        {
            try
            {
                var baseElement = DomUtils.GetBoxByTagName(htmlContainer.Root, "base");
                var baseUrl = "";

                if (baseElement is not null)
                {
                    baseUrl = baseElement.HtmlTag!.TryGetAttribute("href", "");
                }

                var baseUri = string.IsNullOrWhiteSpace(baseUrl) ? htmlContainer.Adapter.BaseUri : new RUri(baseUrl);
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
                    return string.Empty;
                }

                if (stream is null)
                {
                    htmlContainer.ReportError(HtmlRenderErrorType.CssParsing, "No stylesheet found by path: " + src);
                    return string.Empty;
                }

                using var sr = new StreamReader(stream);
                return await sr.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                htmlContainer.ReportError(HtmlRenderErrorType.CssParsing, "Exception in handling stylesheet source", ex);
                return null;
            }
        }
    }
}