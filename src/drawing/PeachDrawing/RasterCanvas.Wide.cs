using PeachDrawing.Core;
using System;

namespace PeachDrawing;

public sealed partial class RasterCanvas
{
    private float[] _wideScratch = [];

    // CompositeRow for a surface that keeps floats: the paint (a color or the pixels of a floating-point image, as floats) is composited into the float row exactly, and the
    // eight-bit view of the same picture is made from the result.
    private void CompositeRowWide(int y, int start, int count, ReadOnlySpan<byte> work, PaintSource paint, PaintBlendMode mode)
    {
        if (_wideScratch.Length < count * 4)
            _wideScratch = new float[count * 4 + 16];

        var source = _wideScratch.AsSpan(0, count * 4);
        var solid = paint.IsWideSolid;
        var (cr, cg, cb, ca) = solid ? paint.WideSolid : default;
        if (!solid)
            paint.FillSpanWide(start, y, count, source);

        var wideRow = _surface.WideRow(y);
        var byteRow = _surface.Row(y);
        for (var i = 0; i < count; i++)
        {
            int c = work[i];
            if (c == 0)
                continue;

            float sr, sg, sb, sa;
            if (solid)
                (sr, sg, sb, sa) = (cr, cg, cb, ca);
            else
                (sr, sg, sb, sa) = (source[i * 4], source[i * 4 + 1], source[i * 4 + 2], source[i * 4 + 3]);

            if (c != 255)
            {
                var k = c / 255f;
                (sr, sg, sb, sa) = (sr * k, sg * k, sb * k, sa * k);
            }

            var p = (start + i) * 4;
            var pixel = wideRow.Slice(p, 4);
            BlendModes.BlendWide(mode, pixel, sr, sg, sb, sa);

            // The eight-bit view: alpha and each color brought into 0 to 1, and a color no larger than the alpha it is premultiplied by.
            var alpha = Math.Clamp(pixel[3], 0f, 1f);
            byteRow[p] = (byte)Math.Round(Math.Clamp(Math.Min(pixel[0], alpha), 0f, 1f) * 255f);
            byteRow[p + 1] = (byte)Math.Round(Math.Clamp(Math.Min(pixel[1], alpha), 0f, 1f) * 255f);
            byteRow[p + 2] = (byte)Math.Round(Math.Clamp(Math.Min(pixel[2], alpha), 0f, 1f) * 255f);
            byteRow[p + 3] = (byte)Math.Round(alpha * 255f);
        }
    }
}
