using System;
using PeachPDF.SourceGenerators.Model;

namespace PeachPDF.SourceGenerators.Emit
{
    /// <summary>
    /// The single place a <c>DataTypeKind.KeywordOrValue</c> entry's <c>valueType</c> ("integer"/"length")
    /// maps to real C# — the value-side validation clause, the storage type, and the <c>TryParse</c>-shaped
    /// parser to hand to <see cref="global::PeachPDF.CSS.CssKeywordOrValueParser.FromCssText{TEnum,TValue}"/>.
    /// <see cref="ValidatorExpressionBuilder"/> and <see cref="RegistryEmitter"/> both consult this rather
    /// than each declaring (and separately guarding) their own copy of the valueType switch. An entry's
    /// optional <see cref="DataTypeSpec.Min"/>/<see cref="DataTypeSpec.Max"/> — the same JSON <c>min</c>/
    /// <c>max</c> fields the plain "integer" DataTypeKind uses — narrow the "integer" value clause the
    /// same way that DataTypeKind's own min/max do. Only the validation clause is narrowed, since
    /// <c>Set_X</c> always runs <c>Validate_X</c> first, so the stored value is already known in range by
    /// the time <c>CssKeywordOrValueParser.FromCssText</c> itself parses it.
    /// </summary>
    internal static class KeywordOrValueGrammar
    {
        public readonly struct Resolved
        {
            public Resolved(string valueClause, string csharpType, string tryParseMethod)
            {
                ValueClause = valueClause;
                CSharpType = csharpType;
                TryParseMethod = tryParseMethod;
            }

            public string ValueClause { get; }
            public string CSharpType { get; }
            public string TryParseMethod { get; }
        }

        public static Resolved Resolve(PropertyEntry entry, DataTypeSpec dt) => dt.ValueType switch
        {
            "integer" => new Resolved(BuildIntegerValueClause(dt), "int", "int.TryParse"),
            // Deliberately validates via Length.TryParse itself, NOT the broader
            // CssValueParser.IsValidLength (which also accepts calc()) - Length.TryParse can't parse a
            // calc() expression needing layout-time context (mixed em/rem/% units; a pure-absolute
            // calc() has already folded to a literal length by the time Layer A hands this text down, so
            // it still passes), and CssKeywordOrValueParser.FromCssText has no third "unresolved, retry
            // at layout time" state the way plain CssProperty<T> has for var() - the value side of this
            // union is only ever Keyword or a concrete TValue. Validating with the broader clause would
            // let an unparseable relative calc() pass validation and then silently fall back to the
            // keyword side in FromCssText (the wrong value, not what was authored) instead of being
            // correctly rejected as an invalid declaration (CSS Syntax 3 error recovery - the previous/
            // inherited/initial value stays in effect, same as any other invalid value already handled
            // by this generator).
            "length" => new Resolved(
                "global::PeachPDF.CSS.Length.TryParse(value, out _)",
                "global::PeachPDF.CSS.Length", "global::PeachPDF.CSS.Length.TryParse"),
            _ => throw new NotSupportedException(
                $"\"{entry.Name}\" declares a keyword-or-value cssDataType with valueType \"{dt.ValueType}\", " +
                "which KeywordOrValueGrammar does not yet implement."),
        };

        // Mirrors ValidatorExpressionBuilder.BuildIntegerClause (the plain "integer" DataTypeKind's own
        // min/max clause) exactly, so a keyword-or-value entry's min/max behaves identically to a plain
        // "integer" entry's min/max rather than a second, independently-derived narrowing rule.
        private static string BuildIntegerValueClause(DataTypeSpec dt)
        {
            if (dt.Min is null && dt.Max is null) return "int.TryParse(value, out _)";

            var clause = "int.TryParse(value, out var parsedInt)";
            if (dt.Min.HasValue) clause += $" && parsedInt >= {(int)dt.Min.Value}";
            if (dt.Max.HasValue) clause += $" && parsedInt <= {(int)dt.Max.Value}";
            return clause;
        }
    }
}
