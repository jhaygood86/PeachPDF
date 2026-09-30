using PeachDrawing.Text.Compression;
using PeachDrawing.Text.Internal.Text;
using System.IO;
using System.IO.Compression;

namespace PeachDrawing.Text.Tests.Compression
{
    /// <summary>
    /// The pluggable Brotli decoder seam: <see cref="BrotliDecompression.SetDecompressor"/> and what
    /// <see cref="TextDataResources.OpenBrotli"/> (every Unicode/hyphenation/dictionary resource reader's entry point)
    /// actually does with it. Every test resets the registered decompressor to <see langword="null"/> in a
    /// <c>finally</c>, since it is process-wide static state other tests (and, under xUnit's parallel execution,
    /// concurrently running ones) also read.
    /// </summary>
    public class BrotliDecompressionTests
    {
        [Fact]
        public void ACustomDecompressor_IsActuallyCalled_NotBypassed()
        {
            var called = false;
            try
            {
                BrotliDecompression.SetDecompressor(compressed =>
                {
                    called = true;
                    return new BrotliStream(compressed, CompressionMode.Decompress);
                });

                using var decompressed = TextDataResources.OpenBrotli("BidiBrackets.txt.br");

                Assert.True(called, "the registered decompressor was never invoked");
                Assert.NotNull(decompressed);

                using var reader = new StreamReader(decompressed);
                var text = reader.ReadToEnd();
                Assert.Contains("0028", text); // U+0028 '(' is one of the first entries of BidiBrackets.txt
            }
            finally
            {
                BrotliDecompression.SetDecompressor(null);
            }
        }

        [Fact]
        public void ACustomDecompressor_ThatReturnsWrongData_IsWhatTheReaderActuallyGets()
        {
            // Proves the registered decompressor's own return value is used, not silently ignored in favor of the
            // default: substituting one that returns unrelated (but well-formed) text changes what comes back.
            try
            {
                BrotliDecompression.SetDecompressor(compressed =>
                {
                    compressed.Dispose();
                    return new MemoryStream(System.Text.Encoding.UTF8.GetBytes("substituted content, not the real resource"));
                });

                using var decompressed = TextDataResources.OpenBrotli("BidiBrackets.txt.br");
                using var reader = new StreamReader(decompressed!);
                var text = reader.ReadToEnd();

                Assert.Equal("substituted content, not the real resource", text);
            }
            finally
            {
                BrotliDecompression.SetDecompressor(null);
            }
        }

        [Fact]
        public void SettingItBackToNull_RestoresTheDefaultBclDecoder()
        {
            try
            {
                BrotliDecompression.SetDecompressor(compressed => new BrotliStream(compressed, CompressionMode.Decompress));
                BrotliDecompression.SetDecompressor(null);

                using var decompressed = TextDataResources.OpenBrotli("BidiBrackets.txt.br");
                using var reader = new StreamReader(decompressed!);
                var text = reader.ReadToEnd();

                Assert.Contains("0028", text);
            }
            finally
            {
                BrotliDecompression.SetDecompressor(null);
            }
        }

        [Fact]
        public void AMissingResource_ReturnsNull_WhateverTheDecompressor()
        {
            Assert.Null(TextDataResources.OpenBrotli("ThisResourceDoesNotExist.txt.br"));
        }
    }
}
