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
using PeachPDF.CSS;
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
        /// What 1em/1rem are, in user units, when the caller has no font in scope: CSS's own initial
        /// <c>font-size</c> (16px). A caller that does have one passes an <see cref="ISvgLengthBasis"/>.
        /// </summary>
        public const double DefaultEmPx = 16.0;

        /// <summary>
        /// The font-relative unit suffixes measured from the used font (CSS Values and Units 4 §6.1.1), longest
        /// first so <c>"rcap"</c>/<c>"rex"</c>/<c>"ric"</c> are matched before the shorter suffix each ends with.
        /// </summary>
        private static readonly (string Suffix, Length.Unit Unit)[] FontRelativeSuffixes =
        [
            ("rcap", Length.Unit.Rcap),
            ("rlh", Length.Unit.Rlh),
            ("rch", Length.Unit.Rch),
            ("rex", Length.Unit.Rex),
            ("ric", Length.Unit.Ric),
            ("cap", Length.Unit.Cap),
            ("ex", Length.Unit.Ex),
            ("ch", Length.Unit.Ch),
            ("ic", Length.Unit.Ic),
            ("lh", Length.Unit.Lh),
        ];

        /// <summary>
        /// Splits a trailing <c>ex</c>/<c>ch</c>/<c>cap</c>/<c>ic</c>/<c>lh</c>/<c>rex</c>/<c>rch</c>/<c>rcap</c>/
        /// <c>ric</c>/<c>rlh</c> unit off <paramref name="value"/>. False when there is none, or the rest is not a number.
        /// </summary>
        internal static bool TryParseMeasuredUnit(string value, out double number, out Length.Unit unit)
        {
            foreach (var (suffix, suffixUnit) in FontRelativeSuffixes)
            {
                if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(value[..^suffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    unit = suffixUnit;
                    return true;
                }
            }

            number = 0;
            unit = default;
            return false;
        }

        /// <summary>
        /// Resolves <paramref name="number"/> of a measured unit to user units: its em multiplied by the basis'
        /// em, or the root's for the <c>r*</c> variants. Every unit's conversion lives in <see cref="Length.ToPixels"/>.
        /// </summary>
        internal static double ResolveMeasuredUnit(double number, Length.Unit unit, ISvgLengthBasis? basis) =>
            number * new Length(1f, unit).ToPixels(basis?.EmPx ?? DefaultEmPx, basis?.RootEmPx ?? DefaultEmPx, 0, fonts: basis);

        /// <summary>
        /// Absolute-unit length suffixes, converted at the standard 96 CSS px/inch. <c>em</c>/<c>rem</c> and the
        /// measured font-relative units are handled ahead of this table (see <see cref="ParseLength"/>).
        /// </summary>
        private static readonly (string Suffix, double PixelsPerUnit)[] UnitConversions =
        [
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
        /// returns null, same as if the attribute were absent. Font-relative units (<c>em</c>/<c>ex</c>/<c>ch</c>/
        /// <c>cap</c>/<c>ic</c>/<c>lh</c> and the root-element <c>rem</c>/<c>rex</c>/...) resolve against
        /// <paramref name="basis"/>, the element's own font; with none they take
        /// <see cref="DefaultEmPx"/> and each unit's spec fallback.
        /// </summary>
        public static double? ParseLength(string? value, double? referenceLength = null, ISvgLengthBasis? basis = null)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();

            // calc()/min()/max()/clamp() are delegated to the shared CSS length parser (which owns the
            // whole calc grammar + evaluator) rather than duplicated here. That parser works in points
            // (1px = 0.75pt), while SVG lengths here are CSS px (1px = 1 user unit), so the percentage/
            // em/rem bases are converted into points on the way in and the result back to px on the way
            // out. A percentage inside the calc resolves against the SVG reference length; em/rem and the
            // measured font-relative units resolve against the element's own font (basis), or the 16px initial
            // size with no basis. Any var() has already been substituted by SvgTreeBuilder.ResolveStyledAttr
            // before this point.
            if (IsCalcExpression(trimmed))
            {
                const double pointsPerPx = 0.75; // Length.PointsPerPx: 1px = 1/96in = 0.75pt
                var points = CssValueParser.ParseLength(
                    trimmed,
                    hundredPercent: (referenceLength ?? 0) * pointsPerPx,
                    emFactor: (basis?.EmPx ?? DefaultEmPx) * pointsPerPx,
                    remFactor: (basis?.RootEmPx ?? DefaultEmPx) * pointsPerPx,
                    defaultUnit: null,
                    returnPoints: false,
                    fonts: basis);
                return points / pointsPerPx;
            }

            if (trimmed.EndsWith('%'))
            {
                if (referenceLength is not { } refLen)
                    return null;

                return double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)
                    ? refLen * pct / 100.0
                    : null;
            }

            if (TryParseMeasuredUnit(trimmed, out var measured, out var measuredUnit))
                return ResolveMeasuredUnit(measured, measuredUnit, basis);

            // rem is checked before the shorter em it ends with.
            if (trimmed.EndsWith("rem", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(trimmed[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rems))
                return rems * (basis?.RootEmPx ?? DefaultEmPx);

            if (trimmed.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(trimmed[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ems))
                return ems * (basis?.EmPx ?? DefaultEmPx);

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
        /// Parses a whitespace/comma-separated list of lengths (the <c>x</c>/<c>y</c>/<c>dx</c>/<c>dy</c>
        /// per-character positioning grammar, SVG 1.1 §10.4): each token is parsed as a
        /// <see cref="ParseLength"/> (so unit suffixes and percentages are honored), a non-parseable
        /// token contributing 0. Returns null for a missing/empty value.
        /// </summary>
        public static double[]? ParseLengthList(string? value, double? referenceLength = null, ISvgLengthBasis? basis = null)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var parts = value.Trim().Split([',', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return null;

            var values = new double[parts.Length];
            for (var i = 0; i < parts.Length; i++)
                values[i] = ParseLength(parts[i], referenceLength, basis) ?? 0;

            return values;
        }

        /// <summary>
        /// Parses a whitespace/comma-separated list of unitless numbers (the <c>rotate</c> per-character
        /// grammar, SVG 1.1 §10.4), scanning with the shared <see cref="SvgNumberScanner"/> like
        /// <c>SvgPointsParser</c>. Returns null for a missing/empty value.
        /// </summary>
        public static double[]? ParseNumberList(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var values = new List<double>();
            var pos = 0;
            while (SvgNumberScanner.TryReadNumber(value, ref pos, out var number))
            {
                values.Add(number);
                SvgNumberScanner.SkipSeparators(value, ref pos);
            }

            return values.Count == 0 ? null : [.. values];
        }

        /// <summary>Is <paramref name="value"/> a CSS math function (<c>calc</c>/<c>min</c>/<c>max</c>/<c>clamp</c>) call?</summary>
        private static bool IsCalcExpression(string value) =>
            value.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("max(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Parses a <c>fill-rule</c>/<c>clip-rule</c> value (<c>nonzero</c> or <c>evenodd</c>,
        /// defaulting to <c>nonzero</c> for a missing/unrecognized value) - both attributes share
        /// the same grammar.
        /// </summary>
        public static RFillMode ParseFillRule(string? value)
        {
            TryParseFillRule(value, out var mode);
            return mode;
        }

        /// <summary>
        /// Validity-returning sibling of <see cref="ParseFillRule"/>: <see langword="true"/> only for
        /// the two real keywords. <c>@supports</c>'s render-layer oracle needs this signal, which the
        /// lenient <see cref="ParseFillRule"/> (used by real rendering, which always wants a value even
        /// for a bad one) discards.
        /// </summary>
        public static bool TryParseFillRule(string? value, out RFillMode mode)
        {
            var trimmed = value?.Trim();

            if (string.Equals(trimmed, "nonzero", StringComparison.OrdinalIgnoreCase)) { mode = RFillMode.Nonzero; return true; }
            if (string.Equals(trimmed, "evenodd", StringComparison.OrdinalIgnoreCase)) { mode = RFillMode.EvenOdd; return true; }

            mode = RFillMode.Nonzero;
            return false;
        }

        /// <summary>Parses a <c>stroke-linecap</c> value (<c>butt</c>/<c>round</c>/<c>square</c>), defaulting to <c>butt</c>.</summary>
        public static RLineCap ParseLineCap(string? value)
        {
            TryParseLineCap(value, out var cap);
            return cap;
        }

        /// <summary>Validity-returning sibling of <see cref="ParseLineCap"/> — see <see cref="TryParseFillRule"/>'s remarks.</summary>
        public static bool TryParseLineCap(string? value, out RLineCap cap)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "butt": cap = RLineCap.Butt; return true;
                case "round": cap = RLineCap.Round; return true;
                case "square": cap = RLineCap.Square; return true;
                default: cap = RLineCap.Butt; return false;
            }
        }

        /// <summary>Parses a <c>stroke-linejoin</c> value (<c>miter</c>/<c>round</c>/<c>bevel</c>), defaulting to <c>miter</c>.</summary>
        public static RLineJoin ParseLineJoin(string? value)
        {
            TryParseLineJoin(value, out var join);
            return join;
        }

        /// <summary>Validity-returning sibling of <see cref="ParseLineJoin"/> — see <see cref="TryParseFillRule"/>'s remarks.</summary>
        public static bool TryParseLineJoin(string? value, out RLineJoin join)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "miter": join = RLineJoin.Miter; return true;
                case "round": join = RLineJoin.Round; return true;
                case "bevel": join = RLineJoin.Bevel; return true;
                default: join = RLineJoin.Miter; return false;
            }
        }

        /// <summary>
        /// Parses a <c>stroke-dasharray</c> value: <c>none</c> or a comma/whitespace-separated list of
        /// non-negative lengths (percentages resolve against <paramref name="referenceLength"/>, per
        /// the same diagonal-formula convention used for other non-axis-specific lengths like
        /// <c>stroke-width</c>). An odd-length list is duplicated to make an even count, per spec. A
        /// list of all zeros is treated the same as <c>none</c>. Returns null (caller should fall back
        /// to the inherited value) for a missing or malformed value.
        /// </summary>
        public static double[]? ParseDashArray(string? value, double? referenceLength, ISvgLengthBasis? basis = null)
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
                if (ParseLength(parts[i], referenceLength, basis) is not { } v || v < 0)
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
            TryParseOpacity(value, out var opacity);
            return opacity;
        }

        /// <summary>Validity-returning sibling of <see cref="ParseOpacity"/> — see <see cref="TryParseFillRule"/>'s remarks.</summary>
        public static bool TryParseOpacity(string? value, out double opacity)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                opacity = 1.0;
                return false;
            }

            var trimmed = value.Trim();
            var isPercent = trimmed.EndsWith('%');

            if (isPercent)
                trimmed = trimmed[..^1];

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                opacity = 1.0;
                return false;
            }

            if (isPercent)
                v /= 100.0;

            opacity = Math.Clamp(v, 0.0, 1.0);
            return true;
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
            TryParsePaint(value, adapter, contextColor, out var paint);
            return paint;
        }

        /// <summary>
        /// Validity-returning sibling of <see cref="ParsePaint"/> — see <see cref="TryParseFillRule"/>'s
        /// remarks. <see langword="false"/> for a malformed <c>url(...)</c> (no <c>#id</c>) or a color
        /// string <see cref="CssValueParser.IsColorValid"/> rejects; <c>paint</c> still gets the same
        /// fallback <see cref="ParsePaint"/> would have returned, for callers that want a value regardless.
        /// </summary>
        public static bool TryParsePaint(string value, RAdapter adapter, RColor contextColor, out SvgPaint paint)
        {
            var trimmed = value.Trim();

            if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                paint = SvgPaint.None;
                return true;
            }

            if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                var hashIndex = trimmed.IndexOf('#');
                var closeIndex = trimmed.IndexOf(')');

                if (hashIndex >= 0 && closeIndex > hashIndex)
                {
                    paint = SvgPaint.GradientRef(trimmed[(hashIndex + 1)..closeIndex].Trim());
                    return true;
                }

                paint = SvgPaint.None;
                return false;
            }

            if (trimmed.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            {
                paint = SvgPaint.Solid(contextColor);
                return true;
            }

            var valueParser = new CssValueParser(adapter);
            paint = SvgPaint.Solid(valueParser.GetActualColor(trimmed));
            return valueParser.IsColorValid(trimmed);
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
        /// length (see <see cref="ParseLength"/>), so <paramref name="basis"/> resolves its font-relative units.
        /// </summary>
        public static double? ParseGradientCoordinate(string? value, bool isObjectBoundingBox, double? userSpaceReferenceLength, ISvgLengthBasis? basis = null)
        {
            if (!isObjectBoundingBox)
                return ParseLength(value, userSpaceReferenceLength, basis);

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
            var colorValue = declarations.TryGetValue("stop-color", out var colorFromStyle) ? colorFromStyle : stopColorAttr;
            var opacityValue = declarations.TryGetValue("stop-opacity", out var opacityFromStyle) ? opacityFromStyle : stopOpacityAttr;

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
