using System;
using PeachPDF.SourceGenerators.Model;

namespace PeachPDF.SourceGenerators.Emit
{
    /// <summary>
    /// The single place a <c>DataTypeKind.KeywordOrValue</c> entry's <c>valueType</c> ("integer"/"length")
    /// maps to real C# — the value-side validation clause, the storage type, and the <c>TryParse</c>-shaped
    /// parser to hand to <see cref="global::PeachPDF.CSS.CssKeywordOrValueParser.FromCssText{TEnum,TValue}"/>.
    /// <see cref="ValidatorExpressionBuilder"/> and <see cref="RegistryEmitter"/> both consult this rather
    /// than each declaring (and separately guarding) their own copy of the valueType switch.
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
            "integer" => new Resolved("int.TryParse(value, out _)", "int", "int.TryParse"),
            "length" => new Resolved(
                "global::PeachPDF.Html.Core.Parse.CssValueParser.IsValidLength(value)",
                "global::PeachPDF.CSS.Length", "global::PeachPDF.CSS.Length.TryParse"),
            _ => throw new NotSupportedException(
                $"\"{entry.Name}\" declares a keyword-or-value cssDataType with valueType \"{dt.ValueType}\", " +
                "which KeywordOrValueGrammar does not yet implement."),
        };
    }
}
