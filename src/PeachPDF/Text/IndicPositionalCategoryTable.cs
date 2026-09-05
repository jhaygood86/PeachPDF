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
    /// Per-codepoint <see cref="IndicPositionalCategory"/> lookup - same Brotli-compressed,
    /// run-length-encoded, binary-searched shape as <see cref="IndicSyllabicCategoryTable"/> (see its
    /// own remarks), reading <c>Text/Resources/Use/IndicPositionalCategory.txt.br</c>.
    /// </summary>
    internal static class IndicPositionalCategoryTable
    {
        private readonly record struct Run(int Start, int End, IndicPositionalCategory Value);

        private static readonly Lazy<Run[]> Runs = new(LoadRuns);

        /// <summary>
        /// Resolves a codepoint's Unicode <c>Indic_Positional_Category</c>. Codepoints outside the
        /// loaded table and hosts that cannot decompress the embedded resource both fall back to
        /// <see cref="IndicPositionalCategory.NotApplicable"/> - the UCD file's own <c>@missing</c>
        /// default, and a safe fallback that never mis-tags a codepoint as pre-base when the real
        /// table couldn't load.
        /// </summary>
        public static IndicPositionalCategory Of(int codepoint)
        {
            var runs = Runs.Value;
            if (runs.Length == 0)
                return IndicPositionalCategory.NotApplicable;

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

            return IndicPositionalCategory.NotApplicable;
        }

        private static Run[] LoadRuns()
        {
            var assembly = typeof(IndicPositionalCategoryTable).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("IndicPositionalCategory.txt.br", StringComparison.OrdinalIgnoreCase));

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

            var runs = new List<Run>(900);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                var parts = line.Split(' ', 3);
                if (parts.Length != 3) continue;

                var start = int.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var end = int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (!Enum.TryParse<IndicPositionalCategory>(parts[2], out var value)) continue;

                runs.Add(new Run(start, end, value));
            }

            return runs.ToArray();
        }
    }
}
