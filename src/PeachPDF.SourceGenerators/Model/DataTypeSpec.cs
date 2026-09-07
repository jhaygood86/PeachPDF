using System.Collections.Generic;

namespace PeachPDF.SourceGenerators.Model
{
    internal enum DataTypeKind
    {
        Unsupported,
        Length,
        Color,
        CurrentColor,
        Transform,
        Keyword,
        Integer,
        Number,
        Parsed,
        /// <summary>Calls a specific, already-existing Layer A grammar (an <c>internal static
        /// TryParse(IReadOnlyList&lt;Token&gt;)</c> method, or an <c>IValueConverter</c> field's
        /// <c>.Convert</c>, named in full by <see cref="DataTypeSpec.Converter"/>) directly against
        /// <c>CssValueParser.GetCssTokens(value, inValueContext: true)</c>, instead of "cssom"'s full
        /// <c>PropertyFactory.Create</c> + <c>StylesheetParser.ParseValue</c> + <c>TrySetValue</c> round
        /// trip. Validator-only — the property still stores the raw string (see
        /// <c>RegistryEmitter.BuildHtmlAssignment</c>'s default assignment), so there is no dedicated
        /// <c>Set_</c> codegen for this kind the way <see cref="Parsed"/> has.</summary>
        CssOmGrammar,
        /// <summary>&lt;length-percentage&gt;{min,max} (or &lt;length&gt;{min,max} with AllowPercentage
        /// false) — span-based, via CssValueParser.IsValidLengthList. No tokenizer at all.</summary>
        LengthList,
        /// <summary>[ auto || &lt;ratio&gt; ] — span-based, via AspectRatioGrammar.TryParseFast. No
        /// tokenizer at all.</summary>
        Ratio,
        /// <summary>A comma-separated list of keyword segments — span-based, via
        /// CssValueParser.IsValidCommaKeywordList. No tokenizer at all.</summary>
        KeywordList,
        /// <summary>The real "transform" property's permissive &lt;transform-list&gt; grammar — span-based,
        /// via CssValueParser.IsSyntacticallyValidTransformList. Distinct from the existing stricter
        /// <see cref="Transform"/> kind (paint-support-only, used for supportsDataType/@supports).</summary>
        TransformList,
        /// <summary>"auto | &lt;custom-ident&gt;" (currently only "page"'s page-name grammar, CSS Paged
        /// Media 3 §4.2) — span-based, via CssValueParser.IsValidPageName. No tokenizer at all.</summary>
        CustomIdentOrAuto,
        EnumKeyword,
        KeywordOrValue,
        SvgPaint,
        SvgOpacity,
        SvgLength,
        SvgLengthList,
        SvgTransform,
        SvgReference,
    }

    /// <summary>
    /// One member of a <c>cssDataType</c> union (most entries declare exactly one). The scalar
    /// string forms ("length", "color", ...) map straight to a <see cref="DataTypeKind"/> with no
    /// extra data; the three object forms ("integer" with min/max, "parsed", "enum-keyword") carry
    /// the fields below. Plain class (not a record) for the same netstandard2.0/no-IsExternalInit
    /// reason as <see cref="Json.JsonValue"/>.
    /// </summary>
    internal sealed class DataTypeSpec
    {
        public DataTypeKind Kind { get; }

        // "integer"
        public double? Min { get; }
        public double? Max { get; }

        // "parsed" (the string-to-typed-result converter); also reused by "cssom-grammar" to name the
        // tokens-to-nullable-result grammar member it calls (see DataTypeKind.CssOmGrammar) — both kinds
        // are "the name of a callable to invoke and check for null", just over different inputs.
        public string? Converter { get; }
        public string? ResultType { get; }
        public string? TypedValueType { get; }

        // "enum-keyword", "keyword-or-value"
        public string? EnumType { get; }
        public string? KeywordMap { get; }
        public string? Fallback { get; }

        // "keyword-or-value" — the non-keyword side's grammar/C# type ("integer", "length", or
        // "length-or-unitless")
        public string? ValueType { get; }

        // "cssom-grammar" — true when the named grammar member returns null for the literal "none" the
        // same as it does for a genuinely invalid value (e.g. BasicShapeGrammar.TryParse, whose own doc
        // comment states this explicitly, since a null result there can't otherwise distinguish "no
        // clip" from "invalid"), so the validator clause must accept the literal separately.
        public bool AcceptsNoneLiteral { get; }

        // "length-list" — component count bounds and whether a percentage component is accepted
        // (border-*-radius: true; border-spacing: false).
        public int? MinCount { get; }
        public int? MaxCount { get; }
        public bool AllowPercentage { get; }

        // "keyword-list" — reuses KeywordMap above (a Map.* dictionary name, same meaning as
        // "enum-keyword"'s) for the per-segment vocabulary. MaxPerSegment is the most space-separated
        // keywords one comma segment may hold (background-repeat's <repeat-style> allows 2); AliasKeywords
        // are single-token literals accepted as a whole segment in place of the MaxPerSegment expansion
        // (background-repeat's repeat-x/repeat-y).
        public int? MaxPerSegment { get; }
        public IReadOnlyList<string>? AliasKeywords { get; }

        public DataTypeSpec(DataTypeKind kind, double? min = null, double? max = null,
            string? converter = null, string? resultType = null, string? typedValueType = null,
            string? enumType = null, string? keywordMap = null, string? fallback = null, string? valueType = null,
            bool acceptsNoneLiteral = false, int? minCount = null, int? maxCount = null, bool allowPercentage = false,
            int? maxPerSegment = null, IReadOnlyList<string>? aliasKeywords = null)
        {
            Kind = kind;
            Min = min;
            Max = max;
            Converter = converter;
            ResultType = resultType;
            TypedValueType = typedValueType;
            EnumType = enumType;
            KeywordMap = keywordMap;
            Fallback = fallback;
            ValueType = valueType;
            AcceptsNoneLiteral = acceptsNoneLiteral;
            MinCount = minCount;
            MaxCount = maxCount;
            AllowPercentage = allowPercentage;
            MaxPerSegment = maxPerSegment;
            AliasKeywords = aliasKeywords;
        }

        public static DataTypeSpec Simple(DataTypeKind kind) => new(kind);
    }
}
