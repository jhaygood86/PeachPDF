using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PeachPDF.Text.Bidi
{
    /// <summary>
    /// Character-mirroring lookup for UAX #9's L4 rule (mirrored characters, e.g. <c>(</c>/<c>)</c>), backed
    /// by a Brotli-compressed table generated from the Unicode Character Database's
    /// <c>BidiMirroring.txt</c> (Unicode 17.0.0 - see <c>assets/unicode/generate_bidi_tables.py</c>),
    /// embedded as <c>Text/Resources/Bidi/BidiMirroring.txt.br</c>.
    /// </summary>
    internal static class BidiMirroring
    {
        private static readonly Lazy<Dictionary<int, int>> Table = new(Load);

        /// <summary>
        /// Looks up <paramref name="codepoint"/>'s mirror-image codepoint (e.g. <c>(</c> -&gt; <c>)</c>).
        /// Returns <see langword="false"/> for a codepoint with no <c>Bidi_Mirroring_Glyph</c> mapping.
        /// </summary>
        public static bool TryGetMirror(int codepoint, out int mirroredCodepoint) =>
            Table.Value.TryGetValue(codepoint, out mirroredCodepoint);

        private static Dictionary<int, int> Load()
        {
            var assembly = typeof(BidiMirroring).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("BidiMirroring.txt.br", StringComparison.OrdinalIgnoreCase));

            var result = new Dictionary<int, int>(450);
            if (resourceName is null)
                return result;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return result;

            Stream decompressed;
            try
            {
                decompressed = new BrotliStream(stream, CompressionMode.Decompress);
            }
            catch (PlatformNotSupportedException)
            {
                return result;
            }

            using var brotli = decompressed;
            using var reader = new StreamReader(brotli, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                var parts = line.Split(' ', 2);
                if (parts.Length != 2) continue;

                var codepoint = int.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var mirror = int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                result[codepoint] = mirror;
            }

            return result;
        }
    }
}
