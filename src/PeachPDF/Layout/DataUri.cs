using System;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Wraps raw image bytes (<see cref="IContainer.Image(byte[])"/>) as an inline <c>data:</c> URI, so
    /// they reach <see cref="Html.Core.Handlers.ImageLoadHandler"/>'s existing, already-battle-tested
    /// inline-base64-image loading path unchanged - the same path a <c>data:</c>-URI <c>&lt;img src&gt;</c>
    /// in HTML already uses.
    /// </summary>
    internal static class DataUri
    {
        public static string FromBytes(byte[] data)
        {
            var mimeType = SniffMimeType(data);
            return $"data:{mimeType};base64,{Convert.ToBase64String(data)}";
        }

        /// <summary>
        /// Sniffs a raster image's mime type from its own file-signature bytes - PNG, JPEG, GIF, BMP, and
        /// WEBP are all distinguishable this way. Defaults to <c>image/png</c> for anything else, since
        /// the underlying loader still needs *some* mime type to attempt decoding with.
        /// </summary>
        private static string SniffMimeType(byte[] data)
        {
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return "image/png";

            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return "image/jpeg";

            if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
                return "image/gif";

            if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M')
                return "image/bmp";

            if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
                && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
                return "image/webp";

            return "image/png";
        }
    }
}
