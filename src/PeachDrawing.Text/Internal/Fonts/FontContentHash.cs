using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// A collision-resistant identity of the bytes of a font file: the first 128 bits of their SHA-256.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fonts come from documents (an <c>@font-face</c> a page names), and the process-wide font source cache and the
    /// per-generator face names are keyed by this, so it must hold up against a font made to collide with another: a cheap
    /// checksum such as Adler-32 does not (two buffers of one length with equal sums are a few bytes apart), and the font that
    /// lost the race would be served the other's parsed data. SHA-256 truncated to 128 bits makes a collision as hard as finding
    /// a 128-bit birthday collision (2^64 work), which is out of reach.
    /// </para>
    /// <para>
    /// This is unrelated to the checksums OpenType itself mandates (the table checksums and <c>head.checkSumAdjustment</c>),
    /// which are a format detail of the font file and are computed elsewhere.
    /// </para>
    /// </remarks>
    internal readonly struct FontContentHash : IEquatable<FontContentHash>
    {
        /// <summary>The number of bytes of the SHA-256 digest that are kept.</summary>
        private const int Length = 16;

        private readonly ulong _high;
        private readonly ulong _low;

        private FontContentHash(ulong high, ulong low)
        {
            _high = high;
            _low = low;
        }

        /// <summary>Whether this is the default value, which is no font's hash: it stands for "not computed yet".</summary>
        public bool IsEmpty => (_high | _low) == 0;

        /// <summary>Computes the SHA-256 of <paramref name="bytes"/> into <paramref name="digest"/>, or reports that it cannot.</summary>
        internal delegate bool Sha256Function(byte[] bytes, Span<byte> digest);

        /// <summary>The hash of <paramref name="bytes"/>.</summary>
        public static FontContentHash Compute(byte[] bytes) => Compute(bytes, static (data, digest) => SHA256.TryHashData(data, digest, out _));

        /// <summary>
        /// The hash of <paramref name="bytes"/>, taken with <paramref name="platform"/>: the platform's SHA-256, which is hardware
        /// accelerated where the processor has it and is by far the fastest way to walk a multi-megabyte font. A platform without one
        /// (it throws, as a WebAssembly host may) is left to the portable implementation, which computes the same digest.
        /// </summary>
        internal static FontContentHash Compute(byte[] bytes, Sha256Function platform)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            Span<byte> digest = stackalloc byte[32];
            bool hashed;
            try
            {
                hashed = platform(bytes, digest);
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException or CryptographicException)
            {
                hashed = false;
            }

            if (!hashed)
                PortableSha256.Hash(bytes, digest);

            return new FontContentHash(
                BinaryPrimitives.ReadUInt64BigEndian(digest),
                BinaryPrimitives.ReadUInt64BigEndian(digest[8..Length]));
        }
        /// <inheritdoc />
        public bool Equals(FontContentHash other) => _high == other._high && _low == other._low;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is FontContentHash other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(_high, _low);

        /// <summary>The hash as 32 lowercase hexadecimal digits.</summary>
        public override string ToString() => string.Create(2 * Length, this, static (chars, hash) =>
        {
            hash._high.TryFormat(chars, out _, "x16");
            hash._low.TryFormat(chars[16..], out _, "x16");
        });

        public static bool operator ==(FontContentHash left, FontContentHash right) => left.Equals(right);

        public static bool operator !=(FontContentHash left, FontContentHash right) => !left.Equals(right);
    }

    /// <summary>
    /// SHA-256 (FIPS 180-4) in managed code, for a platform whose <see cref="SHA256"/> is not available. It is the same digest the
    /// platform's produces, and a test holds the two to that.
    /// </summary>
    internal static class PortableSha256
    {
        private static readonly uint[] RoundConstants =
        [
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        ];

        /// <summary>Writes the 32-byte SHA-256 digest of <paramref name="data"/> to <paramref name="digest"/>.</summary>
        public static void Hash(ReadOnlySpan<byte> data, Span<byte> digest)
        {
            Span<uint> state =
            [
                0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19
            ];
            Span<uint> schedule = stackalloc uint[64];

            int fullBlocks = data.Length / 64;
            for (int block = 0; block < fullBlocks; block++)
                Compress(state, schedule, data.Slice(block * 64, 64));

            // The rest, a 0x80 byte, zeros, and the length in bits as a 64-bit big-endian number close the message in one or two blocks.
            ReadOnlySpan<byte> rest = data[(fullBlocks * 64)..];
            Span<byte> tail = stackalloc byte[128];
            tail.Clear();
            rest.CopyTo(tail);
            tail[rest.Length] = 0x80;
            int tailLength = rest.Length + 1 + 8 <= 64 ? 64 : 128;
            BinaryPrimitives.WriteUInt64BigEndian(tail[(tailLength - 8)..], (ulong)data.Length * 8);
            for (int offset = 0; offset < tailLength; offset += 64)
                Compress(state, schedule, tail.Slice(offset, 64));

            for (int i = 0; i < 8; i++)
                BinaryPrimitives.WriteUInt32BigEndian(digest[(i * 4)..], state[i]);
        }

        private static void Compress(Span<uint> state, Span<uint> w, ReadOnlySpan<byte> block)
        {
            for (int i = 0; i < 16; i++)
                w[i] = BinaryPrimitives.ReadUInt32BigEndian(block[(i * 4)..]);
            for (int i = 16; i < 64; i++)
            {
                uint s0 = RotateRight(w[i - 15], 7) ^ RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
                uint s1 = RotateRight(w[i - 2], 17) ^ RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }

            uint a = state[0], b = state[1], c = state[2], d = state[3];
            uint e = state[4], f = state[5], g = state[6], h = state[7];
            for (int i = 0; i < 64; i++)
            {
                uint sum1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                uint choose = (e & f) ^ (~e & g);
                uint temp1 = h + sum1 + choose + RoundConstants[i] + w[i];
                uint sum0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                uint majority = (a & b) ^ (a & c) ^ (b & c);
                uint temp2 = sum0 + majority;

                h = g; g = f; f = e; e = d + temp1;
                d = c; c = b; b = a; a = temp1 + temp2;
            }

            state[0] += a; state[1] += b; state[2] += c; state[3] += d;
            state[4] += e; state[5] += f; state[6] += g; state[7] += h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint RotateRight(uint value, int count) => (value >> count) | (value << (32 - count));
    }
}
