#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `COLR` (Color) table, versions 0 and 1.
//
//   - v0 exposes, per base glyph, an ordered list of (layer glyph, palette
//     entry) pairs painted bottom-to-top.
//   - v1 exposes, per base glyph, a "paint graph" (gradients, transforms,
//     glyph clips, compositing) parsed lazily into the ColrPaint model below.
//
// Variable paints (PaintVar*) are read at their default instance (variation
// deltas ignored - PeachPDF has no variable-font instancing).
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/colr
//
#endregion

using System.Collections.Generic;

namespace PeachPDF.Fonts.OpenType
{
    // ---- Paint graph model (COLR v1) -----------------------------------------------------------

    internal enum ColrExtend { Pad = 0, Repeat = 1, Reflect = 2 }

    internal readonly record struct ColrColorStop(double Offset, int PaletteIndex, double Alpha);

    internal sealed class ColrColorLine
    {
        public ColrExtend Extend { get; init; }
        public List<ColrColorStop> Stops { get; } = [];
    }

    /// <summary>An affine map: x' = XX*x + XY*y + DX, y' = YX*x + YY*y + DY.</summary>
    internal readonly record struct ColrAffine(double XX, double YX, double XY, double YY, double DX, double DY)
    {
        public static readonly ColrAffine Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>Returns a ∘ b (b applied first, then a).</summary>
        public static ColrAffine Multiply(ColrAffine a, ColrAffine b) => new(
            a.XX * b.XX + a.XY * b.YX,
            a.YX * b.XX + a.YY * b.YX,
            a.XX * b.XY + a.XY * b.YY,
            a.YX * b.XY + a.YY * b.YY,
            a.XX * b.DX + a.XY * b.DY + a.DX,
            a.YX * b.DX + a.YY * b.DY + a.DY);
    }

    internal abstract class ColrPaint;

    internal sealed class ColrPaintColrLayers : ColrPaint
    {
        public int FirstLayerIndex { get; init; }
        public int NumLayers { get; init; }
    }

    internal sealed class ColrPaintSolid : ColrPaint
    {
        public int PaletteIndex { get; init; }
        public double Alpha { get; init; }
    }

    internal sealed class ColrPaintLinearGradient : ColrPaint
    {
        public ColrColorLine Line { get; init; } = null!;
        public double X0 { get; init; }
        public double Y0 { get; init; }
        public double X1 { get; init; }
        public double Y1 { get; init; }
        public double X2 { get; init; }
        public double Y2 { get; init; }
    }

    internal sealed class ColrPaintRadialGradient : ColrPaint
    {
        public ColrColorLine Line { get; init; } = null!;
        public double X0 { get; init; }
        public double Y0 { get; init; }
        public double R0 { get; init; }
        public double X1 { get; init; }
        public double Y1 { get; init; }
        public double R1 { get; init; }
    }

    internal sealed class ColrPaintSweepGradient : ColrPaint
    {
        public ColrColorLine Line { get; init; } = null!;
        public double CenterX { get; init; }
        public double CenterY { get; init; }
        public double StartAngle { get; init; } // radians
        public double EndAngle { get; init; }   // radians
    }

    internal sealed class ColrPaintGlyph : ColrPaint
    {
        public int GlyphId { get; init; }
        public ColrPaint? Paint { get; init; }
    }

    internal sealed class ColrPaintColrGlyph : ColrPaint
    {
        public int GlyphId { get; init; }
    }

    internal sealed class ColrPaintTransform : ColrPaint
    {
        public ColrAffine Affine { get; init; }
        public ColrPaint? Paint { get; init; }
    }

    internal sealed class ColrPaintComposite : ColrPaint
    {
        public ColrPaint? Source { get; init; }
        public int Mode { get; init; }
        public ColrPaint? Backdrop { get; init; }
    }

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
        private readonly Dictionary<int, ColrPaint?> _paintCache = [];

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
        public ColrPaint? GetV1BaseGlyphPaint(int glyphId)
        {
            if (_v1BaseGlyphPaintOffsets is null || !_v1BaseGlyphPaintOffsets.TryGetValue(glyphId, out int offset))
                return null;
            return ParsePaint(offset, [], 0);
        }

        /// <summary>The paint at a LayerList index (used by PaintColrLayers).</summary>
        public ColrPaint? GetLayerPaint(int index)
        {
            if (_v1LayerPaintOffsets is null || index < 0 || index >= _v1LayerPaintOffsets.Length)
                return null;
            return ParsePaint(_v1LayerPaintOffsets[index], [], 0);
        }

        // ---- Paint parsing ---------------------------------------------------------------------

        private ColrPaint? ParsePaint(int offset, HashSet<int> visiting, int depth)
        {
            if (offset <= 0 || depth > MaxPaintDepth)
                return null;
            if (_paintCache.TryGetValue(offset, out ColrPaint? cached))
                return cached;
            if (!visiting.Add(offset))
                return null; // cycle

            ColrPaint? result = ParsePaintCore(offset, visiting, depth);

            visiting.Remove(offset);
            _paintCache[offset] = result;
            return result;
        }

        private ColrPaint? ParsePaintCore(int offset, HashSet<int> visiting, int depth)
        {
            _face.Position = offset;
            int format = _face.ReadByte();

            switch (format)
            {
                case 1: // PaintColrLayers
                {
                    int numLayers = _face.ReadByte();
                    int firstLayerIndex = (int)_face.ReadULong();
                    return new ColrPaintColrLayers { FirstLayerIndex = firstLayerIndex, NumLayers = numLayers };
                }
                case 2: // PaintSolid
                case 3: // PaintVarSolid
                {
                    int paletteIndex = _face.ReadUShort();
                    double alpha = ReadF2Dot14();
                    return new ColrPaintSolid { PaletteIndex = paletteIndex, Alpha = alpha };
                }
                case 4: // PaintLinearGradient
                case 5: // PaintVarLinearGradient
                {
                    int lineOffset = ReadOffset24();
                    double x0 = _face.ReadShort(), y0 = _face.ReadShort();
                    double x1 = _face.ReadShort(), y1 = _face.ReadShort();
                    double x2 = _face.ReadShort(), y2 = _face.ReadShort();
                    ColrColorLine line = ReadColorLine(offset + lineOffset, format == 5);
                    return new ColrPaintLinearGradient { Line = line, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
                }
                case 6: // PaintRadialGradient
                case 7: // PaintVarRadialGradient
                {
                    int lineOffset = ReadOffset24();
                    double x0 = _face.ReadShort(), y0 = _face.ReadShort();
                    double r0 = _face.ReadUShort();
                    double x1 = _face.ReadShort(), y1 = _face.ReadShort();
                    double r1 = _face.ReadUShort();
                    ColrColorLine line = ReadColorLine(offset + lineOffset, format == 7);
                    return new ColrPaintRadialGradient { Line = line, X0 = x0, Y0 = y0, R0 = r0, X1 = x1, Y1 = y1, R1 = r1 };
                }
                case 8: // PaintSweepGradient
                case 9: // PaintVarSweepGradient
                {
                    int lineOffset = ReadOffset24();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    double startAngle = ReadAngle();
                    double endAngle = ReadAngle();
                    ColrColorLine line = ReadColorLine(offset + lineOffset, format == 9);
                    return new ColrPaintSweepGradient { Line = line, CenterX = cx, CenterY = cy, StartAngle = startAngle, EndAngle = endAngle };
                }
                case 10: // PaintGlyph
                {
                    int paintOffset = ReadOffset24();
                    int glyphId = _face.ReadUShort();
                    ColrPaint? child = ParsePaint(offset + paintOffset, visiting, depth + 1);
                    return new ColrPaintGlyph { GlyphId = glyphId, Paint = child };
                }
                case 11: // PaintColrGlyph
                {
                    int glyphId = _face.ReadUShort();
                    return new ColrPaintColrGlyph { GlyphId = glyphId };
                }
                case 12: // PaintTransform
                case 13: // PaintVarTransform
                {
                    int paintOffset = ReadOffset24();
                    int transformOffset = ReadOffset24();
                    ColrAffine affine = ReadAffine(offset + transformOffset);
                    return WrapTransform(affine, offset + paintOffset, visiting, depth);
                }
                case 14: // PaintTranslate
                case 15: // PaintVarTranslate
                {
                    int paintOffset = ReadOffset24();
                    double dx = _face.ReadShort(), dy = _face.ReadShort();
                    return WrapTransform(new ColrAffine(1, 0, 0, 1, dx, dy), offset + paintOffset, visiting, depth);
                }
                case 16: // PaintScale
                case 17: // PaintVarScale
                {
                    int paintOffset = ReadOffset24();
                    double sx = ReadF2Dot14(), sy = ReadF2Dot14();
                    return WrapTransform(new ColrAffine(sx, 0, 0, sy, 0, 0), offset + paintOffset, visiting, depth);
                }
                case 18: // PaintScaleAroundCenter
                case 19: // PaintVarScaleAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    double sx = ReadF2Dot14(), sy = ReadF2Dot14();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    return WrapTransform(AroundCenter(new ColrAffine(sx, 0, 0, sy, 0, 0), cx, cy), offset + paintOffset, visiting, depth);
                }
                case 20: // PaintScaleUniform
                case 21: // PaintVarScaleUniform
                {
                    int paintOffset = ReadOffset24();
                    double s = ReadF2Dot14();
                    return WrapTransform(new ColrAffine(s, 0, 0, s, 0, 0), offset + paintOffset, visiting, depth);
                }
                case 22: // PaintScaleUniformAroundCenter
                case 23: // PaintVarScaleUniformAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    double s = ReadF2Dot14();
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    return WrapTransform(AroundCenter(new ColrAffine(s, 0, 0, s, 0, 0), cx, cy), offset + paintOffset, visiting, depth);
                }
                case 24: // PaintRotate
                case 25: // PaintVarRotate
                {
                    int paintOffset = ReadOffset24();
                    ColrAffine rotate = Rotation(ReadAngle());
                    return WrapTransform(rotate, offset + paintOffset, visiting, depth);
                }
                case 26: // PaintRotateAroundCenter
                case 27: // PaintVarRotateAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    ColrAffine rotate = Rotation(ReadAngle());
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    return WrapTransform(AroundCenter(rotate, cx, cy), offset + paintOffset, visiting, depth);
                }
                case 28: // PaintSkew
                case 29: // PaintVarSkew
                {
                    int paintOffset = ReadOffset24();
                    ColrAffine skew = Skew(ReadAngle(), ReadAngle());
                    return WrapTransform(skew, offset + paintOffset, visiting, depth);
                }
                case 30: // PaintSkewAroundCenter
                case 31: // PaintVarSkewAroundCenter
                {
                    int paintOffset = ReadOffset24();
                    ColrAffine skew = Skew(ReadAngle(), ReadAngle());
                    double cx = _face.ReadShort(), cy = _face.ReadShort();
                    return WrapTransform(AroundCenter(skew, cx, cy), offset + paintOffset, visiting, depth);
                }
                case 32: // PaintComposite
                {
                    int sourceOffset = ReadOffset24();
                    int mode = _face.ReadByte();
                    int backdropOffset = ReadOffset24();
                    ColrPaint? source = ParsePaint(offset + sourceOffset, visiting, depth + 1);
                    ColrPaint? backdrop = ParsePaint(offset + backdropOffset, visiting, depth + 1);
                    return new ColrPaintComposite { Source = source, Mode = mode, Backdrop = backdrop };
                }
                default:
                    return null; // unknown/unsupported paint format
            }
        }

        private ColrPaint WrapTransform(ColrAffine affine, int childOffset, HashSet<int> visiting, int depth)
            => new ColrPaintTransform { Affine = affine, Paint = ParsePaint(childOffset, visiting, depth + 1) };

        private ColrColorLine ReadColorLine(int offset, bool isVariable)
        {
            _face.Position = offset;
            var extend = (ColrExtend)_face.ReadByte();
            int numStops = _face.ReadUShort();
            var line = new ColrColorLine { Extend = extend };
            for (int i = 0; i < numStops; i++)
            {
                double stopOffset = ReadF2Dot14();
                int paletteIndex = _face.ReadUShort();
                double alpha = ReadF2Dot14();
                if (isVariable)
                    _face.ReadULong(); // varIndexBase - ignored
                line.Stops.Add(new ColrColorStop(stopOffset, paletteIndex, alpha));
            }
            return line;
        }

        private ColrAffine ReadAffine(int offset)
        {
            _face.Position = offset;
            double xx = ReadFixed(), yx = ReadFixed(), xy = ReadFixed(), yy = ReadFixed(), dx = ReadFixed(), dy = ReadFixed();
            return new ColrAffine(xx, yx, xy, yy, dx, dy);
        }

        private static ColrAffine Rotation(double radians)
        {
            double cos = System.Math.Cos(radians), sin = System.Math.Sin(radians);
            return new ColrAffine(cos, sin, -sin, cos, 0, 0);
        }

        private static ColrAffine Skew(double xSkewRadians, double ySkewRadians)
        {
            // COLR PaintSkew: x' = x - tan(xSkew)·y, y' = y + tan(ySkew)·x.
            // In ColrAffine (XX, YX, XY, YY, DX, DY): XY = -tan(xSkew), YX = +tan(ySkew).
            return new ColrAffine(1, System.Math.Tan(ySkewRadians), -System.Math.Tan(xSkewRadians), 1, 0, 0);
        }

        private static ColrAffine AroundCenter(ColrAffine m, double cx, double cy)
        {
            var toCenter = new ColrAffine(1, 0, 0, 1, cx, cy);
            var fromCenter = new ColrAffine(1, 0, 0, 1, -cx, -cy);
            return ColrAffine.Multiply(ColrAffine.Multiply(toCenter, m), fromCenter);
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
