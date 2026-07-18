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
                // Per the data: URL spec, the body is percent-decoded first and THEN (if the ;base64
                // flag is present) base64-decoded - percent-decoding is not conditional on the encoding
                // flag. A base64 payload containing reserved characters (/, +, =) is commonly written
                // with them percent-escaped (%2F, %2B, %3D) - e.g. the real Acid2 test's embedded PNGs -
                // and Convert.FromBase64String throws on a literal "%" in its input, so skipping this
                // step here silently failed to decode every such image.
                bytes = isBase64
                    ? Convert.FromBase64String(Uri.UnescapeDataString(data))
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
