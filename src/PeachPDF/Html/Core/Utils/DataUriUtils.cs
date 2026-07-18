using System;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Synchronous <c>data:</c> URI decoding shared by every layer that needs to inspect a data
    /// URI's declared MIME type/bytes without going through the async
    /// <see cref="Network.DataUriNetworkLoader"/>/<see cref="Handlers.ImageLoadHandler"/> pipeline
    /// (e.g. to reject an unsupported MIME type before ever attempting to load it).
    /// </summary>
    internal static class DataUriUtils
    {
        public static bool TryDecodeDataUri(string? uri, out string mimeType, out byte[] bytes)
        {
            mimeType = "";
            bytes = [];

            if (uri is null || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return false;

            var comma = uri.IndexOf(',');
            if (comma < 0)
                return false;

            var header = uri[5..comma];
            var data = uri[(comma + 1)..];

            var headerParts = header.Split(';');
            mimeType = headerParts.Length > 0 && headerParts[0].Length > 0 ? headerParts[0] : "text/plain";
            var isBase64 = headerParts.Any(p => p.Equals("base64", StringComparison.OrdinalIgnoreCase));

            try
            {
                bytes = isBase64
                    ? Convert.FromBase64String(data)
                    : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
            }
            catch (FormatException)
            {
                return false;
            }

            return true;
        }
    }
}
