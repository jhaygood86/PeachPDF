// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;
using System;
using System.Globalization;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Parsers for the plain (non-path, non-transform) SVG attribute value grammars: <c>viewBox</c>,
    /// plain pixel lengths, opacity, paint values, and <c>&lt;stop&gt;</c> color.
    /// </summary>
    internal static class SvgValueParsers
    {
        /// <summary>
        /// Parses a <c>viewBox="min-x min-y width height"</c> attribute.
        /// </summary>
        public static RRect? ParseViewBox(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var pos = 0;

            if (!SvgNumberScanner.TryReadNumber(value, ref pos, out var x)) return null;
            if (!SvgNumberScanner.TryReadNumber(value, ref pos, out var y)) return null;
            if (!SvgNumberScanner.TryReadNumber(value, ref pos, out var width)) return null;
            if (!SvgNumberScanner.TryReadNumber(value, ref pos, out var height)) return null;

            return new RRect(x, y, width, height);
        }

        /// <summary>
        /// Parses a plain pixel length (e.g. <c>width="299.667px"</c> or <c>"299.667"</c>).
        /// Percentages are unsupported in v1 (returns null, same as if the attribute were absent).
        /// </summary>
        public static double? ParseLength(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();

            if (trimmed.EndsWith('%'))
                return null;

            if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^2];

            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        /// <summary>
        /// Parses an opacity value (<c>0..1</c> or <c>0%..100%</c>), clamped to [0, 1]. Defaults to 1
        /// (fully opaque) for a missing/unparseable value.
        /// </summary>
        public static double ParseOpacity(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 1.0;

            var trimmed = value.Trim();
            var isPercent = trimmed.EndsWith('%');

            if (isPercent)
                trimmed = trimmed[..^1];

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return 1.0;

            if (isPercent)
                v /= 100.0;

            return Math.Clamp(v, 0.0, 1.0);
        }

        /// <summary>
        /// Parses a <c>fill</c>/<c>stroke</c> paint value: <c>none</c>, <c>url(#id)</c>, or a solid
        /// color (hex/named, delegated to <see cref="CssValueParser.GetActualColor"/>).
        /// </summary>
        public static SvgPaint ParsePaint(string value, RAdapter adapter)
        {
            var trimmed = value.Trim();

            if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
                return SvgPaint.None;

            if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                var hashIndex = trimmed.IndexOf('#');
                var closeIndex = trimmed.IndexOf(')');

                if (hashIndex >= 0 && closeIndex > hashIndex)
                    return SvgPaint.GradientRef(trimmed[(hashIndex + 1)..closeIndex].Trim());

                return SvgPaint.None;
            }

            // currentColor: no CSS `color` inheritance is wired up for SVG in v1, so fall back to black.
            if (trimmed.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                return SvgPaint.Solid(RColor.Black);

            return SvgPaint.Solid(new CssValueParser(adapter).GetActualColor(trimmed));
        }

        /// <summary>
        /// Resolves a <c>&lt;stop&gt;</c> element's color, reading <c>stop-color</c>/<c>stop-opacity</c>
        /// either as plain attributes or from a <c>style="stop-color:...; stop-opacity:..."</c> attribute
        /// (the latter overrides the former, matching CSS precedence over presentation attributes).
        /// </summary>
        public static RColor ParseStopColor(string? stopColorAttr, string? stopOpacityAttr, string? style, RAdapter adapter)
        {
            var colorValue = stopColorAttr;
            var opacityValue = stopOpacityAttr;

            if (!string.IsNullOrWhiteSpace(style))
            {
                foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var colonIndex = declaration.IndexOf(':');
                    if (colonIndex < 0) continue;

                    var property = declaration[..colonIndex].Trim();
                    var propValue = declaration[(colonIndex + 1)..].Trim();

                    if (property.Equals("stop-color", StringComparison.OrdinalIgnoreCase))
                        colorValue = propValue;
                    else if (property.Equals("stop-opacity", StringComparison.OrdinalIgnoreCase))
                        opacityValue = propValue;
                }
            }

            var color = string.IsNullOrWhiteSpace(colorValue)
                ? RColor.Black
                : new CssValueParser(adapter).GetActualColor(colorValue);

            var opacity = ParseOpacity(opacityValue);

            return opacity >= 1.0
                ? color
                : RColor.FromArgb((int)Math.Round(color.A * opacity), color.R, color.G, color.B);
        }
    }
}
