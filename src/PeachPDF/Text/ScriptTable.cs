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
    /// Per-codepoint Unicode <c>Script</c> property lookup (<see href="https://www.unicode.org/reports/tr24/">UAX #24</see>),
    /// backed by a Brotli-compressed, run-length-encoded table generated from the Unicode Character
    /// Database's Scripts.txt (Unicode 17.0.0 - see <c>assets/unicode/generate_script_table.py</c> for
    /// provenance/regeneration), embedded as <c>Text/Resources/Script/Scripts.txt.br</c>. Mirrors
    /// <see cref="VerticalOrientationTable"/>/<see cref="ArabicShapingTable"/>'s own shape - same
    /// run-array/binary-search/Brotli-resource pattern - but returns the raw Unicode script name
    /// (e.g. <c>"Latin"</c>, <c>"Arabic"</c>, <c>"Common"</c>, <c>"Inherited"</c>) as a string rather
    /// than a fixed enum, since UAX #24 defines ~170 distinct script values (a fixed enum would be pure
    /// boilerplate with no type-safety benefit an <see cref="OpenTypeScriptTags"/> dictionary lookup
    /// doesn't already provide).
    /// </summary>
    internal static class ScriptTable
    {
        /// <summary>The Script value every unassigned/reserved codepoint gets - not itself a real script,
        /// a signal that scans for the nearest real script (see resolving <c>Common</c>/<c>Inherited</c>
        /// against surrounding text) should skip past it the same way they skip <c>Common</c>.</summary>
        public const string Unknown = "Unknown";

        /// <summary>Punctuation/whitespace/digits and other codepoints shared across every script -
        /// resolves to the nearest real script in a run of text, per UAX #24 §5.1.</summary>
        public const string Common = "Common";

        /// <summary>Combining marks and a handful of other codepoints that inherit their Script value
        /// from the preceding base character - resolves the same way <see cref="Common"/> does.</summary>
        public const string Inherited = "Inherited";

        // Each run is [Start, End] inclusive, sorted and non-overlapping, covering every codepoint
        // 0..0x10FFFF (the generator always emits a complete partition of the whole codepoint space).
        private readonly record struct Run(int Start, int End, string Value);

        private static readonly Lazy<Run[]> Runs = new(LoadRuns);

        /// <summary>
        /// Resolves a codepoint's raw Unicode <c>Script</c> value - <see cref="Common"/> or
        /// <see cref="Inherited"/> for a codepoint that doesn't itself carry a specific script (callers
        /// needing the resolved-against-surrounding-text script for a run should look elsewhere for that
        /// UAX #24 §5.1 algorithm, not here). Codepoints outside the loaded table (should never happen)
        /// and hosts that cannot decompress the embedded resource (e.g. a WebAssembly host with no
        /// relinked Brotli decoder) both fall back to <see cref="Unknown"/>.
        /// </summary>
        public static string Of(int codepoint)
        {
            var runs = Runs.Value;
            if (runs.Length == 0)
                return Unknown;

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

            return Unknown;
        }

        public static string Of(System.Text.Rune rune) => Of(rune.Value);

        private static Run[] LoadRuns()
        {
            var assembly = typeof(ScriptTable).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Scripts.txt.br", StringComparison.OrdinalIgnoreCase));

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

            var runs = new List<Run>(2000);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                var parts = line.Split(' ', 3);
                if (parts.Length != 3) continue;

                var start = int.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var end = int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                runs.Add(new Run(start, end, parts[2]));
            }

            return runs.ToArray();
        }
    }
}
