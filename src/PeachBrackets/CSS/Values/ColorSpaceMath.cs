#nullable disable

using System;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The single home for CSS color-space conversion math (CSS Color 4/5): forward/reverse transforms
    /// between gamma-encoded sRGB and every interpolation space, plus polar-hue interpolation. Kept in
    /// the CSS-OM (rather than the render layer) so both solid-color resolution (<see cref="Color"/>'s
    /// <c>oklch()</c>/<c>lab()</c>/<c>color-mix()</c> factories) and gradient interpolation (the render
    /// layer's <c>ColorSpaceConverter</c>, which delegates here) share one implementation.
    /// </summary>
    internal static class ColorSpaceMath
    {
        internal enum Space { Srgb, SrgbLinear, DisplayP3, Lab, Oklab, XyzD65, XyzD50, Hsl, Hwb, Lch, Oklch }

        internal enum HueMethod { Shorter, Longer, Increasing, Decreasing }

        public static bool IsPolar(Space cs) =>
            cs is Space.Hsl or Space.Hwb or Space.Lch or Space.Oklch;

        // The index of the hue coordinate in a space's ToSpace/FromSpace layout, or -1 if rectangular.
        // HSL/HWB carry hue first ([h, s/w, l/bk]); LCH/OKLch carry it last ([L, C, H]).
        public static int HueIndex(Space cs) => cs switch
        {
            Space.Hsl or Space.Hwb => 0,
            Space.Lch or Space.Oklch => 2,
            _ => -1
        };

        /// <summary>
        /// Whether the hue of a colour in lch or oklch means nothing because its chroma is (all but) zero, as it is for a gray: CSS Color 4 takes
        /// such a hue as missing when colours are interpolated (Interpolating with Missing Components), and a mix then uses the other colour's.
        /// The limit is Chrome's, found by bisection: a chroma of 0.02 or less (in lab for lch, in oklab for oklch) is powerless, 0.021 is not
        /// (the specification's own epsilons are smaller, 0.0015 and 0.000004). It sits above the noise converting a gray leaves behind (8-bit gray: 3e-5 lch, 9e-5 oklch).
        /// </summary>
        public static bool IsHuePowerless(double[] v, Space cs) => cs switch
        {
            Space.Lch or Space.Oklch => v[1] <= 0.02,
            _ => false
        };

        /// <summary>
        /// Gives a gray its missing hue before two colours in lch or oklch are mixed in a color-mix() (see <see cref="IsHuePowerless"/>): the other colour's hue
        /// stands in for it, and where both are gray the hue is 0, as in Chrome. Does nothing in any other space.
        /// </summary>
        public static void ResolveMissingHues(double[] v1, double[] v2, Space cs)
        {
            var hueIndex = HueIndex(cs);
            if (hueIndex < 0) return;

            var powerless1 = IsHuePowerless(v1, cs);
            var powerless2 = IsHuePowerless(v2, cs);
            if (powerless1 && powerless2) v1[hueIndex] = v2[hueIndex] = 0;
            else if (powerless1) v1[hueIndex] = v2[hueIndex];
            else if (powerless2) v2[hueIndex] = v1[hueIndex];
        }

        public static double NormalizeHue(double h) => ((h % 360) + 360) % 360;

        public static double InterpolateHue(double h1, double h2, double t, HueMethod hue)
        {
            h1 = NormalizeHue(h1);
            h2 = NormalizeHue(h2);
            var diff = h2 - h1;
            switch (hue)
            {
                case HueMethod.Longer:
                    if (diff > 0 && diff < 180) diff -= 360;
                    else if (diff <= 0 && diff > -180) diff += 360;
                    break;
                case HueMethod.Increasing:
                    if (diff < 0) diff += 360;
                    break;
                case HueMethod.Decreasing:
                    if (diff > 0) diff -= 360;
                    break;
                default: // Shorter
                    if (diff > 180) diff -= 360;
                    else if (diff < -180) diff += 360;
                    break;
            }
            return NormalizeHue(h1 + t * diff);
        }

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
            var fy = (L + 16) / 116;
            return (Xn50 * LabFInv(a / 500 + fy), Yn50 * LabFInv(fy), Zn50 * LabFInv(fy - b / 200));
        }

        // ── OKLab (Björn Ottosson) ────────────────────────────────────────────

        private static (double L, double a, double b) XyzD65ToOkLab(double x, double y, double z)
        {
            var l = Math.Cbrt(0.8189330101 * x + 0.3618667424 * y - 0.1288597137 * z);
            var m = Math.Cbrt(0.0329845436 * x + 0.9293118715 * y + 0.0361456387 * z);
            var s = Math.Cbrt(0.0482003018 * x + 0.2643662691 * y + 0.6338517070 * z);
            return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
        }

        private static (double X, double Y, double Z) OkLabToXyzD65(double L, double a, double b)
        {
            double l = L + 0.3963377774 * a + 0.2158037573 * b; l = l * l * l;
            double m = L - 0.1055613458 * a - 0.0638541728 * b; m = m * m * m;
            double s = L - 0.0894841775 * a - 1.2914855480 * b; s = s * s * s;
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

        // ── Forward: gamma-sRGB (0..1) → color-space coordinates ─────────────

        public static double[] ToSpace(double r, double g, double b, Space cs)
        {
            switch (cs)
            {
                case Space.SrgbLinear:
                    return [Linearize(r), Linearize(g), Linearize(b)];

                case Space.DisplayP3:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (pr, pg, pb) = XyzD65ToLinP3(x, y, z);
                    return [Delinearize(pr), Delinearize(pg), Delinearize(pb)];
                }

                case Space.Lab:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (x50, y50, z50) = XyzD65ToXyzD50(x, y, z);
                    var (L, a, bb) = XyzD50ToLab(x50, y50, z50);
                    return [L, a, bb];
                }

                case Space.Oklab:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (L, a, bb) = XyzD65ToOkLab(x, y, z);
                    return [L, a, bb];
                }

                case Space.XyzD65:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    return [x, y, z];
                }

                case Space.XyzD50:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (x50, y50, z50) = XyzD65ToXyzD50(x, y, z);
                    return [x50, y50, z50];
                }

                case Space.Hsl:
                {
                    var (h, s, l) = SrgbToHsl(r, g, b);
                    return [h, s, l];
                }

                case Space.Hwb:
                {
                    var (h, w, bk) = SrgbToHwb(r, g, b);
                    return [h, w, bk];
                }

                case Space.Lch:
                {
                    var (x, y, z) = LinSrgbToXyzD65(Linearize(r), Linearize(g), Linearize(b));
                    var (x50, y50, z50) = XyzD65ToXyzD50(x, y, z);
                    var (L, a, bb) = XyzD50ToLab(x50, y50, z50);
                    var (lL, lC, lH) = RectToPolar(L, a, bb);
                    return [lL, lC, lH];
                }

                case Space.Oklch:
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

        // ── Color-space coordinates → Oklab, without going through sRGB ──────
        //
        // The Lab and Oklab families can name colours outside the sRGB gamut; ToSpace(FromSpace(v)) would clip
        // them first. This converts straight through XYZ, so an out-of-gamut colour keeps the Oklab coordinates
        // it was written with (a mix of two colours, CSS Color 4 §13, is made on those). So do the spaces a mix result
        // can leave the sRGB gamut in (sRGB, linear sRGB, Display P3, XYZ); hsl and hwb, which hold no such colour, go
        // through the clipped sRGB.

        public static double[] ToOklab(double[] v, Space cs)
        {
            switch (cs)
            {
                case Space.Oklab:
                    return [v[0], v[1], v[2]];

                case Space.Oklch:
                {
                    var (L, a, b) = PolarToRect(v[0], v[1], v[2]);
                    return [L, a, b];
                }

                case Space.Lab:
                case Space.Lch:
                {
                    var (L, a, b) = cs == Space.Lch ? PolarToRect(v[0], v[1], v[2]) : (v[0], v[1], v[2]);
                    var (x50, y50, z50) = LabToXyzD50(L, a, b);
                    var (x, y, z) = XyzD50ToXyzD65(x50, y50, z50);
                    var (ol, oa, ob) = XyzD65ToOkLab(x, y, z);
                    return [ol, oa, ob];
                }

                case Space.Srgb:
                case Space.SrgbLinear:
                case Space.DisplayP3:
                {
                    var (xs, ys, zs) = cs switch
                    {
                        Space.Srgb => LinSrgbToXyzD65(LinearizeExtended(v[0]), LinearizeExtended(v[1]), LinearizeExtended(v[2])),
                        Space.SrgbLinear => LinSrgbToXyzD65(v[0], v[1], v[2]),
                        _ => LinP3ToXyzD65(LinearizeExtended(v[0]), LinearizeExtended(v[1]), LinearizeExtended(v[2])),
                    };
                    var (ol, oa, ob) = XyzD65ToOkLab(xs, ys, zs);
                    return [ol, oa, ob];
                }

                case Space.XyzD65:
                case Space.XyzD50:
                {
                    var (xs, ys, zs) = cs == Space.XyzD65 ? (v[0], v[1], v[2]) : XyzD50ToXyzD65(v[0], v[1], v[2]);
                    var (ol, oa, ob) = XyzD65ToOkLab(xs, ys, zs);
                    return [ol, oa, ob];
                }

                default:
                {
                    var (r, g, b) = FromSpace(v, cs);
                    return ToSpace(r, g, b, Space.Oklab);
                }
            }
        }

        // ── Oklab → color-space coordinates, without a clip to the sRGB gamut ──
        //
        // The inverse of ToOklab for a colour that may lie outside the sRGB gamut (a color-mix() operand written
        // as a wide lab()/oklch()): the coordinates in cs keep their out-of-range values, so the mix is made on
        // them and the clip comes once, at the end. hsl and hwb get the clipped colour (a gamut-mapped one has no
        // meaning there, and color-mix() in those spaces does not resolve yet).

        public static double[] ToSpaceFromOklab(double[] oklab, Space cs)
        {
            switch (cs)
            {
                case Space.Oklab:
                    return [oklab[0], oklab[1], oklab[2]];

                case Space.Oklch:
                {
                    var (L, c, h) = RectToPolar(oklab[0], oklab[1], oklab[2]);
                    return [L, c, h];
                }

                case Space.Lab:
                case Space.Lch:
                {
                    var (x, y, z) = OkLabToXyzD65(oklab[0], oklab[1], oklab[2]);
                    var (x50, y50, z50) = XyzD65ToXyzD50(x, y, z);
                    var (L, a, b) = XyzD50ToLab(x50, y50, z50);

                    // The Oklab matrices do not quite share lab's white, so an exact Oklab gray comes out of lab with a chroma of up to 0.033: it stays a gray.
                    if (Math.Sqrt(oklab[1] * oklab[1] + oklab[2] * oklab[2]) < 5e-5) a = b = 0;
                    if (cs == Space.Lab) return [L, a, b];
                    var (pl, pc, ph) = RectToPolar(L, a, b);
                    return [pl, pc, ph];
                }

                case Space.XyzD65:
                {
                    var (x, y, z) = OkLabToXyzD65(oklab[0], oklab[1], oklab[2]);
                    return [x, y, z];
                }

                case Space.XyzD50:
                {
                    var (x, y, z) = OkLabToXyzD65(oklab[0], oklab[1], oklab[2]);
                    var (x50, y50, z50) = XyzD65ToXyzD50(x, y, z);
                    return [x50, y50, z50];
                }

                case Space.Srgb:
                case Space.SrgbLinear:
                {
                    var (x, y, z) = OkLabToXyzD65(oklab[0], oklab[1], oklab[2]);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    return cs == Space.SrgbLinear ? [rl, gl, bl] : [DelinearizeExtended(rl), DelinearizeExtended(gl), DelinearizeExtended(bl)];
                }

                case Space.DisplayP3:
                {
                    var (x, y, z) = OkLabToXyzD65(oklab[0], oklab[1], oklab[2]);
                    var (pr, pg, pb) = XyzD65ToLinP3(x, y, z);
                    return [DelinearizeExtended(pr), DelinearizeExtended(pg), DelinearizeExtended(pb)];
                }

                default:
                {
                    var (r, g, b) = FromSpace(oklab, Space.Oklab);
                    return ToSpace(r, g, b, cs);
                }
            }
        }

        private static double LinearizeExtended(double c)
        {
            var magnitude = Math.Abs(c);
            var linear = magnitude <= 0.04045 ? magnitude / 12.92 : Math.Pow((magnitude + 0.055) / 1.055, 2.4);
            return c < 0 ? -linear : linear;
        }

        // The sRGB transfer function extended past 0..1 by odd symmetry, as CSS Color 4 §10.2 does.
        private static double DelinearizeExtended(double c)
        {
            var magnitude = Math.Abs(c);
            var encoded = magnitude <= 0.0031308 ? magnitude * 12.92 : 1.055 * Math.Pow(magnitude, 1.0 / 2.4) - 0.055;
            return c < 0 ? -encoded : encoded;
        }

        // ── Reverse: color-space coordinates → gamut-mapped sRGB (0..1) ──────

        public static (double R, double G, double B) FromSpace(double[] v, Space cs)
        {
            double r, g, b;

            switch (cs)
            {
                case Space.SrgbLinear:
                    r = Delinearize(v[0]); g = Delinearize(v[1]); b = Delinearize(v[2]);
                    break;

                case Space.DisplayP3:
                {
                    var (x, y, z) = LinP3ToXyzD65(Linearize(v[0]), Linearize(v[1]), Linearize(v[2]));
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case Space.Lab:
                {
                    var (x50, y50, z50) = LabToXyzD50(v[0], v[1], v[2]);
                    var (x, y, z) = XyzD50ToXyzD65(x50, y50, z50);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case Space.Oklab:
                {
                    var (x, y, z) = OkLabToXyzD65(v[0], v[1], v[2]);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case Space.XyzD65:
                {
                    var (rl, gl, bl) = XyzD65ToLinSrgb(v[0], v[1], v[2]);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case Space.XyzD50:
                {
                    var (x, y, z) = XyzD50ToXyzD65(v[0], v[1], v[2]);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case Space.Hsl:
                    (r, g, b) = HslToSrgb(v[0], v[1], v[2]);
                    break;

                case Space.Hwb:
                    (r, g, b) = HwbToSrgb(v[0], v[1], v[2]);
                    break;

                case Space.Lch:
                {
                    var (L, a, bb) = PolarToRect(v[0], v[1], v[2]);
                    var (x50, y50, z50) = LabToXyzD50(L, a, bb);
                    var (x, y, z) = XyzD50ToXyzD65(x50, y50, z50);
                    var (rl, gl, bl) = XyzD65ToLinSrgb(x, y, z);
                    r = Delinearize(rl); g = Delinearize(gl); b = Delinearize(bl);
                    break;
                }

                case Space.Oklch:
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

            return (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
        }
    }
}
