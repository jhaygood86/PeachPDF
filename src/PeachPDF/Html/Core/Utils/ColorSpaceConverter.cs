using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using System;

namespace PeachPDF.Html.Core.Utils
{
    internal static class ColorSpaceConverter
    {
        public static RColor Interpolate(
            RColor c1, RColor c2, double t,
            GradientColorSpace cs,
            HueInterpolationMethod hue)
        {
            t = Math.Clamp(t, 0.0, 1.0);

            byte a = (byte)Math.Round(Math.Clamp(c1.A + t * (c2.A - c1.A), 0, 255));

            double[] v1 = ToSpace(c1, cs);
            double[] v2 = ToSpace(c2, cs);
            double[] v = new double[3];

            if (IsPolar(cs))
            {
                double h1 = NormalizeHue(v1[2]);
                double h2 = NormalizeHue(v2[2]);
                double diff = h2 - h1;
                switch (hue)
                {
                    case HueInterpolationMethod.Longer:
                        if (diff > 0 && diff < 180) diff -= 360;
                        else if (diff < 0 && diff > -180) diff += 360;
                        break;
                    case HueInterpolationMethod.Increasing:
                        if (diff < 0) diff += 360;
                        break;
                    case HueInterpolationMethod.Decreasing:
                        if (diff > 0) diff -= 360;
                        break;
                    default: // Shorter
                        if (diff > 180) diff -= 360;
                        else if (diff < -180) diff += 360;
                        break;
                }
                v[0] = v1[0] + t * (v2[0] - v1[0]);
                v[1] = v1[1] + t * (v2[1] - v1[1]);
                v[2] = h1 + t * diff;
            }
            else
            {
                v[0] = v1[0] + t * (v2[0] - v1[0]);
                v[1] = v1[1] + t * (v2[1] - v1[1]);
                v[2] = v1[2] + t * (v2[2] - v1[2]);
            }

            var rgb = FromSpace(v, cs);
            return RColor.FromArgb(a, rgb.R, rgb.G, rgb.B);
        }

        private static bool IsPolar(GradientColorSpace cs) =>
            cs == GradientColorSpace.Hsl || cs == GradientColorSpace.Hwb ||
            cs == GradientColorSpace.Lch || cs == GradientColorSpace.Oklch;

        private static double NormalizeHue(double h) => ((h % 360) + 360) % 360;

        // ── sRGB gamma ────────────────────────────────────────────────────────

        private static double Linearize(double c)
        {
            c = Math.Clamp(c, 0, 1);
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static double Delinearize(double c)
        {
            c = Math.Clamp(c, 0, 1);
            return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        }

        // ── Linear sRGB ↔ XYZ-D65 (IEC 61966-2-1) ───────────────────────────

        private static (double X, double Y, double Z) LinSrgbToXyzD65(double r, double g, double b) =>
            (0.4124564 * r + 0.3575761 * g + 0.1804375 * b,
             0.2126729 * r + 0.7151522 * g + 0.0721750 * b,
             0.0193339 * r + 0.1191920 * g + 0.9503041 * b);

        private static (double R, double G, double B) XyzD65ToLinSrgb(double x, double y, double z) =>
            ( 3.2404542 * x - 1.5371385 * y - 0.4985314 * z,
             -0.9692660 * x + 1.8760108 * y + 0.0415560 * z,
              0.0556434 * x - 0.2040259 * y + 1.0572252 * z);

        // ── XYZ-D65 ↔ XYZ-D50 (Bradford) ────────────────────────────────────

        private static (double X, double Y, double Z) XyzD65ToXyzD50(double x, double y, double z) =>
            ( 1.0478112 * x + 0.0228866 * y - 0.0501270 * z,
              0.0295424 * x + 0.9904844 * y - 0.0170491 * z,
             -0.0092345 * x + 0.0150436 * y + 0.7521316 * z);

        private static (double X, double Y, double Z) XyzD50ToXyzD65(double x, double y, double z) =>
            ( 0.9554734 * x - 0.0230393 * y + 0.0631636 * z,
             -0.0282895 * x + 1.0099416 * y + 0.0210077 * z,
              0.0122982 * x - 0.0204830 * y + 1.3299098 * z);

        // ── Display-P3 ↔ XYZ-D65 (P3 uses sRGB gamma) ───────────────────────

        private static (double X, double Y, double Z) LinP3ToXyzD65(double r, double g, double b) =>
            (0.4865709 * r + 0.2656677 * g + 0.1982173 * b,
             0.2289746 * r + 0.6917386 * g + 0.0792869 * b,
             0.0000000 * r + 0.0451134 * g + 1.0439444 * b);

        private static (double R, double G, double B) XyzD65ToLinP3(double x, double y, double z) =>
            ( 2.4934969 * x - 0.9313836 * y - 0.4027108 * z,
             -0.8294890 * x + 1.7626641 * y + 0.0236247 * z,
              0.0358458 * x - 0.0761724 * y + 0.9568845 * z);

        // ── CIE L*a*b* ────────────────────────────────────────────────────────

        private const double Xn50 = 0.96422, Yn50 = 1.0, Zn50 = 0.82521;

        private static double LabF(double t)
        {
            const double d = 6.0 / 29.0;
            return t > d * d * d ? Math.Cbrt(t) : t / (3 * d * d) + 4.0 / 29.0;
        }

        private static double LabFInv(double t)
        {
            const double d = 6.0 / 29.0;
            return t > d ? t * t * t : 3 * d * d * (t - 4.0 / 29.0);
        }

        private static (double L, double a, double b) XyzD50ToLab(double x, double y, double z)
        {
            double fx = LabF(x / Xn50), fy = LabF(y / Yn50), fz = LabF(z / Zn50);
            return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
        }

        private static (double X, double Y, double Z) LabToXyzD50(double L, double a, double b)
        {
            double fy = (L + 16) / 116;
            return (Xn50 * LabFInv(a / 500 + fy), Yn50 * LabFInv(fy), Zn50 * LabFInv(fy - b / 200));
        }

        // ── OKLab (Björn Ottosson) ────────────────────────────────────────────

        private static (double L, double a, double b) XyzD65ToOkLab(double x, double y, double z)
        {
            double l = Math.Cbrt(0.8189330101 * x + 0.3618667424 * y - 0.1288597137 * z);
            double m = Math.Cbrt(0.0329845436 * x + 0.9293118715 * y + 0.0361456387 * z);
            double s = Math.Cbrt(0.0482003018 * x + 0.2643662691 * y + 0.6338517070 * z);
            return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
        }

        private static (double X, double Y, double Z) OkLabToXyzD65(double L, double a, double b)
        {
            double l = (L + 0.3963377774 * a + 0.2158037573 * b); l = l * l * l;
            double m = (L - 0.1055613458 * a - 0.0638541728 * b); m = m * m * m;
            double s = (L - 0.0894841775 * a - 1.2914855480 * b); s = s * s * s;
            return ( 1.2270138511 * l - 0.5577999807 * m + 0.2812561490 * s,
                    -0.0405801784 * l + 1.1122568696 * m - 0.0716766787 * s,
                    -0.0763812845 * l - 0.4214819784 * m + 1.5861632204 * s);
        }

        // ── HSL ───────────────────────────────────────────────────────────────

        private static (double H, double S, double L) SrgbToHsl(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            double l = (max + min) / 2;
            if (d < 1e-10) return (0, 0, l * 100);
            double s = d / (1 - Math.Abs(2 * l - 1));
            double h;
            if (max == r)      h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else               h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
            return (h, s * 100, l * 100);
        }

        private static (double R, double G, double B) HslToSrgb(double h, double s, double l)
        {
            s /= 100; l /= 100;
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            switch ((int)(h / 60) % 6)
            {
                case 0: r = c; g = x; break;
                case 1: r = x; g = c; break;
                case 2: g = c; b = x; break;
                case 3: g = x; b = c; break;
                case 4: r = x; b = c; break;
                case 5: r = c; b = x; break;
            }
            return (r + m, g + m, b + m);
        }

        // ── HWB ───────────────────────────────────────────────────────────────

        private static (double H, double W, double Bk) SrgbToHwb(double r, double g, double b)
        {
            var (h, _, _) = SrgbToHsl(r, g, b);
            return (h,
                    Math.Min(r, Math.Min(g, b)) * 100,
                    (1 - Math.Max(r, Math.Max(g, b))) * 100);
        }

        private static (double R, double G, double B) HwbToSrgb(double h, double w, double bk)
        {
            w /= 100; bk /= 100;
            if (w + bk >= 1) { double grey = w / (w + bk); return (grey, grey, grey); }
            var (r, g, b) = HslToSrgb(h, 100, 50);
            double f = 1 - w - bk;
            return (r * f + w, g * f + w, b * f + w);
        }

        // ── LCH / OKLch (rectangular ↔ polar) ────────────────────────────────

        private static (double L, double C, double H) RectToPolar(double L, double a, double b)
        {
            double c = Math.Sqrt(a * a + b * b);
            double h = Math.Atan2(b, a) * (180.0 / Math.PI);
            if (h < 0) h += 360;
            return (L, c, h);
        }

        private static (double L, double a, double b) PolarToRect(double L, double c, double h)
        {
            double rad = h * (Math.PI / 180.0);
            return (L, c * Math.Cos(rad), c * Math.Sin(rad));
        }

        // ── Forward: sRGB byte → color space doubles ──────────────────────────

        private static double[] ToSpace(RColor c, GradientColorSpace cs)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;

            switch (cs)
            {
                case GradientColorSpace.SrgbLinear:
                    return [Linearize(r), Linearize(g), Linearize(b)];

                case GradientColorSpace.DisplayP3:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (pr, pg, pb) = XyzD65ToLinP3(x, y, z);
                    return [Delinearize(pr), Delinearize(pg), Delinearize(pb)];
                }

                case GradientColorSpace.Lab:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (x50, y50, z50) = XyzD65ToXyzD50(x, y, z);
                    var (L, a, bb) = XyzD50ToLab(x50, y50, z50);
                    return [L, a, bb];
                }

                case GradientColorSpace.Oklab:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (L, a, bb) = XyzD65ToOkLab(x, y, z);
                    return [L, a, bb];
                }

                case GradientColorSpace.XyzD65:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    return [x, y, z];
                }

                case GradientColorSpace.XyzD50:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (x50, y50, z50) = XyzD65ToXyzD50(x, y, z);
                    return [x50, y50, z50];
                }

                case GradientColorSpace.Hsl:
                {
                    var (h, s, l) = SrgbToHsl(r, g, b);
                    return [h, s, l];
                }

                case GradientColorSpace.Hwb:
                {
                    var (h, w, bk) = SrgbToHwb(r, g, b);
                    return [h, w, bk];
                }

                case GradientColorSpace.Lch:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (x50, y50, z50) = XyzD65ToXyzD50(x, y, z);
                    var (L, a, bb) = XyzD50ToLab(x50, y50, z50);
                    var (lL, lC, lH) = RectToPolar(L, a, bb);
                    return [lL, lC, lH];
                }

                case GradientColorSpace.Oklch:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (L, a, bb) = XyzD65ToOkLab(x, y, z);
                    var (oL, oC, oH) = RectToPolar(L, a, bb);
                    return [oL, oC, oH];
                }

                default: // Srgb
                    return [r, g, b];
            }
        }

        // ── Reverse: color space doubles → sRGB byte ─────────────────────────

        private static RColor FromSpace(double[] v, GradientColorSpace cs)
        {
            double r, g, b;

            switch (cs)
            {
                case GradientColorSpace.SrgbLinear:
                    r = Delinearize(v[0]); g = Delinearize(v[1]); b = Delinearize(v[2]);
                    break;

                case GradientColorSpace.DisplayP3:
                {
                    var (x, y, z) = LinP3ToXyzD65(Linearize(v[0]), Linearize(v[1]), Linearize(v[2]));
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case GradientColorSpace.Lab:
                {
                    var (x50, y50, z50) = LabToXyzD50(v[0], v[1], v[2]);
                    var (x, y, z) = XyzD50ToXyzD65(x50, y50, z50);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case GradientColorSpace.Oklab:
                {
                    var (x, y, z) = OkLabToXyzD65(v[0], v[1], v[2]);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case GradientColorSpace.XyzD65:
                {
                    var (rl, gl, bl) = XyzD65ToLinSrgb(v[0], v[1], v[2]);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case GradientColorSpace.XyzD50:
                {
                    var (x, y, z) = XyzD50ToXyzD65(v[0], v[1], v[2]);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case GradientColorSpace.Hsl:
                {
                    (r, g, b) = HslToSrgb(v[0], v[1], v[2]);
                    break;
                }

                case GradientColorSpace.Hwb:
                {
                    (r, g, b) = HwbToSrgb(v[0], v[1], v[2]);
                    break;
                }

                case GradientColorSpace.Lch:
                {
                    var (L, a, bb) = PolarToRect(v[0], v[1], v[2]);
                    var (x50, y50, z50) = LabToXyzD50(L, a, bb);
                    var (x, y, z) = XyzD50ToXyzD65(x50, y50, z50);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case GradientColorSpace.Oklch:
                {
                    var (L, a, bb) = PolarToRect(v[0], v[1], v[2]);
                    var (x, y, z) = OkLabToXyzD65(L, a, bb);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                default: // Srgb
                    r = v[0]; g = v[1]; b = v[2];
                    break;
            }

            return RColor.FromArgb(255,
                (int)Math.Round(Math.Clamp(r, 0, 1) * 255),
                (int)Math.Round(Math.Clamp(g, 0, 1) * 255),
                (int)Math.Round(Math.Clamp(b, 0, 1) * 255));
        }
    }
}
