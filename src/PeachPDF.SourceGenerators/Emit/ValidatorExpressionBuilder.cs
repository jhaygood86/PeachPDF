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
            string.Join(" || ", entry.CssDataTypes.Select(dt => BuildClause(entry, dt, entry.SupportedValues, entry.KeywordComparison)));

        /// <summary>Entry point for a Supports_* override (<see cref="PropertyEntry.SupportsCssDataTypes"/>) — the same
        /// clause logic against an explicit data-type/keyword list instead of the entry's base grammar.</summary>
        public static string BuildHtml(PropertyEntry entry, IReadOnlyList<DataTypeSpec> dataTypes,
            IReadOnlyList<string>? supportedValues, KeywordComparison keywordComparison) =>
            string.Join(" || ", dataTypes.Select(dt => BuildClause(entry, dt, supportedValues, keywordComparison)));

        private static string BuildClause(PropertyEntry entry, DataTypeSpec dt,
            IReadOnlyList<string>? supportedValues, KeywordComparison keywordComparison) => dt.Kind switch
        {
            DataTypeKind.Unsupported => "false",
            DataTypeKind.Length => "global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidLength(value)",
            DataTypeKind.Color => "parser.IsColorValid(value)",
            DataTypeKind.CurrentColor => "value.Equals(\"currentcolor\", global::System.StringComparison.OrdinalIgnoreCase)",
            DataTypeKind.Transform => "global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidTransformValue(value)",
            DataTypeKind.Ratio => "global::PeachPDF.CSS.AspectRatioGrammar.TryParseFast(value, out _, out _)",
            DataTypeKind.TransformList => "global::PeachPDF.Html.Core.Parse.CssValueParser.IsSyntacticallyValidTransformList(value)",
            DataTypeKind.CustomIdentOrAuto => "global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidPageName(value)",
            DataTypeKind.LengthList => BuildLengthListClause(dt),
            DataTypeKind.KeywordList => BuildKeywordListClause(dt),
            DataTypeKind.Keyword => BuildKeywordClause(supportedValues, keywordComparison),
            DataTypeKind.Integer => BuildIntegerClause(dt),
            DataTypeKind.Number => "double.TryParse(value, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out _)",
            DataTypeKind.CssOmGrammar => BuildCssOmGrammarClause(dt),
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
        /// <see cref="DataTypeKind.CssOmGrammar"/>: <see cref="DataTypeSpec.Converter"/> names a fully
        /// invokable member — either a static <c>TryParse(IReadOnlyList&lt;Token&gt;)</c>-shaped method
        /// (e.g. <c>PeachPDF.CSS.BasicShapeGrammar.TryParse</c>) or an <c>IValueConverter</c> field's
        /// <c>.Convert</c> included in the string itself (e.g.
        /// <c>PeachPDF.CSS.Converters.MultipleImageSourceConverter.Convert</c>) — so the generator emits
        /// one uniform call shape without needing to resolve which kind it is via the compilation's
        /// symbol table. <c>GetCssTokens</c> returns a <c>List&lt;Token&gt;</c>, which satisfies both a
        /// <c>TryParse(IReadOnlyList&lt;Token&gt;)</c> parameter and an <c>IValueConverter.Convert(IEnumerable
        /// &lt;Token&gt;)</c> parameter directly, with no wrapping.
        /// </summary>
        private static string BuildCssOmGrammarClause(DataTypeSpec dt)
        {
            var call = $"global::{dt.Converter}(global::PeachPDF.Html.Core.Parse.CssValueParser.GetCssTokens(value, inValueContext: true, preserveWhitespace: true)) is not null";
            return dt.AcceptsNoneLiteral
                ? $"value.Equals(\"none\", global::System.StringComparison.OrdinalIgnoreCase) || {call}"
                : call;
        }

        private static string BuildLengthListClause(DataTypeSpec dt) =>
            $"global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidLengthList(value, {dt.MinCount ?? 1}, {dt.MaxCount ?? 2}, {(dt.AllowPercentage ? "true" : "false")})";

        private static string BuildKeywordListClause(DataTypeSpec dt)
        {
            var max = dt.MaxPerSegment ?? 1;
            var aliasesArg = dt.AliasKeywords is { Count: > 0 }
                ? "new[] { " + string.Join(", ", dt.AliasKeywords.Select(a => $"\"{Escape(a)}\"")) + " }"
                : "null";
            return $"global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidCommaKeywordList(value, {dt.KeywordMap}, {max}, {aliasesArg})";
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
