using PeachBrackets.Html.Utils;

namespace PeachBrackets
{
    /// <summary>Decoding of <c>data:</c> URIs.</summary>
    public static class DataUri
    {
        /// <summary>
        /// Decodes a <c>data:</c> URI synchronously: percent-decoded first, then base64-decoded when the <c>;base64</c> flag is present.
        /// </summary>
        /// <param name="uri">The URI text.</param>
        /// <param name="mimeType">The declared MIME type, or <c>text/plain</c> when the URI declares none.</param>
        /// <param name="bytes">The decoded payload.</param>
        /// <returns><see langword="false"/> when <paramref name="uri"/> is not a well-formed <c>data:</c> URI.</returns>
        public static bool TryDecode(string? uri, out string mimeType, out byte[] bytes) =>
            DataUriUtils.TryDecodeDataUri(uri, out mimeType, out bytes);
    }
}
