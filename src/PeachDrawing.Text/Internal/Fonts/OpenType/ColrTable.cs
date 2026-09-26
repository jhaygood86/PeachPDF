#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `COLR` (Color) table, versions 0 and 1.
//
//   - v0 exposes, per base glyph, an ordered list of (layer glyph, palette
//     entry) pairs painted bottom-to-top.
//   - v1 exposes, per base glyph, a "paint graph" (gradients, transforms,
//     glyph clips, compositing) parsed lazily into the ColorPaint model below.
//
// Variable paints (PaintVar*) are read at their default instance (variation
// deltas ignored - PeachPDF has no variable-font instancing).
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/colr
//
#endregion

using PeachDrawing.Text.Outlines;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    // ---- Table reader --------------------------------------------------------------------------

    internal sealed class ColrTable
    {
        private const int MaxPaintDepth = 64;

        private readonly OpenTypeFontface _face;

        // v0
        private readonly Dictionary<int, (int First, int Count)> _baseGlyphRecords = [];
        private readonly (int Gid, int PaletteIndex)[] _layerRecords;

        // v1
        private readonly Dictionary<int, int>? _v1BaseGlyphPaintOffsets;
        private readonly int[]? _v1LayerPaintOffsets;
        private readonly Dictionary<int, ColorPaint?> _paintCache = [];

        public int Version { get; }

        public ColrTable(OpenTypeFontface face)
        {
            _face = face;
            int tableStart = face.TableDictionary[TableTagNames.Colr].Offset;

            face.Position = tableStart;
            Version = face.ReadUShort();
            int numBaseGlyphRecords = face.ReadUShort();
            uint baseGlyphRecordsOffset = face.ReadULong();
            uint layerRecordsOffset = face.ReadULong();
            int numLayerRecords = face.ReadUShort();

            if (numBaseGlyphRecords > 0 && baseGlyphRecordsOffset != 0)
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

            _layerRecords = new (int, int)[numLayerRecords];
            if (numLayerRecords > 0 && layerRecordsOffset != 0)
            {
                face.Position = tableStart + (int)layerRecordsOffset;
                for (int i = 0; i < numLayerRecords; i++)
                {
                    int gid = face.ReadUShort();
                    int paletteIndex = face.ReadUShort();
                    _layerRecords[i] = (gid, paletteIndex);
                }
            }

            if (Version >= 1)
            {
                face.Position = tableStart + 14; // skip the v0 header
                uint baseGlyphListOffset = face.ReadULong();
                uint layerListOffset = face.ReadULong();
                // clipListOffset, varIndexMapOffset, itemVariationStoreOffset follow - ignored.

                if (baseGlyphListOffset != 0)
                {
                    int listStart = tableStart + (int)baseGlyphListOffset;
                    face.Position = listStart;
                    uint numRecords = face.ReadULong();
                    _v1BaseGlyphPaintOffsets = new Dictionary<int, int>((int)numRecords);
                    for (uint i = 0; i < numRecords; i++)
                    {
                        int gid = face.ReadUShort();
                        uint paintOffset = face.ReadULong();
                        _v1BaseGlyphPaintOffsets[gid] = listStart + (int)paintOffset;
                    }
                }

                if (layerListOffset != 0)
                {
                    int listStart = tableStart + (int)layerListOffset;
                    face.Position = listStart;
                    uint numLayers = face.ReadULong();
                    _v1LayerPaintOffsets = new int[numLayers];
                    for (uint i = 0; i < numLayers; i++)
                        _v1LayerPaintOffsets[i] = listStart + (int)face.ReadULong();
                }
            }
        }

        /// <summary>True if this glyph has any color definition (v0 layers or a v1 paint).</summary>
        public bool HasColorGlyph(int glyphId)
            => _baseGlyphRecords.ContainsKey(glyphId)
               || (_v1BaseGlyphPaintOffsets?.ContainsKey(glyphId) ?? false);

        /// <summary>Resolves a v0 base glyph's ordered (layer glyph, palette entry) layers.</summary>
        public bool TryGetV0Layers(int glyphId, out List<(int LayerGlyphId, int PaletteIndex)> layers)
        {
            layers = null!;
            if (!_baseGlyphRecords.TryGetValue(glyphId, out var record) || record.Count <= 0)
                return false;

            layers = new List<(int, int)>(record.Count);
            for (int i = 0; i < record.Count; i++)
            {
                int index = record.First + i;
                if (index >= 0 && index < _layerRecords.Length)
                    layers.Add(_layerRecords[index]);
            }
            return true;
        }

        /// <summary>The root paint of a v1 color glyph, or null if it has none.</summary>
        public ColorPaint? GetV1BaseGlyphPaint(int glyphId)
        {
            if (_v1BaseGlyphPaintOffsets is null || !_v1BaseGlyphPaintOffsets.TryGetValue(glyphId, out int offset))
                return null;
            // ParsePaint decodes on demand through this fontface's single shared read cursor (and
            // _paintCache is a plain, non-concurrent Dictionary) - see OpenTypeFontface.SyncRoot. Locked
            // at this entry point rather than inside ParsePaint itself so one whole (possibly recursive,
            // for nested layers) paint-graph read is atomic, not just each individual step of it.
            lock (_face.SyncRoot)
            {
                return ParsePaint(offset, [], 0);
            }
        }

        /// <summary>The paint at a LayerList index (used by PaintColrLayers).</summary>
        public ColorPaint? GetLayerPaint(int index)
        {
            if (_v1LayerPaintOffsets is null || index < 0 || index >= _v1LayerPaintOffsets.Length)
                return null;
            lock (_face.SyncRoot)
            {
                return ParsePaint(_v1LayerPaintOffsets[index], [], 0);
            }
        }

        // ---- Paint parsing ---------------------------------------------------------------------

        private ColorPaint? ParsePaint(int offset, HashSet<int> visiting, int depth)
        {
            if (offset <= 0 || depth > MaxPaintDepth)
                return null;
            if (_paintCache.TryGetValue(offset, out ColorPaint? cached))
                return cached;
            if (!visiting.Add(offset))
                return null; // cycle

            ColorPaint? result = ParsePaintCore(offset, visiting, depth);

            visiting.Remove(offset);
            _paintCache[offset] = result;
            return result;
        }

        private ColorPaint? ParsePaintCore(int offset, HashSet<int> visiting, int depth)
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
                    return new PaintSolid { PaletteIndex = paletteIndex, Alpha = alpha };
                }
                case 4: // PaintLinearGradient
                case 5: // PaintVarLinearGradient
                {
                    int lineOffset = ReadOffset24();
                    double x0 = _face.ReadShort(), y0 = _face.ReadShort();
                    double x1 = _face.ReadShort(), y1 = _face.ReadShort();
                    double x2 = _face.ReadShort(), y2 = _face.ReadShort();
                    ColorLine line = ReadColorLine(offset + lineOffset, format == 5);
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
                    ColorLine line = ReadColorLine(offset + lineOffset, format == 7);
                    return new PaintRadialGradient { Line = line, X0 = x0, Y0 = y0, R0 = r0, X1 = x1, Y1 = y1, R1 = r1 };
                }
                case 8: // PaintSweepGradient
                case 9: // PaintVarSweepGradient
                {
                    int lineOffset = ReadOffset24();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    double startAngle = ReadAngle();
                    double endAngle = ReadAngle();
                    ColorLine line = ReadColorLine(offset + lineOffset, format == 9);
                    return new PaintSweepGradient { Line = line, CenterX = cx, CenterY = cy, StartAngle = startAngle, EndAngle = endAngle };
                }
                case 10: // PaintGlyph
                {
                    int paintOffset = ReadOffset24();
                    int glyphId = _face.ReadUShort();
                    ColorPaint? child = ParsePaint(offset + paintOffset, visiting, depth + 1);
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
                    Affine2x3 affine = ReadAffine(offset + transformOffset);
                    return WrapTransform(affine, offset + paintOffset, visiting, depth);
                }
                case 14: // PaintTranslate
                case 15: // PaintVarTranslate
                {
                    int paintOffset = ReadOffset24();
                    double dx = _face.ReadShort(), dy = _face.ReadShort();
                    return WrapTransform(new Affine2x3(1, 0, 0, 1, dx, dy), offset + paintOffset, visiting, depth);
                }
                case 16: // PaintScale
                case 17: // PaintVarScale
                {
                    int paintOffset = ReadOffset24();
                    double sx = ReadF2Dot14(), sy = ReadF2Dot14();
                    return WrapTransform(new Affine2x3(sx, 0, 0, sy, 0, 0), offset + paintOffset, visiting, depth);
                }
                case 18: // PaintScaleAroundCenter
                case 19: // PaintVarScaleAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    double sx = ReadF2Dot14(), sy = ReadF2Dot14();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    return WrapTransform(AroundCenter(new Affine2x3(sx, 0, 0, sy, 0, 0), cx, cy), offset + paintOffset, visiting, depth);
                }
                case 20: // PaintScaleUniform
                case 21: // PaintVarScaleUniform
                {
                    int paintOffset = ReadOffset24();
                    double s = ReadF2Dot14();
                    return WrapTransform(new Affine2x3(s, 0, 0, s, 0, 0), offset + paintOffset, visiting, depth);
                }
                case 22: // PaintScaleUniformAroundCenter
                case 23: // PaintVarScaleUniformAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    double s = ReadF2Dot14();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    return WrapTransform(AroundCenter(new Affine2x3(s, 0, 0, s, 0, 0), cx, cy), offset + paintOffset, visiting, depth);
                }
                case 24: // PaintRotate
                case 25: // PaintVarRotate
                {
                    int paintOffset = ReadOffset24();
                    Affine2x3 rotate = Rotation(ReadAngle());
                    return WrapTransform(rotate, offset + paintOffset, visiting, depth);
                }
                case 26: // PaintRotateAroundCenter
                case 27: // PaintVarRotateAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    Affine2x3 rotate = Rotation(ReadAngle());
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    return WrapTransform(AroundCenter(rotate, cx, cy), offset + paintOffset, visiting, depth);
                }
                case 28: // PaintSkew
                case 29: // PaintVarSkew
                {
                    int paintOffset = ReadOffset24();
                    Affine2x3 skew = Skew(ReadAngle(), ReadAngle());
                    return WrapTransform(skew, offset + paintOffset, visiting, depth);
                }
                case 30: // PaintSkewAroundCenter
                case 31: // PaintVarSkewAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    Affine2x3 skew = Skew(ReadAngle(), ReadAngle());
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    return WrapTransform(AroundCenter(skew, cx, cy), offset + paintOffset, visiting, depth);
                }
                case 32: // PaintComposite
                {
                    int sourceOffset = ReadOffset24();
                    int mode = _face.ReadByte();
                    int backdropOffset = ReadOffset24();
                    ColorPaint? source = ParsePaint(offset + sourceOffset, visiting, depth + 1);
                    ColorPaint? backdrop = ParsePaint(offset + backdropOffset, visiting, depth + 1);
                    return new PaintComposite { Source = source, Mode = mode, Backdrop = backdrop };
                }
                default:
                    return null; // unknown/unsupported paint format
            }
        }

        private ColorPaint WrapTransform(Affine2x3 affine, int childOffset, HashSet<int> visiting, int depth)
            => new PaintTransform { Affine = affine, Paint = ParsePaint(childOffset, visiting, depth + 1) };

        private ColorLine ReadColorLine(int offset, bool isVariable)
        {
            _face.Position = offset;
            var extend = (ColorExtend)_face.ReadByte();
            int numStops = _face.ReadUShort();
            var line = new ColorLine { Extend = extend };
            for (int i = 0; i < numStops; i++)
            {
                double stopOffset = ReadF2Dot14();
                int paletteIndex = _face.ReadUShort();
                double alpha = ReadF2Dot14();
                if (isVariable)
                    _face.ReadULong(); // varIndexBase - ignored
                line.StopList.Add(new ColorStop(stopOffset, paletteIndex, alpha));
            }
            return line;
        }

        private Affine2x3 ReadAffine(int offset)
        {
            _face.Position = offset;
            double xx = ReadFixed(), yx = ReadFixed(), xy = ReadFixed(), yy = ReadFixed(), dx = ReadFixed(), dy = ReadFixed();
            return new Affine2x3(xx, yx, xy, yy, dx, dy);
        }

        private static Affine2x3 Rotation(double radians)
        {
            double cos = System.Math.Cos(radians), sin = System.Math.Sin(radians);
            return new Affine2x3(cos, sin, -sin, cos, 0, 0);
        }

        private static Affine2x3 Skew(double xSkewRadians, double ySkewRadians)
        {
            // COLR PaintSkew: x' = x - tan(xSkew)·y, y' = y + tan(ySkew)·x.
            // In Affine2x3 (XX, YX, XY, YY, DX, DY): XY = -tan(xSkew), YX = +tan(ySkew).
            return new Affine2x3(1, System.Math.Tan(ySkewRadians), -System.Math.Tan(xSkewRadians), 1, 0, 0);
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

        // COLR angles are counter-clockwise degrees encoded as (degrees / 180) in F2Dot14,
        // so radians = value * PI.
        private double ReadAngle() => ReadF2Dot14() * System.Math.PI;
    }
}
