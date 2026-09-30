using System.IO.Compression;
using PeachDrawing.Text.Brotli;

namespace PeachDrawing.Text.Brotli.Tests
{
    /// <summary>
    /// Malformed, truncated and randomly-mutated Brotli input must fail cleanly - throw a well-defined exception within a
    /// bounded time - and never hang or read/write outside a rented buffer (trivially true in managed code: an out-of-range
    /// array access throws <see cref="IndexOutOfRangeException"/> rather than corrupting memory, but a decoder can still spin
    /// forever on crafted input, which is the failure mode these tests actually probe for).
    /// </summary>
    public class ManagedBrotliHostileInputTests
    {
        // Generous relative to how fast these tiny inputs actually decode (milliseconds); this bounds a genuine hang, not
        // ordinary slowness.
        private static readonly TimeSpan PerCaseTimeout = TimeSpan.FromSeconds(5);

        public static IEnumerable<object[]> RandomByteSequences()
        {
            var random = new Random(20260927);
            foreach (var length in new[] { 0, 1, 2, 5, 16, 64, 256, 4096 })
            {
                // A length of 0 has only one possible value - repeating it would just be a duplicate xunit theory case.
                int iterations = length == 0 ? 1 : 10;
                for (int i = 0; i < iterations; i++)
                {
                    var bytes = new byte[length];
                    random.NextBytes(bytes);
                    yield return [bytes];
                }
            }
        }

        [Theory]
        [MemberData(nameof(RandomByteSequences))]
        public void RandomBytesFailCleanlyOrDecodeWithoutHanging(byte[] bytes)
        {
            RunWithTimeout(bytes);
        }

        public static IEnumerable<object[]> TruncatedValidStreams()
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(
                string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 500)));

            using var ms = new MemoryStream();
            using (var brotli = new BrotliStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                brotli.Write(payload, 0, payload.Length);
            }
            var compressed = ms.ToArray();

            // Every prefix length from 0 to just short of the full stream: a decoder reading past what it was given must
            // report a clean error (a Stream.Read returning 0 forever would be an infinite loop for a naive caller upstream;
            // this decoder's contract is that it throws instead - see BitReader.ReadMoreInput's "No more input").
            for (int cut = 0; cut < compressed.Length; cut += Math.Max(1, compressed.Length / 40))
            {
                yield return [compressed[..cut]];
            }
        }

        [Theory]
        [MemberData(nameof(TruncatedValidStreams))]
        public void TruncatedValidStreamsFailCleanlyOrDecodeWithoutHanging(byte[] truncated)
        {
            RunWithTimeout(truncated);
        }

        public static IEnumerable<object[]> MutatedValidStreams()
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(
                string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet, consectetur adipiscing elit. ", 500)));

            using var ms = new MemoryStream();
            using (var brotli = new BrotliStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                brotli.Write(payload, 0, payload.Length);
            }
            var compressed = ms.ToArray();

            var random = new Random(20260927);
            for (int i = 0; i < 300; i++)
            {
                var mutant = (byte[])compressed.Clone();
                // Flip one to four random bits - a single-bit flip is the classic "still parses, but now means something
                // structurally different" fuzz case (a corrupted window size, an impossible Huffman code length, a distance
                // pointing outside the window, ...).
                int flips = 1 + random.Next(4);
                for (int f = 0; f < flips; f++)
                {
                    int byteIndex = random.Next(mutant.Length);
                    int bitIndex = random.Next(8);
                    mutant[byteIndex] ^= (byte)(1 << bitIndex);
                }
                yield return [mutant];
            }
        }

        [Theory]
        [MemberData(nameof(MutatedValidStreams))]
        public void BitFlippedStreamsFailCleanlyOrDecodeWithoutHanging(byte[] mutated)
        {
            RunWithTimeout(mutated);
        }

        [Fact]
        public void NullSourceThrowsArgumentException()
        {
            Assert.ThrowsAny<ArgumentException>(() => ManagedBrotliDecompressor.Decompress(null!));
        }

        /// <summary>
        /// Decodes <paramref name="compressed"/> on a background task with a hard wall-clock timeout: a hang is a test
        /// failure, any exception (well-defined or not) is an acceptable clean failure, and successful decoding is fine too
        /// (some mutations/truncations still happen to produce a validly-structured, if garbage, stream).
        /// </summary>
        private static void RunWithTimeout(byte[] compressed)
        {
            var task = Task.Run(() =>
            {
                try
                {
                    using var source = new MemoryStream(compressed);
                    using var decoded = ManagedBrotliDecompressor.Decompress(source);
                    using var output = new MemoryStream();
                    decoded.CopyTo(output);
                }
                catch (Exception)
                {
                    // Any exception is a clean failure for this test's purposes - malformed input is expected to be
                    // rejected, not accepted. What matters is that it terminates instead of hanging.
                }
            });

            var finished = task.Wait(PerCaseTimeout);
            Assert.True(finished, "Decoding hostile input did not complete within the timeout - suspected hang.");
        }
    }
}
