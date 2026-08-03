namespace PeachPDF.SourceGenerators.Model
{
    internal enum SvgInvalidBehavior
    {
        Inherit,
        Initial,
        LeaveUnset,
    }

    internal enum SvgApplyIn
    {
        Common,
        Manual,
    }

    /// <summary>The <c>"svg"</c> section of a css-properties.json entry.</summary>
    internal sealed class SvgBinding
    {
        /// <summary>Simple member name on <c>SvgElement</c>, or null for a carrier-only property (e.g. direction).</summary>
        public string? PropertyPath { get; }
        public string? CsharpDataType { get; }
        /// <summary>The <c>SvgInheritedPaint</c> member that carries this property's inherited value forward.</summary>
        public string? InheritedFrom { get; }
        public SvgInvalidBehavior InvalidBehavior { get; }
        public SvgApplyIn ApplyIn { get; }
        public string? CustomValidator { get; }
        public string? CustomSetter { get; }

        public SvgBinding(string? propertyPath, string? csharpDataType, string? inheritedFrom,
            SvgInvalidBehavior invalidBehavior, SvgApplyIn applyIn, string? customValidator, string? customSetter)
        {
            PropertyPath = propertyPath;
            CsharpDataType = csharpDataType;
            InheritedFrom = inheritedFrom;
            InvalidBehavior = invalidBehavior;
            ApplyIn = applyIn;
            CustomValidator = customValidator;
            CustomSetter = customSetter;
        }
    }
}
