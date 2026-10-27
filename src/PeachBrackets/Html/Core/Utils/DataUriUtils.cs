using System;
using System.Linq;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Synchronous <c>data:</c> URI decoding shared by every layer that needs to inspect a data
    /// URI's declared MIME type/bytes without going through the async
    /// <c>Network.DataUriNetworkLoader</c>/<see cref="Handlers.ImageLoadHandler"/> pipeline
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

            var header = uri.AsSpan(5, comma - 5);
            var data = uri.AsSpan(comma + 1);

            var firstPart = header;
            var semicolon = header.IndexOf(';');
            if (semicolon >= 0) firstPart = header[..semicolon];
            mimeType = firstPart.Length > 0 ? firstPart.ToString() : "text/plain";

            var isBase64 = false;
            var remaining = header;
            while (true)
            {
                var end = remaining.IndexOf(';');
                var part = end < 0 ? remaining : remaining[..end];
                if (part.Equals("base64", StringComparison.OrdinalIgnoreCase))
                {
                    isBase64 = true;
                    break;
                }

                if (end < 0) break;
                remaining = remaining[(end + 1)..];
            }

            try
            {
                // Per the data: URL spec, the body is percent-decoded first and THEN (if the ;base64
                // flag is present) base64-decoded - percent-decoding is not conditional on the encoding
                // flag. A base64 payload containing reserved characters (/, +, =) is commonly written
                // with them percent-escaped (%2F, %2B, %3D) - e.g. the real Acid2 test's embedded PNGs -
                // and Convert.FromBase64String throws on a literal "%" in its input, so skipping this
                // step here silently failed to decode every such image. A payload with no "%" has
                // nothing to unescape, so it is decoded in place instead of copied first.
                string? unescaped = null;
                if (data.Contains('%'))
                {
                    unescaped = Uri.UnescapeDataString(data.ToString());
                    data = unescaped;
                }

                if (isBase64)
                {
                    // Whitespace (line-wrapped base64) is ignored by TryFromBase64Chars as by FromBase64String;
                    // the buffer is the upper bound, trimmed to what was written.
                    var buffer = new byte[(data.Length + 3) / 4 * 3];
                    if (!Convert.TryFromBase64Chars(data, buffer, out var written))
                        return false;

                    bytes = written == buffer.Length ? buffer : buffer.AsSpan(0, written).ToArray();
                }
                else
                {
                    bytes = System.Text.Encoding.UTF8.GetBytes(data.ToString());
                }
            }
            catch (FormatException)
            {
                return false;
            }

            return true;
        }
    }
}
