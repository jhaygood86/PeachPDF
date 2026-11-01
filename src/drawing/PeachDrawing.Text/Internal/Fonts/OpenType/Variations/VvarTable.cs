using System;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>
    /// The <c>VVAR</c> table: how the vertical advances (and, for a CFF font, the vertical origins of <c>VORG</c>) change with the axes.
    /// The vertical counterpart of <see cref="HvarTable"/>; the side-bearing mappings it may carry are not read, because nothing here
    /// derives an origin from a top side bearing.
    /// </summary>
    internal sealed class VvarTable
    {
        private readonly ItemVariationStore _store;
        private readonly DeltaSetIndexMap? _advanceMap;
        private readonly DeltaSetIndexMap? _originMap;

        private VvarTable(ItemVariationStore store, DeltaSetIndexMap? advanceMap, DeltaSetIndexMap? originMap)
        {
            _store = store;
            _advanceMap = advanceMap;
            _originMap = originMap;
        }

        /// <summary>Whether the table maps vertical origins: without a mapping <c>VORG</c> does not vary.</summary>
        internal bool VariesOrigins => _originMap is not null;

        /// <summary>Reads a <c>VVAR</c> table, or returns <see langword="null"/> when it is malformed.</summary>
        internal static VvarTable? TryParse(ReadOnlySpan<byte> table)
        {
            try
            {
                // major, minor, store, advance height map, top side bearing map, bottom side bearing map, vertical origin map.
                if (table.Length < 24 || BigEndian.U16(table, 0) != 1)
                {
                    return null;
                }

                var store = ItemVariationStore.TryParse(table, (int)BigEndian.U32(table, 4));
                if (store is null)
                {
                    return null;
                }

                if (!TryReadMap(table, 8, out var advanceMap) || !TryReadMap(table, 20, out var originMap))
                {
                    return null;
                }

                return new VvarTable(store, advanceMap, originMap);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        private static bool TryReadMap(ReadOnlySpan<byte> table, int at, out DeltaSetIndexMap? map)
        {
            uint offset = BigEndian.U32(table, at);
            map = offset == 0 ? null : DeltaSetIndexMap.TryParse(table, (int)offset);

            // Reading by glyph index instead of through a map that will not parse would give a wrong answer, not a degraded one.
            return offset == 0 || map is not null;
        }

        /// <summary>How much the vertical advance of <paramref name="glyph"/> changes at <paramref name="coordinates"/>, in design units.</summary>
        internal double GetAdvanceDelta(int glyph, ReadOnlySpan<double> coordinates)
        {
            // Without a mapping the glyph index is the inner index of the first data set.
            var (outer, inner) = _advanceMap is null ? (0, glyph) : _advanceMap.Map(glyph);
            return _store.GetDelta(outer, inner, coordinates);
        }

        /// <summary>How much the vertical origin (<c>VORG</c>) of <paramref name="glyph"/> changes at <paramref name="coordinates"/>, or 0 when the table maps none.</summary>
        internal double GetOriginDelta(int glyph, ReadOnlySpan<double> coordinates)
        {
            if (_originMap is null)
            {
                return 0;
            }

            var (outer, inner) = _originMap.Map(glyph);
            return _store.GetDelta(outer, inner, coordinates);
        }
    }
}
