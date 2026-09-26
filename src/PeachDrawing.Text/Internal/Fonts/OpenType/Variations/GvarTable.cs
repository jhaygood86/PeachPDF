using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>
    /// The <c>gvar</c> table: how the points of every TrueType glyph move with the axes. Each glyph has a set of tuples, each a peak in
    /// the design space (and optionally an intermediate region around it) with a delta for some or all of the glyph's points; at a
    /// location the deltas of the tuples that reach it are added up, each scaled by how close the location is to its peak.
    /// </summary>
    internal sealed class GvarTable
    {
        private const int SharedPointNumbers = 0x8000;
        private const int EmbeddedPeakTuple = 0x8000;
        private const int IntermediateRegion = 0x4000;
        private const int PrivatePointNumbers = 0x2000;
        private const int TupleIndexMask = 0x0FFF;

        private readonly ReadOnlyMemory<byte> _table;
        private readonly int _axisCount;
        private readonly double[][] _sharedTuples;
        private readonly int _glyphCount;
        private readonly int[] _offsets;
        private readonly int _dataArrayOffset;

        private GvarTable(ReadOnlyMemory<byte> table, int axisCount, double[][] sharedTuples, int glyphCount, int[] offsets, int dataArrayOffset)
        {
            _table = table;
            _axisCount = axisCount;
            _sharedTuples = sharedTuples;
            _glyphCount = glyphCount;
            _offsets = offsets;
            _dataArrayOffset = dataArrayOffset;
        }

        internal static GvarTable? TryParse(ReadOnlyMemory<byte> memory, int axisCount)
        {
            try
            {
                var table = memory.Span;
                if (table.Length < 20 || BigEndian.U16(table, 0) != 1 || BigEndian.U16(table, 4) != axisCount)
                {
                    return null;
                }

                int sharedCount = BigEndian.U16(table, 6);
                int sharedOffset = (int)BigEndian.U32(table, 8);
                int glyphCount = BigEndian.U16(table, 12);
                bool longOffsets = (BigEndian.U16(table, 14) & 1) != 0;
                int dataArrayOffset = (int)BigEndian.U32(table, 16);

                var shared = new double[sharedCount][];
                for (int i = 0; i < sharedCount; i++)
                {
                    var tuple = new double[axisCount];
                    for (int a = 0; a < axisCount; a++)
                    {
                        tuple[a] = BigEndian.F2Dot14(table, sharedOffset + (i * axisCount + a) * 2);
                    }

                    shared[i] = tuple;
                }

                var offsets = new int[glyphCount + 1];
                for (int g = 0; g <= glyphCount; g++)
                {
                    offsets[g] = longOffsets ? (int)BigEndian.U32(table, 20 + g * 4) : BigEndian.U16(table, 20 + g * 2) * 2;
                }

                return new GvarTable(memory, axisCount, shared, glyphCount, offsets, dataArrayOffset);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>Whether the glyph has any variation data at all.</summary>
        internal bool HasVariations(int glyph) => (uint)glyph < (uint)_glyphCount && _offsets[glyph + 1] > _offsets[glyph];

        /// <summary>
        /// Adds the deltas of <paramref name="glyph"/> at <paramref name="coordinates"/> to <paramref name="dx"/> and <paramref name="dy"/>,
        /// which have one entry for each point of the glyph and then its four phantom points (a composite glyph has one point for each
        /// component's offset instead of its outline points).
        /// </summary>
        /// <param name="glyph">The glyph.</param>
        /// <param name="coordinates">The normalized location.</param>
        /// <param name="pointCount">The number of points, phantom points included.</param>
        /// <param name="originalX">
        /// The glyph's own x coordinates, which a tuple that gives deltas for only some points needs to interpolate the rest (IUP), or
        /// <see langword="null"/> when there is no outline to interpolate along (a composite glyph, or a caller that wants only the phantom points).
        /// </param>
        /// <param name="originalY">The glyph's own y coordinates.</param>
        /// <param name="contourEnds">The index of the last point of each contour, or <see langword="null"/>.</param>
        /// <param name="dx">The x deltas, added to.</param>
        /// <param name="dy">The y deltas, added to.</param>
        /// <returns><see langword="false"/> when the glyph has no variation data or the data is malformed; nothing is added then.</returns>
        internal bool TryAddDeltas(int glyph, ReadOnlySpan<double> coordinates, int pointCount,
            double[]? originalX, double[]? originalY, int[]? contourEnds, double[] dx, double[] dy)
        {
            if (!HasVariations(glyph))
            {
                return false;
            }

            var addX = new double[pointCount];
            var addY = new double[pointCount];
            try
            {
                if (!Accumulate(glyph, coordinates, pointCount, originalX, originalY, contourEnds, addX, addY))
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return false;
            }

            for (int i = 0; i < pointCount; i++)
            {
                dx[i] += addX[i];
                dy[i] += addY[i];
            }

            return true;
        }

        private bool Accumulate(int glyph, ReadOnlySpan<double> coordinates, int pointCount,
            double[]? originalX, double[]? originalY, int[]? contourEnds, double[] addX, double[] addY)
        {
            var table = _table.Span;
            int start = _dataArrayOffset + _offsets[glyph];
            int end = _dataArrayOffset + _offsets[glyph + 1];
            var data = table.Slice(start, end - start);

            int header = BigEndian.U16(data, 0);
            int tupleCount = header & 0x0FFF;
            int serializedAt = BigEndian.U16(data, 2);

            int headerAt = 4;
            var tuples = new (int Size, double[] Peak, double[]? Start, double[]? End, bool PrivatePoints)[tupleCount];
            for (int t = 0; t < tupleCount; t++)
            {
                int size = BigEndian.U16(data, headerAt);
                int tupleIndex = BigEndian.U16(data, headerAt + 2);
                headerAt += 4;

                double[] peak;
                if ((tupleIndex & EmbeddedPeakTuple) != 0)
                {
                    peak = ReadTuple(data, headerAt);
                    headerAt += _axisCount * 2;
                }
                else
                {
                    int shared = tupleIndex & TupleIndexMask;
                    if (shared >= _sharedTuples.Length)
                    {
                        return false;
                    }

                    peak = _sharedTuples[shared];
                }

                double[]? regionStart = null;
                double[]? regionEnd = null;
                if ((tupleIndex & IntermediateRegion) != 0)
                {
                    regionStart = ReadTuple(data, headerAt);
                    regionEnd = ReadTuple(data, headerAt + _axisCount * 2);
                    headerAt += _axisCount * 4;
                }

                tuples[t] = (size, peak, regionStart, regionEnd, (tupleIndex & PrivatePointNumbers) != 0);
            }

            int at = serializedAt;
            int[]? sharedPoints = null;
            bool sharedAll = true;
            if ((header & SharedPointNumbers) != 0)
            {
                sharedPoints = ReadPointNumbers(data, ref at, out sharedAll);
            }

            bool any = false;
            for (int t = 0; t < tupleCount; t++)
            {
                var (size, peak, regionStart, regionEnd, privatePoints) = tuples[t];
                int tupleEnd = at + size;

                double scalar = TupleScalar(peak, regionStart, regionEnd, coordinates);
                if (scalar != 0)
                {
                    int pos = at;
                    int[]? points = sharedPoints;
                    bool all = sharedAll;
                    if (privatePoints)
                    {
                        points = ReadPointNumbers(data, ref pos, out all);
                    }

                    int deltaCount = all ? pointCount : points!.Length;
                    var xs = ReadDeltas(data, ref pos, deltaCount);
                    var ys = ReadDeltas(data, ref pos, deltaCount);

                    var tupleX = new double[pointCount];
                    var tupleY = new double[pointCount];
                    if (all)
                    {
                        for (int i = 0; i < pointCount; i++)
                        {
                            tupleX[i] = xs[i];
                            tupleY[i] = ys[i];
                        }
                    }
                    else
                    {
                        var touched = new bool[pointCount];
                        for (int i = 0; i < points!.Length; i++)
                        {
                            int p = points[i];
                            if ((uint)p >= (uint)pointCount)
                            {
                                continue;
                            }

                            tupleX[p] = xs[i];
                            tupleY[p] = ys[i];
                            touched[p] = true;
                        }

                        if (originalX is not null && originalY is not null && contourEnds is not null)
                        {
                            InferUntouched(originalX, originalY, contourEnds, touched, tupleX, tupleY);
                        }
                    }

                    for (int i = 0; i < pointCount; i++)
                    {
                        addX[i] += scalar * tupleX[i];
                        addY[i] += scalar * tupleY[i];
                    }

                    any = true;
                }

                at = tupleEnd;
            }

            return any;
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

        /// <summary>How close the location is to the tuple's peak: 1 at it, 0 at the edge of its region and outside.</summary>
        private static double TupleScalar(double[] peak, double[]? start, double[]? end, ReadOnlySpan<double> coordinates)
        {
            double scalar = 1;
            for (int a = 0; a < peak.Length && a < coordinates.Length; a++)
            {
                double p = peak[a];
                if (p == 0)
                {
                    continue;
                }

                double c = coordinates[a];
                if (c == p)
                {
                    continue;
                }

                double lower = start is null ? Math.Min(0, p) : start[a];
                double upper = end is null ? Math.Max(0, p) : end[a];
                if (c <= lower || c >= upper)
                {
                    return 0;
                }

                scalar *= c < p ? (c - lower) / (p - lower) : (c - upper) / (p - upper);
            }

            return scalar;
        }

        /// <summary>Reads packed point numbers; the result is meaningless when <paramref name="all"/> is <see langword="true"/> (every point).</summary>
        private static int[] ReadPointNumbers(ReadOnlySpan<byte> data, ref int at, out bool all)
        {
            int count = data[at++];
            if (count == 0)
            {
                all = true;
                return [];
            }

            if ((count & 0x80) != 0)
            {
                count = ((count & 0x7F) << 8) | data[at++];
            }

            all = false;
            var points = new int[count];
            int filled = 0;
            int last = 0;
            while (filled < count)
            {
                int control = data[at++];
                int run = (control & 0x7F) + 1;
                bool words = (control & 0x80) != 0;
                for (int i = 0; i < run && filled < count; i++)
                {
                    int delta = words ? BigEndian.U16(data, at) : data[at];
                    at += words ? 2 : 1;
                    last += delta;
                    points[filled++] = last;
                }
            }

            return points;
        }

        /// <summary>Reads <paramref name="count"/> packed deltas.</summary>
        private static int[] ReadDeltas(ReadOnlySpan<byte> data, ref int at, int count)
        {
            var deltas = new int[count];
            int filled = 0;
            while (filled < count)
            {
                int control = data[at++];
                int run = (control & 0x3F) + 1;
                for (int i = 0; i < run && filled < count; i++)
                {
                    if ((control & 0x80) != 0)
                    {
                        deltas[filled++] = 0;
                    }
                    else if ((control & 0x40) != 0)
                    {
                        deltas[filled++] = BigEndian.I16(data, at);
                        at += 2;
                    }
                    else
                    {
                        deltas[filled++] = (sbyte)data[at++];
                    }
                }
            }

            return deltas;
        }

        /// <summary>
        /// Interpolates the deltas of the points a tuple left out (IUP): along each contour, a point between two points that have
        /// deltas moves as they do, in proportion to where it lies between them on the glyph's own outline, and a point outside their
        /// range moves like the nearer one. A contour with a single delta moves as a whole; one with none does not move.
        /// </summary>
        private static void InferUntouched(double[] originalX, double[] originalY, int[] contourEnds, bool[] touched, double[] dx, double[] dy)
        {
            int start = 0;
            foreach (int end in contourEnds)
            {
                if (end >= touched.Length - 4)
                {
                    break;
                }

                InferContour(originalX, touched, dx, start, end);
                InferContour(originalY, touched, dy, start, end);
                start = end + 1;
            }
        }

        private static void InferContour(double[] original, bool[] touched, double[] delta, int start, int end)
        {
            var touchedIndexes = new List<int>();
            for (int i = start; i <= end; i++)
            {
                if (touched[i])
                {
                    touchedIndexes.Add(i);
                }
            }

            if (touchedIndexes.Count == 0)
            {
                return;
            }

            if (touchedIndexes.Count == 1)
            {
                double only = delta[touchedIndexes[0]];
                for (int i = start; i <= end; i++)
                {
                    delta[i] = only;
                }

                return;
            }

            for (int k = 0; k < touchedIndexes.Count; k++)
            {
                int a = touchedIndexes[k];
                int b = touchedIndexes[(k + 1) % touchedIndexes.Count];
                for (int p = NextInContour(a, start, end); p != b; p = NextInContour(p, start, end))
                {
                    delta[p] = Interpolate(original[p], original[a], original[b], delta[a], delta[b]);
                }
            }
        }

        private static int NextInContour(int index, int start, int end) => index == end ? start : index + 1;

        private static double Interpolate(double value, double a, double b, double deltaA, double deltaB)
        {
            if (a == b)
            {
                return deltaA == deltaB ? deltaA : 0;
            }

            if (a > b)
            {
                (a, b) = (b, a);
                (deltaA, deltaB) = (deltaB, deltaA);
            }

            if (value <= a)
            {
                return deltaA;
            }

            if (value >= b)
            {
                return deltaB;
            }

            return deltaA + (value - a) * (deltaB - deltaA) / (b - a);
        }
    }
}
