using System;
using PeachPDF.SourceGenerators.Model;

namespace PeachPDF.SourceGenerators.Emit
{
    /// <summary>
    /// The single place a <c>DataTypeKind.KeywordOrValue</c> entry's <c>valueType</c>
    /// ("integer"/"length"/"length-or-unitless") maps to real C# — the value-side validation clause, the
    /// storage type, and the <c>TryParse</c>-shaped parser to hand to
    /// <see cref="global::PeachPDF.CSS.CssKeywordOrValueParser.FromCssText{TEnum,TValue}"/>.
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
            // LengthOrCalc.TryParse (via CssValueParser.TryParseLengthOrCalc), not the plain
            // Length.TryParse - a keyword-or-value property's "length" side must accept calc() too (CSS
            // Values and Units 4 §10.9 - calc() is valid anywhere <length> is), including a calc()
            // mixing relative units that can't fold to a literal Length at cascade time (a pure-absolute
            // calc() already has, by Layer A's CalcSerializer, before this ever runs). LengthOrCalc keeps
            // that text for Layer B's own ParseLength to evaluate lazily against real box context at
            // consumption time - see LengthOrCalc's doc comment. Validating with the same TryParse that
            // storage uses keeps the two in lockstep (unlike a broader validator that accepts more than
            // storage can actually represent, which would let a value pass validation and then silently
            // fall back to the wrong keyword-side value in FromCssText).
            "length" => new Resolved(
                "global::PeachPDF.Html.Core.Parse.CssValueParser.TryParseLengthOrCalc(value, out _)",
                "global::PeachPDF.CSS.LengthOrCalc", "global::PeachPDF.Html.Core.Parse.CssValueParser.TryParseLengthOrCalc"),
            // line-height's own non-keyword grammar (<length-percentage> | <number>) - a superset of
            // "length" that also accepts a bare unitless multiplier (CSS Inline Layout 3 §4). See
            // LengthOrUnitless's own doc comment for why the unitless side needs no separate deferred-calc
            // case the way the length side does.
            "length-or-unitless" => new Resolved(
                "global::PeachPDF.Html.Core.Parse.CssValueParser.TryParseLengthOrUnitless(value, out _)",
                "global::PeachPDF.CSS.LengthOrUnitless", "global::PeachPDF.Html.Core.Parse.CssValueParser.TryParseLengthOrUnitless"),
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
