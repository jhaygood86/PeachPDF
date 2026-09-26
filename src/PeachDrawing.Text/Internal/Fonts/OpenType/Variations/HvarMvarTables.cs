using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>The <c>HVAR</c> table: how the horizontal advances change with the axes.</summary>
    internal sealed class HvarTable
    {
        private readonly ItemVariationStore _store;
        private readonly DeltaSetIndexMap? _advanceMap;

        private HvarTable(ItemVariationStore store, DeltaSetIndexMap? advanceMap)
        {
            _store = store;
            _advanceMap = advanceMap;
        }

        internal static HvarTable? TryParse(ReadOnlySpan<byte> table)
        {
            try
            {
                if (table.Length < 20 || BigEndian.U16(table, 0) != 1)
                {
                    return null;
                }

                var store = ItemVariationStore.TryParse(table, (int)BigEndian.U32(table, 4));
                if (store is null)
                {
                    return null;
                }

                uint mapOffset = BigEndian.U32(table, 8);
                var map = mapOffset == 0 ? null : DeltaSetIndexMap.TryParse(table, (int)mapOffset);
                if (mapOffset != 0 && map is null)
                {
                    // Reading the advances by glyph index instead would give a wrong answer, not a degraded one.
                    return null;
                }

                return new HvarTable(store, map);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>How much the advance width of <paramref name="glyph"/> changes at <paramref name="coordinates"/>, in design units.</summary>
        internal double GetAdvanceDelta(int glyph, ReadOnlySpan<double> coordinates)
        {
            // Without a mapping the glyph index is the inner index of the first data set.
            var (outer, inner) = _advanceMap is null ? (0, glyph) : _advanceMap.Map(glyph);
            return _store.GetDelta(outer, inner, coordinates);
        }
    }

    /// <summary>The <c>MVAR</c> table: how font-wide metrics (ascender, x-height, underline position and so on) change with the axes.</summary>
    internal sealed class MvarTable
    {
        private readonly ItemVariationStore _store;
        private readonly Dictionary<string, (int Outer, int Inner)> _records;

        private MvarTable(ItemVariationStore store, Dictionary<string, (int, int)> records)
        {
            _store = store;
            _records = records;
        }

        internal static MvarTable? TryParse(ReadOnlySpan<byte> table)
        {
            try
            {
                if (table.Length < 12 || BigEndian.U16(table, 0) != 1)
                {
                    return null;
                }

                int recordSize = BigEndian.U16(table, 6);
                int recordCount = BigEndian.U16(table, 8);
                int storeOffset = BigEndian.U16(table, 10);
                if (recordCount == 0 || storeOffset == 0 || recordSize < 8)
                {
                    return null;
                }

                var store = ItemVariationStore.TryParse(table, storeOffset);
                if (store is null)
                {
                    return null;
                }

                var records = new Dictionary<string, (int, int)>(recordCount);
                for (int i = 0; i < recordCount; i++)
                {
                    int at = 12 + i * recordSize;
                    records[BigEndian.Tag(table, at)] = (BigEndian.U16(table, at + 4), BigEndian.U16(table, at + 6));
                }

                return new MvarTable(store, records);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>
        /// How much the metric with the value tag <paramref name="tag"/> (<c>hasc</c>, <c>xhgt</c>, <c>undo</c> and so on) changes at
        /// <paramref name="coordinates"/>, or 0 when the font does not vary it.
        /// </summary>
        internal double GetDelta(string tag, ReadOnlySpan<double> coordinates) =>
            _records.TryGetValue(tag, out var pair) ? _store.GetDelta(pair.Outer, pair.Inner, coordinates) : 0;
    }
}
