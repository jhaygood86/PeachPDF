namespace PeachPDF.SourceGenerators.Model
{
    /// <summary>
    /// The PPGxxx diagnostic catalog (see css-properties.schema.json's remarks and CLAUDE.md's
    /// generator section for the authoring-facing description of each). Kept as a plain enum,
    /// independent of Roslyn's <see cref="Microsoft.CodeAnalysis.DiagnosticDescriptor"/>, so the
    /// JSON/model layer (<see cref="PropertyModelParser"/>) stays unit-testable without a compiler
    /// pipeline; <c>CssPropertyGenerator</c> maps each code to its descriptor and reports it at the
    /// right location once a <see cref="Microsoft.CodeAnalysis.Compilation"/> is available.
    /// </summary>
    internal enum DiagnosticCode
    {
        JsonMalformed = 1,           // PPG001
        UnknownDataType = 2,         // PPG002
        DuplicateName = 3,           // PPG003
        KeywordMissingSupportedValues = 4, // PPG004
        BindingMissingTarget = 5,    // PPG005
        InitialValueMissing = 6,     // PPG006
        AliasMismatch = 7,           // PPG007
        CustomSetterHasReturn = 8,   // PPG008
        SupportedValuesIgnored = 9,  // PPG009
        SvgPropertyPathMismatch = 10, // PPG010
        HtmlPropertyPathMismatch = 11, // PPG011
        UnknownArea = 12,            // PPG012
        UnknownPropertyNameConstant = 13, // PPG013 (not implemented in this pass — reserved)
        InvalidLogicalEntry = 14,    // PPG014
        PropertyPathAreaConflict = 15, // PPG015
    }
}
