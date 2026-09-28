using PeachDrawing.Abstractions;
using System;

namespace PeachDrawing;

/// <summary>
/// The non-<see cref="PaintBlendMode.Normal"/> compositing modes of W3C Compositing and Blending Level 1
/// (§10 separable, §11 non-separable), applied on premultiplied RGBA8. Scalar only: these are the rare path
/// (mix-blend-mode, invert outlines, feBlend), and a single implementation keeps the output identical on
/// every CPU.
/// </summary>
internal static class BlendModes
{
    /// <summary>
    /// Composites one premultiplied source pixel over one premultiplied backdrop pixel with
    /// <paramref name="mode"/>, in place in <paramref name="dst"/> (4 bytes, R G B A).
    /// </summary>
    public static void Blend(PaintBlendMode mode, Span<byte> dst, int sr, int sg, int sb, int sa)
    {
        if (sa == 0)
            return;

        float ab = dst[3] / 255f;
        float a = sa / 255f;

        // Unpremultiply the backdrop and the source; the blend function works on straight colour.
        var (br, bg, bb) = ab > 0 ? (dst[0] / 255f / ab, dst[1] / 255f / ab, dst[2] / 255f / ab) : (0f, 0f, 0f);
        var (cr, cg, cb) = (sr / 255f / a, sg / 255f / a, sb / 255f / a);
        br = Math.Min(br, 1f); bg = Math.Min(bg, 1f); bb = Math.Min(bb, 1f);
        cr = Math.Min(cr, 1f); cg = Math.Min(cg, 1f); cb = Math.Min(cb, 1f);

        var (mr, mg, mb) = Blend(mode, (br, bg, bb), (cr, cg, cb));

        // Cr * ar = (1 - ab) * as * Cs + (1 - as) * ab * Cb + as * ab * B(Cb, Cs), in premultiplied terms.
        float outA = a + ab * (1 - a);
        float r = (1 - ab) * a * cr + (1 - a) * ab * br + a * ab * mr;
        float g = (1 - ab) * a * cg + (1 - a) * ab * bg + a * ab * mg;
        float b = (1 - ab) * a * cb + (1 - a) * ab * bb + a * ab * mb;

        dst[0] = ToByte(r);
        dst[1] = ToByte(g);
        dst[2] = ToByte(b);
        dst[3] = ToByte(outA);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static (float, float, float) Blend(PaintBlendMode mode, (float R, float G, float B) cb, (float R, float G, float B) cs)
    {
        switch (mode)
        {
            case PaintBlendMode.Hue:
                return SetLum(SetSat(cs, Sat(cb)), Lum(cb));
            case PaintBlendMode.Saturation:
                return SetLum(SetSat(cb, Sat(cs)), Lum(cb));
            case PaintBlendMode.Color:
                return SetLum(cs, Lum(cb));
            case PaintBlendMode.Luminosity:
                return SetLum(cb, Lum(cs));
            default:
                return (Channel(mode, cb.R, cs.R), Channel(mode, cb.G, cs.G), Channel(mode, cb.B, cs.B));
        }
    }

    private static float Channel(PaintBlendMode mode, float b, float s)
    {
        switch (mode)
        {
            case PaintBlendMode.Multiply: return b * s;
            case PaintBlendMode.Screen: return b + s - b * s;
            case PaintBlendMode.Overlay: return Channel(PaintBlendMode.HardLight, s, b);
            case PaintBlendMode.Darken: return Math.Min(b, s);
            case PaintBlendMode.Lighten: return Math.Max(b, s);
            case PaintBlendMode.ColorDodge:
                if (b == 0) return 0;
                return s >= 1 ? 1 : Math.Min(1, b / (1 - s));
            case PaintBlendMode.ColorBurn:
                if (b >= 1) return 1;
                return s <= 0 ? 0 : 1 - Math.Min(1, (1 - b) / s);
            case PaintBlendMode.HardLight:
                return s <= 0.5f ? b * 2 * s : b + (2 * s - 1) - b * (2 * s - 1);
            case PaintBlendMode.SoftLight:
                if (s <= 0.5f)
                    return b - (1 - 2 * s) * b * (1 - b);
                var d = b <= 0.25f ? ((16 * b - 12) * b + 4) * b : MathF.Sqrt(b);
                return b + (2 * s - 1) * (d - b);
            case PaintBlendMode.Difference: return Math.Abs(b - s);
            case PaintBlendMode.Exclusion: return b + s - 2 * b * s;
            default: return s;
        }
    }

    private static float Lum((float R, float G, float B) c) => 0.3f * c.R + 0.59f * c.G + 0.11f * c.B;

    private static (float, float, float) ClipColor((float R, float G, float B) c)
    {
        var l = Lum(c);
        var n = Math.Min(c.R, Math.Min(c.G, c.B));
        var x = Math.Max(c.R, Math.Max(c.G, c.B));
        if (n < 0)
        {
            c = (l + (c.R - l) * l / (l - n), l + (c.G - l) * l / (l - n), l + (c.B - l) * l / (l - n));
        }

        if (x > 1)
        {
            c = (l + (c.R - l) * (1 - l) / (x - l), l + (c.G - l) * (1 - l) / (x - l), l + (c.B - l) * (1 - l) / (x - l));
        }

        return c;
    }

    private static (float, float, float) SetLum((float R, float G, float B) c, float l)
    {
        var d = l - Lum(c);
        return ClipColor((c.R + d, c.G + d, c.B + d));
    }

    private static float Sat((float R, float G, float B) c) =>
        Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));

    private static (float, float, float) SetSat((float R, float G, float B) c, float s)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        if (max <= min)
            return (0, 0, 0);

        float Scale(float v) => (v - min) * s / (max - min);
        return (Scale(c.R), Scale(c.G), Scale(c.B));
    }
}
