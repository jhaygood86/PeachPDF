using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>
    /// An <c>ItemVariationStore</c>: sets of deltas, each a row of one delta per variation region, that <c>HVAR</c>, <c>MVAR</c> and
    /// the layout tables index with an (outer, inner) pair to find how much a value changes at a location in the design space.
    /// </summary>
    internal sealed class ItemVariationStore
    {
        private readonly int _axisCount;
        private readonly (double Start, double Peak, double End)[][] _regions;
        private readonly DataSet[] _dataSets;

        private ItemVariationStore(int axisCount, (double, double, double)[][] regions, DataSet[] dataSets)
        {
            _axisCount = axisCount;
            _regions = regions;
            _dataSets = dataSets;
        }

        private sealed class DataSet
        {
            internal int ItemCount;
            internal int[] RegionIndexes = [];
            internal int[][] Deltas = [];
        }

        /// <summary>Reads a store that starts at <paramref name="offset"/> in <paramref name="table"/>, or returns <see langword="null"/> when it is malformed.</summary>
        internal static ItemVariationStore? TryParse(ReadOnlySpan<byte> table, int offset)
        {
            try
            {
                if (BigEndian.U16(table, offset) != 1)
                {
                    return null;
                }

                int regionListOffset = offset + (int)BigEndian.U32(table, offset + 2);
                int dataCount = BigEndian.U16(table, offset + 6);

                int axisCount = BigEndian.U16(table, regionListOffset);
                int regionCount = BigEndian.U16(table, regionListOffset + 2);
                // A count the table is too short to hold is a damaged table; nothing is allocated for it.
                if ((long)regionCount * axisCount * 6 > table.Length - regionListOffset - 4 || (long)dataCount * 4 > table.Length - offset - 8)
                {
                    return null;
                }

                var regions = new (double, double, double)[regionCount][];
                for (int r = 0; r < regionCount; r++)
                {
                    var region = new (double, double, double)[axisCount];
                    int at = regionListOffset + 4 + r * axisCount * 6;
                    for (int a = 0; a < axisCount; a++)
                    {
                        region[a] = (BigEndian.F2Dot14(table, at), BigEndian.F2Dot14(table, at + 2), BigEndian.F2Dot14(table, at + 4));
                        at += 6;
                    }

                    regions[r] = region;
                }

                // Several entries may name one data set, and a hostile table could name a large one thousands of times, so each is read once.
                var parsed = new Dictionary<uint, DataSet>();
                var sets = new DataSet[dataCount];
                for (int d = 0; d < dataCount; d++)
                {
                    uint relative = BigEndian.U32(table, offset + 8 + d * 4);
                    if (relative == 0)
                    {
                        sets[d] = new DataSet();
                        continue;
                    }

                    if (parsed.TryGetValue(relative, out var seen))
                    {
                        sets[d] = seen;
                        continue;
                    }

                    int at = offset + (int)relative;
                    int itemCount = BigEndian.U16(table, at);
                    int wordDeltaCount = BigEndian.U16(table, at + 2);
                    int regionIndexCount = BigEndian.U16(table, at + 4);
                    bool longWords = (wordDeltaCount & 0x8000) != 0;
                    wordDeltaCount &= 0x7FFF;

                    int wordSize = longWords ? 4 : 2;
                    int byteSize = longWords ? 2 : 1;
                    long rowSize = (long)Math.Min(wordDeltaCount, regionIndexCount) * wordSize + (long)Math.Max(0, regionIndexCount - wordDeltaCount) * byteSize;
                    if ((long)itemCount * rowSize > table.Length - at - 6 - regionIndexCount * 2L)
                    {
                        return null;
                    }

                    var set = new DataSet { ItemCount = itemCount, RegionIndexes = new int[regionIndexCount], Deltas = new int[itemCount][] };
                    for (int i = 0; i < regionIndexCount; i++)
                    {
                        set.RegionIndexes[i] = BigEndian.U16(table, at + 6 + i * 2);
                    }

                    int row = at + 6 + regionIndexCount * 2;
                    for (int item = 0; item < itemCount; item++)
                    {
                        var deltas = new int[regionIndexCount];
                        for (int i = 0; i < regionIndexCount; i++)
                        {
                            if (i < wordDeltaCount)
                            {
                                if (longWords)
                                {
                                    deltas[i] = BigEndian.I32(table, row);
                                    row += 4;
                                }
                                else
                                {
                                    deltas[i] = BigEndian.I16(table, row);
                                    row += 2;
                                }
                            }
                            else if (longWords)
                            {
                                deltas[i] = BigEndian.I16(table, row);
                                row += 2;
                            }
                            else
                            {
                                deltas[i] = BigEndian.I8(table, row);
                                row += 1;
                            }
                        }

                        set.Deltas[item] = deltas;
                    }

                    sets[d] = set;
                    parsed[relative] = set;
                }

                return new ItemVariationStore(axisCount, regions, sets);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>
        /// How much the value the (<paramref name="outer"/>, <paramref name="inner"/>) pair stands for changes at the location
        /// <paramref name="coordinates"/> (normalized, one per axis), or 0 when the pair is not in the store.
        /// </summary>
        internal double GetDelta(int outer, int inner, ReadOnlySpan<double> coordinates)
        {
            if ((uint)outer >= (uint)_dataSets.Length)
            {
                return 0;
            }

            var set = _dataSets[outer];
            if ((uint)inner >= (uint)set.ItemCount)
            {
                return 0;
            }

            var deltas = set.Deltas[inner];
            double sum = 0;
            for (int i = 0; i < deltas.Length; i++)
            {
                if (deltas[i] == 0)
                {
                    continue;
                }

                int region = set.RegionIndexes[i];
                if ((uint)region >= (uint)_regions.Length)
                {
                    continue;
                }

                sum += deltas[i] * RegionScalar(_regions[region], coordinates);
            }

            return sum;
        }

        /// <summary>How far a location lies inside a region: 1 at the peak, falling to 0 at the region's edges, and 0 outside it.</summary>
        private static double RegionScalar((double Start, double Peak, double End)[] region, ReadOnlySpan<double> coordinates)
        {
            double scalar = 1;
            for (int a = 0; a < region.Length; a++)
            {
                var (start, peak, end) = region[a];
                // An axis the location does not mention is at its default.
                double coordinate = a < coordinates.Length ? coordinates[a] : 0;
                if (peak == 0 || start > peak || peak > end || (start < 0 && end > 0) || coordinate == peak)
                {
                    continue;
                }

                if (coordinate <= start || coordinate >= end)
                {
                    return 0;
                }

                scalar *= coordinate < peak ? (coordinate - start) / (peak - start) : (end - coordinate) / (end - peak);
            }

            return scalar;
        }
    }

    /// <summary>
    /// A <c>DeltaSetIndexMap</c>: how a glyph index (or another counted item) is turned into the (outer, inner) pair that indexes an
    /// <see cref="ItemVariationStore"/>.
    /// </summary>
    internal sealed class DeltaSetIndexMap
    {
        private readonly int[] _outer;
        private readonly int[] _inner;

        private DeltaSetIndexMap(int[] outer, int[] inner)
        {
            _outer = outer;
            _inner = inner;
        }

        internal static DeltaSetIndexMap? TryParse(ReadOnlySpan<byte> table, int offset)
        {
            try
            {
                int format = BigEndian.U8(table, offset);
                int entryFormat = BigEndian.U8(table, offset + 1);
                int count;
                int at;
                if (format == 0)
                {
                    count = BigEndian.U16(table, offset + 2);
                    at = offset + 4;
                }
                else if (format == 1)
                {
                    count = (int)BigEndian.U32(table, offset + 2);
                    at = offset + 6;
                }
                else
                {
                    return null;
                }

                int entrySize = ((entryFormat >> 4) & 3) + 1;
                int innerBits = (entryFormat & 0xF) + 1;
                if (count < 0 || (long)count * entrySize > table.Length - at)
                {
                    return null;
                }

                var outer = new int[count];
                var inner = new int[count];
                for (int i = 0; i < count; i++)
                {
                    uint value = 0;
                    for (int b = 0; b < entrySize; b++)
                    {
                        value = (value << 8) | table[at++];
                    }

                    outer[i] = (int)(value >> innerBits);
                    inner[i] = (int)(value & ((1u << innerBits) - 1));
                }

                return new DeltaSetIndexMap(outer, inner);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>The pair for <paramref name="index"/>; an index past the end of the map uses the last entry, as the specification says.</summary>
        internal (int Outer, int Inner) Map(int index)
        {
            if (_outer.Length == 0)
            {
                return (0, index);
            }

            int clamped = Math.Min(index, _outer.Length - 1);
            return (_outer[clamped], _inner[clamped]);
        }
    }
}
