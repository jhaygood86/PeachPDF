using System;
using System.Runtime.Intrinsics;

namespace PeachDrawing;

internal abstract partial class PaintSource
{
    internal sealed partial class RadialPaint
    {
        /// <summary>
        /// The 256-bit form of <see cref="FillSpanVector128"/>: four pixels in one <see cref="Vector256{T}"/> (AVX on x64; not hardware accelerated on ARM64, where
        /// the 128-bit form runs), with the same double-precision operations in the same order as <see cref="FillSpanScalar"/>, one pixel per lane. Returns how many
        /// pixels it handled; only for a gradient with no focal radius.
        /// </summary>
        internal int FillSpanVector256(int x0, int y, int count, Span<byte> destination)
        {
            var i = 0;
            var m = _deviceToUser;
            var yc = y + 0.5;
            var yTermX = Vector256.Create(yc * m.M21);
            var yTermY = Vector256.Create(yc * m.M22);
            var m11 = Vector256.Create(m.M11);
            var m12 = Vector256.Create(m.M12);
            var offsetX = Vector256.Create(m.OffsetX);
            var offsetY = Vector256.Create(m.OffsetY);
            var cx = Vector256.Create(_cx);
            var cy = Vector256.Create(_cy);
            var rx = Vector256.Create(_rx);
            var ry = Vector256.Create(_ry);
            var fx = Vector256.Create(_fx);
            var fy = Vector256.Create(_fy);
            var a = Vector256.Create(_a);
            var lanes = Vector256.Create(0.0, 1.0, 2.0, 3.0);

            for (; i + 4 <= count; i += 4)
            {
                var x = Vector256.Create((double)(x0 + i) + 0.5) + lanes;
                var ux = x * m11 + yTermX + offsetX;
                var uy = x * m12 + yTermY + offsetY;
                var px = (ux - cx) / rx;
                var py = (uy - cy) / ry;
                var qx = px - fx;
                var qy = py - fy;
                var qf = qx * fx + qy * fy;
                var q2 = qx * qx + qy * qy;
                var tv = (qf + Vector256.Sqrt(qf * qf + a * q2)) / a;

                for (var k = 0; k < 4; k++)
                {
                    var t = (float)tv.GetElement(k);
                    if (_innerRatio > 0)
                        t = (float)((t - _innerRatio) / (1 - _innerRatio));

                    if (_repeating)
                        t = _stops.Wrap(t);

                    Write(destination, i + k, _stops.At(t));
                }
            }

            return i;
        }
    }
}
