using System.IO.Compression;
using System.Reflection;
using PeachDrawing.Text.Brotli;

namespace PeachDrawing.Text.Brotli.Tests
{
    /// <summary>
    /// Verifies the ported managed decoder byte-for-byte against <see cref="BrotliStream"/> (the BCL's own decoder, which works
    /// fine on this test host) over every ".br" resource <c>PeachDrawing.Text.Data</c> actually embeds - the real corpus this
    /// decoder has to handle on WebAssembly, not a hand-picked sample. Read by reflection over the assembly's manifest resource
    /// names/streams, which need no accessibility grant (resource names are string keys at the assembly level, independent of
    /// the C# accessibility of any type in that assembly).
    /// </summary>
    public class ManagedBrotliRoundTripTests
    {
        public static IEnumerable<object[]> DataResourceNames()
        {
            var assembly = Assembly.Load("PeachDrawing.Text.Data");
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(".br", StringComparison.Ordinal))
                {
                    yield return [name];
                }
            }
        }

        [Theory]
        [MemberData(nameof(DataResourceNames))]
        public void DecodesEveryShippedResourceIdenticallyToTheBcl(string resourceName)
        {
            var assembly = Assembly.Load("PeachDrawing.Text.Data");
            using var compressed = assembly.GetManifestResourceStream(resourceName);
            Assert.NotNull(compressed);

            var compressedBytes = ReadAll(compressed!);

            byte[] expected = DecodeWithBcl(compressedBytes);
            byte[] actual = DecodeWithManaged(compressedBytes);

            Assert.NotEmpty(expected);
            Assert.Equal(expected, actual);
        }

        public static IEnumerable<object[]> SyntheticPayloads()
        {
            var random = new Random(20260927);

            yield return [Array.Empty<byte>()];
            yield return ["a"u8.ToArray()];
            yield return [System.Text.Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog.")];

            // Highly repetitive - exercises long back-references and block splitting.
            yield return [System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abcabcabcabc ", 5000)))];

            // Real, varied natural-language-shaped text (what the word-transform dictionary targets).
            yield return
            [
                System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
                    "The Free Software Foundation, Inc. and the Free Standards Group are not affiliated with this project. ",
                    2000)))
            ];

            // Incompressible random bytes - forces literal-heavy encoding rather than back-references/dictionary hits.
            var incompressible = new byte[262_144];
            random.NextBytes(incompressible);
            yield return [incompressible];

            // Larger than the decoder's internal 1024-int ring buffer capacity, to exercise refills across chunks.
            var large = new byte[2_000_000];
            random.NextBytes(large);
            for (int i = 0; i < large.Length; i++)
            {
                // Bias toward a small alphabet so the compressor actually finds structure to exploit.
                large[i] = (byte)(large[i] % 4 == 0 ? large[i] : large[i] % 40);
            }
            yield return [large];
        }

        [Theory]
        [MemberData(nameof(SyntheticPayloads))]
        public void DecodesSyntheticPayloadsAtHighestQuality(byte[] payload)
        {
            // CompressionLevel.SmallestSize is the closest the BCL exposes to the quality-11 setting this repository's own
            // generator scripts (and fonttools' WOFF2 writer) actually use - see PORTING-NOTES.md for why that matters (it is
            // not a smaller subset of the format; it uses the format's full feature set).
            var compressed = CompressWithBcl(payload, CompressionLevel.SmallestSize);

            byte[] expected = DecodeWithBcl(compressed);
            byte[] actual = DecodeWithManaged(compressed);

            Assert.Equal(payload, expected);
            Assert.Equal(payload, actual);
        }

        private static byte[] CompressWithBcl(byte[] data, CompressionLevel level)
        {
            using var ms = new MemoryStream();
            using (var brotli = new BrotliStream(ms, level, leaveOpen: true))
            {
                brotli.Write(data, 0, data.Length);
            }
            return ms.ToArray();
        }

        private static byte[] DecodeWithBcl(byte[] compressed)
        {
            using var source = new MemoryStream(compressed);
            using var brotli = new BrotliStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] DecodeWithManaged(byte[] compressed)
        {
            using var source = new MemoryStream(compressed);
            using var decoded = ManagedBrotliDecompressor.Decompress(source);
            using var output = new MemoryStream();
            decoded.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] ReadAll(Stream stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
