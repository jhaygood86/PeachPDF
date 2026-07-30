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
    /// Per-codepoint <see cref="BidiClass"/> (Unicode's <c>Bidi_Class</c> property) lookup, backed by a
    /// Brotli-compressed, run-length-encoded table generated from the Unicode Character Database's
    /// <c>DerivedBidiClass.txt</c> (Unicode 17.0.0 - see <c>assets/unicode/generate_bidi_tables.py</c> for
    /// provenance/regeneration), embedded as <c>Text/Resources/Bidi/DerivedBidiClass.txt.br</c>. This is the
    /// one codepoint-classification primitive both the UAX #9 resolver (<see cref="BidiResolver"/>) and the
    /// HTML <c>dir="auto"</c> first-strong-character detection consume.
    /// </summary>
    internal static class BidiClassTable
    {
        // Each run is [Start, End] inclusive, sorted and non-overlapping, covering every codepoint
        // 0..0x10FFFF (the generator always emits a complete partition of the whole codepoint space).
        private readonly record struct Run(int Start, int End, BidiClass Class);

        private static readonly Lazy<Run[]> Runs = new(LoadRuns);

        /// <summary>
        /// Resolves a codepoint's Unicode <c>Bidi_Class</c>. Codepoints outside the loaded table (should
        /// never happen - the generated table spans the full 0..0x10FFFF codepoint space) and hosts that
        /// cannot decompress the embedded resource (e.g. a WebAssembly host with no relinked Brotli decoder)
        /// both fall back to <see cref="BidiClass.L"/> - the Unicode default for the vast majority of
        /// assigned codepoints, and a safe "don't reorder" default for a host that cannot load the real
        /// table at all.
        /// </summary>
        public static BidiClass Of(int codepoint)
        {
            var runs = Runs.Value;
            if (runs.Length == 0)
                return BidiClass.L;

            var lo = 0;
            var hi = runs.Length - 1;
            while (lo <= hi)
            {
                var mid = lo + (hi - lo) / 2;
                var run = runs[mid];
                if (codepoint < run.Start) hi = mid - 1;
                else if (codepoint > run.End) lo = mid + 1;
                else return run.Class;
            }

            return BidiClass.L;
        }

        public static BidiClass Of(System.Text.Rune rune) => Of(rune.Value);

        private static Run[] LoadRuns()
        {
            var assembly = typeof(BidiClassTable).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("DerivedBidiClass.txt.br", StringComparison.OrdinalIgnoreCase));

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

            var runs = new List<Run>(1300);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                var parts = line.Split(' ', 3);
                if (parts.Length != 3) continue;

                var start = int.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var end = int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (!Enum.TryParse<BidiClass>(parts[2], out var cls)) continue;

                runs.Add(new Run(start, end, cls));
            }

            return runs.ToArray();
        }
    }
}
