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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Parsers for the plain (non-path, non-transform) SVG attribute value grammars: <c>viewBox</c>,
    /// lengths (including percentages and unit suffixes), opacity, paint values, fill/clip rule, and
    /// <c>&lt;stop&gt;</c> color.
    /// </summary>
    internal static class SvgValueParsers
    {
        /// <summary>
        /// Absolute-unit and <c>em</c>/<c>rem</c> length suffixes, longest-suffix-first so
        /// <c>"rem"</c> is checked before the shorter <c>"em"</c> it would otherwise also match.
        /// Conversions use the standard 96 CSS px/inch. <c>em</c>/<c>rem</c> have no live CSS
        /// font-size context available to arbitrary SVG geometry attributes (unlike text, which
        /// gets real font resolution in a later phase), so they approximate CSS's own initial
        /// <c>font-size</c> (16px) - a documented v1 simplification.
        /// </summary>
        private static readonly (string Suffix, double PixelsPerUnit)[] UnitConversions =
        [
            ("rem", 16.0),
            ("em", 16.0),
            ("px", 1.0),
            ("pt", 96.0 / 72.0),
            ("pc", 16.0),
            ("in", 96.0),
            ("cm", 96.0 / 2.54),
            ("mm", 96.0 / 25.4),
        ];

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
        /// Parses a length (e.g. <c>width="299.667px"</c>, <c>"299.667"</c>, <c>"50%"</c>,
        /// <c>"2in"</c>). A percentage resolves against <paramref name="referenceLength"/> (the
        /// relevant viewport dimension - width, height, or diagonal per the SVG spec, depending on
        /// which attribute is being parsed); with no reference length available, a percentage
        /// returns null, same as if the attribute were absent.
        /// </summary>
        public static double? ParseLength(string? value, double? referenceLength = null)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();

            if (trimmed.EndsWith('%'))
            {
                if (referenceLength is not { } refLen)
                    return null;

                return double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)
                    ? refLen * pct / 100.0
                    : null;
            }

            var scale = 1.0;

            foreach (var (suffix, pixelsPerUnit) in UnitConversions)
            {
                if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed[..^suffix.Length];
                    scale = pixelsPerUnit;
                    break;
                }
            }

            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v * scale : null;
        }

        /// <summary>
        /// Parses a <c>fill-rule</c>/<c>clip-rule</c> value (<c>nonzero</c> or <c>evenodd</c>,
        /// defaulting to <c>nonzero</c> for a missing/unrecognized value) - both attributes share
        /// the same grammar.
        /// </summary>
        public static RFillMode ParseFillRule(string? value) =>
            string.Equals(value?.Trim(), "evenodd", StringComparison.OrdinalIgnoreCase) ? RFillMode.EvenOdd : RFillMode.Nonzero;

        /// <summary>Parses a <c>stroke-linecap</c> value (<c>butt</c>/<c>round</c>/<c>square</c>), defaulting to <c>butt</c>.</summary>
        public static RLineCap ParseLineCap(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "round" => RLineCap.Round,
            "square" => RLineCap.Square,
            _ => RLineCap.Butt,
        };

        /// <summary>Parses a <c>stroke-linejoin</c> value (<c>miter</c>/<c>round</c>/<c>bevel</c>), defaulting to <c>miter</c>.</summary>
        public static RLineJoin ParseLineJoin(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "round" => RLineJoin.Round,
            "bevel" => RLineJoin.Bevel,
            _ => RLineJoin.Miter,
        };

        /// <summary>
        /// Parses a <c>stroke-dasharray</c> value: <c>none</c> or a comma/whitespace-separated list of
        /// non-negative lengths (percentages resolve against <paramref name="referenceLength"/>, per
        /// the same diagonal-formula convention used for other non-axis-specific lengths like
        /// <c>stroke-width</c>). An odd-length list is duplicated to make an even count, per spec. A
        /// list of all zeros is treated the same as <c>none</c>. Returns null (caller should fall back
        /// to the inherited value) for a missing or malformed value.
        /// </summary>
        public static double[]? ParseDashArray(string? value, double? referenceLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();

            if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
                return [];

            var parts = trimmed.Split([',', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return null;

            var values = new double[parts.Length];

            for (var i = 0; i < parts.Length; i++)
            {
                if (ParseLength(parts[i], referenceLength) is not { } v || v < 0)
                    return null;

                values[i] = v;
            }

            if (values.All(v => v == 0))
                return [];

            return values.Length % 2 == 1 ? [.. values, .. values] : values;
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
        /// Parses a <c>preserveAspectRatio="[defer] &lt;align&gt; [&lt;meetOrSlice&gt;]"</c> value. An
        /// unrecognized align token falls back to the spec default (<c>xMidYMid</c>); a missing
        /// meet/slice keyword defaults to <c>meet</c>.
        /// </summary>
        public static SvgPreserveAspectRatio ParsePreserveAspectRatio(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return SvgPreserveAspectRatio.Default;

            var tokens = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var index = 0;

            if (index < tokens.Length && tokens[index].Equals("defer", StringComparison.OrdinalIgnoreCase))
                index++;

            if (index >= tokens.Length)
                return SvgPreserveAspectRatio.Default;

            var align = tokens[index].ToLowerInvariant() switch
            {
                "none" => SvgAlign.None,
                "xminymin" => SvgAlign.XMinYMin,
                "xmidymin" => SvgAlign.XMidYMin,
                "xmaxymin" => SvgAlign.XMaxYMin,
                "xminymid" => SvgAlign.XMinYMid,
                "xmidymid" => SvgAlign.XMidYMid,
                "xmaxymid" => SvgAlign.XMaxYMid,
                "xminymax" => SvgAlign.XMinYMax,
                "xmidymax" => SvgAlign.XMidYMax,
                "xmaxymax" => SvgAlign.XMaxYMax,
                _ => SvgAlign.XMidYMid,
            };
            index++;

            var slice = index < tokens.Length && tokens[index].Equals("slice", StringComparison.OrdinalIgnoreCase);

            return new SvgPreserveAspectRatio(align, slice);
        }

        /// <summary>
        /// Parses a <c>fill</c>/<c>stroke</c> paint value: <c>none</c>, <c>url(#id)</c>,
        /// <c>currentColor</c> (resolved against <paramref name="contextColor"/> - the CSS <c>color</c>
        /// property of the inline <c>&lt;svg&gt;</c>'s HTML ancestor for inline SVG, or black for
        /// standalone/<c>&lt;img&gt;</c> SVG, which has no CSS context to inherit from), or a solid
        /// color (hex/named, delegated to <see cref="CssValueParser.GetActualColor"/>).
        /// </summary>
        public static SvgPaint ParsePaint(string value, RAdapter adapter, RColor contextColor)
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

            if (trimmed.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                return SvgPaint.Solid(contextColor);

            return SvgPaint.Solid(new CssValueParser(adapter).GetActualColor(trimmed));
        }

        /// <summary>Parses a <c>spreadMethod</c> value (<c>pad</c>/<c>reflect</c>/<c>repeat</c>), defaulting to <c>pad</c>.</summary>
        public static SvgSpreadMethod ParseSpreadMethod(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "reflect" => SvgSpreadMethod.Reflect,
            "repeat" => SvgSpreadMethod.Repeat,
            _ => SvgSpreadMethod.Pad,
        };

        /// <summary>
        /// Parses one gradient coordinate (x1/y1/x2/y2/cx/cy/r/fx/fy), whose interpretation depends on
        /// the gradient's <c>gradientUnits</c>. In <c>objectBoundingBox</c> mode (the spec default), a
        /// percentage or bare number is a fraction of the referencing shape's bounding box - resolved
        /// later at paint time (see <see cref="SvgRenderer"/>), so no reference length applies here,
        /// just percentage-to-fraction conversion. In <c>userSpaceOnUse</c> mode, this is an ordinary
        /// length (see <see cref="ParseLength"/>).
        /// </summary>
        public static double? ParseGradientCoordinate(string? value, bool isObjectBoundingBox, double? userSpaceReferenceLength)
        {
            if (!isObjectBoundingBox)
                return ParseLength(value, userSpaceReferenceLength);

            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            var isPercent = trimmed.EndsWith('%');

            if (isPercent)
                trimmed = trimmed[..^1];

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return null;

            return isPercent ? v / 100.0 : v;
        }

        /// <summary>
        /// Resolves a <c>&lt;stop&gt;</c> element's color, reading <c>stop-color</c>/<c>stop-opacity</c>
        /// either as plain attributes or from a <c>style="stop-color:...; stop-opacity:..."</c> attribute
        /// (the latter overrides the former, matching CSS precedence over presentation attributes).
        /// </summary>
        public static RColor ParseStopColor(string? stopColorAttr, string? stopOpacityAttr, string? style, RAdapter adapter)
        {
            var declarations = ParseStyleDeclarations(style);
            var colorValue = declarations.GetValueOrDefault("stop-color", stopColorAttr);
            var opacityValue = declarations.GetValueOrDefault("stop-opacity", stopOpacityAttr);

            var color = string.IsNullOrWhiteSpace(colorValue)
                ? RColor.Black
                : new CssValueParser(adapter).GetActualColor(colorValue);

            var opacity = ParseOpacity(opacityValue);

            return opacity >= 1.0
                ? color
                : RColor.FromArgb((int)Math.Round(color.A * opacity), color.R, color.G, color.B);
        }

        /// <summary>
        /// Parses a generic <c>style="property: value; property2: value2"</c> attribute into a
        /// property-name-keyed lookup, for any element (not just <c>&lt;stop&gt;</c>, which is the
        /// only element this grammar was originally special-cased for). Per CSS precedence, values
        /// found here take priority over the same property specified as a bare presentation attribute
        /// - callers are expected to check this dictionary first, falling back to the plain attribute.
        /// A malformed declaration (no <c>:</c>) is silently skipped; the last declaration for a given
        /// property wins, matching normal CSS cascade-within-one-declaration-block behavior.
        /// </summary>
        public static Dictionary<string, string> ParseStyleDeclarations(string? style)
        {
            var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(style))
                return declarations;

            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colonIndex = declaration.IndexOf(':');
                if (colonIndex < 0)
                    continue;

                var property = declaration[..colonIndex].Trim();
                var value = declaration[(colonIndex + 1)..].Trim();

                if (property.Length > 0 && value.Length > 0)
                    declarations[property] = value;
            }

            return declarations;
        }

        /// <summary>Parses a <c>marker</c>/<c>marker-start</c>/<c>marker-mid</c>/<c>marker-end</c> value: <c>none</c> or a <c>url(#id)</c> reference. Any other value (unsupported/malformed) resolves to no marker.</summary>
        public static string? ParseMarkerReference(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;

            var trimmed = value.Trim();

            if (!trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
                return null;

            var hashIndex = trimmed.IndexOf('#');
            var closeIndex = trimmed.IndexOf(')');

            return hashIndex >= 0 && closeIndex > hashIndex ? trimmed[(hashIndex + 1)..closeIndex].Trim() : null;
        }
    }
}
