using System.IO.Compression;
using PeachDrawing.Text.Brotli.Internal;

namespace PeachDrawing.Text.Brotli.Tests
{
    /// <summary>
    /// Exercises the vendored <see cref="BrotliInputStream"/> directly (this test project has
    /// <c>InternalsVisibleTo</c> from <c>PeachDrawing.Text.Brotli</c>) - its full <see cref="System.IO.Stream"/>
    /// API surface, every constructor overload, and the parts of <c>Decode.cs</c>/<c>BitReader.cs</c> that only run
    /// for byte-by-byte reads, a custom dictionary, or an I/O failure from the underlying source - none of which
    /// <see cref="ManagedBrotliDecompressor"/>'s own single-constructor usage reaches on its own.
    /// </summary>
    public class BrotliInputStreamTests
    {
        private static byte[] Compress(byte[] data) =>
            CompressWithLevel(data, CompressionLevel.Fastest);

        private static byte[] CompressWithLevel(byte[] data, CompressionLevel level)
        {
            using var ms = new MemoryStream();
            using (var brotli = new BrotliStream(ms, level, leaveOpen: true))
            {
                brotli.Write(data, 0, data.Length);
            }
            return ms.ToArray();
        }

        [Fact]
        public void ByteByByteReadingMatchesBulkReading()
        {
            var payload = System.Text.Encoding.UTF8.GetBytes("Read one byte at a time, and every byte should match.");
            var compressed = Compress(payload);

            using var stream = new BrotliInputStream(new MemoryStream(compressed));
            using var output = new MemoryStream();
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                output.WriteByte((byte)b);
            }

            Assert.Equal(payload, output.ToArray());
            // A second call after end-of-stream must keep reporting end-of-stream, not throw or hang.
            Assert.Equal(-1, stream.ReadByte());
        }

        [Fact]
        public void ReadNearEndOfStreamAfterReadByteDoesNotDiscardAlreadyBufferedBytes()
        {
            // Regression test: Read(byte[], int, int) has an internal read-ahead buffer that ReadByte() draws from
            // one byte at a time. If a caller then calls Read() directly while that buffer still holds a few
            // trailing bytes and decompression has nothing further to produce, an earlier version of this method
            // returned 0 (claiming end-of-stream) even though it had already copied those trailing bytes into the
            // caller's buffer - silently discarding them from the caller's point of view, since a Stream.Read
            // contract of "0" means "nothing was written this call".
            var payload = "short payload"u8.ToArray();
            var compressed = Compress(payload);

            using var stream = new BrotliInputStream(new MemoryStream(compressed));
            var output = new List<byte>();

            // Draw every byte but the last few through ReadByte(), leaving a handful in the internal buffer.
            for (int i = 0; i < payload.Length - 3; i++)
            {
                int b = stream.ReadByte();
                Assert.NotEqual(-1, b);
                output.Add((byte)b);
            }

            // Now read the rest through Read() directly - this must return the 3 buffered bytes, not 0.
            var tail = new byte[16];
            int read = stream.Read(tail, 0, tail.Length);
            Assert.Equal(3, read);
            output.AddRange(tail[..read]);

            Assert.Equal(payload, output.ToArray());
            Assert.Equal(0, stream.Read(tail, 0, tail.Length));
        }

        [Fact]
        public void CustomByteReadBufferSizeConstructorDecodesCorrectly()
        {
            var payload = System.Text.Encoding.UTF8.GetBytes("A tiny internal buffer still has to decode this correctly.");
            var compressed = Compress(payload);

            using var stream = new BrotliInputStream(new MemoryStream(compressed), byteReadBufferSize: 4);
            using var output = new MemoryStream();
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                output.WriteByte((byte)b);
            }

            Assert.Equal(payload, output.ToArray());
        }

        [Fact]
        public void BadByteReadBufferSizeThrowsArgumentException()
        {
            var compressed = Compress([1, 2, 3]);
            Assert.Throws<ArgumentException>(() => new BrotliInputStream(new MemoryStream(compressed), byteReadBufferSize: 0));
        }

        [Fact]
        public void NullSourceThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new BrotliInputStream(null!, byteReadBufferSize: 16));
        }

        [Fact]
        public void CustomDictionaryConstructorDecodesCorrectly()
        {
            // The custom dictionary is prepended ahead of the ring buffer's real content (Decode.cs's
            // MaybeReallocateRingBuffer/WriteRingBuffer) - it doesn't need to actually be referenced by a backward
            // distance for this constructor overload and SetCustomDictionary to run; only that it's set.
            var payload = System.Text.Encoding.UTF8.GetBytes("Content that follows a custom dictionary prefix.");
            var compressed = Compress(payload);
            var customDictionary = System.Text.Encoding.UTF8.GetBytes("unused dictionary prefix bytes");

            using var stream = new BrotliInputStream(new MemoryStream(compressed), BrotliInputStream.DefaultInternalBufferSize, customDictionary);
            using var output = new MemoryStream();
            stream.CopyTo(output);

            Assert.Equal(payload, output.ToArray());
        }

        [Fact]
        public void CloseClosesTheUnderlyingSource()
        {
            var payload = System.Text.Encoding.UTF8.GetBytes("closing should close the source");
            var compressed = Compress(payload);
            var tracking = new CloseTrackingStream(new MemoryStream(compressed));

            var stream = new BrotliInputStream(tracking);
            using (var output = new MemoryStream())
            {
                stream.CopyTo(output);
            }
            stream.Close();

            Assert.True(tracking.WasClosed);
        }

        [Fact]
        public void IOExceptionFromTheSourceStreamDuringInitializationIsWrapped()
        {
            var ex = Assert.Throws<IOException>(() => new BrotliInputStream(new ThrowingStream()));
            Assert.IsType<BrotliRuntimeException>(ex.InnerException);
        }

        [Fact]
        public void IOExceptionFromTheSourceStreamDuringReadIsWrapped()
        {
            // Large and close to incompressible, so the compressed stream itself is bigger than the underlying
            // reader's ~4 KiB read chunks (BitReader.ByteReadSize) - otherwise everything the decoder ever needs is
            // pulled from the source in the first read or two (during construction), and FailAfterNReadsStream's
            // later reads are never reached at all.
            var random = new Random(20260927);
            var payload = new byte[100_000];
            random.NextBytes(payload);
            var compressed = Compress(payload);
            Assert.True(compressed.Length > 16_384, $"Expected a larger compressed payload, got {compressed.Length} bytes.");
            var source = new FailAfterNReadsStream(compressed, failAfter: 2);

            using var stream = new BrotliInputStream(source);
            var buffer = new byte[64];
            Assert.Throws<IOException>(() =>
            {
                int read;
                do
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }
                while (read > 0);
            });
        }

        [Fact]
        public void UnsupportedStreamMembersThrowNotSupportedException()
        {
            var compressed = Compress([1, 2, 3]);
            using var stream = new BrotliInputStream(new MemoryStream(compressed));

            Assert.True(stream.CanRead);
            Assert.False(stream.CanSeek);
            Assert.False(stream.CanWrite);
            Assert.Throws<NotSupportedException>(() => stream.Length);
            Assert.Throws<NotSupportedException>(() => stream.Position);
            Assert.Throws<NotSupportedException>(() => stream.Position = 0);
            Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
            Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
            Assert.Throws<NotSupportedException>(() => stream.BeginWrite([1], 0, 1, null, null));
            stream.Flush(); // no-op; must not throw
        }

        [Fact]
        public void SmallWindowStreamWrapsTheRingBufferManyTimes()
        {
            // Fixtures/SmallWindow.br: brotli.compress(("The quick brown fox jumps over the lazy dog. " * 4000),
            // quality=9, lgwin=10) - 180,000 decompressed bytes through a 1 KiB window, forcing the ring buffer
            // (sized close to the window) to wrap around (Decode.cs's `state.pos++ == ringBufferMask` branches)
            // roughly 175 times, which no other test payload's window/size combination happens to trigger.
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SmallWindow.br");
            var compressed = File.ReadAllBytes(path);
            var expected = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 4000));

            using var bcl = new BrotliStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var bclOutput = new MemoryStream();
            bcl.CopyTo(bclOutput);
            Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(bclOutput.ToArray()));

            using var managed = new BrotliInputStream(new MemoryStream(compressed));
            using var managedOutput = new MemoryStream();
            managed.CopyTo(managedOutput);
            Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(managedOutput.ToArray()));
        }

        /// <summary>A <see cref="MemoryStream"/> wrapper that records whether <see cref="Close"/> was called on it.</summary>
        private sealed class CloseTrackingStream(Stream inner) : Stream
        {
            public bool WasClosed { get; private set; }

            public override void Close()
            {
                WasClosed = true;
                inner.Close();
                base.Close();
            }

            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanWrite => inner.CanWrite;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long value) => inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        }

        /// <summary>A stream whose every <see cref="Read"/> throws <see cref="IOException"/>, to exercise initialization failure.</summary>
        private sealed class ThrowingStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new IOException("synthetic failure");
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>A stream over a fixed byte array whose <see cref="Read"/> throws <see cref="IOException"/> after a few successful reads.</summary>
        private sealed class FailAfterNReadsStream(byte[] data, int failAfter) : Stream
        {
            private readonly MemoryStream _inner = new(data);
            private int _reads;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_reads++ >= failAfter)
                {
                    throw new IOException("synthetic failure mid-stream");
                }
                return _inner.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
