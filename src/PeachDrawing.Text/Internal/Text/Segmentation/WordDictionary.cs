using System;
using System.IO;
using System.IO.Compression;

namespace PeachDrawing.Text.Internal.Text.Segmentation
{
    /// <summary>The scripts that have a word list to break lines with (UAX #14 class SA, Complex_Context).</summary>
    internal enum ComplexScript : byte
    {
        None = 0,
        Thai,
        Lao,
        Khmer,
        Burmese,
    }

    /// <summary>
    /// The words of one script: a sorted list read from an embedded resource the first time text of the script needs it (see
    /// <c>assets/unicode/generate_dictionary_breaking.py</c> for the source, the licences and the format). The resource is raw
    /// DEFLATE, which every host can read: WebAssembly has no Brotli decoder.
    /// </summary>
    /// <remarks>
    /// The list is held as one character pool with the start of each word, in the sorted order it was written in. Finding every word
    /// that starts a piece of text walks the list one character at a time, narrowing the range of words that share the characters
    /// read so far, so a lookup costs a few binary searches however large the list is. There is one instance per script for the life
    /// of the process, four at most, and readers share it: it is never changed after loading.
    /// </remarks>
    internal sealed class WordDictionary
    {
        private const int MaxWords = 1 << 20;
        private const int MaxWordLength = 255;

        private static readonly Lazy<WordDictionary?> Thai = new(() => Load("thai"));
        private static readonly Lazy<WordDictionary?> Lao = new(() => Load("lao"));
        private static readonly Lazy<WordDictionary?> Khmer = new(() => Load("khmer"));
        private static readonly Lazy<WordDictionary?> Burmese = new(() => Load("burmese"));

        private readonly char[] _characters;
        private readonly int[] _starts;

        private WordDictionary(char[] characters, int[] starts, int longest)
        {
            _characters = characters;
            _starts = starts;
            Longest = longest;
        }

        /// <summary>The number of words.</summary>
        internal int Count => _starts.Length - 1;

        /// <summary>The length in characters of the longest word.</summary>
        internal int Longest { get; }

        /// <summary>The word list of a script, or <see langword="null"/> when it cannot be read.</summary>
        internal static WordDictionary? For(ComplexScript script) => script switch
        {
            ComplexScript.Thai => Thai.Value,
            ComplexScript.Lao => Lao.Value,
            ComplexScript.Khmer => Khmer.Value,
            ComplexScript.Burmese => Burmese.Value,
            _ => null,
        };

        /// <summary>The word at an index of the sorted list.</summary>
        internal ReadOnlySpan<char> WordAt(int index) => _characters.AsSpan(_starts[index], _starts[index + 1] - _starts[index]);

        /// <summary>
        /// Writes into <paramref name="lengths"/>, shortest first, the length of every word that is a prefix of
        /// <paramref name="text"/>, and returns how many there are. <paramref name="lengths"/> needs room for <see cref="Longest"/>.
        /// </summary>
        internal int FindPrefixes(ReadOnlySpan<int> text, Span<int> lengths)
        {
            int low = 0;
            int high = Count;
            int found = 0;

            for (int depth = 0; ; depth++)
            {
                // Every word in [low, high) starts with text[..depth]; the one of exactly that length, there is at most one, is first.
                if (depth > 0 && _starts[low + 1] - _starts[low] == depth)
                {
                    lengths[found++] = depth;
                    low++;
                }

                if (low >= high || depth >= text.Length || text[depth] > char.MaxValue)
                {
                    break;
                }

                char next = (char)text[depth];
                low = Bound(low, high, depth, next, upper: false);
                high = Bound(low, high, depth, next, upper: true);
                if (low >= high)
                {
                    break;
                }
            }

            return found;
        }

        /// <summary>
        /// The first index of [low, high) whose word has, at <paramref name="depth"/>, a character above <paramref name="value"/> (an
        /// upper bound) or not below it (a lower bound). Every word of the range is longer than <paramref name="depth"/>.
        /// </summary>
        private int Bound(int low, int high, int depth, char value, bool upper)
        {
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                char at = _characters[_starts[middle] + depth];
                if (upper ? at <= value : at < value)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private static WordDictionary? Load(string script)
        {
            try
            {
                var assembly = typeof(WordDictionary).Assembly;
                string? name = null;
                foreach (var candidate in assembly.GetManifestResourceNames())
                {
                    if (candidate.EndsWith("." + script + ".dict", StringComparison.Ordinal))
                    {
                        name = candidate;
                        break;
                    }
                }

                if (name is null)
                {
                    return null;
                }

                using var resource = assembly.GetManifestResourceStream(name);
                if (resource is null)
                {
                    return null;
                }

                using var inflated = new DeflateStream(resource, CompressionMode.Decompress);
                using var buffer = new MemoryStream();
                inflated.CopyTo(buffer);
                return Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException or OutOfMemoryException)
            {
                // A list that cannot be read leaves the script to rule LB1: no break inside a run, which is what the text got before.
                return null;
            }
        }

        /// <summary>Reads the payload of a word list: see the generator for its layout.</summary>
        internal static WordDictionary? Parse(ReadOnlySpan<byte> data)
        {
            const int HeaderSize = 10;
            if (data.Length < HeaderSize || data[0] != (byte)'P' || data[1] != (byte)'D' || data[2] != (byte)'W' || data[3] != (byte)'1')
            {
                return null;
            }

            int baseCharacter = data[4] | (data[5] << 8);
            uint declared = (uint)(data[6] | (data[7] << 8) | (data[8] << 16) | (data[9] << 24));
            if (declared == 0 || declared > MaxWords || data.Length < HeaderSize + 2L * declared)
            {
                return null;
            }

            int count = (int)declared;
            var shared = data.Slice(HeaderSize, count);
            var suffixes = data.Slice(HeaderSize + count);

            // First pass: how long each word is, and so where it starts in the pool.
            var starts = new int[count + 1];
            long total = 0;
            int longest = 0;
            int previousLength = 0;
            int read = 0;
            for (int i = 0; i < count; i++)
            {
                int end = suffixes[read..].IndexOf((byte)0);
                if (end < 0 || shared[i] > previousLength)
                {
                    return null;
                }

                read += end + 1;
                previousLength = shared[i] + end;
                if (previousLength == 0 || previousLength > MaxWordLength)
                {
                    return null;
                }

                starts[i] = (int)total;
                total += previousLength;
                longest = Math.Max(longest, previousLength);
                if (total > int.MaxValue)
                {
                    return null;
                }
            }

            if (read != suffixes.Length)
            {
                return null;
            }

            // Second pass: the characters, each word's shared start copied from the word before it.
            starts[count] = (int)total;
            var characters = new char[total];
            read = 0;
            for (int i = 0; i < count; i++)
            {
                int at = starts[i];
                if (shared[i] > 0)
                {
                    Array.Copy(characters, starts[i - 1], characters, at, shared[i]);
                    at += shared[i];
                }

                for (int value = suffixes[read++]; value != 0; value = suffixes[read++])
                {
                    characters[at++] = (char)(baseCharacter + value - 1);
                }
            }

            return new WordDictionary(characters, starts, longest);
        }
    }
}
