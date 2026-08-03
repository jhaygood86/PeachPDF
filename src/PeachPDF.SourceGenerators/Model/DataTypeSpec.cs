namespace PeachPDF.SourceGenerators.Model
{
    internal enum DataTypeKind
    {
        /// <summary>No dedicated grammar modeled in this schema — HTML validation delegates to the real
        /// CSS-OM property for the same name (<c>PeachPDF.CSS.PropertyFactory</c>/<c>StylesheetParser</c>),
        /// so "cssom" still only accepts what that property's own grammar accepts, not literally anything.
        /// See <c>ValidatorExpressionBuilder.BuildCssOmClause</c>.</summary>
        CssOm,
        Unsupported,
        Length,
        Color,
        CurrentColor,
        Transform,
        Keyword,
        Integer,
        Number,
        Parsed,
        EnumKeyword,
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

        // "parsed"
        public string? Converter { get; }
        public string? ResultType { get; }
        public string? TypedValueType { get; }

        // "enum-keyword"
        public string? EnumType { get; }
        public string? KeywordMap { get; }
        public string? Fallback { get; }

        public DataTypeSpec(DataTypeKind kind, double? min = null, double? max = null,
            string? converter = null, string? resultType = null, string? typedValueType = null,
            string? enumType = null, string? keywordMap = null, string? fallback = null)
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
        }

        public static DataTypeSpec Simple(DataTypeKind kind) => new(kind);
    }
}
