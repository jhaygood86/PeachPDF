using System.Collections.Generic;
using PeachPDF.SourceGenerators.Model;

namespace PeachPDF.SourceGenerators
{
    /// <summary>
    /// PPG010/PPG011: checks each entry's declared <c>propertyPath</c>/<c>csharpDataType</c> against
    /// the real shape of <c>CssBox</c>/<c>SvgElement</c> captured in a <see cref="TargetTypeModel"/> —
    /// the diagnostics that make the JSON provably in sync with the C# it targets, rather than merely
    /// intended to be (see <see cref="CssPropertyGenerator"/>'s remarks).
    /// </summary>
    internal static class SymbolValidator
    {
        public static List<ModelDiagnostic> Validate(IReadOnlyList<PropertyEntry> entries, TargetTypeModel targets)
        {
            var diagnostics = new List<ModelDiagnostic>();

            foreach (var entry in entries)
            {
                if (entry.Html?.PropertyPath is { } htmlPath)
                {
                    if (!targets.CssBoxFound)
                    {
                        // CssBox isn't in this compilation (e.g. a generator-only test compilation) —
                        // nothing to check against; not an error.
                    }
                    else if (!targets.TryGetCssBoxMember(htmlPath, out var member))
                    {
                        diagnostics.Add(new ModelDiagnostic(DiagnosticCode.HtmlPropertyPathMismatch,
                            $"\"{entry.Name}\".html.propertyPath \"{htmlPath}\" was not found as a property on CssBox.",
                            entry.Line, entry.Column));
                    }
                    else if (!member.HasSetter)
                    {
                        diagnostics.Add(new ModelDiagnostic(DiagnosticCode.HtmlPropertyPathMismatch,
                            $"\"{entry.Name}\".html.propertyPath \"{htmlPath}\" names a CssBox property with no setter.",
                            entry.Line, entry.Column));
                    }
                    else if (entry.Html.CsharpDataType is { } declaredType && !TypesMatch(declaredType, member.TypeDisplay))
                    {
                        diagnostics.Add(new ModelDiagnostic(DiagnosticCode.HtmlPropertyPathMismatch,
                            $"\"{entry.Name}\".html declares csharpDataType \"{declaredType}\" but CssBox.{htmlPath} " +
                            $"is actually \"{member.TypeDisplay}\".",
                            entry.Line, entry.Column));
                    }
                }

                if (entry.Svg?.PropertyPath is { } svgPath)
                {
                    if (!targets.SvgElementFound)
                    {
                        // SvgElement isn't in this compilation — nothing to check against.
                    }
                    else if (!targets.TryGetSvgElementMember(svgPath, out var member))
                    {
                        diagnostics.Add(new ModelDiagnostic(DiagnosticCode.SvgPropertyPathMismatch,
                            $"\"{entry.Name}\".svg.propertyPath \"{svgPath}\" was not found as a property on SvgElement.",
                            entry.Line, entry.Column));
                    }
                    else if (!member.HasSetter)
                    {
                        diagnostics.Add(new ModelDiagnostic(DiagnosticCode.SvgPropertyPathMismatch,
                            $"\"{entry.Name}\".svg.propertyPath \"{svgPath}\" names an SvgElement property with no setter.",
                            entry.Line, entry.Column));
                    }
                    else if (entry.Svg.CsharpDataType is { } declaredType && !TypesMatch(declaredType, member.TypeDisplay))
                    {
                        diagnostics.Add(new ModelDiagnostic(DiagnosticCode.SvgPropertyPathMismatch,
                            $"\"{entry.Name}\".svg declares csharpDataType \"{declaredType}\" but SvgElement.{svgPath} " +
                            $"is actually \"{member.TypeDisplay}\".",
                            entry.Line, entry.Column));
                    }
                }
            }

            return diagnostics;
        }

        /// <summary>
        /// Loose comparison on purpose: the JSON's <c>csharpDataType</c> is authored by hand as a short
        /// name ("string", "CssProperty&lt;DirectionMode&gt;"), while the symbol's display string may
        /// include whitespace differences. Normalizing whitespace is enough for every case this schema
        /// actually uses (no fully-qualified names appear in either side).
        /// </summary>
        private static bool TypesMatch(string declared, string actual) =>
            Normalize(declared) == Normalize(actual);

        private static string Normalize(string type) => type.Replace(" ", "");
    }
}
