namespace PeachPDF.Tests.CSS
{
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PeachPDF.CSS;
    using Xunit;

    public class CssStreamLoaderTests
    {
        private const string Text = "div { color: red; }";

        // Encoding.GetBytes() never emits a BOM by itself (only GetPreamble() does, and callers like
        // StreamWriter prepend it explicitly) - these tests build the byte stream the same way a real
        // BOM-prefixed file would arrive, preamble bytes followed by the encoded content.
        private static byte[] WithPreamble(Encoding encoding, string text)
        {
            var preamble = encoding.GetPreamble();
            var content = encoding.GetBytes(text);
            var bytes = new byte[preamble.Length + content.Length];
            preamble.CopyTo(bytes, 0);
            content.CopyTo(bytes, preamble.Length);
            return bytes;
        }

        private static Task<System.ReadOnlyMemory<char>> LoadAsync(Stream stream, Encoding? encoding = null) =>
            CssStreamLoader.LoadAsync(stream, encoding, CancellationToken.None);

        [Fact]
        public async Task LoadAsync_NoBom_DefaultsToUtf8()
        {
            using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(Text));

            var result = await LoadAsync(stream);

            Assert.Equal(Text, result.ToString());
        }

        [Fact]
        public async Task LoadAsync_Utf8Bom_IsStrippedAndDecodedCorrectly()
        {
            using var stream = new MemoryStream(WithPreamble(new UTF8Encoding(true), Text));

            var result = await LoadAsync(stream);

            Assert.Equal(Text, result.ToString());
        }

        [Fact]
        public async Task LoadAsync_Utf16LeBom_IsStrippedAndDecodedCorrectly()
        {
            var encoding = new UnicodeEncoding(false, true);
            using var stream = new MemoryStream(WithPreamble(encoding, Text));

            var result = await LoadAsync(stream);

            Assert.Equal(Text, result.ToString());
        }

        [Fact]
        public async Task LoadAsync_Utf16BeBom_IsStrippedAndDecodedCorrectly()
        {
            var encoding = new UnicodeEncoding(true, true);
            using var stream = new MemoryStream(WithPreamble(encoding, Text));

            var result = await LoadAsync(stream);

            Assert.Equal(Text, result.ToString());
        }

        [Fact]
        public async Task LoadAsync_Utf32LeBom_IsStrippedAndDecodedCorrectly()
        {
            var encoding = new UTF32Encoding(false, true);
            using var stream = new MemoryStream(WithPreamble(encoding, Text));

            var result = await LoadAsync(stream);

            Assert.Equal(Text, result.ToString());
        }

        [Fact]
        public async Task LoadAsync_Utf32BeBom_IsStrippedAndDecodedCorrectly()
        {
            var encoding = new UTF32Encoding(true, true);
            using var stream = new MemoryStream(WithPreamble(encoding, Text));

            var result = await LoadAsync(stream);

            Assert.Equal(Text, result.ToString());
        }

        [Fact]
        public async Task LoadAsync_EmptyStream_ReturnsEmpty()
        {
            using var stream = new MemoryStream();

            var result = await LoadAsync(stream);

            Assert.Equal(0, result.Length);
        }

        [Fact]
        public async Task LoadAsync_ShortStream_UnderFourBytes_DoesNotThrow()
        {
            using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes("ab"));

            var result = await LoadAsync(stream);

            Assert.Equal("ab", result.ToString());
        }

        [Fact]
        public async Task LoadAsync_LargeContent_SpanningMultiplePipeSegments_DecodesCorrectly()
        {
            // Comfortably larger than PipeReader's default single-segment size, to exercise the
            // multi-segment ReadOnlySequence<byte> decode path, not just a single contiguous buffer.
            var large = string.Concat(System.Linq.Enumerable.Repeat("div { color: red; } ", 10000));
            using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(large));

            var result = await LoadAsync(stream);

            Assert.Equal(large, result.ToString());
        }

        [Fact]
        public async Task LoadAsync_MultiByteCharacter_NearBufferBoundary_DecodesCorrectly()
        {
            // A UTF-8 multi-byte character (e.g. an accented letter) padded so its byte sequence is
            // likely to straddle whatever internal buffer boundary the PipeReader/decoder uses -
            // exercises the stateful Decoder correctly carrying a split sequence across reads/segments.
            var padding = new string('a', 8192);
            var content = padding + "café" + padding;
            using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(content));

            var result = await LoadAsync(stream);

            Assert.Equal(content, result.ToString());
        }

        [Fact]
        public async Task LoadAsync_ExplicitEncoding_UsedWhenNoBomPresent()
        {
            var encoding = new UTF8Encoding(false);
            using var stream = new MemoryStream(encoding.GetBytes(Text));

            var result = await LoadAsync(stream, encoding);

            Assert.Equal(Text, result.ToString());
        }
    }
}
