using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>An axis of a variable font, from its <c>fvar</c> table.</summary>
    internal sealed class AxisInfo
    {
        internal AxisInfo(string tag, double minimum, double defaultValue, double maximum, bool isHidden, string? name)
        {
            Tag = tag;
            Minimum = minimum;
            Default = defaultValue;
            Maximum = maximum;
            IsHidden = isHidden;
            Name = name;
        }

        internal string Tag { get; }
        internal double Minimum { get; }
        internal double Default { get; }
        internal double Maximum { get; }
        internal bool IsHidden { get; }
        internal string? Name { get; }
    }

    /// <summary>A named instance of a variable font, from its <c>fvar</c> table.</summary>
    internal sealed class NamedInstanceInfo
    {
        internal NamedInstanceInfo(string? name, double[] coordinates)
        {
            Name = name;
            Coordinates = coordinates;
        }

        internal string? Name { get; }

        /// <summary>The user-space value on every axis, in the order of the axes.</summary>
        internal double[] Coordinates { get; }
    }

    /// <summary>
    /// A location in the design space of a variable font: what the axes are set to, and the same location normalized to the range
    /// -1 to 1 the variation tables are written in (after the <c>avar</c> mapping). Immutable; two locations that mean the same
    /// have the same <see cref="Key"/>.
    /// </summary>
    internal sealed class VariationCoordinates
    {
        internal VariationCoordinates(double[] userValues, double[] normalized, string[] tags)
        {
            UserValues = userValues;
            Normalized = normalized;
            Tags = tags;

            var key = new StringBuilder();
            bool isDefault = true;
            for (int i = 0; i < tags.Length; i++)
            {
                if (i > 0)
                {
                    key.Append(';');
                }

                key.Append(tags[i]).Append('=').Append(userValues[i].ToString("R", CultureInfo.InvariantCulture));
                if (normalized[i] != 0)
                {
                    isDefault = false;
                }
            }

            Key = key.ToString();
            IsDefault = isDefault;
        }

        /// <summary>The value on every axis, clamped to the axis's range, in the order of the axes.</summary>
        internal double[] UserValues { get; }

        /// <summary>The axis tags, in the same order.</summary>
        internal string[] Tags { get; }

        /// <summary>The normalized coordinates, rounded to the 2.14 fixed point the tables use, in the same order.</summary>
        internal double[] Normalized { get; }

        /// <summary>Whether every axis is at its default, so nothing varies.</summary>
        internal bool IsDefault { get; }

        /// <summary>A string that is the same for the same location, such as <c>wght=700;wdth=100</c>.</summary>
        internal string Key { get; }
    }

    /// <summary>
    /// The variation tables of one font face (<c>fvar</c>, <c>avar</c>, <c>gvar</c>, <c>HVAR</c>, <c>MVAR</c>), parsed from its bytes once.
    /// Nothing here depends on a location; a <see cref="VariationCoordinates"/> says where in the design space to read.
    /// </summary>
    internal sealed class FontVariations
    {
        private FontVariations(AxisInfo[] axes, NamedInstanceInfo[] instances, double[][][]? avar, GvarTable? gvar, HvarTable? hvar, MvarTable? mvar)
        {
            Axes = axes;
            Instances = instances;
            _avar = avar;
            Gvar = gvar;
            Hvar = hvar;
            Mvar = mvar;
        }

        private readonly double[][][]? _avar;

        internal AxisInfo[] Axes { get; }
        internal NamedInstanceInfo[] Instances { get; }
        internal GvarTable? Gvar { get; }
        internal HvarTable? Hvar { get; }
        internal MvarTable? Mvar { get; }

        /// <summary>Reads the variation tables of <paramref name="face"/>, or returns <see langword="null"/> for a font that is not variable.</summary>
        internal static FontVariations? TryCreate(OpenTypeFontface face)
        {
            if (!face.TableDictionary.ContainsKey("fvar"))
            {
                return null;
            }

            try
            {
                var bytes = face.FontSource.Bytes;
                ReadOnlyMemory<byte> Memory(string tag) => face.TableDictionary.TryGetValue(tag, out var entry)
                    ? bytes.AsMemory(entry.Offset, Math.Min(entry.Length, bytes.Length - entry.Offset))
                    : ReadOnlyMemory<byte>.Empty;

                var nameTable = Memory("name").Span;
                var fvar = Memory("fvar").Span;
                int axisCount = BigEndian.U16(fvar, 8);
                int axisSize = BigEndian.U16(fvar, 10);
                int instanceCount = BigEndian.U16(fvar, 12);
                int instanceSize = BigEndian.U16(fvar, 14);
                int axesOffset = BigEndian.U16(fvar, 4);
                if (axisCount == 0 || axisSize < 20)
                {
                    return null;
                }

                var axes = new AxisInfo[axisCount];
                for (int i = 0; i < axisCount; i++)
                {
                    int at = axesOffset + i * axisSize;
                    axes[i] = new AxisInfo(
                        BigEndian.Tag(fvar, at),
                        BigEndian.Fixed(fvar, at + 4),
                        BigEndian.Fixed(fvar, at + 8),
                        BigEndian.Fixed(fvar, at + 12),
                        (BigEndian.U16(fvar, at + 16) & 1) != 0,
                        FindName(nameTable, BigEndian.U16(fvar, at + 18)));
                }

                var instances = new NamedInstanceInfo[instanceCount];
                int instancesAt = axesOffset + axisCount * axisSize;
                for (int i = 0; i < instanceCount; i++)
                {
                    int at = instancesAt + i * instanceSize;
                    var coordinates = new double[axisCount];
                    for (int a = 0; a < axisCount; a++)
                    {
                        coordinates[a] = BigEndian.Fixed(fvar, at + 4 + a * 4);
                    }

                    instances[i] = new NamedInstanceInfo(FindName(nameTable, BigEndian.U16(fvar, at)), coordinates);
                }

                double[][][]? avar = ParseAvar(Memory("avar").Span, axisCount);
                GvarTable? gvar = Memory("gvar") is { Length: > 0 } g ? GvarTable.TryParse(g, axisCount) : null;
                HvarTable? hvar = Memory("HVAR") is { Length: > 0 } h ? HvarTable.TryParse(h.Span) : null;
                MvarTable? mvar = Memory("MVAR") is { Length: > 0 } m ? MvarTable.TryParse(m.Span) : null;
                return new FontVariations(axes, instances, avar, gvar, hvar, mvar);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>
        /// Puts the axes at <paramref name="settings"/> (an axis not mentioned stays at its default, an unknown tag is ignored, a value
        /// outside the axis's range is clamped) and normalizes the result.
        /// </summary>
        internal VariationCoordinates CreateCoordinates(IEnumerable<(string Tag, double Value)> settings)
        {
            var user = new double[Axes.Length];
            var tags = new string[Axes.Length];
            for (int i = 0; i < Axes.Length; i++)
            {
                user[i] = Axes[i].Default;
                tags[i] = Axes[i].Tag;
            }

            foreach (var (tag, value) in settings)
            {
                for (int i = 0; i < Axes.Length; i++)
                {
                    if (Axes[i].Tag == tag)
                    {
                        user[i] = Math.Clamp(double.IsNaN(value) ? Axes[i].Default : value, Axes[i].Minimum, Axes[i].Maximum);
                    }
                }
            }

            var normalized = new double[Axes.Length];
            for (int i = 0; i < Axes.Length; i++)
            {
                normalized[i] = Normalize(i, user[i]);
            }

            return new VariationCoordinates(user, normalized, tags);
        }

        private double Normalize(int axis, double value)
        {
            var info = Axes[axis];
            double n;
            if (value < info.Default)
            {
                n = info.Default == info.Minimum ? 0 : (value - info.Default) / (info.Default - info.Minimum);
            }
            else if (value > info.Default)
            {
                n = info.Maximum == info.Default ? 0 : (value - info.Default) / (info.Maximum - info.Default);
            }
            else
            {
                n = 0;
            }

            n = Math.Clamp(n, -1, 1);
            if (_avar is not null && axis < _avar.Length)
            {
                n = MapSegments(_avar[axis], n);
            }

            return Math.Round(n * 16384.0) / 16384.0;
        }

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

        private static double[][][]? ParseAvar(ReadOnlySpan<byte> avar, int axisCount)
        {
            if (avar.Length < 8 || BigEndian.U16(avar, 0) is not (1 or 2) || BigEndian.U16(avar, 6) != axisCount)
            {
                return null;
            }

            var result = new double[axisCount][][];
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

                result[a] = map;
            }

            return result;
        }

        /// <summary>The string with a name ID in the <c>name</c> table, preferring English, or <see langword="null"/>.</summary>
        internal static string? FindName(ReadOnlySpan<byte> name, int nameId)
        {
            if (name.Length < 6)
            {
                return null;
            }

            int count = BigEndian.U16(name, 2);
            int stringsAt = BigEndian.U16(name, 4);
            int best = -1;
            int bestRank = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int record = 6 + i * 12;
                if (record + 12 > name.Length || BigEndian.U16(name, record + 6) != nameId)
                {
                    continue;
                }

                int platform = BigEndian.U16(name, record);
                int language = BigEndian.U16(name, record + 4);
                int rank = platform == 3 && language == 0x0409 ? 0 : platform == 0 ? 1 : platform == 3 ? 2 : platform == 1 && language == 0 ? 3 : 4;
                if (rank < bestRank)
                {
                    best = record;
                    bestRank = rank;
                }
            }

            if (best < 0)
            {
                return null;
            }

            int length = BigEndian.U16(name, best + 8);
            int offset = stringsAt + BigEndian.U16(name, best + 10);
            if (offset + length > name.Length)
            {
                return null;
            }

            int platformId = BigEndian.U16(name, best);
            var raw = name.Slice(offset, length);
            return platformId is 0 or 3 ? Encoding.BigEndianUnicode.GetString(raw) : Encoding.Latin1.GetString(raw);
        }
    }
}
