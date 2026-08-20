using System.Collections.Generic;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// A shared big-endian byte-writing helper for hand-crafting synthetic SFNT/OpenType table bytes in
    /// tests - see <c>GsubTableSyntheticTests</c>'s own remarks for why this approach (append bytes past
    /// the end of a real, already-valid font file) is used instead of authoring a whole new font file.
    /// Every synthetic-table test file should use this rather than a private per-file copy.
    /// </summary>
    internal sealed class SfntByteBuilder
    {
        private readonly List<byte> _bytes = [];

        public int Position => _bytes.Count;

        public void Byte(byte v) => _bytes.Add(v);

        public void U16(int v)
        {
            _bytes.Add((byte)(v >> 8));
            _bytes.Add((byte)v);
        }

        /// <summary>Writes a signed 16-bit value's two's-complement bit pattern via <see cref="U16"/>.</summary>
        public void S16(int v) => U16((ushort)v);

        public void U32(uint v)
        {
            _bytes.Add((byte)(v >> 24));
            _bytes.Add((byte)(v >> 16));
            _bytes.Add((byte)(v >> 8));
            _bytes.Add((byte)v);
        }

        public void Tag(string fourChars)
        {
            foreach (char c in fourChars)
                _bytes.Add((byte)c);
        }

        /// <summary>Writes a placeholder u16 and returns its byte index, for <see cref="PatchU16"/>.</summary>
        public int PlaceholderU16()
        {
            int at = _bytes.Count;
            U16(0);
            return at;
        }

        public void PatchU16(int at, int value)
        {
            _bytes[at] = (byte)(value >> 8);
            _bytes[at + 1] = (byte)value;
        }

        public void PatchU32(int at, uint value)
        {
            _bytes[at] = (byte)(value >> 24);
            _bytes[at + 1] = (byte)(value >> 16);
            _bytes[at + 2] = (byte)(value >> 8);
            _bytes[at + 3] = (byte)value;
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}
