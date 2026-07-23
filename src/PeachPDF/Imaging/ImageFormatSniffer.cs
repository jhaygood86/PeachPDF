using System;

namespace PeachPDF.Imaging
{
    /// <summary>
    /// Cheap container-format sniffing from magic bytes, used by codecs (like <see cref="Linux.LinuxPlatformCodec"/>)
    /// that only handle specific formats and need to know which native library to even attempt before decoding.
    /// </summary>
    internal static class ImageFormatSniffer
    {
        /// <summary>
        /// A WebP file is a RIFF container: bytes 0-3 "RIFF", bytes 8-11 "WEBP".
        /// </summary>
        public static bool IsWebP(ReadOnlySpan<byte> bytes)
        {
            return bytes.Length >= 12
                && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
        }

        /// <summary>
        /// An AVIF file is an ISO base media file with an "ftyp" box (bytes 4-7) whose major or
        /// compatible brand is "avif" (still image) or "avis" (image sequence).
        /// </summary>
        public static bool IsAvif(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 12 || bytes[4] != (byte)'f' || bytes[5] != (byte)'t' || bytes[6] != (byte)'y' || bytes[7] != (byte)'p')
            {
                return false;
            }

            var boxSize = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            if (boxSize < 8 || boxSize > bytes.Length)
            {
                boxSize = (uint)bytes.Length;
            }

            // Major brand (bytes 8-11), then minor version (bytes 12-15), then a list of compatible
            // brands (4 bytes each) filling the rest of the box.
            for (var offset = 8; offset + 4 <= boxSize; offset += 4)
            {
                if (offset == 12) continue; // minor version, not a brand

                if (IsBrand(bytes, offset, "avif") || IsBrand(bytes, offset, "avis"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBrand(ReadOnlySpan<byte> bytes, int offset, string brand)
        {
            return bytes[offset] == (byte)brand[0]
                && bytes[offset + 1] == (byte)brand[1]
                && bytes[offset + 2] == (byte)brand[2]
                && bytes[offset + 3] == (byte)brand[3];
        }
    }
}
