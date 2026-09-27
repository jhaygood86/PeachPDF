using System;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>
    /// The <c>cvar</c> table: how the control values of the <c>cvt</c> table (the numbers a TrueType font's hinting programs measure with)
    /// change with the axes. It is laid out like the per-glyph data of <c>gvar</c>, except that there is one delta for each control value
    /// and not an x and a y for each point, that every tuple carries its own peak (there are no shared tuples), and that a control value a
    /// tuple leaves out is unchanged (there is no outline to interpolate along).
    /// </summary>
    internal sealed class CvarTable
    {
        private const int SharedPointNumbers = 0x8000;
        private const int EmbeddedPeakTuple = 0x8000;
        private const int IntermediateRegion = 0x4000;
        private const int PrivatePointNumbers = 0x2000;

        /// <summary>
        /// The most delta values (one for each point of each tuple that reaches the location, or for each control value where a tuple has all
        /// of them) a table may make this reader read for one location. A tuple's data is found by the sizes in the headers, which may all be
        /// zero so that every tuple reads the same bytes, so the size of the table does not bound the work; a real font uses a small part of this.
        /// </summary>
        private const long MaxWork = 1 << 24;

        private readonly ReadOnlyMemory<byte> _table;
        private readonly int _axisCount;

        private CvarTable(ReadOnlyMemory<byte> table, int axisCount)
        {
            _table = table;
            _axisCount = axisCount;
        }

        /// <summary>Reads the header of a <c>cvar</c> table of a font with <paramref name="axisCount"/> axes, or returns <see langword="null"/> when it is not one.</summary>
        internal static CvarTable? TryParse(ReadOnlyMemory<byte> table, int axisCount)
        {
            var span = table.Span;
            if (span.Length < 8 || BigEndian.U16(span, 0) != 1 || BigEndian.U16(span, 2) != 0 || axisCount <= 0)
            {
                return null;
            }

            int dataOffset = BigEndian.U16(span, 6);
            return dataOffset > span.Length ? null : new CvarTable(table, axisCount);
        }

        /// <summary>
        /// The change of each control value at <paramref name="coordinates"/> (normalized, one per axis), in font units, or <see langword="null"/>
        /// when no tuple reaches the location or the table is malformed. Fractional: the tuples' deltas are whole numbers, and each is scaled
        /// by how close the location is to the tuple's peak.
        /// </summary>
        /// <param name="coordinates">The normalized location.</param>
        /// <param name="cvtCount">The number of control values the <c>cvt</c> table has.</param>
        internal double[]? GetDeltas(ReadOnlySpan<double> coordinates, int cvtCount)
        {
            if (cvtCount <= 0)
            {
                return null;
            }

            try
            {
                return Accumulate(coordinates, cvtCount);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        private double[]? Accumulate(ReadOnlySpan<double> coordinates, int cvtCount)
        {
            var table = _table.Span;
            int tupleField = BigEndian.U16(table, 4);
            int tupleCount = tupleField & 0x0FFF;
            int dataOffset = BigEndian.U16(table, 6);

            // Read every header first: the serialized data of the tuples follows them one after another.
            int headerAt = 8;
            var tuples = new (int Size, double[] Peak, double[]? Start, double[]? End, bool PrivatePoints)[tupleCount];
            for (int t = 0; t < tupleCount; t++)
            {
                int size = BigEndian.U16(table, headerAt);
                int tupleIndex = BigEndian.U16(table, headerAt + 2);
                headerAt += 4;
                if ((tupleIndex & EmbeddedPeakTuple) == 0)
                {
                    // cvar has no shared tuples to take a peak from.
                    return null;
                }

                var peak = ReadTuple(table, headerAt);
                headerAt += _axisCount * 2;

                double[]? start = null, end = null;
                if ((tupleIndex & IntermediateRegion) != 0)
                {
                    start = ReadTuple(table, headerAt);
                    end = ReadTuple(table, headerAt + _axisCount * 2);
                    headerAt += _axisCount * 4;
                }

                tuples[t] = (size, peak, start, end, (tupleIndex & PrivatePointNumbers) != 0);
            }

            int at = dataOffset;
            int[]? sharedPoints = null;
            bool sharedAll = true;
            if ((tupleField & SharedPointNumbers) != 0)
            {
                sharedPoints = GvarTable.ReadPointNumbers(table, ref at, out sharedAll);
            }

            double[]? result = null;
            long work = 0;
            foreach (var (size, peak, start, end, privatePoints) in tuples)
            {
                int tupleEnd = at + size;
                double scalar = GvarTable.TupleScalar(peak, start, end, coordinates);
                if (scalar != 0)
                {
                    int pos = at;
                    int[]? points = sharedPoints;
                    bool all = sharedAll;
                    if (privatePoints)
                    {
                        points = GvarTable.ReadPointNumbers(table, ref pos, out all);
                    }

                    int count = all ? cvtCount : points!.Length;
                    work += count;
                    if (work > MaxWork)
                    {
                        return null;
                    }

                    int[] deltas = GvarTable.ReadDeltas(table, ref pos, count);
                    result ??= new double[cvtCount];
                    if (all)
                    {
                        for (int i = 0; i < cvtCount; i++)
                        {
                            result[i] += scalar * deltas[i];
                        }
                    }
                    else
                    {
                        for (int i = 0; i < points!.Length; i++)
                        {
                            if ((uint)points[i] < (uint)cvtCount)
                            {
                                result[points[i]] += scalar * deltas[i];
                            }
                        }
                    }
                }

                at = tupleEnd;
            }

            return result;
        }

        private double[] ReadTuple(ReadOnlySpan<byte> data, int at)
        {
            var tuple = new double[_axisCount];
            for (int a = 0; a < _axisCount; a++)
            {
                tuple[a] = BigEndian.F2Dot14(data, at + a * 2);
            }

            return tuple;
        }
    }
}
