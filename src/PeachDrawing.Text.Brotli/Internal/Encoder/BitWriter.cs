using System;

namespace PeachDrawing.Text.Brotli.Internal.Encoder
{
    /// <summary>
    /// Writes bits least-significant first, the order Brotli (RFC 7932, section 1.5) packs everything in: a value is placed in the
    /// next free bits of the current byte, low bit first, spilling into the following byte.
    /// </summary>
    internal sealed class BitWriter
    {
        private byte[] _buffer = new byte[1024];
        private int _length;
        private ulong _accumulator;
        private int _pendingBits;

        /// <summary>A rewind point; see <see cref="Mark"/>.</summary>
        internal readonly record struct Position(int Length, ulong Accumulator, int PendingBits);

        /// <summary>Appends the low <paramref name="count"/> bits (at most 32) of <paramref name="value"/>.</summary>
        public void Write(int count, uint value)
        {
            if (count == 0)
            {
                return;
            }

            _accumulator |= (ulong)(value & (uint)((1UL << count) - 1)) << _pendingBits;
            _pendingBits += count;
            while (_pendingBits >= 8)
            {
                Append((byte)_accumulator);
                _accumulator >>= 8;
                _pendingBits -= 8;
            }
        }

        /// <summary>Pads with zero bits to the next byte boundary.</summary>
        public void AlignToByte()
        {
            if (_pendingBits > 0)
            {
                Append((byte)_accumulator);
                _accumulator = 0;
                _pendingBits = 0;
            }
        }

        /// <summary>Appends whole bytes; the writer must be byte-aligned.</summary>
        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            if (_pendingBits != 0)
            {
                throw new InvalidOperationException("The writer is not byte-aligned.");
            }

            EnsureCapacity(bytes.Length);
            bytes.CopyTo(_buffer.AsSpan(_length));
            _length += bytes.Length;
        }

        /// <summary>Remembers the current position so <see cref="Rewind"/> can discard everything written after it.</summary>
        public Position Mark() => new(_length, _accumulator, _pendingBits);

        /// <summary>Discards everything written since <paramref name="position"/>.</summary>
        public void Rewind(Position position)
        {
            _length = position.Length;
            _accumulator = position.Accumulator;
            _pendingBits = position.PendingBits;
        }

        /// <summary>The number of bits written so far.</summary>
        public long BitCount => (long)_length * 8 + _pendingBits;

        /// <summary>Returns the bytes written; the writer must be byte-aligned.</summary>
        public byte[] ToArray()
        {
            AlignToByte();
            return _buffer.AsSpan(0, _length).ToArray();
        }

        private void Append(byte value)
        {
            EnsureCapacity(1);
            _buffer[_length++] = value;
        }

        private void EnsureCapacity(int extra)
        {
            if (_length + extra > _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + extra));
            }
        }
    }
}
