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

                // + 0 turns -0 into 0, so the two spell one location.
                key.Append(tags[i]).Append('=').Append((userValues[i] + 0).ToString("R", CultureInfo.InvariantCulture));
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
        private FontVariations(AxisInfo[] axes, NamedInstanceInfo[] instances, AvarTable? avar, GvarTable? gvar, HvarTable? hvar, VvarTable? vvar, MvarTable? mvar)
        {
            Axes = axes;
            Instances = instances;
            _avar = avar;
            Gvar = gvar;
            Hvar = hvar;
            Vvar = vvar;
            Mvar = mvar;
        }

        private readonly AvarTable? _avar;

        internal AxisInfo[] Axes { get; }
        internal NamedInstanceInfo[] Instances { get; }
        internal GvarTable? Gvar { get; }
        internal HvarTable? Hvar { get; }
        internal VvarTable? Vvar { get; }
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
                if (axisCount == 0 || axisSize < 20 || axesOffset < 16
                    || (long)axesOffset + (long)axisCount * axisSize > fvar.Length
                    || instanceSize < 4 + 4 * axisCount
                    || (long)axesOffset + (long)axisCount * axisSize + (long)instanceCount * instanceSize > fvar.Length)
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

                    // An axis whose range is upside down or not a number would have no location to be at.
                    if (!(axes[i].Minimum <= axes[i].Default && axes[i].Default <= axes[i].Maximum))
                    {
                        return null;
                    }
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

                AvarTable? avar = Memory("avar") is { Length: > 0 } av ? AvarTable.TryParse(av.Span, axisCount) : null;
                GvarTable? gvar = Memory("gvar") is { Length: > 0 } g ? GvarTable.TryParse(g, axisCount) : null;
                HvarTable? hvar = Memory("HVAR") is { Length: > 0 } h ? HvarTable.TryParse(h.Span) : null;
                VvarTable? vvar = Memory("VVAR") is { Length: > 0 } v ? VvarTable.TryParse(v.Span) : null;
                MvarTable? mvar = Memory("MVAR") is { Length: > 0 } m ? MvarTable.TryParse(m.Span) : null;
                return new FontVariations(axes, instances, avar, gvar, hvar, vvar, mvar);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>Rounds half up (towards positive infinity), which is what the specification, FreeType and fontTools do; <see cref="Math.Round(double)"/> rounds half to even.</summary>
        internal static int Round(double value) => (int)Math.Floor(value + 0.5);

        /// <summary>
        /// How much the advance width of <paramref name="glyph"/> differs from its <c>hmtx</c> entry at <paramref name="coordinates"/>, in design
        /// units: from <c>HVAR</c> when the font has it, otherwise from the phantom points of <c>gvar</c>.
        /// </summary>
        internal double GetAdvanceDelta(OpenTypeFontface face, int glyph, VariationCoordinates coordinates)
        {
            if (Hvar is { } hvar)
                return hvar.GetAdvanceDelta(glyph, coordinates.Normalized);

            if (Gvar is not { } gvar || !gvar.HasVariations(glyph))
                return 0;

            int total = GlyphOutlineDecoder.GetVariationPointCount(face, glyph);
            var dx = new double[total];
            var dy = new double[total];
            // The advance is the distance between the first two phantom points, which are the last four points.
            return gvar.TryAddDeltas(glyph, coordinates.Normalized, total, null, null, null, dx, dy) ? dx[total - 3] - dx[total - 4] : 0;
        }

        /// <summary>
        /// How much the vertical advance of <paramref name="glyph"/> differs from its <c>vmtx</c> entry at <paramref name="coordinates"/>, in design
        /// units: from <c>VVAR</c> when the font has it, otherwise from the phantom points of <c>gvar</c>.
        /// </summary>
        internal double GetVerticalAdvanceDelta(OpenTypeFontface face, int glyph, VariationCoordinates coordinates)
        {
            if (Vvar is { } vvar)
                return vvar.GetAdvanceDelta(glyph, coordinates.Normalized);

            if (Gvar is not { } gvar || !gvar.HasVariations(glyph))
                return 0;

            int total = GlyphOutlineDecoder.GetVariationPointCount(face, glyph);
            var dx = new double[total];
            var dy = new double[total];
            // The vertical advance is the distance between the last two phantom points: the top one, where the glyph hangs from, and the bottom one.
            return gvar.TryAddDeltas(glyph, coordinates.Normalized, total, null, null, null, dx, dy) ? dy[total - 2] - dy[total - 1] : 0;
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
                        // Quantized to 1/64 so that a value driven continuously (an animation) cannot make a new location for every step.
                        double quantized = double.IsNaN(value) ? Axes[i].Default : Math.Floor(value * 64 + 0.5) / 64;
                        user[i] = Math.Clamp(quantized, Axes[i].Minimum, Axes[i].Maximum);
                    }
                }
            }

            var normalized = new double[Axes.Length];
            for (int i = 0; i < Axes.Length; i++)
            {
                normalized[i] = NormalizeToRange(i, user[i]);
            }

            // The segment maps (and, at version 2, the cross-axis mapping) of avar; either way the result is rounded to the 2.14 fixed
            // point the tables are written in.
            if (_avar is not null)
            {
                normalized = _avar.Map(normalized);
            }
            else
            {
                for (int i = 0; i < normalized.Length; i++)
                {
                    normalized[i] = AvarTable.Round14(normalized[i]);
                }
            }

            return new VariationCoordinates(user, normalized, tags);
        }

        /// <summary>The value of an axis as a fraction of its range on either side of the default, -1 to 1 (before <c>avar</c>).</summary>
        private double NormalizeToRange(int axis, double value)
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

            return Math.Clamp(n, -1, 1);
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
