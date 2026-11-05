using System;
using System.Buffers.Binary;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// The AAT tracking table (<c>trak</c>): how much space to add to (or take from) every glyph's advance at a given point size, so that a face
    /// that is meant to be set tighter when large and looser when small (Apple's San Francisco) is. The values are in design units, per glyph.
    /// </summary>
    internal sealed class TrackingTable
    {
        private readonly double[] _sizes;
        private readonly double[] _values;

        private TrackingTable(double[] sizes, double[] values)
        {
            _sizes = sizes;
            _values = values;
        }

        /// <summary>Reads the horizontal tracking of the "normal" track (0), or <see langword="null"/> when the font has no usable one.</summary>
        /// <param name="data">the bytes of the font file</param>
        /// <param name="offset">where the table starts in <paramref name="data"/></param>
        /// <param name="length">the table's length</param>
        internal static TrackingTable? TryRead(byte[] data, int offset, int length)
        {
            try
            {
                var table = data.AsSpan(offset, length);
                if (table.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(table) != 0x00010000)
                    return null;

                var horizontal = BinaryPrimitives.ReadUInt16BigEndian(table[6..]);
                if (horizontal == 0 || horizontal + 8 > table.Length)
                    return null;

                var trackCount = BinaryPrimitives.ReadUInt16BigEndian(table[horizontal..]);
                var sizeCount = BinaryPrimitives.ReadUInt16BigEndian(table[(horizontal + 2)..]);
                var sizeTable = (int)BinaryPrimitives.ReadUInt32BigEndian(table[(horizontal + 4)..]);
                if (trackCount == 0 || sizeCount == 0 || sizeTable + sizeCount * 4 > table.Length)
                    return null;

                // The track whose value is 0 (Fixed) is the normal one; failing that, the one nearest to it.
                var best = -1;
                var bestDistance = double.MaxValue;
                for (var i = 0; i < trackCount; i++)
                {
                    var entry = horizontal + 8 + i * 8;
                    if (entry + 8 > table.Length)
                        return null;

                    var distance = Math.Abs(BinaryPrimitives.ReadInt32BigEndian(table[entry..]) / 65536.0);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = entry;
                    }
                }

                var valuesAt = BinaryPrimitives.ReadUInt16BigEndian(table[(best + 6)..]);
                if (valuesAt + sizeCount * 2 > table.Length)
                    return null;

                var sizes = new double[sizeCount];
                var values = new double[sizeCount];
                for (var i = 0; i < sizeCount; i++)
                {
                    sizes[i] = BinaryPrimitives.ReadInt32BigEndian(table[(sizeTable + i * 4)..]) / 65536.0;
                    values[i] = BinaryPrimitives.ReadInt16BigEndian(table[(valuesAt + i * 2)..]);
                }

                return new TrackingTable(sizes, values);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>The tracking at <paramref name="sizeInPoints"/>, in design units per glyph: interpolated between the sizes the table lists, and held at the ends beyond them.</summary>
        internal double At(double sizeInPoints)
        {
            if (sizeInPoints <= _sizes[0])
                return _values[0];

            for (var i = 1; i < _sizes.Length; i++)
            {
                if (sizeInPoints <= _sizes[i])
                {
                    var span = _sizes[i] - _sizes[i - 1];
                    var t = span > 0 ? (sizeInPoints - _sizes[i - 1]) / span : 1;
                    return _values[i - 1] + (_values[i] - _values[i - 1]) * t;
                }
            }

            return _values[^1];
        }
    }
}
