namespace PeachPDF.SourceGenerators.Model
{
    /// <summary>The <c>"html"</c> section of a css-properties.json entry — see PropertyEntry.cssDataType schema remarks.</summary>
    internal sealed class HtmlBinding
    {
        /// <summary>Simple member name on <c>CssBox</c> (or the logical scratch-property name), or null for an unsupported/no-storage entry.</summary>
        public string? PropertyPath { get; }
        public string? CsharpDataType { get; }
        /// <summary>The <c>ComputedStyleAreas</c> record this property lives in, or null for a logical/unsupported entry.</summary>
        public string? Area { get; }
        public bool HasGetter { get; }
        public string? GetterExpression { get; }
        public string? CustomValidator { get; }
        public string? CustomSetter { get; }
        public bool Snapshot { get; }

        public HtmlBinding(string? propertyPath, string? csharpDataType, string? area, bool hasGetter,
            string? getterExpression, string? customValidator, string? customSetter, bool snapshot)
        {
            PropertyPath = propertyPath;
            CsharpDataType = csharpDataType;
            Area = area;
            HasGetter = hasGetter;
            GetterExpression = getterExpression;
            CustomValidator = customValidator;
            CustomSetter = customSetter;
            Snapshot = snapshot;
        }
    }
}
