#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `BASE` (baseline) table: for each of the horizontal and vertical axes, the
// coordinate of each named baseline (romn, ideo, hang, math, icfb, icft, ...) per script. Everything is read
// eagerly (the table is small) into dictionaries, in design units, under the font reader's own lock, since
// the reader's cursor is shared. Not read: the per-script/language MinMax extents, and the version 1.1
// item variation store (variable-font baseline deltas).
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/base
//
#endregion

using PeachDrawing.Text.Internal.Fonts.OpenType;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.OpenType
{
    /// <summary>
    /// The <c>BASE</c> table of a font: where the font puts each named baseline (<c>romn</c>, <c>ideo</c>, <c>hang</c>, <c>math</c>, <c>icfb</c>, <c>icft</c>,
    /// ...) for each script, on the horizontal axis (the baselines of horizontal text, a y coordinate) and on the vertical axis (the baselines of vertical text,
    /// an x coordinate). Values are design units in the font's own coordinate system, positive up (or, on the vertical axis, to the right).
    /// </summary>
    public sealed class BaselineTable
    {
        private readonly Dictionary<string, Dictionary<string, short>>? _horizontal;
        private readonly Dictionary<string, Dictionary<string, short>>? _vertical;

        internal BaselineTable(OpenTypeFontface face, int tableStart)
        {
            lock (face.SyncRoot)
            {
                face.Position = tableStart;
                face.ReadUShort(); // majorVersion
                face.ReadUShort(); // minorVersion
                int horizontalOffset = face.ReadUShort();
                int verticalOffset = face.ReadUShort();

                _horizontal = horizontalOffset == 0 ? null : ReadAxis(face, tableStart + horizontalOffset);
                _vertical = verticalOffset == 0 ? null : ReadAxis(face, tableStart + verticalOffset);
            }
        }

        /// <summary>Whether the table names any baseline for the given axis.</summary>
        /// <param name="vertical"><see langword="true"/> for the vertical axis, <see langword="false"/> for the horizontal one.</param>
        public bool HasAxis(bool vertical) => (vertical ? _vertical : _horizontal) is { Count: > 0 };

        /// <summary>
        /// Looks up where the font puts a baseline.
        /// </summary>
        /// <param name="vertical"><see langword="true"/> for the vertical axis, <see langword="false"/> for the horizontal one.</param>
        /// <param name="scriptTag">The OpenType script tag (for example <c>latn</c> or <c>hani</c>). A script the table does not list falls back to <c>DFLT</c>, then <c>latn</c>; <see langword="null"/> uses that same fallback.</param>
        /// <param name="baselineTag">The baseline's four-character tag, such as <c>romn</c>, <c>ideo</c>, <c>hang</c>, <c>math</c>, <c>icfb</c> or <c>icft</c>.</param>
        /// <param name="designUnits">The baseline's coordinate in design units, when this returns <see langword="true"/>.</param>
        /// <returns><see langword="true"/> when the table gives a coordinate for the baseline.</returns>
        public bool TryGetBaseline(bool vertical, string? scriptTag, string baselineTag, out double designUnits)
        {
            ArgumentNullException.ThrowIfNull(baselineTag);

            designUnits = 0;
            if ((vertical ? _vertical : _horizontal) is not { } scripts)
                return false;

            Dictionary<string, short>? values = null;
            if ((scriptTag is null || !scripts.TryGetValue(scriptTag, out values))
                && !scripts.TryGetValue("DFLT", out values)
                && !scripts.TryGetValue("latn", out values))
                return false;

            if (!values.TryGetValue(baselineTag, out var coordinate))
                return false;

            designUnits = coordinate;
            return true;
        }

        private static Dictionary<string, Dictionary<string, short>>? ReadAxis(OpenTypeFontface face, int axisStart)
        {
            face.Position = axisStart;
            int tagListOffset = face.ReadUShort();
            int scriptListOffset = face.ReadUShort();
            if (tagListOffset == 0 || scriptListOffset == 0)
                return null;

            face.Position = axisStart + tagListOffset;
            int tagCount = face.ReadUShort();
            var tags = new string[tagCount];
            for (var i = 0; i < tagCount; i++)
                tags[i] = face.ReadTag();

            var scriptListStart = axisStart + scriptListOffset;
            face.Position = scriptListStart;
            int scriptCount = face.ReadUShort();
            var records = new (string Tag, int Offset)[scriptCount];
            for (var i = 0; i < scriptCount; i++)
                records[i] = (face.ReadTag(), face.ReadUShort());

            var result = new Dictionary<string, Dictionary<string, short>>(scriptCount, StringComparer.Ordinal);
            foreach (var (scriptTag, scriptOffset) in records)
            {
                var scriptStart = scriptListStart + scriptOffset;
                face.Position = scriptStart;
                int baseValuesOffset = face.ReadUShort();
                if (baseValuesOffset == 0)
                    continue;

                var baseValuesStart = scriptStart + baseValuesOffset;
                face.Position = baseValuesStart;
                face.ReadUShort(); // defaultBaselineIndex
                int coordCount = face.ReadUShort();
                var coordOffsets = new int[coordCount];
                for (var i = 0; i < coordCount; i++)
                    coordOffsets[i] = face.ReadUShort();

                var values = new Dictionary<string, short>(Math.Min(coordCount, tagCount), StringComparer.Ordinal);
                for (var i = 0; i < coordCount && i < tagCount; i++)
                {
                    // BaseCoord formats 1, 2 and 3 all open with the format and the coordinate itself.
                    face.Position = baseValuesStart + coordOffsets[i];
                    face.ReadUShort(); // format
                    values[tags[i]] = face.ReadShort();
                }

                result[scriptTag] = values;
            }

            return result;
        }
    }
}
