using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PeachPDF.Text
{
    /// <summary>
    /// Per-codepoint <see cref="VerticalOrientationClass"/> (Unicode's <c>Vertical_Orientation</c>
    /// property) lookup, backed by a Brotli-compressed, run-length-encoded table generated from the
    /// Unicode Character Database's <c>VerticalOrientation.txt</c> (Unicode 17.0.0 - see
    /// <c>assets/unicode/generate_vertical_orientation_table.py</c> for provenance/regeneration),
    /// embedded as <c>Text/Resources/VerticalOrientation/VerticalOrientation.txt.br</c>. Mirrors
    /// <see cref="Bidi.BidiClassTable"/>'s own shape - same run-array/binary-search/Brotli-resource
    /// pattern, for the same UAX-property-lookup problem under a different UAX.
    /// </summary>
    internal static class VerticalOrientationTable
    {
        // Each run is [Start, End] inclusive, sorted and non-overlapping, covering every codepoint
        // 0..0x10FFFF (the generator always emits a complete partition of the whole codepoint space).
        private readonly record struct Run(int Start, int End, VerticalOrientationClass Value);

        private static readonly Lazy<Run[]> Runs = new(LoadRuns);

        /// <summary>
        /// Resolves a codepoint's Unicode <c>Vertical_Orientation</c>. Codepoints outside the loaded
        /// table (should never happen - the generated table spans the full 0..0x10FFFF codepoint space)
        /// and hosts that cannot decompress the embedded resource (e.g. a WebAssembly host with no
        /// relinked Brotli decoder) both fall back to <see cref="VerticalOrientationClass.R"/> - the
        /// Unicode default for the vast majority of codepoints (the file's own single
        /// <c>@missing: 0000..10FFFF; R</c> line), and a safe fallback for a host that cannot load the
        /// real table at all: it reproduces this repo's prior "everything rotates" behavior rather than
        /// silently treating unclassifiable text as upright.
        /// </summary>
        public static VerticalOrientationClass Of(int codepoint)
        {
            var runs = Runs.Value;
            if (runs.Length == 0)
                return VerticalOrientationClass.R;

            var lo = 0;
            var hi = runs.Length - 1;
            while (lo <= hi)
            {
                var mid = lo + (hi - lo) / 2;
                var run = runs[mid];
                if (codepoint < run.Start) hi = mid - 1;
                else if (codepoint > run.End) lo = mid + 1;
                else return run.Value;
            }

            return VerticalOrientationClass.R;
        }

        public static VerticalOrientationClass Of(System.Text.Rune rune) => Of(rune.Value);

        private static Run[] LoadRuns()
        {
            var assembly = typeof(VerticalOrientationTable).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("VerticalOrientation.txt.br", StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
                return [];

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return [];

            Stream decompressed;
            try
            {
                decompressed = new BrotliStream(stream, CompressionMode.Decompress);
            }
            catch (PlatformNotSupportedException)
            {
                return [];
            }

            using var brotli = decompressed;
            using var reader = new StreamReader(brotli, Encoding.UTF8);

            var runs = new List<Run>(300);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                var parts = line.Split(' ', 3);
                if (parts.Length != 3) continue;

                var start = int.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var end = int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (!Enum.TryParse<VerticalOrientationClass>(parts[2], out var value)) continue;

                runs.Add(new Run(start, end, value));
            }

            return runs.ToArray();
        }
    }
}
