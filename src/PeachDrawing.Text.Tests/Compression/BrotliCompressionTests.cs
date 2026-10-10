using PeachDrawing.Text.Compression;
using System.IO;
using System.IO.Compression;

namespace PeachDrawing.Text.Tests.Compression
{
    /// <summary>The pluggable Brotli encoder seam; serialized because the registered compressor is process-wide.</summary>
    [Collection(BrotliDecompressionCollection.Name)]
    public class BrotliCompressionTests
    {
        private static byte[] Sample => System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("PeachPDF brotli seam. ", 200)));

        private static byte[] Decode(byte[] compressed)
        {
            using var input = new MemoryStream(compressed);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return output.ToArray();
        }

        [Fact]
        public void DefaultEncoder_RoundTripsAndShrinks()
        {
            BrotliCompression.SetCompressor(null);
            Assert.True(BrotliCompression.IsAvailable);
            Assert.True(BrotliCompression.TryCompress(Sample, 5, out var compressed));
            Assert.True(compressed.Length < Sample.Length / 4);
            Assert.Equal(Sample, Decode(compressed));
        }

        [Theory]
        [InlineData(-3)]
        [InlineData(99)]
        public void Quality_IsClamped(int quality)
        {
            BrotliCompression.SetCompressor(null);
            Assert.True(BrotliCompression.TryCompress(Sample, quality, out var compressed));
            Assert.Equal(Sample, Decode(compressed));
        }

        [Fact]
        public void EmptyInput_RoundTrips()
        {
            BrotliCompression.SetCompressor(null);
            Assert.True(BrotliCompression.TryCompress([], 5, out var compressed));
            Assert.Empty(Decode(compressed));
        }

        [Fact]
        public void CustomCompressor_IsUsed_WithTheClampedQuality()
        {
            var seenQuality = -1;
            try
            {
                BrotliCompression.SetCompressor((destination, quality) =>
                {
                    seenQuality = quality;
                    return new BrotliStream(destination, CompressionLevel.Fastest, leaveOpen: true);
                });
                Assert.True(BrotliCompression.IsAvailable);
                Assert.True(BrotliCompression.TryCompress(Sample, 42, out var compressed));
                Assert.Equal(11, seenQuality);
                Assert.Equal(Sample, Decode(compressed));
            }
            finally
            {
                BrotliCompression.SetCompressor(null);
            }
        }

        [Fact]
        public void ACompressorThatThrowsPlatformNotSupported_ReportsFailure()
        {
            try
            {
                BrotliCompression.SetCompressor((_, _) => throw new PlatformNotSupportedException());
                Assert.False(BrotliCompression.TryCompress(Sample, 5, out var compressed));
                Assert.Empty(compressed);
            }
            finally
            {
                BrotliCompression.SetCompressor(null);
            }
        }

        [Fact]
        public void ACompressorThatThrowsIo_ReportsFailure()
        {
            try
            {
                BrotliCompression.SetCompressor((_, _) => throw new IOException("boom"));
                Assert.False(BrotliCompression.TryCompress(Sample, 5, out _));
            }
            finally
            {
                BrotliCompression.SetCompressor(null);
            }
        }
    }
}
