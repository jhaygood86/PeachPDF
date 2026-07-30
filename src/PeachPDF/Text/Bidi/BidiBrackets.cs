using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PeachPDF.Text.Bidi
{
    /// <summary>Whether a codepoint with a bracket pairing is the opening or closing member of the pair.</summary>
    internal enum BidiBracketType : byte
    {
        None,
        Open,
        Close
    }

    /// <summary>
    /// Bracket-pair lookup for UAX #9's N0 rule (bracket pairs), backed by a Brotli-compressed table
    /// generated from the Unicode Character Database's <c>BidiBrackets.txt</c> (Unicode 17.0.0 - see
    /// <c>assets/unicode/generate_bidi_tables.py</c>), embedded as
    /// <c>Text/Resources/Bidi/BidiBrackets.txt.br</c>.
    /// </summary>
    internal static class BidiBrackets
    {
        private readonly record struct Entry(int Paired, BidiBracketType Type);

        private static readonly Lazy<Dictionary<int, Entry>> Table = new(Load);

        /// <summary>
        /// Looks up <paramref name="codepoint"/>'s bracket pairing. Returns <see langword="false"/> for a
        /// codepoint with no <c>Bidi_Paired_Bracket_Type</c> (i.e. not a bracket for N0 purposes at all).
        /// </summary>
        public static bool TryGetBracket(int codepoint, out int pairedCodepoint, out BidiBracketType type)
        {
            if (Table.Value.TryGetValue(codepoint, out var entry))
            {
                pairedCodepoint = entry.Paired;
                type = entry.Type;
                return true;
            }

            pairedCodepoint = 0;
            type = BidiBracketType.None;
            return false;
        }

        private static Dictionary<int, Entry> Load()
        {
            var assembly = typeof(BidiBrackets).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("BidiBrackets.txt.br", StringComparison.OrdinalIgnoreCase));

            var result = new Dictionary<int, Entry>(160);
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

                var parts = line.Split(' ', 3);
                if (parts.Length != 3) continue;

                var codepoint = int.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var paired = int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var type = parts[2] == "o" ? BidiBracketType.Open : BidiBracketType.Close;

                result[codepoint] = new Entry(paired, type);
            }

            return result;
        }
    }
}
