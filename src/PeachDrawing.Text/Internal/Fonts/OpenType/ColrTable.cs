#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `COLR` (Color) table, versions 0 and 1.
//
//   - v0 exposes, per base glyph, an ordered list of (layer glyph, palette
//     entry) pairs painted bottom-to-top.
//   - v1 exposes, per base glyph, a "paint graph" (gradients, transforms,
//     glyph clips, compositing) parsed lazily into the ColorPaint model below.
//
// Variable paints (PaintVar*, VarColorLine, VarAffine2x3 and the variable clip
// boxes) are read at a location of a variable font: the deltas that the
// table's ItemVariationStore gives for each value are added to it as the
// paint is parsed, so the ColorPaint nodes carry the numbers that apply at
// the location and nothing downstream needs to know about variations.
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/colr
//
#endregion

using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    // ---- Table reader --------------------------------------------------------------------------

    internal sealed class ColrTable
    {
        private const int MaxPaintDepth = 64;

        /// <summary>How many parsed paints (across every location) the table keeps: a font cannot make it hold more than this, whatever locations are asked for.</summary>
        private const int MaxCachedPaints = 1 << 17;

        private readonly OpenTypeFontface _face;

        // v0
        private readonly Dictionary<int, (int First, int Count)> _baseGlyphRecords = [];
        private readonly (int Gid, int PaletteIndex)[] _layerRecords;

        // v1
        private readonly Dictionary<int, int>? _v1BaseGlyphPaintOffsets;
        private readonly int[]? _v1LayerPaintOffsets;
        private readonly Dictionary<(string Location, int Offset), ColorPaint?> _paintCache = [];
        private readonly Dictionary<(string Location, int Offset), ColorLine> _lineCache = [];

        // v1 variations and clip boxes
        private readonly ColrVariations? _variations;
        private readonly ReadOnlyMemory<byte> _bytes;
        private readonly (int First, int Last, int Offset)[] _clipRecords = [];

        public int Version { get; }

        public ColrTable(OpenTypeFontface face)
        {
            _face = face;
            var entry = face.TableDictionary[TableTagNames.Colr];
            int tableStart = entry.Offset;
            long tableLength = entry.Length;

            face.Position = tableStart;
            Version = face.ReadUShort();
            int numBaseGlyphRecords = face.ReadUShort();
            uint baseGlyphRecordsOffset = face.ReadULong();
            uint layerRecordsOffset = face.ReadULong();
            int numLayerRecords = face.ReadUShort();

            _layerRecords = new (int, int)[numLayerRecords];

            // A count or an offset the table cannot hold is a damaged table: what it names is left out, and the rest of the table is used.
            // Every record is read through the face's cursor, so the reads are guarded as a whole too.
            try
            {
                if (numBaseGlyphRecords > 0 && baseGlyphRecordsOffset != 0 && baseGlyphRecordsOffset + (long)numBaseGlyphRecords * 6 <= tableLength)
                {
                    face.Position = tableStart + (int)baseGlyphRecordsOffset;
                    for (int i = 0; i < numBaseGlyphRecords; i++)
                    {
                        int gid = face.ReadUShort();
                        int first = face.ReadUShort();
                        int count = face.ReadUShort();
                        _baseGlyphRecords[gid] = (first, count);
                    }
                }

                if (numLayerRecords > 0 && layerRecordsOffset != 0 && layerRecordsOffset + (long)numLayerRecords * 4 <= tableLength)
                {
                    face.Position = tableStart + (int)layerRecordsOffset;
                    for (int i = 0; i < numLayerRecords; i++)
                    {
                        int gid = face.ReadUShort();
                        int paletteIndex = face.ReadUShort();
                        _layerRecords[i] = (gid, paletteIndex);
                    }
                }

                if (Version >= 1 && tableLength >= 34)
                {
                    face.Position = tableStart + 14; // skip the v0 header
                    uint baseGlyphListOffset = face.ReadULong();
                    uint layerListOffset = face.ReadULong();
                    uint clipListOffset = face.ReadULong();
                    uint varIndexMapOffset = face.ReadULong();
                    uint itemVariationStoreOffset = face.ReadULong();

                    if (baseGlyphListOffset != 0 && baseGlyphListOffset + 4L <= tableLength)
                    {
                        int listStart = tableStart + (int)baseGlyphListOffset;
                        face.Position = listStart;
                        uint numRecords = face.ReadULong();
                        if (baseGlyphListOffset + 4L + numRecords * 6L <= tableLength)
                        {
                            var offsets = new Dictionary<int, int>((int)numRecords);
                            for (uint i = 0; i < numRecords; i++)
                            {
                                int gid = face.ReadUShort();
                                uint paintOffset = face.ReadULong();
                                offsets[gid] = listStart + (int)paintOffset;
                            }

                            _v1BaseGlyphPaintOffsets = offsets;
                        }
                    }

                    if (layerListOffset != 0 && layerListOffset + 4L <= tableLength)
                    {
                        int listStart = tableStart + (int)layerListOffset;
                        face.Position = listStart;
                        uint numLayers = face.ReadULong();
                        if (layerListOffset + 4L + numLayers * 4L <= tableLength)
                        {
                            var offsets = new int[numLayers];
                            for (uint i = 0; i < numLayers; i++)
                                offsets[i] = listStart + (int)face.ReadULong();

                            _v1LayerPaintOffsets = offsets;
                        }
                    }

                    // The clip list and the variation data are read from the table's bytes, not through the face's cursor, and are ignored
                    // when malformed (the variable paints are then read at their defaults).
                    var bytes = face.FontSource.Bytes;
                    int length = Math.Max(0, Math.Min(entry.Length, bytes.Length - tableStart));
                    _bytes = bytes.AsMemory(tableStart, length);
                    _variations = ColrVariations.TryParse(_bytes.Span, varIndexMapOffset, itemVariationStoreOffset);
                    _clipRecords = ReadClipRecords(_bytes.Span, clipListOffset);
                }
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                // What was read before the damage stays; the rest is left out.
            }
        }

        /// <summary>True if this glyph has any color definition (v0 layers or a v1 paint).</summary>
        public bool HasColorGlyph(int glyphId)
            => _baseGlyphRecords.ContainsKey(glyphId)
               || (_v1BaseGlyphPaintOffsets?.ContainsKey(glyphId) ?? false);

        /// <summary>Resolves a v0 base glyph's ordered (layer glyph, palette entry) layers.</summary>
        public bool TryGetV0Layers(int glyphId, out ColorLayer[] layers)
        {
            layers = null!;
            if (!_baseGlyphRecords.TryGetValue(glyphId, out var record) || record.Count <= 0)
                return false;

            var found = new List<ColorLayer>(record.Count);
            for (int i = 0; i < record.Count; i++)
            {
                int index = record.First + i;
                if (index >= 0 && index < _layerRecords.Length)
                    found.Add(new ColorLayer(_layerRecords[index].Gid, _layerRecords[index].PaletteIndex));
            }

            layers = found.ToArray();
            return true;
        }

        /// <summary>The root paint of a v1 color glyph at <paramref name="variation"/> (<see langword="null"/> for the default location), or null if it has none.</summary>
        public ColorPaint? GetV1BaseGlyphPaint(int glyphId, VariationCoordinates? variation = null)
        {
            if (_v1BaseGlyphPaintOffsets is null || !_v1BaseGlyphPaintOffsets.TryGetValue(glyphId, out int offset))
                return null;
            // ParsePaint decodes on demand through this fontface's single shared read cursor (and
            // _paintCache is a plain, non-concurrent Dictionary) - see OpenTypeFontface.SyncRoot. Locked
            // at this entry point rather than inside ParsePaint itself so one whole (possibly recursive,
            // for nested layers) paint-graph read is atomic, not just each individual step of it.
            lock (_face.SyncRoot)
            {
                return ParseGuarded(offset, variation);
            }
        }

        /// <summary>The paint at a LayerList index (used by PaintColrLayers) at <paramref name="variation"/>.</summary>
        public ColorPaint? GetLayerPaint(int index, VariationCoordinates? variation = null)
        {
            if (_v1LayerPaintOffsets is null || index < 0 || index >= _v1LayerPaintOffsets.Length)
                return null;
            lock (_face.SyncRoot)
            {
                return ParseGuarded(_v1LayerPaintOffsets[index], variation);
            }
        }

        /// <summary>Parses the paint at <paramref name="offset"/>; a paint that reaches past the end of the font is a damaged one, and is no paint.</summary>
        private ColorPaint? ParseGuarded(int offset, VariationCoordinates? variation)
        {
            try
            {
                return ParsePaint(offset, [], 0, new Location(this, variation));
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        // ---- Clip boxes ------------------------------------------------------------------------

        private static (int First, int Last, int Offset)[] ReadClipRecords(ReadOnlySpan<byte> table, uint clipListOffset)
        {
            if (clipListOffset == 0 || clipListOffset >= table.Length || table[(int)clipListOffset] != 1)
                return [];

            int list = (int)clipListOffset;
            uint count = BigEndian.U32(table, list + 1);

            // A count the table is too short to hold is a damaged table; nothing is allocated for it.
            if ((long)count * 7 > table.Length - list - 5)
                return [];

            var records = new (int, int, int)[count];
            for (int i = 0; i < records.Length; i++)
            {
                int at = list + 5 + i * 7;
                int offset24 = (table[at + 4] << 16) | (table[at + 5] << 8) | table[at + 6];
                records[i] = (BigEndian.U16(table, at), BigEndian.U16(table, at + 2), list + offset24);
            }

            return records;
        }

        /// <summary>
        /// The clip box of a v1 colour glyph at <paramref name="variation"/>: a rectangle that contains everything the glyph paints. False when
        /// the font gives the glyph none.
        /// </summary>
        public bool TryGetClipBox(int glyphId, VariationCoordinates? variation, out ColorClipBox box)
        {
            box = default;

            // The records are sorted by glyph and do not overlap.
            int low = 0, high = _clipRecords.Length - 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                var (first, last, offset) = _clipRecords[mid];
                if (glyphId < first)
                    high = mid - 1;
                else if (glyphId > last)
                    low = mid + 1;
                else
                    return TryReadClipBox(offset, variation, out box);
            }

            return false;
        }

        private bool TryReadClipBox(int offset, VariationCoordinates? variation, out ColorClipBox box)
        {
            box = default;
            var table = _bytes.Span;
            try
            {
                int format = table[offset];
                if (format is not (1 or 2))
                    return false;

                double xMin = BigEndian.I16(table, offset + 1), yMin = BigEndian.I16(table, offset + 3);
                double xMax = BigEndian.I16(table, offset + 5), yMax = BigEndian.I16(table, offset + 7);
                if (format == 2 && variation is not null && _variations is { } variations)
                {
                    uint varIndexBase = BigEndian.U32(table, offset + 9);
                    var coordinates = variation.Normalized;
                    xMin += variations.GetDelta(varIndexBase, 0, coordinates);
                    yMin += variations.GetDelta(varIndexBase, 1, coordinates);
                    xMax += variations.GetDelta(varIndexBase, 2, coordinates);
                    yMax += variations.GetDelta(varIndexBase, 3, coordinates);
                }

                box = new ColorClipBox(xMin, yMin, xMax, yMax);
                return true;
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return false;
            }
        }

        // ---- Paint parsing ---------------------------------------------------------------------

        /// <summary>The location a paint graph is read at, and the deltas it has there for a variable value.</summary>
        private readonly struct Location
        {
            private readonly ColrVariations? _variations;
            private readonly double[]? _coordinates;

            public Location(ColrTable table, VariationCoordinates? variation)
            {
                // At the default every delta is 0, so the default is read as a font without variations is.
                bool varies = variation is { IsDefault: false } && table._variations is not null;
                _variations = varies ? table._variations : null;
                _coordinates = varies ? variation!.Normalized : null;
                Key = varies ? variation!.Key : "";
            }

            /// <summary>Names the location for the cache of parsed paints.</summary>
            public string Key { get; }

            /// <summary>How much the value at variation index <paramref name="varIndexBase"/> plus <paramref name="field"/> changes here, in the units it is written in.</summary>
            public double Delta(uint varIndexBase, int field) => _variations is null ? 0 : _variations.GetDelta(varIndexBase, field, _coordinates);

            /// <summary>An FWord value with its delta.</summary>
            public double Units(double value, uint varIndexBase, int field) => value + Delta(varIndexBase, field);

            /// <summary>A UFWord value (a radius) with its delta, which cannot take it below 0.</summary>
            public double UnsignedUnits(double value, uint varIndexBase, int field)
            {
                double delta = Delta(varIndexBase, field);
                return delta == 0 ? value : Math.Max(0, value + delta);
            }

            /// <summary>A 2.14 fixed-point value with its delta (the delta is in units of 1/16384).</summary>
            public double Fixed14(double value, uint varIndexBase, int field) => value + Delta(varIndexBase, field) / 16384.0;

            /// <summary>A 16.16 fixed-point value with its delta (the delta is in units of 1/65536).</summary>
            public double Fixed16(double value, uint varIndexBase, int field) => value + Delta(varIndexBase, field) / 65536.0;

            /// <summary>An opacity (a 2.14 fixed-point value) with its delta, kept between 0 and 1 where the delta moves it.</summary>
            public double Alpha(double value, uint varIndexBase, int field)
            {
                double delta = Delta(varIndexBase, field);
                return delta == 0 ? value : Math.Clamp(value + delta / 16384.0, 0, 1);
            }

            /// <summary>An angle written as a 2.14 fixed-point number of half turns, with its delta, in radians.</summary>
            public double Angle(double halfTurns, uint varIndexBase, int field) => Fixed14(halfTurns, varIndexBase, field) * Math.PI;
        }

        private ColorPaint? ParsePaint(int offset, HashSet<int> visiting, int depth, Location location)
        {
            if (offset <= 0 || depth > MaxPaintDepth)
                return null;
            if (_paintCache.TryGetValue((location.Key, offset), out ColorPaint? cached))
                return cached;
            if (!visiting.Add(offset))
                return null; // cycle

            ColorPaint? result = ParsePaintCore(offset, visiting, depth, location);

            visiting.Remove(offset);
            if (_paintCache.Count >= MaxCachedPaints)
                _paintCache.Clear();
            _paintCache[(location.Key, offset)] = result;
            return result;
        }

        private ColorPaint? ParsePaintCore(int offset, HashSet<int> visiting, int depth, Location at)
        {
            _face.Position = offset;
            int format = _face.ReadByte();

            switch (format)
            {
                case 1: // PaintColrLayers
                {
                    int numLayers = _face.ReadByte();
                    int firstLayerIndex = (int)_face.ReadULong();
                    return new PaintColrLayers { FirstLayerIndex = firstLayerIndex, NumLayers = numLayers };
                }
                case 2: // PaintSolid
                case 3: // PaintVarSolid
                {
                    int paletteIndex = _face.ReadUShort();
                    double alpha = ReadF2Dot14();
                    if (format == 3)
                    {
                        uint varIndexBase = _face.ReadULong();
                        alpha = at.Alpha(alpha, varIndexBase, 0);
                    }

                    return new PaintSolid { PaletteIndex = paletteIndex, Alpha = alpha };
                }
                case 4: // PaintLinearGradient
                case 5: // PaintVarLinearGradient
                {
                    int lineOffset = ReadOffset24();
                    double x0 = _face.ReadShort(), y0 = _face.ReadShort();
                    double x1 = _face.ReadShort(), y1 = _face.ReadShort();
                    double x2 = _face.ReadShort(), y2 = _face.ReadShort();
                    if (format == 5)
                    {
                        uint b = _face.ReadULong();
                        (x0, y0, x1, y1, x2, y2) = (at.Units(x0, b, 0), at.Units(y0, b, 1), at.Units(x1, b, 2), at.Units(y1, b, 3), at.Units(x2, b, 4), at.Units(y2, b, 5));
                    }

                    ColorLine line = ReadColorLine(offset + lineOffset, format == 5, at);
                    return new PaintLinearGradient { Line = line, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
                }
                case 6: // PaintRadialGradient
                case 7: // PaintVarRadialGradient
                {
                    int lineOffset = ReadOffset24();
                    double x0 = _face.ReadShort(), y0 = _face.ReadShort();
                    double r0 = _face.ReadUShort();
                    double x1 = _face.ReadShort(), y1 = _face.ReadShort();
                    double r1 = _face.ReadUShort();
                    if (format == 7)
                    {
                        uint b = _face.ReadULong();
                        (x0, y0, r0, x1, y1, r1) = (at.Units(x0, b, 0), at.Units(y0, b, 1), at.UnsignedUnits(r0, b, 2), at.Units(x1, b, 3), at.Units(y1, b, 4), at.UnsignedUnits(r1, b, 5));
                    }

                    ColorLine line = ReadColorLine(offset + lineOffset, format == 7, at);
                    return new PaintRadialGradient { Line = line, X0 = x0, Y0 = y0, R0 = r0, X1 = x1, Y1 = y1, R1 = r1 };
                }
                case 8: // PaintSweepGradient
                case 9: // PaintVarSweepGradient
                {
                    int lineOffset = ReadOffset24();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    // The angles of a sweep gradient are stored with a half turn taken off (so that a full turn, +360 degrees, fits in the
                    // 2.14 range): the angle is the stored value plus 1, in half turns. Reading it as stored turns the gradient by 180 degrees.
                    double startAngle = ReadF2Dot14() + 1;
                    double endAngle = ReadF2Dot14() + 1;
                    if (format == 9)
                    {
                        uint b = _face.ReadULong();
                        (cx, cy) = (at.Units(cx, b, 0), at.Units(cy, b, 1));
                        startAngle = at.Angle(startAngle, b, 2);
                        endAngle = at.Angle(endAngle, b, 3);
                    }
                    else
                    {
                        startAngle *= Math.PI;
                        endAngle *= Math.PI;
                    }

                    ColorLine line = ReadColorLine(offset + lineOffset, format == 9, at);
                    return new PaintSweepGradient { Line = line, CenterX = cx, CenterY = cy, StartAngle = startAngle, EndAngle = endAngle };
                }
                case 10: // PaintGlyph
                {
                    int paintOffset = ReadOffset24();
                    int glyphId = _face.ReadUShort();
                    ColorPaint? child = ParsePaint(offset + paintOffset, visiting, depth + 1, at);
                    return new PaintGlyph { GlyphId = glyphId, Paint = child };
                }
                case 11: // PaintColrGlyph
                {
                    int glyphId = _face.ReadUShort();
                    return new PaintColrGlyph { GlyphId = glyphId };
                }
                case 12: // PaintTransform
                case 13: // PaintVarTransform
                {
                    int paintOffset = ReadOffset24();
                    int transformOffset = ReadOffset24();
                    Affine2x3 affine = ReadAffine(offset + transformOffset, format == 13, at);
                    return WrapTransform(affine, offset + paintOffset, visiting, depth, at);
                }
                case 14: // PaintTranslate
                case 15: // PaintVarTranslate
                {
                    int paintOffset = ReadOffset24();
                    double dx = _face.ReadShort(), dy = _face.ReadShort();
                    if (format == 15)
                    {
                        uint b = _face.ReadULong();
                        (dx, dy) = (at.Units(dx, b, 0), at.Units(dy, b, 1));
                    }

                    return WrapTransform(new Affine2x3(1, 0, 0, 1, dx, dy), offset + paintOffset, visiting, depth, at);
                }
                case 16: // PaintScale
                case 17: // PaintVarScale
                {
                    int paintOffset = ReadOffset24();
                    double sx = ReadF2Dot14(), sy = ReadF2Dot14();
                    if (format == 17)
                    {
                        uint b = _face.ReadULong();
                        (sx, sy) = (at.Fixed14(sx, b, 0), at.Fixed14(sy, b, 1));
                    }

                    return WrapTransform(new Affine2x3(sx, 0, 0, sy, 0, 0), offset + paintOffset, visiting, depth, at);
                }
                case 18: // PaintScaleAroundCenter
                case 19: // PaintVarScaleAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    double sx = ReadF2Dot14(), sy = ReadF2Dot14();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    if (format == 19)
                    {
                        uint b = _face.ReadULong();
                        (sx, sy, cx, cy) = (at.Fixed14(sx, b, 0), at.Fixed14(sy, b, 1), at.Units(cx, b, 2), at.Units(cy, b, 3));
                    }

                    return WrapTransform(AroundCenter(new Affine2x3(sx, 0, 0, sy, 0, 0), cx, cy), offset + paintOffset, visiting, depth, at);
                }
                case 20: // PaintScaleUniform
                case 21: // PaintVarScaleUniform
                {
                    int paintOffset = ReadOffset24();
                    double s = ReadF2Dot14();
                    if (format == 21)
                        s = at.Fixed14(s, _face.ReadULong(), 0);

                    return WrapTransform(new Affine2x3(s, 0, 0, s, 0, 0), offset + paintOffset, visiting, depth, at);
                }
                case 22: // PaintScaleUniformAroundCenter
                case 23: // PaintVarScaleUniformAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    double s = ReadF2Dot14();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    if (format == 23)
                    {
                        uint b = _face.ReadULong();
                        (s, cx, cy) = (at.Fixed14(s, b, 0), at.Units(cx, b, 1), at.Units(cy, b, 2));
                    }

                    return WrapTransform(AroundCenter(new Affine2x3(s, 0, 0, s, 0, 0), cx, cy), offset + paintOffset, visiting, depth, at);
                }
                case 24: // PaintRotate
                case 25: // PaintVarRotate
                {
                    int paintOffset = ReadOffset24();
                    double angle = ReadF2Dot14();
                    angle = format == 25 ? at.Angle(angle, _face.ReadULong(), 0) : angle * Math.PI;
                    return WrapTransform(Rotation(angle), offset + paintOffset, visiting, depth, at);
                }
                case 26: // PaintRotateAroundCenter
                case 27: // PaintVarRotateAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    double angle = ReadF2Dot14();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    if (format == 27)
                    {
                        uint b = _face.ReadULong();
                        (angle, cx, cy) = (at.Angle(angle, b, 0), at.Units(cx, b, 1), at.Units(cy, b, 2));
                    }
                    else
                    {
                        angle *= Math.PI;
                    }

                    return WrapTransform(AroundCenter(Rotation(angle), cx, cy), offset + paintOffset, visiting, depth, at);
                }
                case 28: // PaintSkew
                case 29: // PaintVarSkew
                {
                    int paintOffset = ReadOffset24();
                    double xSkew = ReadF2Dot14(), ySkew = ReadF2Dot14();
                    if (format == 29)
                    {
                        uint b = _face.ReadULong();
                        (xSkew, ySkew) = (at.Angle(xSkew, b, 0), at.Angle(ySkew, b, 1));
                    }
                    else
                    {
                        (xSkew, ySkew) = (xSkew * Math.PI, ySkew * Math.PI);
                    }

                    return WrapTransform(Skew(xSkew, ySkew), offset + paintOffset, visiting, depth, at);
                }
                case 30: // PaintSkewAroundCenter
                case 31: // PaintVarSkewAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    double xSkew = ReadF2Dot14(), ySkew = ReadF2Dot14();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    if (format == 31)
                    {
                        uint b = _face.ReadULong();
                        (xSkew, ySkew, cx, cy) = (at.Angle(xSkew, b, 0), at.Angle(ySkew, b, 1), at.Units(cx, b, 2), at.Units(cy, b, 3));
                    }
                    else
                    {
                        (xSkew, ySkew) = (xSkew * Math.PI, ySkew * Math.PI);
                    }

                    return WrapTransform(AroundCenter(Skew(xSkew, ySkew), cx, cy), offset + paintOffset, visiting, depth, at);
                }
                case 32: // PaintComposite
                {
                    int sourceOffset = ReadOffset24();
                    int mode = _face.ReadByte();
                    int backdropOffset = ReadOffset24();
                    ColorPaint? source = ParsePaint(offset + sourceOffset, visiting, depth + 1, at);
                    ColorPaint? backdrop = ParsePaint(offset + backdropOffset, visiting, depth + 1, at);
                    return new PaintComposite { Source = source, Mode = mode, Backdrop = backdrop };
                }
                default:
                    return null; // unknown/unsupported paint format
            }
        }

        private ColorPaint WrapTransform(Affine2x3 affine, int childOffset, HashSet<int> visiting, int depth, Location at)
            => new PaintTransform { Affine = affine, Paint = ParsePaint(childOffset, visiting, depth + 1, at) };

        private ColorLine ReadColorLine(int offset, bool isVariable, Location at)
        {
            // Many paints may name one colour line, and a line can have tens of thousands of stops: it is read once for each location.
            var key = (at.Key, isVariable ? -offset : offset);
            if (_lineCache.TryGetValue(key, out var cached))
                return cached;

            _face.Position = offset;
            var extend = (ColorExtend)_face.ReadByte();
            int numStops = _face.ReadUShort();
            var line = new ColorLine { Extend = extend };
            bool sorted = true;
            bool moved = false;
            for (int i = 0; i < numStops; i++)
            {
                double stopOffset = ReadF2Dot14();
                int paletteIndex = _face.ReadUShort();
                double alpha = ReadF2Dot14();
                if (isVariable)
                {
                    uint b = _face.ReadULong();
                    double delta = at.Delta(b, 0);
                    if (delta != 0)
                    {
                        stopOffset += delta / 16384.0;
                        moved = true;
                    }

                    alpha = at.Alpha(alpha, b, 1);
                }

                sorted &= line.StopList.Count == 0 || line.StopList[^1].Offset <= stopOffset;
                line.StopList.Add(new ColorStop(stopOffset, paletteIndex, alpha));
            }

            // A delta can move a stop past its neighbour, and a colour line is defined with its stops in order. Only where a delta moved a
            // stop is the order restored (a font's own unsorted line is left as it is), and stably, so that the stops of a hard edge, which
            // share an offset, keep their order.
            if (moved && !sorted)
            {
                var ordered = line.StopList.OrderBy(static s => s.Offset).ToList();
                line.StopList.Clear();
                line.StopList.AddRange(ordered);
            }

            if (_lineCache.Count >= MaxCachedPaints)
                _lineCache.Clear();
            _lineCache[key] = line;
            return line;
        }
        private Affine2x3 ReadAffine(int offset, bool isVariable, Location at)
        {
            _face.Position = offset;
            double xx = ReadFixed(), yx = ReadFixed(), xy = ReadFixed(), yy = ReadFixed(), dx = ReadFixed(), dy = ReadFixed();
            if (isVariable)
            {
                uint b = _face.ReadULong();
                (xx, yx, xy, yy, dx, dy) = (at.Fixed16(xx, b, 0), at.Fixed16(yx, b, 1), at.Fixed16(xy, b, 2), at.Fixed16(yy, b, 3), at.Fixed16(dx, b, 4), at.Fixed16(dy, b, 5));
            }

            return new Affine2x3(xx, yx, xy, yy, dx, dy);
        }

        private static Affine2x3 Rotation(double radians)
        {
            double cos = Math.Cos(radians), sin = Math.Sin(radians);
            return new Affine2x3(cos, sin, -sin, cos, 0, 0);
        }

        private static Affine2x3 Skew(double xSkewRadians, double ySkewRadians)
        {
            // COLR PaintSkew: x' = x - tan(xSkew)·y, y' = y + tan(ySkew)·x.
            // In Affine2x3 (XX, YX, XY, YY, DX, DY): XY = -tan(xSkew), YX = +tan(ySkew).
            return new Affine2x3(1, Math.Tan(ySkewRadians), -Math.Tan(xSkewRadians), 1, 0, 0);
        }

        private static Affine2x3 AroundCenter(Affine2x3 m, double cx, double cy)
        {
            var toCenter = new Affine2x3(1, 0, 0, 1, cx, cy);
            var fromCenter = new Affine2x3(1, 0, 0, 1, -cx, -cy);
            return Affine2x3.Multiply(Affine2x3.Multiply(toCenter, m), fromCenter);
        }

        private int ReadOffset24()
        {
            int b0 = _face.ReadByte();
            int b1 = _face.ReadByte();
            int b2 = _face.ReadByte();
            return (b0 << 16) | (b1 << 8) | b2;
        }

        private double ReadF2Dot14() => _face.ReadShort() / 16384.0;
        private double ReadFixed() => _face.ReadLong() / 65536.0;
    }
}
