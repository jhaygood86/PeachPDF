using Microsoft.CodeAnalysis;
using PeachPDF.SourceGenerators.Model;

namespace PeachPDF.SourceGenerators
{
    /// <summary>
    /// The <c>PPGxxx</c> diagnostic catalog for <c>css-properties.json</c> authoring mistakes. See
    /// <see cref="Model.DiagnosticCode"/> for the enum these map from, and CLAUDE.md for the
    /// authoring-facing description of when each fires.
    /// </summary>
    internal static class Diagnostics
    {
        private const string Category = "PeachPDF.SourceGenerators";

        private static DiagnosticDescriptor D(string id, string title, DiagnosticSeverity severity) =>
            new(id, title, "{0}", Category, severity, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor JsonMalformed =
            D("PPG001", "css-properties.json is malformed", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor UnknownDataType =
            D("PPG002", "Unknown cssDataType", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor DuplicateName =
            D("PPG003", "Duplicate property name", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor KeywordMissingSupportedValues =
            D("PPG004", "\"keyword\" data type requires supportedValues", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor BindingMissingTarget =
            D("PPG005", "Binding has no propertyPath and no customSetter", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor InitialValueMissing =
            D("PPG006", "initialValue must be declared explicitly", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor AliasMismatch =
            D("PPG007", "aliasOf target missing or initialValue mismatch", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor CustomSetterHasReturn =
            D("PPG008", "Custom setter code must not contain return", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor SupportedValuesIgnored =
            D("PPG009", "supportedValues is ignored by this data type", DiagnosticSeverity.Warning);

        public static readonly DiagnosticDescriptor SvgPropertyPathMismatch =
            D("PPG010", "svg.propertyPath does not match SvgElement's real shape", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor HtmlPropertyPathMismatch =
            D("PPG011", "html.propertyPath does not match CssBox's real shape", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor UnknownArea =
            D("PPG012", "Unknown ComputedStyleAreas record", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor InvalidLogicalEntry =
            D("PPG014", "Invalid logical-category entry", DiagnosticSeverity.Error);

        public static readonly DiagnosticDescriptor PropertyPathAreaConflict =
            D("PPG015", "html.propertyPath reused across different html.area values", DiagnosticSeverity.Error);

        public static DiagnosticDescriptor For(DiagnosticCode code) => code switch
        {
            DiagnosticCode.JsonMalformed => JsonMalformed,
            DiagnosticCode.UnknownDataType => UnknownDataType,
            DiagnosticCode.DuplicateName => DuplicateName,
            DiagnosticCode.KeywordMissingSupportedValues => KeywordMissingSupportedValues,
            DiagnosticCode.BindingMissingTarget => BindingMissingTarget,
            DiagnosticCode.InitialValueMissing => InitialValueMissing,
            DiagnosticCode.AliasMismatch => AliasMismatch,
            DiagnosticCode.CustomSetterHasReturn => CustomSetterHasReturn,
            DiagnosticCode.SupportedValuesIgnored => SupportedValuesIgnored,
            DiagnosticCode.SvgPropertyPathMismatch => SvgPropertyPathMismatch,
            DiagnosticCode.HtmlPropertyPathMismatch => HtmlPropertyPathMismatch,
            DiagnosticCode.UnknownArea => UnknownArea,
            DiagnosticCode.InvalidLogicalEntry => InvalidLogicalEntry,
            DiagnosticCode.PropertyPathAreaConflict => PropertyPathAreaConflict,
            _ => JsonMalformed,
        };
    }
}
