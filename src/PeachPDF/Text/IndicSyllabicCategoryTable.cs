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
    /// Per-codepoint <see cref="IndicSyllabicCategory"/> lookup, backed by a Brotli-compressed,
    /// run-length-encoded table generated from the Unicode Character Database's
    /// IndicSyllabicCategory.txt (Unicode 17.0.0 - see
    /// <c>assets/unicode/generate_use_category_tables.py</c> for provenance/regeneration), embedded
    /// as <c>Text/Resources/Use/IndicSyllabicCategory.txt.br</c>. Mirrors
    /// <see cref="ArabicShapingTable"/>'s own shape - same run-array/binary-search/Brotli-resource
    /// pattern, for the same UAX-property-lookup problem under a different property.
    /// </summary>
    internal static class IndicSyllabicCategoryTable
    {
        private readonly record struct Run(int Start, int End, IndicSyllabicCategory Value);

        private static readonly Lazy<Run[]> Runs = new(LoadRuns);

        /// <summary>
        /// Resolves a codepoint's Unicode <c>Indic_Syllabic_Category</c>. Codepoints outside the
        /// loaded table (should never happen) and hosts that cannot decompress the embedded resource
        /// both fall back to <see cref="IndicSyllabicCategory.Other"/> - the UCD file's own
        /// <c>@missing</c> default, and a safe fallback for a host that cannot load the real table at
        /// all: every codepoint classifies as a non-participating "other" character, so USE syllable
        /// classification degrades to treating Devanagari text as a run of isolated single-glyph
        /// syllables rather than guessing wrong about consonant/vowel/virama structure.
        /// </summary>
        public static IndicSyllabicCategory Of(int codepoint)
        {
            var runs = Runs.Value;
            if (runs.Length == 0)
                return IndicSyllabicCategory.Other;

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

            return IndicSyllabicCategory.Other;
        }

        private static Run[] LoadRuns()
        {
            var assembly = typeof(IndicSyllabicCategoryTable).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("IndicSyllabicCategory.txt.br", StringComparison.OrdinalIgnoreCase));

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

            var runs = new List<Run>(1200);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                var parts = line.Split(' ', 3);
                if (parts.Length != 3) continue;

                var start = int.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var end = int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (!Enum.TryParse<IndicSyllabicCategory>(parts[2], out var value)) continue;

                runs.Add(new Run(start, end, value));
            }

            return runs.ToArray();
        }
    }
}
