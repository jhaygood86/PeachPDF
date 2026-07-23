using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using System;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Gradient color-stop interpolation across CSS Color 4 interpolation spaces. The actual color-space
    /// transform math lives once in <see cref="ColorSpaceMath"/> (CSS-OM); this render-layer type just
    /// maps the gradient enums onto it and interpolates each channel (with polar-hue handling), lerping
    /// alpha separately. Note this is <b>not</b> premultiplied — unlike <see cref="Color.Mix(Color, Color)"/>
    /// (which premultiplies per CSS Color 5 color-mix). Gradient-stop premultiplication (CSS Images 4 §3.2)
    /// only matters for translucent stops and is a pre-existing, separate gap.
    /// </summary>
    internal static class ColorSpaceConverter
    {
        public static RColor Interpolate(
            RColor c1, RColor c2, double t,
            GradientColorSpace cs,
            HueInterpolationMethod hue)
        {
            t = Math.Clamp(t, 0.0, 1.0);

            var a = (byte)Math.Round(Math.Clamp(c1.A + t * (c2.A - c1.A), 0, 255));

            var space = ToMathSpace(cs);
            var v1 = ColorSpaceMath.ToSpace(c1.R / 255.0, c1.G / 255.0, c1.B / 255.0, space);
            var v2 = ColorSpaceMath.ToSpace(c2.R / 255.0, c2.G / 255.0, c2.B / 255.0, space);
            var v = new double[3];

            var hueIndex = ColorSpaceMath.HueIndex(space);
            for (var i = 0; i < 3; i++)
            {
                v[i] = i == hueIndex
                    ? ColorSpaceMath.InterpolateHue(v1[i], v2[i], t, ToMathHue(hue))
                    : v1[i] + t * (v2[i] - v1[i]);
            }

            var (r, g, b) = ColorSpaceMath.FromSpace(v, space);
            return RColor.FromArgb(a,
                (int)Math.Round(r * 255),
                (int)Math.Round(g * 255),
                (int)Math.Round(b * 255));
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
