using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using PeachPDF.SourceGenerators.Emit;
using PeachPDF.SourceGenerators.Model;

namespace PeachPDF.SourceGenerators
{
    /// <summary>
    /// Turns <c>css-properties.json</c> into <c>CssPropertyRegistry.g.cs</c> / <c>SvgPropertyRegistry.g.cs</c>.
    /// See CLAUDE.md's generator section for the JSON schema and the authoring workflow this drives.
    /// </summary>
    [Generator]
    internal sealed class CssPropertyGenerator : IIncrementalGenerator
    {
        private const string JsonFileName = "css-properties.json";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var jsonDocuments = context.AdditionalTextsProvider
                .Where(f => Path.GetFileName(f.Path).Equals(JsonFileName, StringComparison.OrdinalIgnoreCase))
                .Select((f, ct) => ParsedDocument.Parse(f.Path, f.GetText(ct)?.ToString() ?? ""))
                .WithTrackingName(TrackingNames.ParsedJson)
                .Collect();

            // Projected down to a small equatable model (see TargetTypeModel's remarks) specifically so
            // this stage — and everything downstream of it — is reused across an edit that doesn't
            // change CssBox's/SvgElement's own shape, even though CompilationProvider's identity changes
            // on every keystroke. GeneratorIncrementalityTests asserts this actually holds.
            var targetTypes = context.CompilationProvider
                .Select((compilation, _) => CaptureTargetTypes(compilation))
                .WithTrackingName(TrackingNames.TargetTypes);

            context.RegisterSourceOutput(jsonDocuments.Combine(targetTypes), Emit);
        }

        private static void Emit(SourceProductionContext context, (ImmutableArray<ParsedDocument> Documents, TargetTypeModel Targets) input)
        {
            if (input.Documents.Length == 0)
                return; // no css-properties.json AdditionalFile in this compilation — nothing to generate

            // Only one css-properties.json is expected; if more than one AdditionalFile matches the
            // name (e.g. a stray copy), the first one wins and the rest are ignored rather than merged
            // — merging would silently combine two authors' intents into one registry.
            var document = input.Documents[0];

            foreach (var diagnostic in document.Diagnostics)
                context.ReportDiagnostic(ToDiagnostic(diagnostic, document.Path));

            var symbolDiagnostics = SymbolValidator.Validate(document.Entries, input.Targets);
            foreach (var diagnostic in symbolDiagnostics)
                context.ReportDiagnostic(ToDiagnostic(diagnostic, document.Path));

            if (document.Diagnostics.Any(d => IsError(d.Code)) || symbolDiagnostics.Any(d => IsError(d.Code)))
                return; // don't emit a registry built from an entry we already know is wrong

            var htmlSource = RegistryEmitter.EmitHtmlRegistry(document.Entries);
            context.AddSource("CssPropertyRegistry.g.cs", htmlSource);

            var svgSource = RegistryEmitter.EmitSvgRegistry(document.Entries);
            context.AddSource("SvgPropertyRegistry.g.cs", svgSource);
        }

        private static bool IsError(DiagnosticCode code) => Diagnostics.For(code).DefaultSeverity == DiagnosticSeverity.Error;

        private static Diagnostic ToDiagnostic(ModelDiagnostic diagnostic, string path)
        {
            var span = new LinePositionSpan(
                new LinePosition(Math.Max(0, diagnostic.Line - 1), Math.Max(0, diagnostic.Column - 1)),
                new LinePosition(Math.Max(0, diagnostic.Line - 1), Math.Max(0, diagnostic.Column - 1)));
            var location = Location.Create(path, TextSpan.FromBounds(0, 0), span);
            return Diagnostic.Create(Diagnostics.For(diagnostic.Code), location, diagnostic.Message);
        }

        private static TargetTypeModel CaptureTargetTypes(Compilation compilation)
        {
            var cssBoxType = compilation.GetTypeByMetadataName("PeachPDF.Html.Core.Dom.CssBox");
            var svgElementType = compilation.GetTypeByMetadataName("PeachPDF.Svg.SvgElement");

            return new TargetTypeModel(
                cssBoxType is not null, CaptureMembers(cssBoxType),
                svgElementType is not null, CaptureMembers(svgElementType));
        }

        private static EquatableArray<TargetMember> CaptureMembers(INamedTypeSymbol? type)
        {
            if (type is null) return EquatableArray<TargetMember>.Empty;

            var format = SymbolDisplayFormat.MinimallyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

            var members = new List<TargetMember>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = type;

            while (current is not null)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is IPropertySymbol { IsStatic: false } property && seen.Add(property.Name))
                    {
                        var hasSetter = property.SetMethod is not null;
                        members.Add(new TargetMember(property.Name, property.Type.ToDisplayString(format), hasSetter));
                    }
                }

                current = current.BaseType;
            }

            return members.ToEquatableArray();
        }
    }
}
