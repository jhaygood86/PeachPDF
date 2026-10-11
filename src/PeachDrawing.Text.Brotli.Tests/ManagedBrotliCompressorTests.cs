using System.IO.Compression;
using System.Reflection;
using System.Text;
using PeachDrawing.Text.Brotli;
using PeachDrawing.Text.Compression;

namespace PeachDrawing.Text.Brotli.Tests
{
    /// <summary>
    /// The managed encoder's output is checked by decoding it twice - with the BCL's <see cref="BrotliStream"/>, an independent
    /// implementation of the format, and with this assembly's own managed decoder - and comparing with the input. A stream
    /// only one of them accepts would not pass.
    /// </summary>
    public class ManagedBrotliCompressorTests
    {
        private static byte[] DecodeWithBcl(byte[] compressed)
        {
            using var input = new MemoryStream(compressed);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] DecodeWithManaged(byte[] compressed)
        {
            using var decoder = ManagedBrotliDecompressor.Decompress(new MemoryStream(compressed));
            using var output = new MemoryStream();
            decoder.CopyTo(output);
            return output.ToArray();
        }

        private static void AssertRoundTrips(byte[] data, int quality)
        {
            var compressed = ManagedBrotliCompressor.Compress(data, quality);

            Assert.Equal(data, DecodeWithBcl(compressed));
            Assert.Equal(data, DecodeWithManaged(compressed));
        }

        private static byte[] Text(int length)
        {
            var words = new[] { "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "PeachPDF", "brotli", "stream", "glyph" };
            var random = new Random(7);
            var builder = new StringBuilder();
            while (builder.Length < length)
            {
                builder.Append(words[random.Next(words.Length)]).Append(random.Next(10) == 0 ? ".\n" : " ");
            }

            return Encoding.ASCII.GetBytes(builder.ToString(0, length));
        }

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        public static IEnumerable<object[]> Qualities() => Enumerable.Range(0, 12).Select(q => new object[] { q });

        [Fact]
        public void EmptyInput_IsAValidStream()
        {
            var compressed = ManagedBrotliCompressor.Compress([], 6);

            Assert.Empty(DecodeWithBcl(compressed));
            Assert.Empty(DecodeWithManaged(compressed));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(17)]
        [InlineData(255)]
        public void TinyInputs_RoundTrip(int length)
        {
            AssertRoundTrips(Random(length, length), 6);
            AssertRoundTrips(Enumerable.Repeat((byte)'a', length).ToArray(), 6);
        }

        [Theory]
        [MemberData(nameof(Qualities))]
        public void Text_RoundTripsAtEveryQuality_AndShrinks(int quality)
        {
            var data = Text(200_000);
            var compressed = ManagedBrotliCompressor.Compress(data, quality);

            Assert.Equal(data, DecodeWithBcl(compressed));
            Assert.Equal(data, DecodeWithManaged(compressed));
            Assert.True(compressed.Length < data.Length / 2, $"quality {quality}: {compressed.Length} of {data.Length}");
        }

        [Fact]
        public void HigherQualityCompressesAtLeastAsWell_OnRepetitiveText()
        {
            var data = Text(300_000);

            Assert.True(ManagedBrotliCompressor.Compress(data, 9).Length <= ManagedBrotliCompressor.Compress(data, 0).Length);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        [InlineData(11)]
        public void RunsOfOneByte_RoundTrip(int quality)
        {
            AssertRoundTrips(new byte[1_000_000], quality);
            AssertRoundTrips(Enumerable.Repeat((byte)0xFF, 70_000).ToArray(), quality);
        }

        [Fact]
        public void IncompressibleData_IsStored_WithBoundedExpansion()
        {
            var data = Random(300_000, 99);
            var compressed = ManagedBrotliCompressor.Compress(data, 6);

            Assert.Equal(data, DecodeWithBcl(compressed));
            Assert.Equal(data, DecodeWithManaged(compressed));
            Assert.True(compressed.Length < data.Length + 64);
        }

        [Fact]
        public void InputLargerThanOneMetaBlock_RoundTrips()
        {
            // 2.5 MB: three meta-blocks, with matches reaching back across the boundaries.
            var data = Text(2_500_000);

            AssertRoundTrips(data, 5);
        }

        [Fact]
        public void MixedCompressibleAndRandomBlocks_RoundTrip()
        {
            // One compressible meta-block, then a stored one, then a compressible one again: the stored block must leave the
            // distance state exactly as the compressed one before it did.
            var data = Text(1 << 20).Concat(Random(1 << 20, 3)).Concat(Text(1 << 20)).ToArray();

            AssertRoundTrips(data, 6);
        }

        [Fact]
        public void RepeatedDistances_UseTheLastDistanceCode()
        {
            var unit = Random(1000, 5);
            var data = Enumerable.Range(0, 200).SelectMany(_ => unit).ToArray();

            AssertRoundTrips(data, 6);
            Assert.True(ManagedBrotliCompressor.Compress(data, 6).Length < 3000);
        }

        [Fact]
        public void EveryByteValue_AndSkewedAlphabets_RoundTrip()
        {
            var random = new Random(11);
            var skewed = Enumerable.Range(0, 100_000).Select(_ => (byte)(random.Next(100) < 90 ? random.Next(3) : random.Next(256))).ToArray();
            var allBytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

            AssertRoundTrips(skewed, 6);
            AssertRoundTrips(allBytes, 6);
            AssertRoundTrips(allBytes.Concat(allBytes).Concat(allBytes).ToArray(), 6);
        }

        [Fact]
        public void TwoToFourDistinctBytes_UseSimplePrefixCodes()
        {
            foreach (var distinct in new[] { 2, 3, 4, 5 })
            {
                var random = new Random(distinct);
                var data = Enumerable.Range(0, 20_000).Select(_ => (byte)(random.Next(distinct) * 40)).ToArray();

                AssertRoundTrips(data, 6);
            }
        }

        [Fact]
        public void ARealFont_RoundTrips()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Inter-Medium.woff2");
            var data = File.ReadAllBytes(path);

            AssertRoundTrips(data, 6);
        }

        [Fact]
        public void EveryShippedResource_RoundTrips()
        {
            var assembly = Assembly.Load("PeachDrawing.Text.Data");
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".br", StringComparison.Ordinal)))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                var original = DecodeWithBcl(ReadAll(stream));

                AssertRoundTrips(original, 6);
            }
        }

        [Fact]
        public void QualityOutsideTheRange_IsClamped()
        {
            var data = Text(5000);

            Assert.Equal(data, DecodeWithBcl(ManagedBrotliCompressor.Compress(data, -5)));
            Assert.Equal(data, DecodeWithBcl(ManagedBrotliCompressor.Compress(data, 99)));
        }

        [Fact]
        public void StreamOverload_CompressesOnDispose_AndLeavesTheDestinationOpen()
        {
            var data = Text(10_000);
            var destination = new MemoryStream();

            using (var writer = ManagedBrotliCompressor.Compress(destination, 6))
            {
                writer.Write(data, 0, 4000);
                writer.Write(data.AsSpan(4000));
                Assert.True(writer.CanWrite);
                writer.Flush();
            }

            Assert.True(destination.CanWrite);
            Assert.Equal(data, DecodeWithBcl(destination.ToArray()));
        }

        [Fact]
        public void StreamOverload_RejectsReadsAndSeeks()
        {
            using var writer = ManagedBrotliCompressor.Compress(new MemoryStream(), 6);

            Assert.False(writer.CanRead);
            Assert.False(writer.CanSeek);
            Assert.Throws<NotSupportedException>(() => writer.Length);
            Assert.Throws<NotSupportedException>(() => writer.Position);
            Assert.Throws<NotSupportedException>(() => writer.Position = 0);
            Assert.Throws<NotSupportedException>(() => writer.Read(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => writer.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => writer.SetLength(0));
            Assert.Throws<ArgumentNullException>(() => ManagedBrotliCompressor.Compress((Stream)null!, 6));
        }

        [Fact]
        public void RegisteredCompressor_IsUsedByTheSeam()
        {
            var data = Text(20_000);
            try
            {
                ManagedBrotliCompressor.Register();

                Assert.True(BrotliCompression.IsAvailable);
                Assert.True(BrotliCompression.TryCompress(data, 6, out var compressed));
                Assert.Equal(data, DecodeWithBcl(compressed));
            }
            finally
            {
                ManagedBrotliCompressor.Unregister();
            }
        }

        private static byte[] ReadAll(Stream stream)
        {
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }
    }
}
