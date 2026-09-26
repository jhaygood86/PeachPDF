using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using System;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// The variation data of a <c>COLR</c> version 1 table: an <see cref="ItemVariationStore"/> and, usually, a <see cref="DeltaSetIndexMap"/>
    /// that turns the <c>VarIndexBase</c> of a variable paint (plus the position of a value within the paint's record) into the (outer, inner)
    /// pair that finds the value's deltas in the store.
    /// </summary>
    internal sealed class ColrVariations
    {
        /// <summary>The variation index that means a value does not vary.</summary>
        internal const uint NoVariation = 0xFFFFFFFF;

        private readonly ItemVariationStore _store;
        private readonly DeltaSetIndexMap? _map;

        private ColrVariations(ItemVariationStore store, DeltaSetIndexMap? map)
        {
            _store = store;
            _map = map;
        }

        /// <summary>
        /// Reads the store at <paramref name="storeOffset"/> and the index map at <paramref name="mapOffset"/> (both from the start of
        /// <paramref name="table"/>, 0 for none), or returns <see langword="null"/> when there is no store or it is malformed. A map that is
        /// present but malformed also makes the data unusable: reading the indices without it would give wrong values, not degraded ones.
        /// </summary>
        internal static ColrVariations? TryParse(ReadOnlySpan<byte> table, uint mapOffset, uint storeOffset)
        {
            if (storeOffset == 0 || storeOffset >= table.Length || mapOffset >= table.Length)
            {
                return null;
            }

            var store = ItemVariationStore.TryParse(table, (int)storeOffset);
            if (store is null)
            {
                return null;
            }

            DeltaSetIndexMap? map = null;
            if (mapOffset != 0)
            {
                map = DeltaSetIndexMap.TryParse(table, (int)mapOffset);
                if (map is null)
                {
                    return null;
                }
            }

            return new ColrVariations(store, map);
        }

        /// <summary>
        /// How much the value with the variation index <paramref name="varIndexBase"/> plus <paramref name="field"/> changes at the location
        /// <paramref name="coordinates"/> (normalized, one per axis), in the units the value is written in.
        /// </summary>
        internal double GetDelta(uint varIndexBase, int field, ReadOnlySpan<double> coordinates)
        {
            if (varIndexBase == NoVariation)
            {
                return 0;
            }

            long index = (long)varIndexBase + field;
            if (index >= NoVariation)
            {
                return 0;
            }

            // Without a map the index is the pair itself: the outer index above 16 bits and the inner one in the low 16.
            var (outer, inner) = _map is null ? ((int)(index >> 16), (int)(index & 0xFFFF)) : index > int.MaxValue ? (int.MaxValue, 0) : _map.Map((int)index);
            return _store.GetDelta(outer, inner, coordinates);
        }
    }
}
