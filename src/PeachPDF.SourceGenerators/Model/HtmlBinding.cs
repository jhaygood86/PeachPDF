using System.Collections.Generic;

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
        /// <summary><c>DerivedStyle</c> method name(s) the generated <c>CssBox</c> property setter calls
        /// after a successful, actually-changed set - e.g. <c>"InvalidateBorderTopWidth"</c>. Null for a
        /// property with no cached derived value to invalidate.</summary>
        public IReadOnlyList<string>? Invalidates { get; }
        /// <summary>Names a closed, generator-known value-computation strategy for the generated <c>CssBox</c>
        /// property setter's body, for the handful of properties whose stored value isn't just the
        /// (possibly canonicalized) authored string - e.g. <c>"font-size"</c> for the eager em/ex/percent/
        /// smaller/larger-to-absolute-points resolution. Null for the default: store the value as-is.</summary>
        public string? ValueComputation { get; }

        public HtmlBinding(string? propertyPath, string? csharpDataType, string? area, bool hasGetter,
            string? getterExpression, string? customValidator, string? customSetter, bool snapshot,
            IReadOnlyList<string>? invalidates, string? valueComputation)
        {
            PropertyPath = propertyPath;
            CsharpDataType = csharpDataType;
            Area = area;
            HasGetter = hasGetter;
            GetterExpression = getterExpression;
            CustomValidator = customValidator;
            CustomSetter = customSetter;
            Snapshot = snapshot;
            Invalidates = invalidates;
            ValueComputation = valueComputation;
        }
    }
}
