using System;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>
    /// The <c>avar</c> table: how the normalized coordinates of the axes are remapped before the variation tables see them. Version 1 has a
    /// piecewise-linear segment map for each axis on its own; version 2 adds an <see cref="ItemVariationStore"/> that lets the value of
    /// one axis depend on the others.
    /// </summary>
    internal sealed class AvarTable
    {
        /// <summary>Most axes a font can have and keep its version 2 mapping: real fonts have a dozen at most.</summary>
        private const int MaxAxesForCrossAxisMapping = 128;

        private readonly double[][][] _segments;
        private readonly ItemVariationStore? _store;
        private readonly DeltaSetIndexMap? _indexMap;

        private AvarTable(double[][][] segments, ItemVariationStore? store, DeltaSetIndexMap? indexMap)
        {
            _segments = segments;
            _store = store;
            _indexMap = indexMap;
        }

        /// <summary>Whether the table has the second, cross-axis mapping of version 2.</summary>
        internal bool HasCrossAxisMapping => _store is not null;

        /// <summary>
        /// Reads an <c>avar</c> table of <paramref name="axisCount"/> axes, or returns <see langword="null"/> when it is not one (a wrong version or
        /// axis count). A truncated table throws; the caller treats that as a font whose variations cannot be read.
        /// </summary>
        internal static AvarTable? TryParse(ReadOnlySpan<byte> avar, int axisCount)
        {
            int version = avar.Length < 8 ? 0 : BigEndian.U16(avar, 0);
            if (version is not (1 or 2) || BigEndian.U16(avar, 6) != axisCount)
            {
                return null;
            }

            var segments = new double[axisCount][][];
            int at = 8;
            for (int a = 0; a < axisCount; a++)
            {
                int count = BigEndian.U16(avar, at);
                at += 2;
                var map = new double[count][];
                for (int i = 0; i < count; i++)
                {
                    map[i] = [BigEndian.F2Dot14(avar, at), BigEndian.F2Dot14(avar, at + 2)];
                    at += 4;
                }

                segments[a] = map;
            }

            ItemVariationStore? store = null;
            DeltaSetIndexMap? indexMap = null;
            if (version == 2)
            {
                // The rest of a damaged version 2 table is dropped and the segment maps kept: the font then reads as it would at version 1.
                try
                {
                    uint mapOffset = BigEndian.U32(avar, at);
                    uint storeOffset = BigEndian.U32(avar, at + 4);
                    if (storeOffset != 0)
                    {
                        store = ItemVariationStore.TryParse(avar, (int)storeOffset);

                        // The regions of the store have a range for every axis of the font, and a font has no more axes than a mapping that
                        // works each of them out from all of them (a quadratic cost for every location) can afford: a store that says
                        // otherwise is not one this reader can use.
                        if (store is not null && (store.AxisCount != axisCount || axisCount > MaxAxesForCrossAxisMapping))
                        {
                            store = null;
                        }

                        if (store is not null && mapOffset != 0)
                        {
                            indexMap = DeltaSetIndexMap.TryParse(avar, (int)mapOffset);
                            if (indexMap is null)
                            {
                                // Mapping the axes by their own index instead would give a wrong answer, not a degraded one.
                                store = null;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
                {
                    store = null;
                    indexMap = null;
                }
            }

            return new AvarTable(segments, store, indexMap);
        }

        /// <summary>
        /// Maps the normalized coordinates of every axis (each in -1 to 1, from the axis's own range) to the ones the variation tables are
        /// read at: each through its segment map, rounded to the 2.14 fixed point the tables use, and then, for version 2, moved by the delta
        /// the store gives for the whole location.
        /// </summary>
        internal double[] Map(double[] normalized)
        {
            var mapped = new double[normalized.Length];
            for (int axis = 0; axis < mapped.Length; axis++)
            {
                double value = normalized[axis];
                if (axis < _segments.Length)
                {
                    value = Math.Clamp(MapSegments(_segments[axis], value), -1, 1);
                }

                mapped[axis] = Round14(value);
            }

            if (_store is null)
            {
                return mapped;
            }

            // Every axis's delta is worked out from the coordinates after the segment maps, none from another axis's result.
            var result = new double[mapped.Length];
            for (int axis = 0; axis < result.Length; axis++)
            {
                var (outer, inner) = _indexMap is null ? (0, axis) : _indexMap.Map(axis);

                // The store's deltas are in units of the 2.14 fixed point the coordinates are written in, and are rounded to a whole one.
                result[axis] = Math.Clamp(mapped[axis] + FontVariations.Round(_store.GetDelta(outer, inner, mapped)) / 16384.0, -1, 1);
            }

            return result;
        }

        /// <summary>Rounds half up to the 2.14 fixed point, as the conversion of fontTools and FreeType does.</summary>
        internal static double Round14(double value) => Math.Floor(value * 16384.0 + 0.5) / 16384.0;

        /// <summary>Piecewise-linear <c>avar</c> mapping of a normalized coordinate.</summary>
        private static double MapSegments(double[][] map, double value)
        {
            if (map.Length < 2)
            {
                return value;
            }

            if (value <= map[0][0])
            {
                return map[0][1];
            }

            for (int i = 1; i < map.Length; i++)
            {
                double from = map[i][0];
                if (value <= from)
                {
                    double previousFrom = map[i - 1][0];
                    if (from == previousFrom)
                    {
                        return map[i][1];
                    }

                    return map[i - 1][1] + (value - previousFrom) * (map[i][1] - map[i - 1][1]) / (from - previousFrom);
                }
            }

            return map[^1][1];
        }
    }
}
