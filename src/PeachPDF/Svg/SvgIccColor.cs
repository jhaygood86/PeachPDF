using System;
using System.Collections.Generic;
using System.Globalization;
using PeachDrawing.Core;
using PeachImage;

namespace PeachPDF.Svg
{
    /// <summary>
    /// SVG 1.1 §11.2 <c>&lt;color&gt; icc-color(profile-name, c1, c2, ...)</c>: one <c>&lt;color-profile&gt;</c>
    /// registered by name, and the rewrite that turns a value carrying an <c>icc-color()</c> into the plain
    /// sRGB color everything downstream already understands. When the profile is unknown or unusable (wrong
    /// component count, a colorspace the converter does not handle, a malformed list) the sRGB fallback that
    /// precedes the <c>icc-color()</c> is used, which is exactly what the specification asks of a user agent
    /// that cannot honor it.
    /// </summary>
    internal sealed class SvgIccColor
    {
        private const string Function = "icc-color(";

        private readonly IccColorProfile _profile;
        private readonly IccRenderingIntent? _intent;

        public SvgIccColor(IccColorProfile profile, IccRenderingIntent? intent)
        {
            _profile = profile;
            _intent = intent;
        }

        /// <summary>Maps a <c>&lt;color-profile&gt;</c> <c>rendering-intent</c>; <c>auto</c>/absent/unknown is null (the profile's own default).</summary>
        public static IccRenderingIntent? ParseRenderingIntent(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "perceptual" => IccRenderingIntent.Perceptual,
            "relative-colorimetric" => IccRenderingIntent.RelativeColorimetric,
            "saturation" => IccRenderingIntent.Saturation,
            "absolute-colorimetric" => IccRenderingIntent.AbsoluteColorimetric,
            _ => null,
        };

        /// <summary>
        /// Rewrites <paramref name="value"/> when it contains an <c>icc-color()</c>: to <c>rgb(r, g, b)</c> when the named
        /// profile in <paramref name="profiles"/> converts the components, otherwise to the leading sRGB fallback alone.
        /// A value with no <c>icc-color()</c> is returned unchanged.
        /// </summary>
        public static string? Rewrite(string? value, IReadOnlyDictionary<string, SvgIccColor>? profiles)
        {
            if (value is null)
                return null;

            var start = value.IndexOf(Function, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return value;

            var fallback = value[..start].Trim();
            var end = value.IndexOf(')', start);
            if (end < 0)
                return fallback;

            var arguments = value.Substring(start + Function.Length, end - start - Function.Length).Split(',');
            if (profiles is not null
                && profiles.TryGetValue(arguments[0].Trim().ToLowerInvariant(), out var profile)
                && profile.TryConvert(arguments.AsSpan(1), out var r, out var g, out var b))
                return string.Create(CultureInfo.InvariantCulture, $"rgb({r}, {g}, {b})");

            return fallback;
        }

        /// <summary>Converts the <c>icc-color()</c> components (each 0..1) to 8-bit sRGB, or false when they do not fit the profile.</summary>
        private bool TryConvert(ReadOnlySpan<string> components, out int r, out int g, out int b)
        {
            r = g = b = 0;

            if (components.Length != _profile.ChannelCount)
                return false;

            Span<byte> device = stackalloc byte[_profile.ChannelCount];
            for (var i = 0; i < device.Length; i++)
            {
                if (!double.TryParse(components[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var component)
                    || double.IsNaN(component))
                    return false;

                device[i] = (byte)Math.Round(Math.Clamp(component, 0.0, 1.0) * 255.0);
            }

            Span<byte> srgb = stackalloc byte[4];
            try
            {
                _profile.ConvertToSrgb(device, srgb, 1, _intent);
            }
            catch (Exception e) when (e is NotSupportedException or ArgumentException or IccProfileException)
            {
                return false;
            }

            r = srgb[0];
            g = srgb[1];
            b = srgb[2];
            return true;
        }
    }
}
