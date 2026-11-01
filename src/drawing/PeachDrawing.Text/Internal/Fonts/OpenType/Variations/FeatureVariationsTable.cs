using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>
    /// The <c>FeatureVariations</c> table of <c>GSUB</c> or <c>GPOS</c>: at some regions of a variable font's design space, a feature uses
    /// another list of lookups (the way <c>rvrn</c> swaps glyphs at a weight). The first record whose conditions all hold at a location wins.
    /// </summary>
    /// <remarks>
    /// https://learn.microsoft.com/en-us/typography/opentype/spec/chapter2#featurevariations-table. The table comes from the font file and
    /// is parsed from the font's bytes with every count checked against the table, without the face's shared cursor.
    /// </remarks>
    internal sealed class FeatureVariationsTable
    {
        private readonly record struct Condition(int Axis, double Minimum, double Maximum);

        private readonly record struct Record(Condition[]? Conditions, Dictionary<int, int[]> Substitutions);

        private readonly Record[] _records;

        private FeatureVariationsTable(Record[] records)
        {
            _records = records;
        }

        /// <summary>Reads the table at <paramref name="offset"/> of <paramref name="table"/>, or returns <see langword="null"/> when it is malformed or empty.</summary>
        internal static FeatureVariationsTable? TryParse(ReadOnlySpan<byte> table, int offset)
        {
            try
            {
                if (BigEndian.U16(table, offset) != 1)
                {
                    return null;
                }

                long count = BigEndian.U32(table, offset + 4);
                if (count == 0 || offset + 8 + count * 8 > table.Length)
                {
                    return null;
                }

                var records = new Record[count];
                for (int i = 0; i < count; i++)
                {
                    int at = offset + 8 + i * 8;
                    uint conditionSetOffset = BigEndian.U32(table, at);
                    uint substitutionOffset = BigEndian.U32(table, at + 4);
                    var conditions = conditionSetOffset == 0 ? null : ReadConditionSet(table, offset + (int)conditionSetOffset);
                    var substitutions = substitutionOffset == 0 ? [] : ReadSubstitutions(table, offset + (int)substitutionOffset);
                    records[i] = new Record(conditions, substitutions);
                }

                return new FeatureVariationsTable(records);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        private static Condition[] ReadConditionSet(ReadOnlySpan<byte> table, int start)
        {
            int count = BigEndian.U16(table, start);
            if (start + 2 + (long)count * 4 > table.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(table));
            }

            var conditions = new Condition[count];
            for (int i = 0; i < count; i++)
            {
                int conditionAt = start + (int)BigEndian.U32(table, start + 2 + i * 4);
                // Only format 1 (an axis range) exists in the versions of the table this reads; a condition of another format cannot be
                // met, which keeps its record from applying rather than applying it wrongly.
                conditions[i] = BigEndian.U16(table, conditionAt) == 1
                    ? new Condition(BigEndian.U16(table, conditionAt + 2), BigEndian.F2Dot14(table, conditionAt + 4), BigEndian.F2Dot14(table, conditionAt + 6))
                    : new Condition(-1, 1, -1);
            }

            return conditions;
        }

        private static Dictionary<int, int[]> ReadSubstitutions(ReadOnlySpan<byte> table, int start)
        {
            int count = BigEndian.U16(table, start + 4);
            if (start + 6 + (long)count * 6 > table.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(table));
            }

            var substitutions = new Dictionary<int, int[]>(count);
            for (int i = 0; i < count; i++)
            {
                int at = start + 6 + i * 6;
                int featureIndex = BigEndian.U16(table, at);
                int featureTable = start + (int)BigEndian.U32(table, at + 2);
                int lookupCount = BigEndian.U16(table, featureTable + 2);
                if (featureTable + 4 + (long)lookupCount * 2 > table.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(table));
                }

                var lookups = new int[lookupCount];
                for (int l = 0; l < lookupCount; l++)
                {
                    lookups[l] = BigEndian.U16(table, featureTable + 4 + l * 2);
                }

                substitutions[featureIndex] = lookups;
            }

            return substitutions;
        }

        /// <summary>
        /// The lookups that replace a feature's own at <paramref name="coordinates"/> (normalized, one per axis), by feature index, or
        /// <see langword="null"/> when no record applies there.
        /// </summary>
        internal IReadOnlyDictionary<int, int[]>? SubstitutionsAt(ReadOnlySpan<double> coordinates)
        {
            foreach (var record in _records)
            {
                if (record.Conditions is null || AllHold(record.Conditions, coordinates))
                {
                    return record.Substitutions;
                }
            }

            return null;
        }

        private static bool AllHold(Condition[] conditions, ReadOnlySpan<double> coordinates)
        {
            foreach (var condition in conditions)
            {
                double value = (uint)condition.Axis < (uint)coordinates.Length ? coordinates[condition.Axis] : 0;
                if (condition.Axis < 0 || value < condition.Minimum || value > condition.Maximum)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
