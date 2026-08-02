using System;
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
            string.Join(" || ", entry.CssDataTypes.Select(dt => BuildClause(entry, dt, isSvg: false)));

        public static string BuildSvg(PropertyEntry entry) =>
            string.Join(" || ", entry.CssDataTypes.Select(dt => BuildClause(entry, dt, isSvg: true)));

        private static string BuildClause(PropertyEntry entry, DataTypeSpec dt, bool isSvg) => dt.Kind switch
        {
            DataTypeKind.Any => "true",
            DataTypeKind.Unsupported => "false",
            DataTypeKind.Length => "global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidLength(value)",
            DataTypeKind.Color => "parser.IsColorValid(value)",
            DataTypeKind.CurrentColor => "value.Equals(\"currentcolor\", global::System.StringComparison.OrdinalIgnoreCase)",
            DataTypeKind.Keyword => BuildKeywordClause(entry),
            DataTypeKind.Integer => BuildIntegerClause(dt),
            DataTypeKind.Number => "double.TryParse(value, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out _)",
            DataTypeKind.EnumKeyword => "true", // CssProperty<T>.FromCssText never rejects — falls back to the keyword instead. See css-properties.schema.json's remarks.
            DataTypeKind.SvgPaint => "global::PeachPDF.Svg.SvgValueParsers.TryParsePaint(value, ctx.Adapter, ctx.ContextColor, out _)",
            DataTypeKind.SvgOpacity => "global::PeachPDF.Svg.SvgValueParsers.TryParseOpacity(value, out _)",
            DataTypeKind.SvgLength => "global::PeachPDF.Svg.SvgValueParsers.ParseLength(value, null) is not null",
            _ => throw new NotSupportedException(
                $"DataTypeKind.{dt.Kind} is not yet implemented by RegistryEmitter (property \"{entry.Name}\") — " +
                "add its codegen to ValidatorExpressionBuilder before authoring an entry that uses it."),
        };

        private static string BuildKeywordClause(PropertyEntry entry)
        {
            var values = entry.SupportedValues ?? Array.Empty<string>();
            if (values.Count == 0) return "false";

            if (entry.KeywordComparison == KeywordComparison.Ordinal)
            {
                var pattern = string.Join(" or ", values.Select(v => $"\"{Escape(v)}\""));
                return $"value is {pattern}";
            }

            var comparison = entry.KeywordComparison == KeywordComparison.OrdinalIgnoreCase
                ? "global::System.StringComparison.OrdinalIgnoreCase"
                : "global::System.StringComparison.InvariantCultureIgnoreCase";

            return string.Join(" || ", values.Select(v => $"value.Equals(\"{Escape(v)}\", {comparison})"));
        }

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
