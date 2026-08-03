using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.SourceGenerators.Model;

namespace PeachPDF.SourceGenerators.Emit
{
    /// <summary>
    /// Turns a <see cref="PropertyEntry"/>'s <c>cssDataType</c> union into a C# boolean expression,
    /// reusing PeachPDF's existing real value grammar (<c>CssValueParser</c>/<c>SvgValueParsers</c>)
    /// per this repo's "one parser" convention rather than re-deriving length/color/keyword validity —
    /// see CLAUDE.md. <c>parser</c>/<c>ctx</c> below are the fixed parameter names the emitted
    /// Validate_*/Set_* methods declare (see <see cref="RegistryEmitter"/>), not JSON tokens.
    /// </summary>
    internal static class ValidatorExpressionBuilder
    {
        public static string BuildHtml(PropertyEntry entry) =>
            BuildHtml(entry, entry.CssDataTypes, entry.SupportedValues, entry.KeywordComparison);

        public static string BuildSvg(PropertyEntry entry) =>
            string.Join(" || ", entry.CssDataTypes.Select(dt => BuildClause(entry, dt, isSvg: true, entry.SupportedValues, entry.KeywordComparison)));

        /// <summary>Entry point for a Supports_* override (<see cref="PropertyEntry.SupportsCssDataTypes"/>) — the same
        /// clause logic against an explicit data-type/keyword list instead of the entry's base grammar.</summary>
        public static string BuildHtml(PropertyEntry entry, IReadOnlyList<DataTypeSpec> dataTypes,
            IReadOnlyList<string>? supportedValues, KeywordComparison keywordComparison) =>
            string.Join(" || ", dataTypes.Select(dt => BuildClause(entry, dt, isSvg: false, supportedValues, keywordComparison)));

        private static string BuildClause(PropertyEntry entry, DataTypeSpec dt, bool isSvg,
            IReadOnlyList<string>? supportedValues, KeywordComparison keywordComparison) => dt.Kind switch
        {
            DataTypeKind.CssOm => BuildCssOmClause(entry, isSvg),
            DataTypeKind.Unsupported => "false",
            DataTypeKind.Length => "global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidLength(value)",
            DataTypeKind.Color => "parser.IsColorValid(value)",
            DataTypeKind.CurrentColor => "value.Equals(\"currentcolor\", global::System.StringComparison.OrdinalIgnoreCase)",
            DataTypeKind.Transform => "global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidTransformValue(value)",
            DataTypeKind.Keyword => BuildKeywordClause(supportedValues, keywordComparison),
            DataTypeKind.Integer => BuildIntegerClause(dt),
            DataTypeKind.Number => "double.TryParse(value, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out _)",
            DataTypeKind.EnumKeyword => $"{dt.KeywordMap}.ContainsKey(value)",
            DataTypeKind.KeywordOrValue => BuildKeywordOrValueClause(entry, dt),
            DataTypeKind.SvgPaint => "global::PeachPDF.Svg.SvgValueParsers.TryParsePaint(value, ctx.Adapter, ctx.ContextColor, out _)",
            DataTypeKind.SvgOpacity => "global::PeachPDF.Svg.SvgValueParsers.TryParseOpacity(value, out _)",
            DataTypeKind.SvgLength => "global::PeachPDF.Svg.SvgValueParsers.ParseLength(value, ctx.ViewportDiagonal) is not null",
            DataTypeKind.SvgLengthList => "global::PeachPDF.Svg.SvgValueParsers.ParseDashArray(value, ctx.ViewportDiagonal) is not null",
            _ => throw new NotSupportedException(
                $"DataTypeKind.{dt.Kind} is not yet implemented by RegistryEmitter (property \"{entry.Name}\") — " +
                "add its codegen to ValidatorExpressionBuilder before authoring an entry that uses it."),
        };

        /// <summary>
        /// "cssom" means "no dedicated grammar modeled here — ask the real CSS-OM property for this
        /// name instead," not "accept anything": <c>PropertyFactory.Instance.Create(name)</c> gives the
        /// same <c>Property</c> subclass Layer A's own stylesheet parser would construct for this
        /// declaration, and <c>StylesheetParser.Default.ParseValue</c> tokenizes <c>value</c> exactly as
        /// stylesheet parsing does, so <c>TrySetValue</c> runs that property's genuine grammar. If Layer A
        /// has no property under this name at all (a PeachPDF-only extension like
        /// <c>-peachpdf-pdf-tag-type</c>, never registered in <c>PropertyFactory</c>), there is no CSS-OM
        /// grammar to defer to, so the property's own registration in this file (the caller already found
        /// it by name before reaching here) is the only fact available and the clause accepts. On the SVG
        /// side there is no CSS-OM equivalent to delegate to at all — every current "cssom" entry with an
        /// svg binding (e.g. opacity) overrides this with its own customValidator.
        /// </summary>
        private static string BuildCssOmClause(PropertyEntry entry, bool isSvg)
        {
            if (isSvg) return "true";

            var name = Escape(entry.Name);
            return $"global::PeachPDF.CSS.PropertyFactory.Instance.Create(\"{name}\") is not {{ }} knownProperty || " +
                   "(global::PeachPDF.CSS.StylesheetParser.Default.ParseValue(value) is { } tokenValue && knownProperty.TrySetValue(tokenValue))";
        }

        private static string BuildKeywordClause(IReadOnlyList<string>? supportedValues, KeywordComparison keywordComparison)
        {
            var values = supportedValues ?? Array.Empty<string>();
            if (values.Count == 0) return "false";

            if (keywordComparison == KeywordComparison.Ordinal)
            {
                var pattern = string.Join(" or ", values.Select(v => $"\"{Escape(v)}\""));
                return $"value is {pattern}";
            }

            var comparison = keywordComparison == KeywordComparison.OrdinalIgnoreCase
                ? "global::System.StringComparison.OrdinalIgnoreCase"
                : "global::System.StringComparison.InvariantCultureIgnoreCase";

            return string.Join(" || ", values.Select(v => $"value.Equals(\"{Escape(v)}\", {comparison})"));
        }

        /// <summary>A "&lt;value&gt; | keyword" union (e.g. <c>z-index</c>'s <c>&lt;integer&gt; | auto</c>) —
        /// the keyword side is matched case-insensitively via its own <see cref="DataTypeSpec.KeywordMap"/>
        /// (same as <see cref="DataTypeKind.EnumKeyword"/>), the value side reuses the real grammar for
        /// <see cref="DataTypeSpec.ValueType"/> (<see cref="KeywordOrValueGrammar"/>).</summary>
        private static string BuildKeywordOrValueClause(PropertyEntry entry, DataTypeSpec dt) =>
            $"{dt.KeywordMap}.ContainsKey(value) || {KeywordOrValueGrammar.Resolve(entry, dt).ValueClause}";

        private static string BuildIntegerClause(DataTypeSpec dt)
        {
            var clause = "int.TryParse(value, out var parsedInt)";
            if (dt.Min.HasValue) clause += $" && parsedInt >= {(int)dt.Min.Value}";
            if (dt.Max.HasValue) clause += $" && parsedInt <= {(int)dt.Max.Value}";
            return clause;
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
