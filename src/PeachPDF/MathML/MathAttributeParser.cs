#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Grammar for MathML's own attribute value types (MathML 3 §2.1 "Attribute Value Types" /
// MathML Core's equivalent). These are plain XML attribute strings, not CSS properties - MathML Core
// does not map mathvariant/displaystyle/scriptlevel/stretchy/etc. onto CSS, so there is no
// css-properties.json/registry involvement here (see the architecture note in the implementation plan:
// genuine CSS properties like color/font-family already reach <math> content through the ordinary HTML
// cascade before this parser ever runs). Each parser here is self-contained and returns null/a default
// on anything it doesn't recognize, matching MathML's own "ignore invalid attribute, use the initial
// value" error-handling model rather than throwing.
//
#endregion

using System.Globalization;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.MathML
{
    internal static class MathAttributeParser
    {
        /// <summary>
        /// Parses a MathML length (MathML 3 §2.1.5): a signed number followed by one of the CSS-like
        /// unit suffixes (<c>em</c>/<c>ex</c>/<c>px</c>/<c>in</c>/<c>cm</c>/<c>mm</c>/<c>pt</c>/<c>pc</c>/<c>%</c>),
        /// or one of the seven named "math space" keywords (<c>thinmathspace</c> ...
        /// <c>veryverythickmathspace</c>, and their <c>negative...</c> variants) - all expressed in
        /// <see cref="MathLengthUnit.Mu"/> (1 mu = 1/18 em), per MathML Core's own definition. A bare
        /// <c>"0"</c> with no unit is accepted (the one unitless value MathML Core permits); any other
        /// unitless number is invalid.
        /// </summary>
        public static MathLength? TryParseLength(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();

            if (NamedSpaces.TryGetValue(trimmed, out var namedMu))
                return new MathLength(namedMu, MathLengthUnit.Mu);

            if (trimmed == "0")
                return MathLength.Zero;

            foreach (var (suffix, unit) in UnitSuffixes)
            {
                if (trimmed.EndsWith(suffix, System.StringComparison.Ordinal) &&
                    double.TryParse(trimmed[..^suffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var withUnit))
                {
                    return new MathLength(withUnit, unit);
                }
            }

            return null;
        }

        static readonly (string Suffix, MathLengthUnit Unit)[] UnitSuffixes =
        [
            // "%" and "pc" must be checked before "px"/"pt"/"p"-prefixed ones only insofar as none of
            // these suffixes are prefixes of one another except through this ordering being irrelevant -
            // every suffix here is checked independently via EndsWith, so order doesn't affect matching.
            ("%", MathLengthUnit.Percent),
            ("em", MathLengthUnit.Em),
            ("ex", MathLengthUnit.Ex),
            ("px", MathLengthUnit.Px),
            ("in", MathLengthUnit.In),
            ("cm", MathLengthUnit.Cm),
            ("mm", MathLengthUnit.Mm),
            ("pt", MathLengthUnit.Pt),
            ("pc", MathLengthUnit.Pc),
        ];

        static readonly System.Collections.Generic.Dictionary<string, double> NamedSpaces = new()
        {
            ["veryverythinmathspace"] = 1,
            ["verythinmathspace"] = 2,
            ["thinmathspace"] = 3,
            ["mediummathspace"] = 4,
            ["thickmathspace"] = 5,
            ["verythickmathspace"] = 6,
            ["veryverythickmathspace"] = 7,
            ["negativeveryverythinmathspace"] = -1,
            ["negativeverythinmathspace"] = -2,
            ["negativethinmathspace"] = -3,
            ["negativemediummathspace"] = -4,
            ["negativethickmathspace"] = -5,
            ["negativeverythickmathspace"] = -6,
            ["negativeveryverythickmathspace"] = -7,
        };

        /// <summary>Parses a MathML boolean attribute (<c>"true"</c>/<c>"false"</c>), or null if absent/invalid.</summary>
        public static bool? TryParseBool(string? value) => value?.Trim() switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };

        /// <summary><c>&lt;mo&gt;</c>'s <c>form</c> attribute: <c>"prefix"</c>/<c>"infix"</c>/<c>"postfix"</c>, or null if absent/invalid.</summary>
        public static string? TryParseForm(string? value) => value?.Trim() switch
        {
            "prefix" or "infix" or "postfix" => value.Trim(),
            _ => null,
        };

        /// <summary>
        /// Parses <c>scriptlevel</c> (MathML 3 §3.3.4): a signed <c>+n</c>/<c>-n</c> is a delta applied
        /// to <paramref name="inherited"/>; a plain (unsigned) integer is an absolute level. Returns
        /// <paramref name="inherited"/> unchanged if absent/invalid.
        /// </summary>
        public static int ParseScriptLevel(string? value, int inherited)
        {
            if (string.IsNullOrWhiteSpace(value))
                return inherited;

            var trimmed = value.Trim();
            if ((trimmed.StartsWith('+') || trimmed.StartsWith('-')) &&
                int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta))
            {
                return inherited + delta;
            }

            return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var absolute)
                ? absolute
                : inherited;
        }

        /// <summary>Parses <c>displaystyle</c>/the effective display mode from <c>&lt;math display="..."&gt;</c>,
        /// or returns <paramref name="inherited"/> unchanged if absent/invalid.</summary>
        public static bool ParseDisplayStyle(string? value, bool inherited) => TryParseBool(value) ?? inherited;

        /// <summary>Parses <c>mathsize</c> - a MathML length, or one of the legacy <c>"small"</c>/
        /// <c>"normal"</c>/<c>"big"</c> keywords (0.71/1/1.41 em respectively, MathML 3 §3.2.2's own
        /// suggested ratios) - relative to <paramref name="inheritedSizePt"/> (already-resolved points,
        /// read off the cascaded CSS <c>font-size</c>). Returns <paramref name="inheritedSizePt"/>
        /// unchanged if absent/invalid.
        /// </summary>
        public static double ParseMathSize(string? value, double inheritedSizePt)
        {
            if (string.IsNullOrWhiteSpace(value))
                return inheritedSizePt;

            var trimmed = value.Trim();
            var keywordRatio = trimmed switch
            {
                "small" => 0.71,
                "normal" => 1.0,
                "big" => 1.41,
                _ => (double?)null,
            };
            if (keywordRatio is { } ratio)
                return inheritedSizePt * ratio;

            if (TryParseLength(trimmed) is not { } length)
                return inheritedSizePt;

            return length.Unit switch
            {
                MathLengthUnit.Pt => length.Value,
                MathLengthUnit.Px => length.Value * 0.75,
                MathLengthUnit.Em => length.Value * inheritedSizePt,
                MathLengthUnit.Percent => length.Value / 100.0 * inheritedSizePt,
                MathLengthUnit.In => length.Value * 72,
                MathLengthUnit.Cm => length.Value * 72 / 2.54,
                MathLengthUnit.Mm => length.Value * 72 / 25.4,
                MathLengthUnit.Pc => length.Value * 12,
                _ => inheritedSizePt, // ex/mu are meaningless for a font-size itself
            };
        }

        /// <summary>Parses <c>mathcolor</c>/<c>mathbackground</c> as ordinary CSS <c>&lt;color&gt;</c>
        /// syntax (MathML Core §2.2 explicitly reuses the CSS Color grammar) - reuses the real CSS color
        /// parser rather than a second, independently-derived one. Returns null if absent/invalid
        /// (background specifically also treats the CSS keyword <c>"transparent"</c> as "no color",
        /// consistent with how it's actually used - MathML 3's own <c>"none"</c> keyword also means this).
        /// </summary>
        public static RColor? TryParseColor(string? value, RAdapter adapter)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() is "none" or "transparent")
                return null;

            var parsed = new Html.Core.Parse.CssValueParser(adapter).GetActualColor(value.Trim());
            return parsed.IsEmpty ? null : parsed;
        }
    }
}
