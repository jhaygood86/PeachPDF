using System;
using System.IO;
using PeachDrawing.Text.Brotli.Internal.Encoder;
using PeachDrawing.Text.Compression;

namespace PeachDrawing.Text.Brotli
{
    /// <summary>
    /// A pure-managed Brotli encoder for hosts where <see cref="System.IO.Compression.BrotliStream"/> throws
    /// <see cref="PlatformNotSupportedException"/> - WebAssembly in a browser, at the time of writing. Register it once, before
    /// compressing, with <see cref="Register"/>.
    /// </summary>
    /// <remarks>
    /// Written for this repository (the decoder in this assembly is a port of Google's; the encoder is not). Every stream it
    /// produces is ordinary RFC 7932 Brotli that any decoder reads, but it does not use the static dictionary, context modeling
    /// or block splitting, so the output is somewhat larger than the reference encoder's at the same quality. The quality
    /// (0-11) sets how hard the match search works (and enables lazy matching from 5); the Brotli window grows with the input,
    /// up to 4 MB. Data that does not shrink is stored, so the output is never much larger than the input.
    /// </remarks>
    public static class ManagedBrotliCompressor
    {
        /// <summary>Compresses <paramref name="data"/> as one complete Brotli stream.</summary>
        /// <param name="data">the bytes to compress</param>
        /// <param name="quality">0 (fastest) to 11 (most thorough); values outside the range are clamped</param>
        public static byte[] Compress(ReadOnlySpan<byte> data, int quality = 6) => ManagedEncoder.Compress(data, quality);

        /// <summary>
        /// Returns a writable stream whose contents are compressed into <paramref name="destination"/> when it is disposed. The
        /// destination is left open - the shape <see cref="BrotliCompression.SetCompressor"/> asks for.
        /// </summary>
        public static Stream Compress(Stream destination, int quality = 6)
        {
            ArgumentNullException.ThrowIfNull(destination);
            return new CompressingStream(destination, quality);
        }

        /// <summary>
        /// Registers this encoder with <see cref="BrotliCompression.SetCompressor"/>, so PeachDrawing.Text (and PeachPDF's
        /// <c>/BrotliDecode</c> streams) use it in place of the .NET runtime's own Brotli support.
        /// </summary>
        public static void Register() => BrotliCompression.SetCompressor(Compress);

        /// <summary>Reverts <see cref="Register"/>, going back to the default (BCL-only) behavior.</summary>
        public static void Unregister() => BrotliCompression.SetCompressor(null);

        private sealed class CompressingStream(Stream destination, int quality) : Stream
        {
            private readonly MemoryStream _buffer = new();
            private bool _completed;

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => !_completed;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count) => _buffer.Write(buffer, offset, count);

            public override void Write(ReadOnlySpan<byte> buffer) => _buffer.Write(buffer);

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_completed)
                {
                    _completed = true;
                    destination.Write(ManagedEncoder.Compress(_buffer.GetBuffer().AsSpan(0, (int)_buffer.Length), quality));
                    _buffer.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
