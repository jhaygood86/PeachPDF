using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachBrackets.Html.Entities;
using System;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Gradient color-stop interpolation across CSS Color 4 interpolation spaces. The actual color-space
    /// transform math lives once in <see cref="ColorSpaceMath"/> (CSS-OM); this render-layer type just
    /// maps the gradient enums onto it and interpolates each channel (with polar-hue handling) premultiplied
    /// by alpha (CSS Color 4 §12.3, CSS Images 3 §3.5.1), so a colour fading to <c>transparent</c> keeps its
    /// hue instead of drifting to the transparent black. A polar space's hue is not premultiplied.
    /// </summary>
    internal static class ColorSpaceConverter
    {
        public static PaintColor Interpolate(
            PaintColor c1, PaintColor c2, double t,
            GradientColorSpace cs,
            HueInterpolationMethod hue)
        {
            t = Math.Clamp(t, 0.0, 1.0);

            var alpha1 = c1.A / 255.0;
            var alpha2 = c2.A / 255.0;
            var alpha = alpha1 + t * (alpha2 - alpha1);
            var a = (byte)Math.Round(Math.Clamp(alpha * 255.0, 0, 255));

            var space = ToMathSpace(cs);
            var v1 = ColorSpaceMath.ToSpace(c1.R / 255.0, c1.G / 255.0, c1.B / 255.0, space);
            var v2 = ColorSpaceMath.ToSpace(c2.R / 255.0, c2.G / 255.0, c2.B / 255.0, space);

            // A gray's lch hue is missing, so the other stop's stands in for it, as in color-mix(). Not done for oklch:
            // Chrome interpolates a gray's own hue in an oklch gradient (see the accepted gap).
            if (space == ColorSpaceMath.Space.Lch)
                ColorSpaceMath.ResolveMissingHues(v1, v2, space);

            var v = new double[3];

            var hueIndex = ColorSpaceMath.HueIndex(space);
            for (var i = 0; i < 3; i++)
            {
                if (i == hueIndex)
                {
                    v[i] = ColorSpaceMath.InterpolateHue(v1[i], v2[i], t, ToMathHue(hue));
                    continue;
                }

                var p1 = v1[i] * alpha1;
                var p2 = v2[i] * alpha2;
                var mixed = p1 + t * (p2 - p1);
                // Both ends fully transparent: nothing to premultiply by, so the colour is the plain lerp.
                v[i] = alpha > 0 ? mixed / alpha : v1[i] + t * (v2[i] - v1[i]);
            }

            var (r, g, b) = ColorSpaceMath.FromSpace(v, space);
            return PaintColor.FromArgb(a,
                (int)Math.Round(r * 255),
                (int)Math.Round(g * 255),
                (int)Math.Round(b * 255));
        }

        /// <summary>
        /// Interpolates two sRGB stops premultiplied by alpha (CSS Color 4 §12.3): the colour of a stop only counts
        /// as much as it is opaque. Used for the hint and eased-stop lerps, which stay in sRGB.
        /// </summary>
        public static PaintColor LerpPremultiplied(PaintColor a, PaintColor b, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            double a1 = a.A / 255.0, a2 = b.A / 255.0;
            var alpha = a1 + t * (a2 - a1);

            int Channel(int c1, int c2)
            {
                if (alpha <= 0) return (int)Math.Round(c1 + t * (c2 - c1));
                var p1 = c1 * a1;
                var p2 = c2 * a2;
                return (int)Math.Round((p1 + t * (p2 - p1)) / alpha);
            }

            return PaintColor.FromArgb(
                (int)Math.Round(alpha * 255.0),
                Channel(a.R, b.R),
                Channel(a.G, b.G),
                Channel(a.B, b.B));
        }

        private static ColorSpaceMath.Space ToMathSpace(GradientColorSpace cs) => cs switch
        {
            GradientColorSpace.SrgbLinear => ColorSpaceMath.Space.SrgbLinear,
            GradientColorSpace.DisplayP3 => ColorSpaceMath.Space.DisplayP3,
            GradientColorSpace.Lab => ColorSpaceMath.Space.Lab,
            GradientColorSpace.Oklab => ColorSpaceMath.Space.Oklab,
            GradientColorSpace.XyzD65 => ColorSpaceMath.Space.XyzD65,
            GradientColorSpace.XyzD50 => ColorSpaceMath.Space.XyzD50,
            GradientColorSpace.Hsl => ColorSpaceMath.Space.Hsl,
            GradientColorSpace.Hwb => ColorSpaceMath.Space.Hwb,
            GradientColorSpace.Lch => ColorSpaceMath.Space.Lch,
            GradientColorSpace.Oklch => ColorSpaceMath.Space.Oklch,
            _ => ColorSpaceMath.Space.Srgb
        };

        private static ColorSpaceMath.HueMethod ToMathHue(HueInterpolationMethod hue) => hue switch
        {
            HueInterpolationMethod.Longer => ColorSpaceMath.HueMethod.Longer,
            HueInterpolationMethod.Increasing => ColorSpaceMath.HueMethod.Increasing,
            HueInterpolationMethod.Decreasing => ColorSpaceMath.HueMethod.Decreasing,
            _ => ColorSpaceMath.HueMethod.Shorter
        };
    }
}
